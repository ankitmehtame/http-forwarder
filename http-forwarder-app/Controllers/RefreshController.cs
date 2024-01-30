using System.Threading.Tasks;
using http_forwarder_app.Core;
using Microsoft.AspNetCore.Mvc;

namespace http_forwarder_app;

[ApiController]
[Route("api/[controller]")]
public class RefreshController(ForwardingRulesReader forwardingRulesReader)
{
    [HttpPost]
    [Route("")]
    public async Task Refresh()
    {
        await Task.Yield();
        forwardingRulesReader.Read();
    }
}
