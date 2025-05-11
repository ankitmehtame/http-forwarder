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

}