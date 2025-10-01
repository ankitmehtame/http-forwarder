using System;

namespace http_forwarder_app.Extensions;

public static class RetryExtensions
{
    public static double CalculateExponentialDelay(this int attemptCount, int baseDelay, int maxDelay)
    {
        var delay = Math.Min(baseDelay * Math.Pow(2, attemptCount - 1), maxDelay);
        return delay;
    }
}
