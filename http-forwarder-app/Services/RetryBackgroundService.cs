using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Internal;
using http_forwarder_app.Models;
using http_forwarder_app.Extensions;

namespace http_forwarder_app.Services;

public class RetryBackgroundService : BackgroundService
{
    private readonly IFailedRequestStorage _storage;
    private readonly IForwardingService _forwardingService;
    private readonly ISystemClock _clock;
    private readonly ILogger<RetryBackgroundService> _logger;

    public RetryBackgroundService(
        IFailedRequestStorage storage,
        IForwardingService forwardingService,
        ISystemClock clock,
        ILogger<RetryBackgroundService> logger)
    {
        _storage = storage;
        _forwardingService = forwardingService;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var requests = _storage.GetPendingRequests();
            foreach (var request in requests)
            {
                try
                {
                    var result = await _forwardingService.ProcessPostEvent(request.Rule.Event, null, request.Rule.Content ?? string.Empty);

                    await result.Match(
                        ruleResult =>
                        {
                            if (ruleResult.Response.IsSuccessStatusCode)
                            {
                                _storage.Remove(request.Id);
                            }
                            return Task.CompletedTask;
                        },
                        noRule => Task.CompletedTask,
                        noBody => Task.CompletedTask,
                        remoteRule => Task.CompletedTask
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Retry attempt failed for request {RequestId}", request.Id);
                }

                // Update retry information
                var now = _clock.UtcNow;
                var nextAttempt = request with
                {
                    AttemptCount = request.AttemptCount + 1,
                    LastAttempt = now,
                    NextAttempt = now.AddSeconds(request.AttemptCount.CalculateExponentialDelay(30, 3600))
                };

                if (nextAttempt.NextAttempt > request.FirstAttempt.AddHours(24))
                {
                    _storage.Remove(request.Id);
                }
                else
                {
                    _storage.Store(nextAttempt);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }
}
