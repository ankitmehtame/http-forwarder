using Microsoft.Extensions.Configuration;

namespace http_forwarder_app.Core;

public static class ConfigurationExtensions
{
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

}
