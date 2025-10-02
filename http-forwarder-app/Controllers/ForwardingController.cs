using System;
using System.IO;
using System.Threading.Tasks;
using http_forwarder_app.Core;
using http_forwarder_app.Models;
using http_forwarder_app.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using OneOf;

namespace http_forwarder_app.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [Route("api/[controller]")]
    [Route("forward")]
    [Route("api/forward")]
    public class ForwardingController(
        IForwardingService forwardingService,
        RemoteRulePublishingService remoteRulePublishingService,
        IFailedRequestStorage failedRequestStorage,
        IConfiguration configuration,
        ISystemClock clock,
        ILogger<ForwardingController> logger) : ControllerBase
    {
        private readonly IForwardingService _forwardingService = forwardingService;
        private readonly IFailedRequestStorage _failedRequestStorage = failedRequestStorage;
        private readonly IConfiguration _configuration = configuration;
        private readonly ILogger<ForwardingController> _logger = logger;
        private readonly RemoteRulePublishingService _remoteRulePublishingService = remoteRulePublishingService;
        private readonly ISystemClock _clock = clock;

        [HttpGet]
        public object Get()
        {
            return new { Message = "Hello, I am running" };
        }

        [HttpGet]
        [Route("{eventName}")]
        public async Task Get(string eventName)
        {
            string method = Request.Method;
            var result = await _forwardingService.ProcessGetEvent(eventName, GetHostUrl(Request));
            await result.Match(
                async ruleResult => await HttpContext.CopyHttpResponse(ruleResult.Response),
                async noRuleFound =>
                {
                    Response.StatusCode = StatusCodes.Status404NotFound;
                    await Response.WriteAsync($"Rule not found for event {eventName} and method {method}");
                },
                async remoteRuleFound =>
                {
                    Response.StatusCode = StatusCodes.Status404NotFound;
                    await Response.WriteAsync($"Rule not found for event {eventName}, method {method} and location {_configuration.GetLocationTag()}");
                }
            );
        }

        /// <summary>
        /// Forward a POST request to configured endpoint
        /// </summary>
        /// <param name="eventName">Event name to match forwarding rule</param>
        /// <param name="body">Request body (shown in Swagger). Raw body will still be used for processing.</param>
        [HttpPost("{eventName}")]
        public async Task Post(string eventName, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] object? body = null)
        {
            string method = Request.Method;
            Request.EnableBuffering();
            var requestContent = await ReadRequestBody(Request);
            var result = await _forwardingService.ProcessPostEvent(eventName, GetHostUrl(Request), requestContent);

            await result.Match(
                async ruleResult =>
                {
                    if (!ruleResult.Response.IsSuccessStatusCode && IsServerError(ruleResult.Response.StatusCode))
                    {
                        if (ruleResult.Rule.IsRetryable)
                        {
                            await HandleFailedRequest(ruleResult.Rule, requestContent, await ruleResult.Response.Content.ReadAsStringAsync());
                            return;
                        }
                    }
                    await HttpContext.CopyHttpResponse(ruleResult.Response);
                },
                async noRuleFound =>
                {
                    Response.StatusCode = StatusCodes.Status404NotFound;
                    await Response.WriteAsync($"Rule not found for event {eventName} and method {method}");
                },
                async noBodyFound =>
                {
                    Response.StatusCode = StatusCodes.Status400BadRequest;
                    await Response.WriteAsync($"Body not found for event {eventName} and method {method}");
                },
                async remoteRuleFound =>
                {
                    await HandleRemoteRule(remoteRuleFound.RemoteRule, requestContent);
                }
            );
        }

        /// <summary>
        /// Forward a PUT request to configured endpoint
        /// </summary>
        [HttpPut("{eventName}")]
        public async Task Put(string eventName, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] object? body = null)
        {
            string method = Request.Method;
            Request.EnableBuffering();
            var requestContent = await ReadRequestBody(Request);
            var result = await _forwardingService.ProcessPutEvent(eventName, GetHostUrl(Request), requestContent);

            await result.Match(
                async ruleResult =>
                {
                    if (!ruleResult.Response.IsSuccessStatusCode && IsServerError(ruleResult.Response.StatusCode))
                    {
                        if (ruleResult.Rule.IsRetryable)
                        {
                            await HandleFailedRequest(ruleResult.Rule, requestContent, await ruleResult.Response.Content.ReadAsStringAsync());
                            return;
                        }
                    }
                    await HttpContext.CopyHttpResponse(ruleResult.Response);
                },
                async noRuleFound =>
                {
                    Response.StatusCode = StatusCodes.Status404NotFound;
                    await Response.WriteAsync($"Rule not found for event {eventName} and method {method}");
                },
                async noBodyFound =>
                {
                    Response.StatusCode = StatusCodes.Status400BadRequest;
                    await Response.WriteAsync($"Body not found for event {eventName} and method {method}");
                },
                async remoteRuleFound =>
                {
                    await HandleRemoteRule(remoteRuleFound.RemoteRule, requestContent);
                }
            );
        }

        /// <summary>
        /// Forward a DELETE request to configured endpoint
        /// </summary>
        [HttpDelete("{eventName}")]
        public async Task Delete(string eventName, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] object? body = null)
        {
            string method = Request.Method;
            Request.EnableBuffering();
            var requestContent = await ReadRequestBody(Request);
            var result = await _forwardingService.ProcessDeleteEvent(eventName, GetHostUrl(Request));
            await result.Match(
                async ruleResult =>
                {
                    if (!ruleResult.Response.IsSuccessStatusCode && IsServerError(ruleResult.Response.StatusCode))
                    {
                        if (ruleResult.Rule.IsRetryable)
                        {
                            await HandleFailedRequest(ruleResult.Rule, null, await ruleResult.Response.Content.ReadAsStringAsync());
                            return;
                        }
                    }
                    await HttpContext.CopyHttpResponse(ruleResult.Response);
                },
                async noRuleFound =>
                {
                    Response.StatusCode = StatusCodes.Status404NotFound;
                    await Response.WriteAsync($"Rule not found for event {eventName} and method {method}");
                },
                async remoteRuleFound =>
                {
                    Response.StatusCode = StatusCodes.Status404NotFound;
                    await Response.WriteAsync($"Rule not found for event {eventName}, method {method} and location {_configuration.GetLocationTag()}");
                }
            );
        }

        private async Task HandleRemoteRule(ForwardingRule remoteRule, string requestContent)
        {
            if (!_configuration.IsPublisherEnabled())
            {
                _logger.LogWarning("Request can not be processed by this system - {rule}", remoteRule.ToMinimal());
                Response.StatusCode = StatusCodes.Status406NotAcceptable;
                await Response.WriteAsync("Request can not be processed by this system");
                return;
            }
            ForwardingRequest forwardingRequest = new(Method: remoteRule.Method, Event: remoteRule.Event, Content: requestContent);
            var publishResult = await _remoteRulePublishingService.Publish(forwardingRequest, remoteRule);
            publishResult.Switch(
                success =>
                {
                    Response.StatusCode = StatusCodes.Status202Accepted;
                    Response.WriteAsync($"Request will be processed by another system, published successfully with message Id {success.MessageId}");
                },
                failure =>
                {
                    Response.StatusCode = StatusCodes.Status500InternalServerError;
                    Response.WriteAsync($"Request could not be published to be processed by another system - {failure.ErrorMessage}");
                }
            );
        }

        private async Task HandleFailedRequest(ForwardingRule rule, string? content, string error)
        {
            var creationTime = _clock.UtcNow;
            var failedRequest = new FailedRequest(
                Id: Guid.NewGuid(),
                Rule: rule with { Content = content },
                RequestHostUrl: GetHostUrl(Request),
                FirstAttempt: creationTime,
                LastAttempt: creationTime,
                AttemptCount: 1,
                NextAttempt: creationTime.AddSeconds(30),
                LastError: error
            );

            _failedRequestStorage.Store(failedRequest);
            Response.StatusCode = StatusCodes.Status202Accepted;
            await Response.WriteAsync("Request accepted for retry");
        }

        private static bool IsServerError(System.Net.HttpStatusCode statusCode) =>
            (int)statusCode >= 500 && (int)statusCode <= 599;

        private static string GetHostUrl(HttpRequest request)
        {
            return $"{request.Scheme}://{request.Host}";
        }

        // helper to read the raw body; leaves stream position reset for other readers
        private static async Task<string> ReadRequestBody(HttpRequest request)
        {
            request.Body.Position = 0;
            using var reader = new StreamReader(request.Body, leaveOpen: true);
            var content = await reader.ReadToEndAsync().ConfigureAwait(false);
            request.Body.Position = 0;
            return content;
        }
    }
}
