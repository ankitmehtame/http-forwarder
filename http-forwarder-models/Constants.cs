namespace http_forwarder_app;

public static class Constants
{
    public const string HTTP_CLIENT_IGNORE_SSL_ERROR = "ignore_ssl_error";

    public const string LISTENER_ENABLED = "LISTENER_ENABLED";

    public const string PUBLISHER_ENABLED = "PUBLISHER_ENABLED";

    public const string LOCATION_TAG = "LOCATION_TAG";

    public const string CLOUD_PROJECT_ID = "GOOGLE_CLOUD_PROJECT_ID";

    public const string GENERIC_SUBSCRIPTION_ID = "PUBSUB_SUBSCRIPTION_ID";

    public const string SUBSCRIPTION_ID_PREFIX = "PUBSUB_SUBSCRIPTION_ID_";

    public const string GENERIC_TOPIC_ID = "PUBSUB_TOPIC_ID";

    public const string TOPIC_ID_PREFIX = "PUBSUB_TOPIC_ID_";

    public const string GENERIC_TOPIC_ALLOWED_EVENTS = "ALLOWED_EVENTS";

    public const string STORAGE_DIR_PATH = "STORAGE_DIR_PATH";

    public const string RETRY_POLICY_MAX_CONCURRENCY = "RETRY_POLICY_MAX_CONCURRENCY";

    public const string RETRY_BACKGROUND_MONITORING_ENABLED = "RETRY_BACKGROUND_MONITORING_ENABLED";

    public const string MASKED_HEADERS = "MASKED_HEADERS";

    public const string OUTBOUND_HTTP_TIMEOUT_SECONDS = "OUTBOUND_HTTP_TIMEOUT_SECONDS";

    public const string REGEX_MATCH_TIMEOUT_MILLISECONDS = "REGEX_MATCH_TIMEOUT_MILLISECONDS";

    public const string RATE_LIMITING_ENABLED = "RATE_LIMITING_ENABLED";

    public const string RATE_LIMIT_PER_WINDOW = "RATE_LIMIT_PER_WINDOW";

    public const string RATE_LIMIT_WINDOW_SECONDS = "RATE_LIMIT_WINDOW_SECONDS";

    public static readonly TimeSpan RetryIntervalMin = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan RetryIntervalMax = TimeSpan.FromHours(1);
    public static readonly TimeSpan RetryExpiry = TimeSpan.FromHours(24);
    public static readonly TimeSpan OutboundHttpTimeoutDefault = TimeSpan.FromSeconds(100);
    public static readonly TimeSpan RegexMatchTimeoutDefault = TimeSpan.FromMilliseconds(100);
    public const int RateLimitPerWindowDefault = 60;
    public static readonly TimeSpan RateLimitWindowDefault = TimeSpan.FromSeconds(60);
}
