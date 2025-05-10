using System.Collections.ObjectModel;
using Google.Cloud.PubSub.V1;
using http_forwarder_app.Cloud;
using Moq;

namespace http_forwarder_acceptance_tests;

public class StubPublisherClientFactory : IPublisherClientFactory
{
    private readonly Mock<PublisherClient> _mockPublisherClient = new();
    private int _messageCounter = 0;
    private List<PubsubMessage> _publishedMessages = new();

    public void Reset()
    {
        _messageCounter = 0;
        _publishedMessages.Clear();
    }

    public PublisherClient GetOrCreate(string projectId, string topicId)
    {
        _mockPublisherClient.Reset();
        _mockPublisherClient.Setup(x => x.PublishAsync(Capture.In(_publishedMessages))).ReturnsAsync($"message-id-{Interlocked.Increment(ref _messageCounter)}").Verifiable();
        return _mockPublisherClient.Object;
    }

    public Mock<PublisherClient> MockPublisherClient => _mockPublisherClient;

    public ReadOnlyCollection<PubsubMessage> PublishedMessages => _publishedMessages.AsReadOnly();
}