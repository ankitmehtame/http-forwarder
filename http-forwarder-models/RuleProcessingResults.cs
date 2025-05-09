namespace http_forwarder_app.Models;

public record class NoMatchingRuleResult()
{
    private static readonly Lazy<NoMatchingRuleResult> _lazyInstance = new();
    public static readonly NoMatchingRuleResult Instance = _lazyInstance.Value;
}

public record class NoBodyRuleResult()
{
    private static readonly Lazy<NoBodyRuleResult> _lazyInstance = new();
    public static readonly NoBodyRuleResult Instance = _lazyInstance.Value;
}

public record class MethodNotSupportedRuleResult(string Method);