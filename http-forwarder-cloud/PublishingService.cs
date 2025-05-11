using Google.Cloud.PubSub.V1;
using http_forwarder_app.Core;
using http_forwarder_app.Models;
using http_forwarder_app.Models.Services;
using Microsoft.Extensions.Logging;
using OneOf;

namespace http_forwarder_app.Cloud;

public class PublishingService : IPublishingService
{
    private readonly ILogger<PublishingService> _logger;
    private readonly IPublisherClientFactory _publisherClientFactory;

    public PublishingService(ILogger<PublishingService> logger, IPublisherClientFactory publisherClientFactory)
    {
        _logger = logger;
        _publisherClientFactory = publisherClientFactory;
    }

    public async Task<OneOf<RemoteRulePublishSuccessResult, RemoteRulePublishFailureResult>> Publish(string projectId, string topicId, ForwardingRequest fwdRequest)
    {
        var publisher = _publisherClientFactory.GetOrCreate(projectId, topicId);
        string eventName = fwdRequest.Event;
        string requestMethod = fwdRequest.Method;
        string messageData = JsonUtils.Serialize(fwdRequest, false);
        var messageBytes = System.Text.Encoding.UTF8.GetBytes(messageData);

        var pubsubMessage = new PubsubMessage
        {
            Data = Google.Protobuf.ByteString.CopyFrom(messageBytes),
        };
        pubsubMessage.Attributes.Add(FunctionAttributes.EventAttribute, eventName);
        pubsubMessage.Attributes.Add(FunctionAttributes.MethodAttribute, requestMethod);

        try
        {
            string messageId = await publisher.PublishAsync(pubsubMessage);

            _logger.LogInformation("Message published to Pub/Sub with ID: {messageId} for event {eventName} & method {requestMethod}", messageId, eventName, requestMethod);
            return new RemoteRulePublishSuccessResult(messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to publish message to Pub/Sub: {errorMessage} for {event} & method {requestMethod}", ex, eventName, requestMethod);
            return new RemoteRulePublishFailureResult(ex.Message);
        }
    }
}