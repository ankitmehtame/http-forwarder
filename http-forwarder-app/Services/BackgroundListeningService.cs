using System;
using System.Threading;
using System.Threading.Tasks;
using Google.Cloud.PubSub.V1;
using http_forwarder_app.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OneOf;

namespace http_forwarder_app.Services;

public class BackgroundListeningService : IHostedService
{
    private readonly ILogger<BackgroundListeningService> _logger;
    private readonly CloudMessageHandlerFactory _cloudMessageHandlerFactory;
    private readonly IConfiguration _configuration;

    private const int _retryConnectionSeconds = 60;
    private readonly CancellationTokenSource _shutdownSource;

    public BackgroundListeningService(ILogger<BackgroundListeningService> logger, CloudMessageHandlerFactory cloudMessageHandlerFactory, IConfiguration configuration, ForwardingService forwardingService, RemoteRulePublishingService remoteRulePublishingService)
    {
        _logger = logger;
        _cloudMessageHandlerFactory = cloudMessageHandlerFactory;
        _configuration = configuration;
        _shutdownSource = new CancellationTokenSource();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();

        string? projectId = _configuration.GetCloudProjectId();
        string? genericSubscriptionId = _configuration.GetGenericSubscriptionId();
        string locationTag = _configuration.GetLocationTag() ?? string.Empty;
        string? locationSubscriptionId = _configuration.GetSubscriptionId(locationTag);

        if (string.IsNullOrEmpty(projectId))
        {
            _logger.LogError("Environment variable '{CLOUD_PROJECT_ID}' is not set for Pub/Sub client registration.", Constants.CLOUD_PROJECT_ID);
            throw new ArgumentNullException(nameof(projectId), $"Environment variable '{Constants.CLOUD_PROJECT_ID}' is not set for Pub/Sub client registration.");
        }
        if (string.IsNullOrEmpty(genericSubscriptionId))
        {
            _logger.LogError("Environment variable '{subscriptionId}' is not set for Pub/Sub client registration.", Constants.GENERIC_SUBSCRIPTION_ID);
            throw new ArgumentNullException(nameof(genericSubscriptionId), $"Environment variable '{Constants.GENERIC_SUBSCRIPTION_ID}' is not set for Pub/Sub client registration.");
        }
        if (string.IsNullOrEmpty(locationSubscriptionId))
        {
            _logger.LogError("Environment variable '{subscriptionId}' is not set for Pub/Sub client registration.", _configuration.GetSubscriptionIdConfigurationVariable(locationTag));
            throw new ArgumentNullException(nameof(locationSubscriptionId), $"Environment variable '{_configuration.GetSubscriptionIdConfigurationVariable(locationTag)}' is not set for Pub/Sub client registration.");
        }

        SubscriptionName genericSubscriptionName = SubscriptionName.FromProjectSubscription(projectId, genericSubscriptionId);

        SubscriberClient genericSubscriber = await SubscriberClient.CreateAsync(genericSubscriptionName);

        SubscriptionName locationSubscriptionName = SubscriptionName.FromProjectSubscription(projectId, locationSubscriptionId);

        SubscriberClient locationSubscriber = await SubscriberClient.CreateAsync(locationSubscriptionName);

        var shutdownToken = _shutdownSource.Token;

        var genericMessageHandler = _cloudMessageHandlerFactory.CreateHandler(canForwardToTopic: true, cancellationToken: shutdownToken);
        var locationMessageHandler = _cloudMessageHandlerFactory.CreateHandler(canForwardToTopic: false, cancellationToken: shutdownToken);

        var genericSubscriptionTask = Task.Run(() => Subscribe(genericSubscriber, genericMessageHandler, shutdownToken), CancellationToken.None);
        var locationSubscriptionTask = Task.Run(() => Subscribe(locationSubscriber, locationMessageHandler, shutdownToken), CancellationToken.None);
    }

    private async Task Subscribe(SubscriberClient subscriber, CloudMessageHandler cloudMessageHandler, CancellationToken cancellationToken)
    {
        await Task.Yield();
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Looking for subscription messages");
                await subscriber.StartAsync(cloudMessageHandler.OnMessage);
            }
            catch (OperationCanceledException)
            { }
            catch (Exception ex)
            {
                _logger.LogWarning("Error during subscription {errorMessage}", ex);
            }
            if (cancellationToken.IsCancellationRequested) break;
            await Task.Delay(TimeSpan.FromSeconds(_retryConnectionSeconds), cancellationToken).IgnoreCancellation();
        }
        _logger.LogInformation("Stopped subscription");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _shutdownSource.CancelAsync();
    }
}