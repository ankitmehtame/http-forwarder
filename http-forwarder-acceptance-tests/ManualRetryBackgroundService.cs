using http_forwarder_app.Models;
using http_forwarder_app.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;

namespace http_forwarder_acceptance_tests;

internal class ManualRetryBackgroundService : RetryBackgroundService
{
    public ManualRetryBackgroundService(
        IFailedRequestStorage storage,
        IForwardingService forwardingService,
        ISystemClock clock,
        ITimeDelayService timeDelayService,
        ILogger<RetryBackgroundService> logger,
        IConfiguration configuration) : base(storage, forwardingService, clock, timeDelayService, logger, configuration)
    { }

    public async Task ProcessPendingRequestsAsync(DateTimeOffset asOf, CancellationToken stoppingToken)
    {
        await base.ProcessPendingAsync(asOf, stoppingToken);
    }
}