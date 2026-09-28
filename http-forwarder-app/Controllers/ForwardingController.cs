using System.Collections.Immutable;
using System.Globalization;
using http_forwarder_app.Core;
using http_forwarder_app.Models;
using http_forwarder_app.Services;
using http_forwarder_app.Utils;
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
        ForwardingOrchestrator orchestrator,
        IConfiguration configuration,
        ILogger<ForwardingController> logger) : ControllerBase
    {
        private readonly ForwardingOrchestrator _orchestrator = orchestrator;
        private readonly IConfiguration _configuration = configuration;
        private readonly ILogger<ForwardingController> _logger = logger;

        [HttpGet]
        public object Get()
        {
            return new { Message = "Hello, I am running" };
        }

        [HttpGet]
        [Route("{eventName}")]
        public async Task Get(string eventName)
        {
            await Forward(eventName, null);
        }

        /// <summary>
        /// Forward a POST request to configured endpoint
        /// </summary>
        /// <param name="eventName">Event name to match forwarding rule</param>
        /// <param name="body">Request body (shown in Swagger). Raw body will still be used for processing.</param>
        [HttpPost("{eventName}")]
        public async Task Post(string eventName, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] object? body = null)
        {
            await Forward(eventName, await ReadRequestBody(Request));
        }

        /// <summary>
        /// Forward a PUT request to configured endpoint
        /// </summary>
        [HttpPut("{eventName}")]
        public async Task Put(string eventName, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] object? body = null)
        {
            await Forward(eventName, await ReadRequestBody(Request));
        }

        /// <summary>
        /// Forward a DELETE request to configured endpoint
        /// </summary>
        [HttpDelete("{eventName}")]
        public async Task Delete(string eventName, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] object? body = null)
        {
            await Forward(eventName, await ReadRequestBody(Request));
        }

        private async Task Forward(string eventName, string? body)
        {
            var method = Request.Method;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
            timeout.CancelAfter(_configuration.GetOutboundHttpTimeout());
            using var outcome = await _orchestrator.ForwardAsync(method, eventName, body,
                Request.Headers.GetHeaders(), GetHostUrl(Request), timeout.Token);
            if (outcome.Response is not null)
            {
                if (outcome.RetryId is not null || outcome.Kind == ForwardingOutcomeKind.RemoteRule)
                {
                    Response.StatusCode = outcome.StatusCode!.Value;
                    await Response.WriteAsync(await outcome.Response.Content.ReadAsStringAsync(timeout.Token), timeout.Token);
                }
                else
                {
                    await ResponseUtils.CopyHttpResponse(Response, outcome.Response, timeout.Token);
                }
                return;
            }
            Response.StatusCode = outcome.Kind == ForwardingOutcomeKind.NoBody ? StatusCodes.Status400BadRequest : StatusCodes.Status404NotFound;
            var isRemoteGetOrDelete = outcome.Kind == ForwardingOutcomeKind.RemoteRule &&
                (method.Equals("GET", StringComparison.OrdinalIgnoreCase) || method.Equals("DELETE", StringComparison.OrdinalIgnoreCase));
            var message = outcome.Kind == ForwardingOutcomeKind.NoBody
                ? $"Body not found for event {eventName} and method {method}"
                : isRemoteGetOrDelete
                    ? $"Rule not found for event {eventName}, method {method} and location {_configuration.GetLocationTag()}"
                    : $"Rule not found for event {eventName} and method {method}";
            await Response.WriteAsync(message);
        }

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
