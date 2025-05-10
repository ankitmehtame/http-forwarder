using System.Threading;
using Microsoft.Extensions.Logging;

namespace http_forwarder_app.Services;

public class CloudMessageHandlerFactory
{
    private readonly ILogger<CloudMessageHandler> _logger;
    private readonly ForwardingService _forwardingService;
    private readonly RemoteRulePublishingService _remoteRulePublishingService;

    public CloudMessageHandlerFactory(ILogger<CloudMessageHandler> logger, ForwardingService forwardingService, RemoteRulePublishingService remoteRulePublishingService)
    {
        _logger = logger;
        _forwardingService = forwardingService;
        _remoteRulePublishingService = remoteRulePublishingService;
    }

    public CloudMessageHandler CreateHandler(bool canForwardToTopic, CancellationToken cancellationToken)
    {
        return new CloudMessageHandler(_logger,
            forwardingService: _forwardingService,
            remoteRulePublishingService: _remoteRulePublishingService,
            canForwardToTopic: canForwardToTopic,
            cancellationToken: cancellationToken);
    }
}