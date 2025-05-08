using System.Text.RegularExpressions;
using Google.Cloud.Functions.Framework;
using Google.Cloud.PubSub.V1;
using http_forwarder_app.Core;
using http_forwarder_app.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace http_forwarder_app.Functions;

public class Function : IHttpFunction
{
    private readonly ILogger<Function> _logger;
    private readonly PublisherClient _publisher;

    private readonly HashSet<string> _allowedEvents;

    private const string AllowedEventsEnvVar = "ALLOWED_EVENTS";
    private static long InstantiationCounter = 0;

    public Function(ILogger<Function> logger, IConfiguration configuration, PublisherClient publisherClient)
    {
        var instanceCount = Interlocked.Increment(ref InstantiationCounter);
        var isFirstTime = instanceCount == 1;
        _logger = logger;

        try
        {
            var allowedEvents = configuration.GetValue<string?>(AllowedEventsEnvVar) ?? string.Empty;
            if (isFirstTime)
            {
                _logger.LogInformation("Allowed events - {allowedEvents}", allowedEvents);
            }

            if (string.IsNullOrEmpty(allowedEvents))
            {
                _logger.LogError("Environment variable '{AllowedEventNamesEnvVar}' is not set.", AllowedEventsEnvVar);
                throw new InvalidOperationException($"Environment variable '{AllowedEventsEnvVar}' is not set.");
            }
            _allowedEvents = allowedEvents
                                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            _publisher = publisherClient;
        }
        catch (Exception ex)
        {
            _logger?.LogError("Error during instantiating {className} {instanceCount} - {errorMessage}", nameof(Function), instanceCount, ex);
            throw;
        }
    }

    public async Task HandleAsync(HttpContext context)
    {
        var requestMethod = context.Request.Method;
        var requestPath = context.Request.Path.Value;
        _logger.LogInformation("Received HTTP {requestMethod} request at {requestPath}", requestMethod, requestPath);

        // Ensure it's a POST/PUT request
        if (requestMethod != "POST" && requestMethod != "PUT")
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            await context.Response.WriteAsync("Only POST/PUT requests are allowed.");
            return;
        }

        var eventName = GetEventName(requestPath);
        _logger.LogInformation("Event is {eventName}", eventName);
        if (string.IsNullOrEmpty(eventName))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync($"Error processing event name");
            return;
        }
        if (!(_allowedEvents.Contains("*") ||
            _allowedEvents.Contains(eventName) ||
            _allowedEvents.Any(allowedEventName => Regex.IsMatch(eventName, allowedEventName, RegexOptions.IgnoreCase))))
        {
            context.Response.StatusCode = StatusCodes.Status406NotAcceptable;
            await context.Response.WriteAsync($"Not allowed event {eventName}");
            return;
        }

        string requestBody = null!;
        using (var reader = new StreamReader(context.Request.Body, System.Text.Encoding.UTF8))
        {
            requestBody = ((await reader.ReadToEndAsync()) ?? string.Empty).Trim();
        }

        ForwardingRequest fwdRequest = new(Method: requestMethod, Event: eventName, Content: requestBody);

        try
        {
            string messageData = JsonUtils.Serialize(fwdRequest, false);
            var messageBytes = System.Text.Encoding.UTF8.GetBytes(messageData);

            var pubsubMessage = new PubsubMessage
            {
                Data = Google.Protobuf.ByteString.CopyFrom(messageBytes),
            };
            pubsubMessage.Attributes.Add("EVENT", eventName);
            pubsubMessage.Attributes.Add("METHOD", requestMethod);

            string messageId = await _publisher.PublishAsync(pubsubMessage);

            _logger.LogInformation("Message published to Pub/Sub with ID: {messageId}", messageId);

            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.WriteAsync($"Message published successfully. Message ID: {messageId} for event {eventName} & method {requestMethod}");
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to publish message to Pub/Sub: {errorMessage}", ex.Message);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsync($"Failed to publish message: {ex.Message}");
        }
    }

    private static string GetEventName(string? requestPath)
    {
        if (string.IsNullOrEmpty(requestPath)) return string.Empty;
        // Split the path by '/'
        // The last segment after the leading '/' will be the eventName
        var pathSegments = requestPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (pathSegments.Length > 0)
        {
            var eventName = pathSegments.Last();
            return eventName;
        }
        return string.Empty;
    }
}