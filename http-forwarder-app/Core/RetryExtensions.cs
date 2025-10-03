using System;

namespace http_forwarder_app.Extensions;

public static class RetryExtensions
{
    public static TimeSpan CalculateExponentialDelay(this int attemptCount, TimeSpan baseDelay, TimeSpan maxDelay)
    {
        var delay = Math.Min(baseDelay.TotalSeconds * Math.Pow(2, attemptCount - 1), maxDelay.TotalSeconds);
        return TimeSpan.FromSeconds(delay);
    }
}
