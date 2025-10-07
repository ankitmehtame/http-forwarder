using System.Collections.Immutable;
using System.ComponentModel;
using System.Text;
using System.Text.Json.Serialization;

namespace http_forwarder_app.Models;

public record class ForwardingRule
{
    public ForwardingRule(string method, string @event, string targetUrl, bool hasContent, string? content, bool ignoreSslError, ImmutableDictionary<string, string> headers, ImmutableHashSet<string> tags, RuleRetry retry)
    {
        Method = method;
        Event = @event;
        TargetUrl = targetUrl;
        HasContent = hasContent;
        Content = content;
        IgnoreSslError = ignoreSslError;
        Headers = headers;
        Tags = tags;
        Retry = retry;
    }

    public ForwardingRule(string method, string @event, string targetUrl) : this(method, @event, targetUrl, true, null, false, ImmutableDictionary<string, string>.Empty, [], RuleRetry.DisabledDefault)
    { }

    public ForwardingRule(ForwardingRuleDto dto) : this(dto.Method, dto.Event, dto.TargetUrl, dto.HasContent ?? true, dto.Content, dto.IgnoreSslError ?? false, dto.Headers ?? ImmutableDictionary<string, string>.Empty, dto.Tags ?? [], dto.Retry ?? RuleRetry.DisabledDefault)
    { }


    public string Method { get; init; }

    public string Event { get; init; }

    public string TargetUrl { get; init; }

    [DefaultValue(true)]
    public bool HasContent { get; init; }

    public string? Content { get; init; }

    public bool IgnoreSslError { get; init; }

    public ImmutableDictionary<string, string> Headers { get; init; }

    public ImmutableHashSet<string> Tags { get; init; }

    [JsonIgnore]
    public PrettyPrintDictionary? __PrettyHeaders { get; private set; } = null;


    [JsonIgnore]
    public string? __PrettyTags { get; private set; } = null;

    public bool HasTag(string tag) => Tags.Contains(tag);

    public RuleRetry Retry { get; init; }

    public override string ToString()
    {
        __PrettyHeaders ??= new(Headers);
        __PrettyTags ??= "[" + string.Join(", ", Tags.Order(StringComparer.OrdinalIgnoreCase)) + "]";
        var builder = new StringBuilder();
        PrintMembers(builder);
        builder.Replace($", {nameof(Headers)} = System.Collections.Generic.Dictionary`2[System.String,System.String]", string.Empty);
        builder.Replace($", {nameof(Headers)} = System.Collections.Immutable.ImmutableDictionary`2[System.String,System.String]", string.Empty);
        builder.Replace($", {nameof(__PrettyHeaders)} = ", $", {nameof(Headers)} = ");
        builder.Replace($", {nameof(Tags)} = System.Collections.Generic.HashSet`1[System.String]", string.Empty);
        builder.Replace($", {nameof(Tags)} = System.Collections.Immutable.ImmutableHashSet`1[System.String]", string.Empty);
        builder.Replace($", {nameof(__PrettyTags)} = ", $", {nameof(Tags)} = ");
        builder.Replace($", {nameof(Retry)} = {nameof(RuleRetry)} ", $", {nameof(Retry)} = ");

        return builder.ToString();
    }

    public ForwardingRuleMinimal ToMinimal()
    {
        return new ForwardingRuleMinimal(Method: Method, Event: Event, Tags: Tags);
    }
}

public class PrettyPrintDictionary(IDictionary<string, string> Pairs)
{
    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append('[');
        var curIndex = 0;
        foreach (var pair in Pairs.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
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

public record class ForwardingRuleMinimal(string Method, string Event, ImmutableHashSet<string> Tags)
{
    [JsonIgnore]
    public string __PrettyTags { get; init; } = "[" + string.Join(", ", Tags ?? []) + "]";

    public override string ToString()
    {
        var builder = new StringBuilder();
        PrintMembers(builder);
        builder.Replace($", {nameof(Tags)} = System.Collections.Generic.HashSet`1[System.String]", string.Empty);
        builder.Replace($", {nameof(__PrettyTags)} = System.Collections.Immutable.ImmutableHashSet`1[System.String]", $", {nameof(Tags)} = ");

        return builder.ToString();
    }
}

public record class ForwardingRuleDto(string Method,
    string Event,
    string TargetUrl,
    bool? HasContent = null,
    string? Content = null,
    bool? IgnoreSslError = null,
    ImmutableDictionary<string, string>? Headers = null,
    ImmutableHashSet<string>? Tags = null,
    RuleRetry? Retry = null)
{
    public ForwardingRule ToForwardingRule()
    {
        return new(this);
    }

    public ForwardingRuleDto(ForwardingRule rule) :
        this(
            Method: rule.Method,
            Event: rule.Event,
            TargetUrl: rule.TargetUrl,
            HasContent: rule.HasContent,
            Content: rule.Content,
            IgnoreSslError: rule.IgnoreSslError,
            Headers: rule.Headers,
            Tags: rule.Tags,
            Retry: rule.Retry
        )
    { }

    public ForwardingRuleDto() : this(string.Empty, string.Empty, string.Empty) { }
}

public static class ForwardingRuleExtensions
{
    public static ForwardingRule[] ToForwardingRules(this ForwardingRuleDto[] forwardingRuleDtoList)
    {
        return forwardingRuleDtoList.Select(dto => dto.ToForwardingRule()).ToArray();
    }

    public static ForwardingRuleDto ToDto(this ForwardingRule rule)
    {
        return new ForwardingRuleDto(rule);
    }
}