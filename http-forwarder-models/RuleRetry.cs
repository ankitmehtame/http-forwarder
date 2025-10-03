using System.ComponentModel;
using System.Text.Json.Serialization;

namespace http_forwarder_app.Models;

public record class RuleRetry
{
    [DefaultValue(true)]
    public bool Allow { get; init; } = true;

    public TimeSpan Expiry { get; init; } = new(hours: 23, minutes: 59, seconds: 59);

    [JsonConstructor]
    private RuleRetry()
    {
    }

    public static RuleRetry Create(bool allow)
    {
        return new RuleRetry { Allow = allow };
    }

    public static RuleRetry Create(bool allow, TimeSpan expiry)
    {
        return new RuleRetry { Allow = allow, Expiry = expiry };
    }

    public static readonly RuleRetry DisabledDefault = new() { Allow = false };
    public static readonly RuleRetry AllowedDefault = new() { Allow = true };
}