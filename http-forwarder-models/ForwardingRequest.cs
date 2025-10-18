using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Serialization;

namespace http_forwarder_app.Models;

public record class ForwardingRequest(
    string Method,
    string Event,
    string? Content,
    ImmutableSortedDictionary<string, string> RequestHeaders)
{
    [JsonConstructor]
    public ForwardingRequest(string method, string @event, string? content)
        : this(Method: method, Event: @event, Content: content, RequestHeaders: ImmutableSortedDictionary<string, string>.Empty)
    { }

    [JsonIgnore]
    internal PrettyDictionary? __PrettyHeaders { get; private set; } = null;

    public override int GetHashCode()
    {
        HashCode hashCode = new();
        hashCode.Add(Method);
        hashCode.Add(Event);
        hashCode.Add(Content);
        foreach (var kvp in RequestHeaders)
        {
            hashCode.Add(kvp.Key);
            hashCode.Add(kvp.Value);
        }
        return hashCode.ToHashCode();
    }

    public virtual bool Equals(ForwardingRequest? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return Method == other.Method &&
            Event == other.Event &&
            Content == other.Content &&
            RequestHeaders.SequenceEqual(other.RequestHeaders);
    }

    public override string ToString()
    {
        __PrettyHeaders ??= new(RequestHeaders);
        var builder = new StringBuilder();
        PrintMembers(builder);
        builder.Replace($", {nameof(RequestHeaders)} = System.Collections.Generic.Dictionary`2[System.String,System.String]", $", {nameof(RequestHeaders)} = {__PrettyHeaders}");
        builder.Replace($", {nameof(RequestHeaders)} = System.Collections.Immutable.ImmutableSortedDictionary`2[System.String,System.String]", $", {nameof(RequestHeaders)} = {__PrettyHeaders}");
        return builder.ToString();
    }
}