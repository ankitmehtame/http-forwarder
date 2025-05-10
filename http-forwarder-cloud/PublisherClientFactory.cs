using System.Collections.Concurrent;
using Google.Cloud.PubSub.V1;
using Microsoft.Extensions.Logging;

namespace http_forwarder_app.Cloud;

public class PublisherClientFactory : IPublisherClientFactory
{
    private readonly ILogger<PublisherClientFactory> _logger;

    private readonly ConcurrentDictionary<string, Lazy<PublisherClient>> _publisherClients;

    public PublisherClientFactory(ILogger<PublisherClientFactory> logger)
    {
        _logger = logger;
        _publisherClients = new(StringComparer.OrdinalIgnoreCase);
    }

    public PublisherClient GetOrCreate(string projectId, string topicId)
    {
        string publisherClientKey = $"{projectId}__{topicId}".ToUpperInvariant().Normalize();
        lock (publisherClientKey)
        {
            var publisherClientLazy = _publisherClients.GetOrAdd(publisherClientKey, (_) => new Lazy<PublisherClient>(() => CreateInstance(projectId, topicId), false));
            return publisherClientLazy.Value;
        }
    }

    private PublisherClient CreateInstance(string projectId, string topicId)
    {
        if (string.IsNullOrEmpty(projectId))
        {
            _logger.LogError("Environment variable 'GOOGLE_CLOUD_PROJECT_ID' is not set for Pub/Sub client registration.");
            throw new InvalidOperationException("Environment variable 'GOOGLE_CLOUD_PROJECT_ID' is not set for Pub/Sub client registration.");
        }
        if (string.IsNullOrEmpty(topicId))
        {
            _logger.LogError("Environment variable 'PUBSUB_TOPIC_ID' is not set for Pub/Sub client registration.");
            throw new InvalidOperationException("Environment variable 'PUBSUB_TOPIC_ID' is not set for Pub/Sub client registration.");
        }

        TopicName topicName = TopicName.FromProjectTopic(projectId, topicId);
        _logger.LogInformation("Creating PublisherClient for topic: {topicName}", topicName);

        try
        {
            PublisherClient client = PublisherClient.Create(topicName);
            return client;
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to create PublisherClient. {errorMessage}", ex);
            throw;
        }
    }
}

public interface IPublisherClientFactory
{
    PublisherClient GetOrCreate(string projectId, string topicId);
}