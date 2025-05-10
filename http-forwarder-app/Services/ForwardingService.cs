using System.Net.Http;
using http_forwarder_app.Core;
using http_forwarder_app.Models;
using Microsoft.Extensions.Logging;
using OneOf;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System;

namespace http_forwarder_app.Services;

public class ForwardingService
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

    public Task<OneOf<HttpResponseMessage, NoMatchingRuleResult, RemoteRuleFoundResult>> ProcessGetEvent(string eventName, string? requestHostUrl)
    {
        return ProcessGetOrDeleteEvent(HttpMethods.Get, eventName, requestHostUrl);
    }

    public Task<OneOf<HttpResponseMessage, NoMatchingRuleResult, NoBodyRuleResult, RemoteRuleFoundResult>> ProcessPostEvent(string eventName, string? requestHostUrl, string requestContent)
    {
        return ProcessPostOrPutEvent(HttpMethods.Post, eventName, requestHostUrl, requestContent);
    }

    public Task<OneOf<HttpResponseMessage, NoMatchingRuleResult, NoBodyRuleResult, RemoteRuleFoundResult>> ProcessPutEvent(string eventName, string? requestHostUrl, string requestContent)
    {
        return ProcessPostOrPutEvent(HttpMethods.Put, eventName, requestHostUrl, requestContent);
    }

    public Task<OneOf<HttpResponseMessage, NoMatchingRuleResult, RemoteRuleFoundResult>> ProcessDeleteEvent(string eventName, string? requestHostUrl)
    {
        return ProcessGetOrDeleteEvent(HttpMethods.Delete, eventName, requestHostUrl);
    }

    private async Task<OneOf<HttpResponseMessage, NoMatchingRuleResult, NoBodyRuleResult, RemoteRuleFoundResult>> ProcessPostOrPutEvent(string method, string eventName, string? requestHostUrl, string requestContent)
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
        _logger.LogDebug("{method} called with event {eventName} and body {body}", method, eventName, body);
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
                    ? RestClient.MakePostCall(eventName, targetUrl, body, fwdRule.Headers, fwdRule.IgnoreSslError)
                    : RestClient.MakePutCall(eventName, targetUrl, body, fwdRule.Headers, fwdRule.IgnoreSslError);
        return await call;
    }

    private async Task<OneOf<HttpResponseMessage, NoMatchingRuleResult, RemoteRuleFoundResult>> ProcessGetOrDeleteEvent(string method, string eventName, string? requestHostUrl)
    {
        if (method != HttpMethods.Get && method != HttpMethods.Delete) throw new ArgumentException($"Method {method} is not supported here", nameof(method));
        _logger.LogDebug("{method} called with event {eventName}", method, eventName);
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
                    ? RestClient.MakeGetCall(eventName, targetUrl, fwdRule.Headers, fwdRule.IgnoreSslError)
                    : RestClient.MakeDeleteCall(eventName, targetUrl, fwdRule.Headers, fwdRule.IgnoreSslError);
        return await call;
    }

    private static string GetValidTargetUrl(ForwardingRule rule, string? requestHostUrl)
    {
        if (rule.TargetUrl != null && !rule.TargetUrl.StartsWith("http", System.StringComparison.Ordinal))
        {
            return $"{requestHostUrl}{rule.TargetUrl}";
        }
        return rule.TargetUrl ?? string.Empty;
    }
}