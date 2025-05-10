using OneOf;

namespace http_forwarder_app.Models.Services;

public interface IPublishingService
{
    Task<OneOf<RemoteRulePublishSuccessResult, RemoteRulePublishFailureResult>> Publish(string projectId, string topicId, ForwardingRequest fwdRequest);
}