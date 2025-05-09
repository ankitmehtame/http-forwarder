using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Google.Cloud.PubSub.V1;
using http_forwarder_app.Core;
using http_forwarder_app.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OneOf;

namespace http_forwarder_app.Services;

public class SubscriptionService : IHostedService
{
    private readonly ILogger<SubscriptionService> _logger;
    private readonly IConfiguration _configuration;
    private readonly ForwardingService _forwardingService;

    private const int _retryConnectionSeconds = 60;
    private readonly CancellationTokenSource _shutdownSource;
    private readonly HashSet<string> _allowedMethods = [HttpMethods.Post, HttpMethods.Put];

    public SubscriptionService(ILogger<SubscriptionService> logger, IConfiguration configuration, ForwardingService forwardingService)
    {
        _logger = logger;
        _configuration = configuration;
        _forwardingService = forwardingService;
        _shutdownSource = new CancellationTokenSource();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();

        string? projectId = _configuration.GetValue<string?>(Constants.CLOUD_PROJECT_ID);
        string? subscriptionId = _configuration.GetValue<string?>(Constants.SUBSCRIPTION_ID);

        if (string.IsNullOrEmpty(projectId))
        {
            _logger.LogError("Environment variable '{CLOUD_PROJECT_ID}' is not set for Pub/Sub client registration.", Constants.CLOUD_PROJECT_ID);
            throw new ArgumentNullException(nameof(projectId), $"Environment variable '{Constants.CLOUD_PROJECT_ID}' is not set for Pub/Sub client registration.");
        }
        if (string.IsNullOrEmpty(subscriptionId))
        {
            _logger.LogError("Environment variable '{SUBSCRIPTION_ID}' is not set for Pub/Sub client registration.", Constants.SUBSCRIPTION_ID);
            throw new ArgumentNullException(nameof(subscriptionId), $"Environment variable '{Constants.SUBSCRIPTION_ID}' is not set for Pub/Sub client registration.");
        }

        SubscriptionName subscriptionName = SubscriptionName.FromProjectSubscription(projectId, subscriptionId);

        SubscriberClient subscriber = await SubscriberClient.CreateAsync(subscriptionName);

        var shutdownToken = _shutdownSource.Token;
        var subscriptionTask = Task.Run(() => Subscribe(subscriber, shutdownToken), CancellationToken.None);
    }

    private async Task Subscribe(SubscriberClient subscriber, CancellationToken cancellationToken)
    {
        await Task.Yield();
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Looking for subscription messages");
                await subscriber.StartAsync(OnMessage);
            }
            catch (OperationCanceledException)
            { }
            catch (Exception ex)
            {
                _logger.LogWarning("Error during subscription {errorMessage}", ex);
            }
            if (cancellationToken.IsCancellationRequested) break;
            await Task.Delay(TimeSpan.FromSeconds(_retryConnectionSeconds), cancellationToken).IgnoreCancellation();
        }
        _logger.LogInformation("Stopped subscription");
    }

    private async Task<SubscriberClient.Reply> OnMessage(PubsubMessage message, CancellationToken cancellationToken)
    {
        string messageData = System.Text.Encoding.UTF8.GetString(message.Data.ToByteArray());
        string messageId = message.MessageId;
        string publishTime = message.PublishTime.ToDateTimeOffset().ToString("o");

        message.Attributes.TryGetValue(FunctionAttributes.EventAttribute, out string eventName);
        message.Attributes.TryGetValue(FunctionAttributes.MethodAttribute, out string requestMethod);

        eventName ??= string.Empty;
        requestMethod ??= string.Empty;

        _logger.LogInformation("Received message {messageId} published at {publishTime} for event {eventName}, method {requestMethod}", messageId, publishTime, eventName, requestMethod);

        ForwardingRequest? forwardingRequest = JsonUtils.Deserialize<ForwardingRequest>(messageData);

        if (forwardingRequest == null)
        {
            _logger.LogError("Unable to parse message {messageId} for event {eventName} & method {requestMethod} published at {publishTime}", messageId, eventName, requestMethod, publishTime);
            return SubscriberClient.Reply.Nack;
        }

        if (!_allowedMethods.Contains(requestMethod))
        {
            _logger.LogWarning("Request method {requestMethod} is not supported", requestMethod);
            return SubscriberClient.Reply.Nack;
        }

        var result = requestMethod switch
        {
            "POST" => ProcessResult(_forwardingService.ProcessPostEvent(eventName, requestHostUrl: null, forwardingRequest.Content ?? string.Empty), requestMethod: requestMethod, eventName: eventName),
            "PUT" => ProcessResult(_forwardingService.ProcessPutEvent(eventName, requestHostUrl: null, forwardingRequest.Content ?? string.Empty), eventName: eventName, requestMethod: requestMethod),
            _ => HandleUnsupportedRequestMethod(requestMethod)
        };

        return await result;
    }

    private Task<SubscriberClient.Reply> HandleUnsupportedRequestMethod(string requestMethod)
    {
        _logger.LogWarning("Method {requestMethod} not supported", requestMethod);
        return Task.FromResult(SubscriberClient.Reply.Nack);
    }

    private async Task<SubscriberClient.Reply> ProcessResult(Task<OneOf<HttpResponseMessage, NoMatchingRuleResult, NoBodyRuleResult>> processTask, string eventName, string requestMethod)
    {
        var result = await processTask;

        var ackResult = result.Match(
            respMessage =>
            {
                if (respMessage.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Success ({statusCode}) for event {eventName}, method {requestMethod}", respMessage.StatusCode, eventName, requestMethod);
                    return SubscriberClient.Reply.Ack;
                }
                else
                {
                    _logger.LogWarning("Error code {statusCode} for event {eventName}, method {requestMethod}", respMessage.StatusCode, eventName, requestMethod);
                    return SubscriberClient.Reply.Nack;
                }
            },
            noRule => SubscriberClient.Reply.Nack,
            noBody => SubscriberClient.Reply.Ack
        );
        return ackResult;
    }

    private static async Task<OneOf<HttpResponseMessage, NoMatchingRuleResult, NoBodyRuleResult, MethodNotSupportedRuleResult>> WrapResultTypes(Task<OneOf<HttpResponseMessage, NoMatchingRuleResult, NoBodyRuleResult>> task)
    {
        var result = await task;
        return result.Match<OneOf<HttpResponseMessage, NoMatchingRuleResult, NoBodyRuleResult, MethodNotSupportedRuleResult>>(
            httpResponse => httpResponse,
            noRule => noRule,
            noBody => noBody
        );
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _shutdownSource.CancelAsync();
    }
}