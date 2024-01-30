using System.Collections.Generic;
using System.ComponentModel;

namespace http_forwarder_app.Models
{
    public record class ForwardingRule(string Method, string Event, string TargetUrl, [DefaultValue(true)] bool HasContent = true, string? Content = null, bool IgnoreSslError = false, Dictionary<string, string>? Headers = default)
    {
        public Dictionary<string, string> Headers { get; init; } = Headers ?? [];

    }
}
