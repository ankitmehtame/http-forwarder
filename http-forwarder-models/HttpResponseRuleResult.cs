using System.Net.Http;

namespace http_forwarder_app.Models;

public record HttpResponseRuleResult(HttpResponseMessage Response, ForwardingRule Rule);
