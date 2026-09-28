using System.Globalization;
using System.Collections.Immutable;
using http_forwarder_app.Core;
using http_forwarder_app.Models;
using http_forwarder_app.Utils;
using OneOf;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;

namespace http_forwarder_app.Services;

public sealed class ForwardingOrchestrator(
    IForwardingService forwardingService,
    RemoteRulePublishingService publishingService,
    IFailedRequestStorage storage,
    IConfiguration configuration,
    ISystemClock clock,
    ILogger<ForwardingOrchestrator> logger)
{
    public async Task<ForwardingOutcome> ForwardAsync(string method, string eventName, string? body,
        IDictionary<string, string> headers, string? requestBaseUrl, CancellationToken cancellationToken = default, int? maxRetryErrorBytes = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedMethod = method.ToUpperInvariant();
        OneOf<HttpResponseRuleResult, NoMatchingRuleResult, NoBodyRuleResult, RemoteRuleFoundResult> result;
        switch (normalizedMethod)
        {
            case "GET":
                result = (await forwardingService.ProcessGetEvent(eventName, requestBaseUrl, headers)).Match<OneOf<HttpResponseRuleResult, NoMatchingRuleResult, NoBodyRuleResult, RemoteRuleFoundResult>>(r => r, n => n, remote => remote);
                break;
            case "POST": result = await forwardingService.ProcessPostEvent(eventName, requestBaseUrl, body ?? string.Empty, headers); break;
            case "PUT": result = await forwardingService.ProcessPutEvent(eventName, requestBaseUrl, body ?? string.Empty, headers); break;
            case "DELETE":
                result = (await forwardingService.ProcessDeleteEvent(eventName, requestBaseUrl, headers)).Match<OneOf<HttpResponseRuleResult, NoMatchingRuleResult, NoBodyRuleResult, RemoteRuleFoundResult>>(r => r, n => n, remote => remote);
                break;
            default: throw new ArgumentException($"Unsupported method {method}", nameof(method));
        }

        return await result.Match<Task<ForwardingOutcome>>(
            async response =>
            {
                if (response.Response.IsServerError() && response.Rule.Retry.Allow && normalizedMethod is "POST" or "PUT" or "DELETE")
                {
                    string error;
                    try
                    {
                        if (maxRetryErrorBytes is null) error = await response.Response.Content.ReadAsStringAsync(cancellationToken);
                        else
                        {
                            await using var stream = await response.Response.Content.ReadAsStreamAsync(cancellationToken);
                            var bytes = new byte[maxRetryErrorBytes.Value];
                            var count = 0;
                            while (count < bytes.Length)
                            {
                                var read = await stream.ReadAsync(bytes.AsMemory(count), cancellationToken);
                                if (read == 0) break;
                                count += read;
                            }
                            error = System.Text.Encoding.UTF8.GetString(bytes, 0, count);
                        }
                    }
                    catch { response.Response.Dispose(); throw; }
                    var now = clock.UtcNow;
                    var failedRequest = new FailedRequest(
                        Id: Guid.NewGuid(), Rule: response.Rule.ToMinimal(), RequestHostUrl: requestBaseUrl ?? string.Empty,
                        RequestBody: body ?? string.Empty, RequestHeaders: headers.ToImmutableSortedDictionary(StringComparer.OrdinalIgnoreCase),
                        FirstAttempt: now, LastAttempt: now, AttemptCount: 1, NextAttempt: now.Add(Constants.RetryIntervalMin), LastError: error);
                    try { storage.Store(failedRequest); }
                    finally { response.Response.Dispose(); }
                    logger.LogInformation("Stored failed request {requestId} with event {eventName} for retry at {attemptTime}", failedRequest.Id, response.Rule.Event, failedRequest.NextAttempt);
                    return new ForwardingOutcome(ForwardingOutcomeKind.Response,
                        new HttpResponseMessage(System.Net.HttpStatusCode.Accepted)
                        { Content = new StringContent(string.Format(CultureInfo.InvariantCulture, "Request {0} accepted for retry - {1} at {2}", response.Rule.Event, failedRequest.Id, failedRequest.FirstAttempt.ToLocalTime())) },
                        retryId: failedRequest.Id, rule: response.Rule);
                }
                return new ForwardingOutcome(ForwardingOutcomeKind.Response, response.Response, rule: response.Rule);
            },
            _ => Task.FromResult(new ForwardingOutcome(ForwardingOutcomeKind.NoMatchingRule)),
            _ => Task.FromResult(new ForwardingOutcome(ForwardingOutcomeKind.NoBody)),
            async remote =>
            {
                if (normalizedMethod is "GET" or "DELETE") return new ForwardingOutcome(ForwardingOutcomeKind.RemoteRule, remoteRule: remote.RemoteRule);
                if (!configuration.IsPublisherEnabled())
                {
                    logger.LogWarning("Cannot publish remote rule {remoteRule} for event {eventName} because publishing is disabled", remote.RemoteRule.ToMinimal(), eventName);
                    return new ForwardingOutcome(ForwardingOutcomeKind.RemoteRule, new HttpResponseMessage(System.Net.HttpStatusCode.NotAcceptable) { Content = new StringContent("Request can not be processed by this system") }, remoteRule: remote.RemoteRule);
                }
                var publish = await publishingService.Publish(new ForwardingRequest(normalizedMethod, eventName, body ?? string.Empty, headers.ToImmutableSortedDictionary(StringComparer.OrdinalIgnoreCase)), remote.RemoteRule);
                return publish.Match(
                    success => new ForwardingOutcome(ForwardingOutcomeKind.RemoteRule, new HttpResponseMessage(System.Net.HttpStatusCode.Accepted) { Content = new StringContent($"Request will be processed by another system, published successfully with message Id {success.MessageId}") }, messageId: success.MessageId, remoteRule: remote.RemoteRule),
                    failure => new ForwardingOutcome(ForwardingOutcomeKind.RemoteRule, new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError) { Content = new StringContent($"Request could not be published to be processed by another system - {failure.ErrorMessage}") }, remoteRule: remote.RemoteRule));
            });
    }
}
