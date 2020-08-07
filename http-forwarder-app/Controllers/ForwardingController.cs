using System.IO;
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
    public class ForwardingController : ControllerBase
    {
        private readonly ILogger<ForwardingController> _logger;

        public ForwardingController(ILogger<ForwardingController> logger, ForwardingRulesReader rulesReader, AppState appState, IRestClient restClient)
        {
            _logger = logger;
            RulesReader = rulesReader;
            RestClient = restClient;
            AppState = appState;
        }

        private AppState AppState { get; }
        private ForwardingRulesReader RulesReader { get; }
        private IRestClient RestClient { get; }

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
            _logger.LogDebug($"{method} called with event {eventName}");
            _logger.LogDebug($"Found {AppState.Rules.Length} rules");
            if (AppState.Rules.Length > 0)
            {
                _logger.LogDebug($"First rule - Event: {AppState.Rules[0].Event}, Method: {AppState.Rules[0].Method}, TargetUrl: {AppState.Rules[0].TargetUrl}");
            }
            var fwdRule = RulesReader.Find(method, eventName);
            if (fwdRule == null)
            {
                _logger.LogWarning($"{method} for event {eventName} does not match any rules");
                return;
            }
            var callResp = await RestClient.MakeGetCall(eventName, fwdRule.TargetUrl);
            await HttpContext.CopyHttpResponse(callResp);
        }

        /// <summary>
        /// Post method can take body also
        /// </summary>
        [HttpPost]
        [Route("{eventName}")]
        public async Task Post(string eventName, [FromBody] dynamic requestBody)
        {
            const string method = "POST";
            var fwdRule = RulesReader.Find(method, eventName);
            if (fwdRule == null)
            {
                _logger.LogWarning($"{method} for event {eventName} does not match any rules");
                return;
            }
            var body = await GetBodyFromHttpRequest(HttpContext.Request);
            _logger.LogDebug($"{method} called with event {eventName} and body {body}");
            if (string.IsNullOrEmpty(body))
            {
                _logger.LogWarning($"Body can't be null");
            }
            var callResp = await RestClient.MakePostCall(eventName, fwdRule.TargetUrl, body);
            await HttpContext.CopyHttpResponse(callResp);
        }

        /// <summary>
        /// Put method can take body also
        /// </summary>
        [HttpPut]
        [Route("{eventName}")]
        public async Task Put(string eventName, [FromBody] dynamic requestBody)
        {
            const string method = "PUT";
            var fwdRule = RulesReader.Find(method, eventName);
            if (fwdRule == null)
            {
                _logger.LogWarning($"{method} for event {eventName} does not match any rules");
                return;
            }
            var body = await GetBodyFromHttpRequest(HttpContext.Request);
            _logger.LogDebug($"{method} called with event {eventName} and body {body}");
            if (string.IsNullOrEmpty(body))
            {
                _logger.LogWarning($"Body can't be null");
            }
            var callResp = await RestClient.MakePutCall(eventName, fwdRule.TargetUrl, body);
            await HttpContext.CopyHttpResponse(callResp);
        }

        [HttpDelete]
        [Route("{eventName}")]
        public async Task Delete(string eventName)
        {
            const string method = "DELETE";
            _logger.LogDebug($"{method} called with event {eventName}");
            var fwdRule = RulesReader.Find(method, eventName);
            if (fwdRule == null)
            {
                _logger.LogWarning($"{method} for event {eventName} does not match any rules");
                return;
            }
            var callResp = await RestClient.MakeDeleteCall(eventName, fwdRule.TargetUrl);
            await HttpContext.CopyHttpResponse(callResp);
        }

        private Task<string> GetBodyFromHttpRequest(HttpRequest request)
        {
            var bodyStream = request?.Body;
            if (bodyStream != null)
            {
                TextReader tr = new StreamReader(bodyStream);
                return tr.ReadToEndAsync();
            }
            return null;
        }
    }
}
