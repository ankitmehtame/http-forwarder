using System.Text.RegularExpressions;

namespace http_forwarder_app.Models;

public static class SafeRegex
{
    public static bool IsMatch(string input, string pattern, RegexOptions options = RegexOptions.None, TimeSpan? timeout = null)
    {
        try
        {
            return Regex.IsMatch(input, pattern, options, timeout ?? Constants.RegexMatchTimeoutDefault);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
