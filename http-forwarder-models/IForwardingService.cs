using OneOf;

namespace http_forwarder_app.Models;

public interface IForwardingService
{
    Task<OneOf<HttpResponseRuleResult, NoMatchingRuleResult, RemoteRuleFoundResult>> ProcessGetEvent(
        string eventName,
        string? requestHostUrl,
        IDictionary<string, string> requestHeaders);
    Task<OneOf<HttpResponseRuleResult, NoMatchingRuleResult, NoBodyRuleResult, RemoteRuleFoundResult>> ProcessPostEvent(
        string eventName,
        string? requestHostUrl,
        string requestContent,
        IDictionary<string, string> requestHeaders);
    Task<OneOf<HttpResponseRuleResult, NoMatchingRuleResult, NoBodyRuleResult, RemoteRuleFoundResult>> ProcessPutEvent(
        string eventName,
        string? requestHostUrl,
        string requestContent,
        IDictionary<string, string> requestHeaders);
    Task<OneOf<HttpResponseRuleResult, NoMatchingRuleResult, RemoteRuleFoundResult>> ProcessDeleteEvent(
        string eventName,
        string? requestHostUrl,
        IDictionary<string, string> requestHeaders);
}
