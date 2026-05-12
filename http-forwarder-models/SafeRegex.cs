using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace http_forwarder_app.Models;

public static class SafeRegex
{
    public static bool IsMatch(string input, string pattern, RegexOptions options = RegexOptions.None, TimeSpan? timeout = null, ILogger? logger = null)
    {
        var matchTimeout = timeout ?? Constants.RegexMatchTimeoutDefault;
        try
        {
            return Regex.IsMatch(input, pattern, options, matchTimeout);
        }
        catch (RegexMatchTimeoutException ex)
        {
            logger?.LogWarning(ex, "Regex match timed out after {timeoutMilliseconds}ms for pattern {pattern}", matchTimeout.TotalMilliseconds, pattern);
            return false;
        }
        catch (ArgumentException ex)
        {
            logger?.LogWarning(ex, "Invalid regex pattern {pattern}", pattern);
            return false;
        }
    }
}
