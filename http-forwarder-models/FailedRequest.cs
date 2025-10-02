namespace http_forwarder_app.Models;

public record class FailedRequest(
    Guid Id,
    ForwardingRule Rule,
    string RequestHostUrl,
    DateTimeOffset FirstAttempt,
    DateTimeOffset LastAttempt,
    int AttemptCount,
    DateTimeOffset NextAttempt,
    string? LastError
);
