using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Google.Cloud.PubSub.V1;
using http_forwarder_app.Core;
using http_forwarder_app.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using OneOf;

namespace http_forwarder_app.Services;

public class CloudMessageHandler
{
    private readonly ILogger<CloudMessageHandler> _logger;
    private readonly ForwardingService _forwardingService;
    private readonly RemoteRulePublishingService _remoteRulePublishingService;
    private readonly HashSet<string> _allowedMethods = [HttpMethods.Post, HttpMethods.Put];

    public bool CanForwardToTopic { get; init; }

    public CloudMessageHandler(ILogger<CloudMessageHandler> logger, ForwardingService forwardingService, RemoteRulePublishingService remoteRulePublishingService, bool canForwardToTopic, CancellationToken cancellationToken)
    {
        _logger = logger;
        _forwardingService = forwardingService;
        _remoteRulePublishingService = remoteRulePublishingService;
        CanForwardToTopic = canForwardToTopic;
    }

    public async Task<SubscriberClient.Reply> OnMessage(PubsubMessage message, CancellationToken cancellationToken)
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
            "POST" => ProcessResult(_forwardingService.ProcessPostEvent(eventName, requestHostUrl: null, forwardingRequest.Content ?? string.Empty), forwardingRequest: forwardingRequest),
            "PUT" => ProcessResult(_forwardingService.ProcessPutEvent(eventName, requestHostUrl: null, forwardingRequest.Content ?? string.Empty), forwardingRequest: forwardingRequest),
            _ => HandleUnsupportedRequestMethod(requestMethod)
        };

        return await result;
    }

    private Task<SubscriberClient.Reply> HandleUnsupportedRequestMethod(string requestMethod)
    {
        _logger.LogWarning("Method {requestMethod} not supported", requestMethod);
        return Task.FromResult(SubscriberClient.Reply.Nack);
    }

    private async Task<SubscriberClient.Reply> ProcessResult(Task<OneOf<HttpResponseMessage, NoMatchingRuleResult, NoBodyRuleResult, RemoteRuleFoundResult>> processTask, ForwardingRequest forwardingRequest)
    {
        var result = await processTask;

        var ackResult = result.Match(
            respMessage =>
            {
                string eventName = forwardingRequest.Event;
                string requestMethod = forwardingRequest.Method;
                if (respMessage.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Success ({statusCode}) for event {eventName}, method {requestMethod}", respMessage.StatusCode, eventName, requestMethod);
                    return Task.FromResult(SubscriberClient.Reply.Ack);
                }
                else
                {
                    _logger.LogWarning("Error code {statusCode} for event {eventName}, method {requestMethod}", respMessage.StatusCode, eventName, requestMethod);
                    return Task.FromResult(SubscriberClient.Reply.Nack);
                }
            },
            noRule => Task.FromResult(SubscriberClient.Reply.Nack),
            noBody => Task.FromResult(SubscriberClient.Reply.Ack),
            remoteRule => HandleRemoteRule(forwardingRequest, remoteRule.RemoteRule)
        );
        return await ackResult;
    }

    private async Task<SubscriberClient.Reply> HandleRemoteRule(ForwardingRequest forwardingRequest, ForwardingRule remoteRule)
    {
        if (!CanForwardToTopic)
        {
            return SubscriberClient.Reply.Nack;
        }
        var publishResult = await _remoteRulePublishingService.Publish(forwardingRequest, remoteRule);
        return publishResult.Match(
            success => SubscriberClient.Reply.Ack,
            failure => SubscriberClient.Reply.Nack
        );
    }

}