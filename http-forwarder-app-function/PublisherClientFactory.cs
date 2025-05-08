using Google.Cloud.PubSub.V1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace http_forwarder_app.Functions;

public class PublisherClientFactory : IPublisherClientFactory
{
    private readonly ILogger<PublisherClientFactory> _logger;
    private readonly IConfiguration _configuration;
    private readonly Lazy<PublisherClient> _publisherClientLazy;

    public PublisherClientFactory(ILogger<PublisherClientFactory> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _publisherClientLazy = new Lazy<PublisherClient>(CreateInstance, false);
    }

    public PublisherClient Create() => _publisherClientLazy.Value;

    private PublisherClient CreateInstance()
    {
        _logger.LogInformation("Creating {type}", nameof(PublisherClient));
        string? projectId = _configuration.GetValue<string?>("GOOGLE_CLOUD_PROJECT_ID");
        string? topicId = _configuration.GetValue<string?>("PUBSUB_TOPIC_ID");

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
        _logger.LogInformation($"Creating PublisherClient for topic: {topicName}");

        try
        {
            PublisherClient client = PublisherClient.Create(topicName);
            _logger.LogInformation("PublisherClient created successfully.");
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
    PublisherClient Create();
}