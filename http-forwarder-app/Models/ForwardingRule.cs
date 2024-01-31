using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Text.Json.Serialization;

namespace http_forwarder_app.Models
{
    public record class ForwardingRule(string Method, string Event, string TargetUrl, [DefaultValue(true)] bool HasContent = true, string? Content = null, bool IgnoreSslError = false, Dictionary<string, string>? Headers = default)
    {
        public Dictionary<string, string> Headers { get; init; } = Headers ?? [];

        [JsonIgnore]
        public PrettyPrintDictionary __PrettyHeaders { get; init; } = new(Headers ?? []);

        public override string ToString()
        {
            var builder = new StringBuilder();
            PrintMembers(builder);
            builder.Replace($", {nameof(Headers)} = System.Collections.Generic.Dictionary`2[System.String,System.String]", string.Empty);
            builder.Replace($", {nameof(__PrettyHeaders)} = ", $", {nameof(Headers)} = ");
            return builder.ToString();
        }
    }

    public class PrettyPrintDictionary(IDictionary<string, string> Pairs)
    {
        public override string ToString()
        {
            var builder = new StringBuilder();
            builder.Append('[');
            var curIndex = 0;
            foreach(var pair in Pairs)
            {
                if (curIndex > 0) builder.Append(", ");
                builder.Append(pair.Key);
                builder.Append('=');
                builder.Append(pair.Value);
                curIndex++;
            }
            builder.Append(']');
            return builder.ToString();
        }
    }
}
