using System.IO;
using System.Net.Mime;
using System.Threading.Tasks;
using http_forwarder_app.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OneOf;

namespace http_forwarder_app.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [Route("api/[controller]")]
    [Route("forward")]
    [Route("api/forward")]
    public class ForwardingController(ForwardingService forwardingService) : ControllerBase
    {
        private readonly ForwardingService _forwardingService = forwardingService;

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
                async callResp => await HttpContext.CopyHttpResponse(callResp),
                async noRuleFound =>
                {
                    Response.StatusCode = StatusCodes.Status404NotFound;
                    await Response.WriteAsync($"Rule not found for event {eventName} and method {method}");
                }
            );
        }

        /// <summary>
        /// Post method can take body also
        /// </summary>
        [HttpPost]
        [Consumes(MediaTypeNames.Text.Plain, MediaTypeNames.Application.Json, MediaTypeNames.Image.Jpeg, MediaTypeNames.Application.Octet, MediaTypeNames.Application.Zip, MediaTypeNames.Image.Tiff, MediaTypeNames.Text.Html, MediaTypeNames.Text.RichText, MediaTypeNames.Text.Xml)]
        [Route("{eventName}")]
        public async Task Post(string eventName)
        {
            string method = Request.Method;

            var requestContent = await GetBodyFromHttpRequest(Request) ?? string.Empty;

            var result = await _forwardingService.ProcessPostEvent(eventName, GetHostUrl(Request), requestContent);

            await result.Match(
                async callResp => await HttpContext.CopyHttpResponse(callResp),
                async noRuleFound =>
                {
                    Response.StatusCode = StatusCodes.Status404NotFound;
                    await Response.WriteAsync($"Rule not found for event {eventName} and method {method}");
                },
                async noBodyFound =>
                {
                    Response.StatusCode = StatusCodes.Status400BadRequest;
                    await Response.WriteAsync($"Body not found for event {eventName} and method {method}");
                }
            );
        }

        /// <summary>
        /// Put method can take body also
        /// </summary>
        [HttpPut]
        [Route("{eventName}")]
        public async Task Put(string eventName)
        {
            string method = Request.Method;

            var requestContent = await GetBodyFromHttpRequest(Request) ?? string.Empty;

            var result = await _forwardingService.ProcessPutEvent(eventName, GetHostUrl(Request), requestContent);

            await result.Match(
                async callResp => await HttpContext.CopyHttpResponse(callResp),
                async noRuleFound =>
                {
                    Response.StatusCode = StatusCodes.Status404NotFound;
                    await Response.WriteAsync($"Rule not found for event {eventName} and method {method}");
                },
                async noBodyFound =>
                {
                    Response.StatusCode = StatusCodes.Status400BadRequest;
                    await Response.WriteAsync($"Body not found for event {eventName} and method {method}");
                }
            );
        }

        [HttpDelete]
        [Route("{eventName}")]
        public async Task Delete(string eventName)
        {
            string method = Request.Method;

            var result = await _forwardingService.ProcessDeleteEvent(eventName, GetHostUrl(Request));
            await result.Match(
                async callResp => await HttpContext.CopyHttpResponse(callResp),
                async noRuleFound =>
                {
                    Response.StatusCode = StatusCodes.Status404NotFound;
                    await Response.WriteAsync($"Rule not found for event {eventName} and method {method}");
                }
            );
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

        private static string GetHostUrl(HttpRequest request)
        {
            return $"{request.Scheme}://{request.Host}";
        }
    }
}
