namespace http_forwarder_app.Models;

public record class ForwardingRequest(string Method, string Event, string? Content);