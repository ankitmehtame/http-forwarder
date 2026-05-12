using http_forwarder_app.Core;
using http_forwarder_app.Models;
using http_forwarder_app.Utils;
using Microsoft.Extensions.Logging;
using OneOf;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;

namespace http_forwarder_app.Services;

public class ForwardingService : IForwardingService
{
    private readonly ILogger<ForwardingService> _logger;
    private AppState AppState { get; init; }
    private ForwardingRulesReader RulesReader { get; init; }
    private IRestClient RestClient { get; init; }

    public ForwardingService(ILogger<ForwardingService> logger, ForwardingRulesReader rulesReader, AppState appState, IRestClient restClient)
    {
        _logger = logger;
        RulesReader = rulesReader;
        RestClient = restClient;
        AppState = appState;
    }

    public Task<OneOf<HttpResponseRuleResult, NoMatchingRuleResult, RemoteRuleFoundResult>> ProcessGetEvent(string eventName, string? requestHostUrl, IDictionary<string, string> requestHeaders)
    {
        return ProcessGetOrDeleteEvent(HttpMethods.Get, eventName, requestHostUrl, requestHeaders);
    }

    public Task<OneOf<HttpResponseRuleResult, NoMatchingRuleResult, NoBodyRuleResult, RemoteRuleFoundResult>> ProcessPostEvent(string eventName, string? requestHostUrl, string requestContent, IDictionary<string, string> requestHeaders)
    {
        return ProcessPostOrPutEvent(HttpMethods.Post, eventName, requestHostUrl, requestContent, requestHeaders);
    }

    public Task<OneOf<HttpResponseRuleResult, NoMatchingRuleResult, NoBodyRuleResult, RemoteRuleFoundResult>> ProcessPutEvent(string eventName, string? requestHostUrl, string requestContent, IDictionary<string, string> requestHeaders)
    {
        return ProcessPostOrPutEvent(HttpMethods.Put, eventName, requestHostUrl, requestContent, requestHeaders);
    }

    public Task<OneOf<HttpResponseRuleResult, NoMatchingRuleResult, RemoteRuleFoundResult>> ProcessDeleteEvent(string eventName, string? requestHostUrl, IDictionary<string, string> requestHeaders)
    {
        return ProcessGetOrDeleteEvent(HttpMethods.Delete, eventName, requestHostUrl, requestHeaders);
    }

    private async Task<OneOf<HttpResponseRuleResult, NoMatchingRuleResult, NoBodyRuleResult, RemoteRuleFoundResult>> ProcessPostOrPutEvent(string method, string eventName, string? requestHostUrl, string requestContent, IDictionary<string, string> requestHeaders)
    {
        if (method != HttpMethods.Post && method != HttpMethods.Put) throw new ArgumentException($"Method {method} is not supported here", nameof(method));
        var fwdRule = RulesReader.Find(method, eventName);
        if (fwdRule == null)
        {
            var remoteRule = RulesReader.FindRemote(method, eventName);
            if (remoteRule != null)
            {
                return new RemoteRuleFoundResult(remoteRule);
            }
            _logger.LogWarning("{method} for event {eventName} does not match any rules", method, eventName);
            return NoMatchingRuleResult.Instance;
        }
        var body = requestContent;
        _logger.LogDebug("{method} called with event {eventName}, body {body} and headers {headers}",
            method,
            eventName,
            body?.TrimEnd(),
            RulesReader.Configuration.CreatePrettyDictionary(requestHeaders));
        if (string.IsNullOrEmpty(body) && fwdRule.HasContent)
        {
            _logger.LogWarning($"Body can't be null");
            return NoBodyRuleResult.Instance;
        }
        if (fwdRule.Content != null)
        {
            body = fwdRule.Content;
        }
        var targetUrl = GetValidTargetUrl(fwdRule, requestHostUrl);
        var call = method == HttpMethods.Post
                    ? RestClient.MakePostCall(
                        eventName: eventName,
                        targetUrl: targetUrl,
                        content: body,
                        headers: fwdRule.MergeHeaders(requestHeaders, _logger),
                        ignoreSslError: fwdRule.IgnoreSslError)
                    : RestClient.MakePutCall(
                        eventName: eventName,
                        targetUrl: targetUrl,
                        content: body,
                        headers: fwdRule.MergeHeaders(requestHeaders, _logger),
                        ignoreSslError: fwdRule.IgnoreSslError);
        var response = await call;
        return new HttpResponseRuleResult(response, fwdRule);
    }

    private async Task<OneOf<HttpResponseRuleResult, NoMatchingRuleResult, RemoteRuleFoundResult>> ProcessGetOrDeleteEvent(string method, string eventName, string? requestHostUrl, IDictionary<string, string> requestHeaders)
    {
        if (method != HttpMethods.Get && method != HttpMethods.Delete) throw new ArgumentException($"Method {method} is not supported here", nameof(method));
        _logger.LogDebug("{method} called with event {eventName} and {headers}",
            method,
            eventName,
            RulesReader.Configuration.CreatePrettyDictionary(requestHeaders));
        _logger.LogDebug("Found {rulesCount} rules", AppState.Rules.Length);
        if (AppState.Rules.Length > 0)
        {
            _logger.LogDebug("First rule - Event: {firstEventName}, Method: {firstMethod}, TargetUrl: {firstTargetUrl}", AppState.Rules[0].Event, AppState.Rules[0].Method, AppState.Rules[0].TargetUrl);
        }
        var fwdRule = RulesReader.Find(method, eventName);
        if (fwdRule == null)
        {
            var remoteRule = RulesReader.FindRemote(method, eventName);
            if (remoteRule != null)
            {
                return new RemoteRuleFoundResult(remoteRule);
            }
            _logger.LogWarning("{method} for event {eventName} does not match any rules", method, eventName);
            return NoMatchingRuleResult.Instance;
        }
        var targetUrl = GetValidTargetUrl(fwdRule, requestHostUrl);
        var call = method == HttpMethods.Get
                    ? RestClient.MakeGetCall(eventName, targetUrl, fwdRule.MergeHeaders(requestHeaders, _logger), fwdRule.IgnoreSslError)
                    : RestClient.MakeDeleteCall(eventName, targetUrl, fwdRule.MergeHeaders(requestHeaders, _logger), fwdRule.IgnoreSslError);
        var response = await call;
        return new HttpResponseRuleResult(response, fwdRule);
    }

    private static string GetValidTargetUrl(ForwardingRule rule, string? requestHostUrl)
    {
        if (rule.TargetUrl != null && !rule.TargetUrl.StartsWith("http", StringComparison.Ordinal))
        {
            return $"{requestHostUrl}{rule.TargetUrl}";
        }
        return rule.TargetUrl ?? string.Empty;
    }
}
