using System.IO;
using System.Net.Mime;
using System.Threading.Tasks;
using http_forwarder_app.Core;
using http_forwarder_app.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace http_forwarder_app.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [Route("api/[controller]")]
    [Route("forward")]
    [Route("api/forward")]
    public class ForwardingController(ILogger<ForwardingController> logger, ForwardingRulesReader rulesReader, AppState appState, IRestClient restClient) : ControllerBase
    {
        private AppState AppState { get; } = appState;
        private ForwardingRulesReader RulesReader { get; } = rulesReader;
        private IRestClient RestClient { get; } = restClient;

        [HttpGet]
        public object Get()
        {
            return new { Message = "Hello, I am running" };
        }

        [HttpGet]
        [Route("{eventName}")]
        public async Task Get(string eventName)
        {
            const string method = "GET";
            logger.LogDebug($"{method} called with event {eventName}");
            logger.LogDebug($"Found {AppState.Rules.Length} rules");
            if (AppState.Rules.Length > 0)
            {
                logger.LogDebug($"First rule - Event: {AppState.Rules[0].Event}, Method: {AppState.Rules[0].Method}, TargetUrl: {AppState.Rules[0].TargetUrl}");
            }
            var fwdRule = RulesReader.Find(method, eventName);
            if (fwdRule == null)
            {
                logger.LogWarning($"{method} for event {eventName} does not match any rules");
                return;
            }
            var targetUrl = GetValidTargetUrl(fwdRule, Request);
            var callResp = await RestClient.MakeGetCall(eventName, targetUrl, fwdRule.Headers, fwdRule.IgnoreSslError);
            await HttpContext.CopyHttpResponse(callResp);
        }

        /// <summary>
        /// Post method can take body also
        /// </summary>
        [HttpPost]
        [Consumes(MediaTypeNames.Text.Plain, MediaTypeNames.Application.Json, MediaTypeNames.Image.Jpeg, MediaTypeNames.Application.Octet, MediaTypeNames.Application.Zip, MediaTypeNames.Image.Tiff, MediaTypeNames.Text.Html, MediaTypeNames.Text.RichText, MediaTypeNames.Text.Xml)]
        [Route("{eventName}")]
        public async Task Post(string eventName /*, [FromBody] dynamic requestBody */)
        {
            const string method = "POST";
            var fwdRule = RulesReader.Find(method, eventName);
            if (fwdRule == null)
            {
                logger.LogWarning($"{method} for event {eventName} does not match any rules");
                return;
            }
            var body = await GetBodyFromHttpRequest(HttpContext.Request);
            logger.LogDebug($"{method} called with event {eventName} and body {body}");
            if (string.IsNullOrEmpty(body) && fwdRule.HasContent)
            {
                logger.LogWarning($"Body can't be null");
                Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            if (fwdRule.Content != null)
            {
                body = fwdRule.Content;
            }
            var targetUrl = GetValidTargetUrl(fwdRule, Request);
            var callResp = await RestClient.MakePostCall(eventName, targetUrl, body, fwdRule.Headers, fwdRule.IgnoreSslError);
            await HttpContext.CopyHttpResponse(callResp);
        }

        /// <summary>
        /// Put method can take body also
        /// </summary>
        [HttpPut]
        [Route("{eventName}")]
        public async Task Put(string eventName /*, [FromBody] dynamic requestBody */)
        {
            const string method = "PUT";
            var fwdRule = RulesReader.Find(method, eventName);
            if (fwdRule == null)
            {
                logger.LogWarning($"{method} for event {eventName} does not match any rules");
                return;
            }
            var body = await GetBodyFromHttpRequest(HttpContext.Request);
            logger.LogDebug($"{method} called with event {eventName} and body {body}");
            if (string.IsNullOrEmpty(body) && fwdRule.HasContent)
            {
                logger.LogWarning($"Body can't be null");
                Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            if (fwdRule.Content != null)
            {
                body = fwdRule.Content;
            }
            var targetUrl = GetValidTargetUrl(fwdRule, Request);
            var callResp = await RestClient.MakePutCall(eventName, targetUrl, body, fwdRule.Headers, fwdRule.IgnoreSslError);
            await HttpContext.CopyHttpResponse(callResp);
        }

        [HttpDelete]
        [Route("{eventName}")]
        public async Task Delete(string eventName)
        {
            const string method = "DELETE";
            logger.LogDebug($"{method} called with event {eventName}");
            var fwdRule = RulesReader.Find(method, eventName);
            if (fwdRule == null)
            {
                logger.LogWarning($"{method} for event {eventName} does not match any rules");
                return;
            }
            var targetUrl = GetValidTargetUrl(fwdRule, Request);
            var callResp = await RestClient.MakeDeleteCall(eventName, targetUrl, fwdRule.Headers, fwdRule.IgnoreSslError);
            await HttpContext.CopyHttpResponse(callResp);
        }

        private static async Task<string?> GetBodyFromHttpRequest(HttpRequest request)
        {
            var bodyStream = request?.Body;
            if (bodyStream != null)
            {
                TextReader tr = new StreamReader(bodyStream);
                return await tr.ReadToEndAsync();
            }
            return null;
        }

        private static string GetValidTargetUrl(ForwardingRule rule, HttpRequest request)
        {
            if (rule.TargetUrl != null && !rule.TargetUrl.StartsWith("http", System.StringComparison.Ordinal))
            {
                return $"{request.Scheme}://{request.Host}{rule.TargetUrl}";
            }
            return rule.TargetUrl ?? string.Empty;
        }
    }
}
