using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace http_forwarder_app.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PingController : ControllerBase
{
    [HttpGet]
    public Task<Pong> PongAsync()
    {
        return Task.FromResult(new Pong("Pong"));
    }

    [HttpPost]
    public Task<IActionResult> PongAsync(Ping? ping)
    {
        if (ping == null) return Task.FromResult<IActionResult>(Ok(new Pong("Pong")));
        if (string.Equals(ping.Message, "fail", System.StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<IActionResult>(StatusCode(StatusCodes.Status503ServiceUnavailable, "Service temporarily unavailable"));
        }
        return Task.FromResult<IActionResult>(Ok(new Pong(ping.Message)));
    }

    public record class Pong(string Message);
    public record class Ping(string Message);
}
