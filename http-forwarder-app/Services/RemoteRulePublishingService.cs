using System;
using System.Linq;
using System.Threading.Tasks;
using http_forwarder_app.Core;
using http_forwarder_app.Models;
using http_forwarder_app.Models.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OneOf;

namespace http_forwarder_app.Services;

public class RemoteRulePublishingService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<RemoteRulePublishingService> _logger;
    private readonly IPublishingService _publishingService;

    public RemoteRulePublishingService(IConfiguration configuration, ILogger<RemoteRulePublishingService> logger, IPublishingService publishingService)
    {
        _configuration = configuration;
        _logger = logger;
        _publishingService = publishingService;
    }

    public async Task<OneOf<RemoteRulePublishSuccessResult, RemoteRulePublishFailureResult>> Publish(ForwardingRequest forwardingRequest, ForwardingRule forwardingRule)
    {
        string projectId = _configuration.GetCloudProjectId() ?? string.Empty;
        string currentLocation = _configuration.GetLocationTag() ?? string.Empty;
        var ruleLocations = forwardingRule.Tags.Except([currentLocation], StringComparer.OrdinalIgnoreCase);
        var topicIdForLocation = ruleLocations
                                    .Select(loc => _configuration.GetPubSubTopicId(loc))
                                    .FirstOrDefault(locTopicId => !string.IsNullOrEmpty(locTopicId));
        if (string.IsNullOrEmpty(topicIdForLocation))
        {
            _logger.LogWarning("Not able to find Pub/Sub topic for rule's location for {rule}", forwardingRule.ToMinimal());
            return new RemoteRulePublishFailureResult($"Not able to find Pub/Sub topic for rule's location for {forwardingRule.ToMinimal()}");
        }

        var publisherClient = await _publishingService.Publish(projectId, topicIdForLocation, forwardingRequest);
        return publisherClient;
    }
}