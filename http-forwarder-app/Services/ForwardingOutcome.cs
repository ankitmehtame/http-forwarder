using http_forwarder_app.Models;

namespace http_forwarder_app.Services;

public enum ForwardingOutcomeKind
{
    Response,
    NoMatchingRule,
    NoBody,
    RemoteRule
}

public sealed class ForwardingOutcome : IDisposable
{
    public ForwardingOutcomeKind Kind { get; }
    public HttpResponseMessage? Response { get; }
    public int? StatusCode => Response is null ? null : (int)Response.StatusCode;
    public Guid? RetryId { get; }
    public string? MessageId { get; }
    public ForwardingRule? Rule { get; }
    public ForwardingRule? RemoteRule { get; }

    public ForwardingOutcome(ForwardingOutcomeKind kind, HttpResponseMessage? response = null, Guid? retryId = null, string? messageId = null, ForwardingRule? rule = null, ForwardingRule? remoteRule = null)
    {
        Kind = kind;
        Response = response;
        RetryId = retryId;
        MessageId = messageId;
        Rule = rule;
        RemoteRule = remoteRule;
    }

    public void Dispose() => Response?.Dispose();
}
