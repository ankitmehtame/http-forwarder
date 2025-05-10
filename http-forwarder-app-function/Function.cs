using System.Text.RegularExpressions;
using Google.Cloud.Functions.Framework;
using Google.Cloud.Functions.Hosting;
using http_forwarder_app.Core;
using http_forwarder_app.Models;
using http_forwarder_app.Models.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace http_forwarder_app.Functions;

[FunctionsStartup(typeof(Startup))]
public class Function : IHttpFunction
{
    private readonly ILogger<Function> _logger;
    private readonly string _projectId;
    private readonly string _topicId;

    private readonly HashSet<string> _allowedEvents;
    private static long InstantiationCounter = 0;
    private readonly IPublishingService _publishingService;

    public Function(ILogger<Function> logger, IConfiguration configuration, IPublishingService publishingService)
    {
        var instanceCount = Interlocked.Increment(ref InstantiationCounter);
        var isFirstTime = instanceCount == 1;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _publishingService = publishingService;
        try
        {
            var allowedEvents = configuration.GetGenericTopicAllowedEvents() ?? string.Empty;
            if (isFirstTime)
            {
                _logger.LogInformation("Allowed events - {allowedEvents}", allowedEvents);
            }

            if (string.IsNullOrEmpty(allowedEvents))
            {
                _logger.LogError("Environment variable '{AllowedEventNamesEnvVar}' is not set.", Constants.GENERIC_TOPIC_ALLOWED_EVENTS);
                throw new InvalidOperationException($"Environment variable '{Constants.GENERIC_TOPIC_ALLOWED_EVENTS}' is not set.");
            }
            _allowedEvents = allowedEvents
                                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            _projectId = configuration.GetCloudProjectId() ?? string.Empty;
            _topicId = configuration.GetGenericPubSubTopicId() ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError("Error during instantiating {className} {instanceCount} - {errorMessage}", nameof(Function), instanceCount, ex);
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
            await context.Response.WriteAsync("Not allowed");
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

        var publishingResult = await _publishingService.Publish(projectId: _projectId, topicId: _topicId, fwdRequest: fwdRequest);

        await publishingResult.Match(
            async success =>
            {
                var messageId = success.MessageId;
                context.Response.StatusCode = StatusCodes.Status200OK;
                await context.Response.WriteAsync($"Message published successfully. Message ID: {messageId} for event {eventName} & method {requestMethod}");
            },
            async failure =>
            {
                var errorMessage = failure.ErrorMessage;
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsync($"Failed to publish message: {errorMessage}");
            }
        );
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