using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Internal;
using http_forwarder_app.Models;
using http_forwarder_app.Extensions;
using http_forwarder_app.Core;
using http_forwarder_app.Utils;
using Microsoft.Extensions.Configuration;

namespace http_forwarder_app.Services;

public class RetryBackgroundService : BackgroundService
{
    private readonly IFailedRequestStorage _storage;
    private readonly IForwardingService _forwardingService;
    private readonly ITimeDelayService _timeDelayService;
    private readonly ISystemClock _clock;
    private readonly ILogger<RetryBackgroundService> _logger;
    private readonly int _maxConcurrency;
    private readonly bool _backgroundMonitoringEnabled;

    private CancellationTokenSource _waitTokenSource;


    public RetryBackgroundService(
        IFailedRequestStorage storage,
        IForwardingService forwardingService,
        ISystemClock clock,
        ITimeDelayService timeDelayService,
        ILogger<RetryBackgroundService> logger,
        IConfiguration configuration)
    {
        _storage = storage;
        _timeDelayService = timeDelayService;
        _forwardingService = forwardingService;
        _clock = clock;
        _logger = logger;
        _maxConcurrency = configuration.GetRetryMaxConcurrency();
        _backgroundMonitoringEnabled = configuration.IsRetryBackgroundMonitoringEnabled();
        _waitTokenSource = new CancellationTokenSource();
    }

    private async Task ProcessRequestAsync(FailedRequest request)
    {
        try
        {
            var result = await _forwardingService.ProcessPostEvent(
                eventName: request.Rule.Event,
                requestHostUrl: request.RequestHostUrl,
                requestContent: request.RequestBody,
                requestHeaders: request.RequestHeaders);

            await result.Match(
                ruleResult =>
                {
                    if (ruleResult.Response.IsSuccessStatusCode)
                    {
                        // Remove request from storage if successful
                        _storage.Remove(request.Id);
                    }
                    else if (ruleResult.Response.IsServerError() && ruleResult.Rule.Retry.Allow)
                    {
                        // Try again
                        UpdateStorageRequest(request, ruleResult.Rule.Retry);
                    }
                    else
                    {
                        // Failure - won't be retried
                        _logger.LogInformation("Request {requestId} for event {eventName} failed with status {statusCode}, but is not a server error. Hence, will be removed from storage",
                            request.Id,
                            request.Rule.Event,
                            ruleResult.Response.StatusCode);
                        _storage.Remove(request.Id);
                    }
                    return Task.CompletedTask;
                },
                noRule =>
                {
                    // Invalid request - won't be retried
                    _logger.LogInformation("Removing request {requestId} with event {eventName} from storage as there is no matching rule", request.Id, request.Rule.Event);
                    _storage.Remove(request.Id);
                    return Task.CompletedTask;
                },
                noBody =>
                {
                    // Invalid request - won't be retried
                    _logger.LogInformation("Removing request {requestId} with event {eventName} from storage as there is no body", request.Id, request.Rule.Event);
                    _storage.Remove(request.Id);
                    return Task.CompletedTask;
                },
                remoteRule =>
                {
                    // Invalid request - won't be retried
                    _logger.LogInformation("Removing request {requestId} with event {eventName} from storage as it is a remote rule with tags [{tags}]",
                        request.Id,
                        request.Rule.Event,
                        string.Join(", ", remoteRule.RemoteRule.Tags));
                    _storage.Remove(request.Id);
                    return Task.CompletedTask;
                }
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Retry attempt failed for request {RequestId}", request.Id);
        }
    }

    private void UpdateStorageRequest(FailedRequest request, RuleRetry retry)
    {
        // Update retry information
        var now = _clock.UtcNow;
        var nextAttempt = request with
        {
            AttemptCount = request.AttemptCount + 1,
            LastAttempt = now,
            NextAttempt = now.Add(request.AttemptCount.CalculateExponentialDelay(Constants.RetryIntervalMin, Constants.RetryIntervalMax))
        };

        if (nextAttempt.NextAttempt > request.FirstAttempt.Add(GetValidExpiry(retry)))
        {
            _logger.LogInformation("Removing request {requestId} with event {eventName} after {attempts} attempts from storage as it has expired", request.Id, request.Rule.Event, request.AttemptCount);
            _storage.Remove(request.Id);
        }
        else
        {
            _logger.LogInformation("Re-adding request {requestId} with event {eventName} to storage for attempt {attempts} to be executed at {attemptTime}", request.Id, request.Rule.Event, nextAttempt.AttemptCount, nextAttempt.NextAttempt);
            _storage.Store(nextAttempt);
        }
    }

    // Load and process all pending requests once
    protected async Task ProcessPendingAsync(DateTimeOffset asOf, CancellationToken stoppingToken)
    {
        var pendingNow = _storage.GetRequestsDue(asOf);
        if (pendingNow.Any())
        {
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = _maxConcurrency,
                CancellationToken = stoppingToken
            };
            await Parallel.ForEachAsync(pendingNow, parallelOptions, async (request, token) =>
            {
                await ProcessRequestAsync(request);
            });
        }
    }

    private void OnStorageUpdated(object? sender, EventArgs e)
    {
        _waitTokenSource.Cancel();
    }

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        if (!_backgroundMonitoringEnabled)
        {
            _logger.LogInformation("Retry background monitoring is not enabled");
            return;
        }
        var maxWait = Constants.RetryIntervalMax;
        var nextAttempt = DateTimeOffset.MaxValue;
        int currentHash = 0;
        _storage.StorageUpdated -= OnStorageUpdated;
        _storage.StorageUpdated += OnStorageUpdated;

        while (!stoppingToken.IsCancellationRequested)
        {
            var prevHash = currentHash;

            var waitTokenSource = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var prevWaitTokenSource = Interlocked.Exchange(ref _waitTokenSource, waitTokenSource);
            var prevWaitCancellationRequested = prevWaitTokenSource.IsCancellationRequested;
            prevWaitTokenSource?.Dispose();

            var executionTime = _clock.UtcNow;
            currentHash = _storage.StorageHash;

            if (currentHash != prevHash || nextAttempt <= executionTime || waitTokenSource.IsCancellationRequested || prevWaitCancellationRequested)
            {
                // Storage has changed or next attempt is due, process any requests that are due now
                await ProcessPendingAsync(executionTime, stoppingToken);

                try
                {
                    // Get all requests to compute the nearest NextAttempt in the future
                    var notDue = _storage.GetAllRequests();
                    var next = notDue
                        .OrderBy(r => r.NextAttempt)
                        .FirstOrDefault();
                    nextAttempt = next?.NextAttempt ?? DateTimeOffset.MaxValue;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to compute next retry time from storage. Falling back to periodic wake.");
                }
            }

            var currentTime = _clock.UtcNow;
            TimeSpan? waitUntil = null;
            if (nextAttempt != DateTimeOffset.MaxValue)
            {
                waitUntil = nextAttempt - currentTime;
                if (waitUntil < TimeSpan.Zero)
                {
                    waitUntil = TimeSpan.Zero;
                }
            }

            var delay = waitUntil.HasValue
                ? (waitUntil.Value < maxWait ? waitUntil.Value : maxWait)
                : maxWait;

            var delayTask = _timeDelayService.DelayAsync(delay, waitTokenSource.Token);
            await delayTask.IgnoreCancellation();
        }
        _storage.StorageUpdated -= OnStorageUpdated;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunAsync(stoppingToken);
    }

    private static TimeSpan GetValidExpiry(RuleRetry retry)
    {
        return retry.Expiry > Constants.RetryExpiry ? Constants.RetryExpiry : retry.Expiry;
    }
}
