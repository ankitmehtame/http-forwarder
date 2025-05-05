using System.Threading.Tasks;
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
    public Task<Pong> PongAsync(Ping? ping)
    {
        if (ping == null) return Task.FromResult(new Pong("Pong"));
        return Task.FromResult(new Pong(ping.Message));
    }

    public record class Pong(string Message);
    public record class Ping(string Message);
}
