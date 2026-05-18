using Microsoft.Extensions.Configuration;

namespace http_forwarder_app.Core;

public static class ConfigurationExtensions
{
    public static void ValidateStartupConfiguration(this IConfiguration configuration)
    {
        var errors = new List<string>();
        var locationTag = configuration.GetLocationTag();

        if (string.IsNullOrWhiteSpace(locationTag))
        {
            errors.Add($"{Constants.LOCATION_TAG} is required");
        }

        ValidatePositiveNumber(configuration, Constants.OUTBOUND_HTTP_TIMEOUT_SECONDS, errors);
        ValidatePositiveNumber(configuration, Constants.REGEX_MATCH_TIMEOUT_MILLISECONDS, errors);
        ValidatePositiveInteger(configuration, Constants.RATE_LIMIT_PER_WINDOW, errors);
        ValidatePositiveNumber(configuration, Constants.RATE_LIMIT_WINDOW_SECONDS, errors);

        if (configuration.IsPublisherEnabled())
        {
            if (string.IsNullOrWhiteSpace(configuration.GetCloudProjectId()))
            {
                errors.Add($"{Constants.CLOUD_PROJECT_ID} is required when {Constants.PUBLISHER_ENABLED}=true");
            }

            if (!HasAnyConfiguredTopic(configuration))
            {
                errors.Add($"At least one {Constants.TOPIC_ID_PREFIX}* value is required when {Constants.PUBLISHER_ENABLED}=true");
            }
        }

        if (configuration.IsListenerEnabled())
        {
            if (string.IsNullOrWhiteSpace(configuration.GetCloudProjectId()))
            {
                errors.Add($"{Constants.CLOUD_PROJECT_ID} is required when {Constants.LISTENER_ENABLED}=true");
            }

            if (string.IsNullOrWhiteSpace(configuration.GetGenericSubscriptionId()))
            {
                errors.Add($"{Constants.GENERIC_SUBSCRIPTION_ID} is required when {Constants.LISTENER_ENABLED}=true");
            }

            if (!string.IsNullOrWhiteSpace(locationTag) && string.IsNullOrWhiteSpace(configuration.GetSubscriptionId(locationTag)))
            {
                errors.Add($"{configuration.GetSubscriptionIdConfigurationVariable(locationTag)} is required when {Constants.LISTENER_ENABLED}=true");
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Configuration validation failed: {string.Join("; ", errors)}");
        }
    }

    public static bool IsListenerEnabled(this IConfiguration configuration)
    {
        return configuration.GetValue<bool?>(Constants.LISTENER_ENABLED) ?? false;
    }

    public static bool IsPublisherEnabled(this IConfiguration configuration)
    {
        return configuration.GetValue<bool?>(Constants.PUBLISHER_ENABLED) ?? false;
    }

    public static string? GetLocationTag(this IConfiguration configuration)
    {
        return configuration.GetValue<string?>(Constants.LOCATION_TAG);
    }

    public static string? GetCloudProjectId(this IConfiguration configuration)
    {
        return configuration.GetValue<string?>(Constants.CLOUD_PROJECT_ID);
    }

    public static string? GetGenericSubscriptionId(this IConfiguration configuration)
    {
        return configuration.GetValue<string?>(Constants.GENERIC_SUBSCRIPTION_ID);
    }

    public static string GetSubscriptionIdConfigurationVariable(this IConfiguration _, string locationTag)
    {
        return Constants.SUBSCRIPTION_ID_PREFIX + locationTag.ToUpperInvariant();
    }

    public static string? GetSubscriptionId(this IConfiguration configuration, string locationTag)
    {
        return configuration.GetValue<string?>(configuration.GetSubscriptionIdConfigurationVariable(locationTag));
    }

    public static string? GetGenericPubSubTopicId(this IConfiguration configuration)
    {
        return configuration.GetValue<string?>(Constants.GENERIC_TOPIC_ID);
    }

    public static string? GetPubSubTopicId(this IConfiguration configuration, string locationTag)
    {
        return configuration.GetValue<string?>(Constants.TOPIC_ID_PREFIX + locationTag.ToUpperInvariant());
    }

    public static string? GetGenericTopicAllowedEvents(this IConfiguration configuration)
    {
        return configuration.GetValue<string?>(Constants.GENERIC_TOPIC_ALLOWED_EVENTS);
    }

    public static string? GetConfiguredStoragePath(this IConfiguration configuration)
    {
        return configuration.GetValue<string?>(Constants.STORAGE_DIR_PATH, null);
    }

    public static int GetRetryMaxConcurrency(this IConfiguration configuration)
    {
        return configuration.GetValue<int>(Constants.RETRY_POLICY_MAX_CONCURRENCY, 4);
    }

    public static bool IsRetryBackgroundMonitoringEnabled(this IConfiguration configuration)
    {
        return configuration.GetValue<bool>(Constants.RETRY_BACKGROUND_MONITORING_ENABLED, true);
    }

    public static bool IsRateLimitingEnabled(this IConfiguration configuration)
    {
        return configuration.GetValue<bool>(Constants.RATE_LIMITING_ENABLED, true);
    }

    public static int GetRateLimitPerWindow(this IConfiguration configuration)
    {
        var value = configuration.GetValue<int?>(Constants.RATE_LIMIT_PER_WINDOW);
        return value is > 0 ? value.Value : Constants.RateLimitPerWindowDefault;
    }

    public static TimeSpan GetRateLimitWindow(this IConfiguration configuration)
    {
        return GetPositiveTimeSpan(
            configuration,
            Constants.RATE_LIMIT_WINDOW_SECONDS,
            Constants.RateLimitWindowDefault,
            TimeSpan.FromSeconds);
    }

    public static string GetMaskedHeadersValue(this IConfiguration configuration)
    {
        return (configuration.GetValue<string?>(Constants.MASKED_HEADERS, string.Empty) ?? string.Empty).Trim();
    }

    public static TimeSpan GetOutboundHttpTimeout(this IConfiguration configuration)
    {
        return GetPositiveTimeSpan(
            configuration,
            Constants.OUTBOUND_HTTP_TIMEOUT_SECONDS,
            Constants.OutboundHttpTimeoutDefault,
            TimeSpan.FromSeconds);
    }

    public static TimeSpan GetRegexMatchTimeout(this IConfiguration configuration)
    {
        return GetPositiveTimeSpan(
            configuration,
            Constants.REGEX_MATCH_TIMEOUT_MILLISECONDS,
            Constants.RegexMatchTimeoutDefault,
            TimeSpan.FromMilliseconds);
    }

    private static TimeSpan GetPositiveTimeSpan(IConfiguration configuration, string key, TimeSpan defaultValue, Func<double, TimeSpan> toTimeSpan)
    {
        var value = configuration.GetValue<double?>(key);
        return value is > 0 ? toTimeSpan(value.Value) : defaultValue;
    }

    private static void ValidatePositiveNumber(IConfiguration configuration, string key, IList<string> errors)
    {
        var configuredValue = configuration.GetValue<string?>(key);
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return;
        }

        if (!double.TryParse(configuredValue, out var value) || value <= 0)
        {
            errors.Add($"{key} must be a positive number when configured");
        }
    }

    private static void ValidatePositiveInteger(IConfiguration configuration, string key, IList<string> errors)
    {
        var configuredValue = configuration.GetValue<string?>(key);
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return;
        }

        if (!int.TryParse(configuredValue, out var value) || value <= 0)
        {
            errors.Add($"{key} must be a positive integer when configured");
        }
    }

    private static bool HasAnyConfiguredTopic(IConfiguration configuration)
    {
        return configuration.AsEnumerable().Any(pair =>
            !string.IsNullOrWhiteSpace(pair.Value)
            && (string.Equals(pair.Key, Constants.GENERIC_TOPIC_ID, StringComparison.OrdinalIgnoreCase)
                || pair.Key.StartsWith(Constants.TOPIC_ID_PREFIX, StringComparison.OrdinalIgnoreCase)));
    }

}
