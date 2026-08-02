using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Google.Apis.Util;
using Google.Cloud.Functions.Framework;
using Google.Cloud.Functions.Hosting;
using http_forwarder_app.Core;
using http_forwarder_app.Models;
using http_forwarder_app.Models.Services;
using http_forwarder_app.Utils;
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
    private readonly HashSet<string> _allowedApiKeys;
    private readonly TimeSpan _regexMatchTimeout;
    private readonly bool _rateLimitingEnabled;
    private readonly int _rateLimitPerWindow;
    private readonly TimeSpan _rateLimitWindow;
    private static long InstantiationCounter = 0;
    private static readonly ConcurrentDictionary<string, RateLimitWindow> RateLimitWindows = new();
    private readonly IPublishingService _publishingService;
    private const string ApiKeyHeaderName = "X-API-Key";
    private const string ApiKeyQueryParameterName = "apiKey";

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

            var allowedApiKeys = configuration.GetAllowedApiKeys() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(allowedApiKeys))
            {
                _logger.LogError("Environment variable '{AllowedApiKeysEnvVar}' is not set.", Constants.ALLOWED_API_KEYS);
                throw new InvalidOperationException($"Environment variable '{Constants.ALLOWED_API_KEYS}' is not set.");
            }
            _allowedApiKeys = allowedApiKeys
                                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .ToHashSet(StringComparer.Ordinal);
            if (_allowedApiKeys.Count == 0)
            {
                _logger.LogError("Environment variable '{AllowedApiKeysEnvVar}' does not contain any API keys.", Constants.ALLOWED_API_KEYS);
                throw new InvalidOperationException($"Environment variable '{Constants.ALLOWED_API_KEYS}' does not contain any API keys.");
            }
            if (isFirstTime)
            {
                _logger.LogInformation("Configured {allowedApiKeyCount} API key(s)", _allowedApiKeys.Count);
            }

            _regexMatchTimeout = configuration.GetRegexMatchTimeout();
            _rateLimitingEnabled = configuration.IsRateLimitingEnabled();
            _rateLimitPerWindow = configuration.GetRateLimitPerWindow();
            _rateLimitWindow = configuration.GetRateLimitWindow();

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
        if (!TryConsumeRateLimit(context))
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = ((int)_rateLimitWindow.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
            await context.Response.WriteAsync("Rate limit exceeded");
            return;
        }

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

        if (!TryAuthorizeApiKey(context))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized");
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
            _allowedEvents.Any(allowedEventName => SafeRegex.IsMatch(eventName, allowedEventName, RegexOptions.IgnoreCase, _regexMatchTimeout, _logger))))
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

        // Strip API key header so it is not republished to Pub/Sub
        var requestHeaders = StripApiKeyHeader(context.Request.Headers.GetHeaders());

        ForwardingRequest fwdRequest = new(
            Method: requestMethod,
            Event: eventName,
            Content: requestBody,
            RequestHeaders: requestHeaders);

        var publishingResult = await _publishingService.Publish(
            projectId: _projectId,
            topicId: _topicId,
            fwdRequest: fwdRequest);

        await publishingResult.Match(
            async success =>
            {
                success.ThrowIfNull("Success result can not be null");
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

    private bool TryAuthorizeApiKey(HttpContext context)
    {
        var headerApiKey = context.Request.Headers[ApiKeyHeaderName].FirstOrDefault();
        var queryApiKey = context.Request.Query[ApiKeyQueryParameterName].FirstOrDefault();

        var hasHeaderKey = !string.IsNullOrEmpty(headerApiKey);
        var hasQueryKey = !string.IsNullOrEmpty(queryApiKey);

        if (hasHeaderKey && hasQueryKey)
        {
            _logger.LogWarning(
                "Multiple API keys were provided via both {ApiKeyHeaderName} header and {ApiKeyQueryParameterName} query parameter; this is likely a mistake",
                ApiKeyHeaderName,
                ApiKeyQueryParameterName);
        }

        return (hasHeaderKey && _allowedApiKeys.Contains(headerApiKey!))
            || (hasQueryKey && _allowedApiKeys.Contains(queryApiKey!));
    }

    private static ImmutableSortedDictionary<string, string> StripApiKeyHeader(ImmutableSortedDictionary<string, string> headers)
    {
        return headers
            .Where(kvp => !string.Equals(kvp.Key, ApiKeyHeaderName, StringComparison.OrdinalIgnoreCase))
            .ToImmutableSortedDictionary();
    }

    private bool TryConsumeRateLimit(HttpContext context)
    {
        if (!_rateLimitingEnabled) return true;

        var clientIp = GetClientIp(context);
        var now = DateTimeOffset.UtcNow;
        var window = RateLimitWindows.GetOrAdd(clientIp, _ => new RateLimitWindow(now, _rateLimitWindow));
        lock (window)
        {
            if (now >= window.ExpiresAt)
            {
                window.Reset(now, _rateLimitWindow);
            }

            if (window.Count >= _rateLimitPerWindow)
            {
                return false;
            }

            window.Count++;
            return true;
        }
    }

    private static string GetClientIp(HttpContext context)
    {
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            return forwardedFor.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "unknown";
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private sealed class RateLimitWindow(DateTimeOffset startsAt, TimeSpan duration)
    {
        public DateTimeOffset ExpiresAt { get; private set; } = startsAt.Add(duration);
        public int Count { get; set; }

        public void Reset(DateTimeOffset startsAt, TimeSpan duration)
        {
            ExpiresAt = startsAt.Add(duration);
            Count = 0;
        }
    }
}
