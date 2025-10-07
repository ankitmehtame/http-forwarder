using System.ComponentModel;

namespace http_forwarder_app.Models;

public record class FailedRequest(
    Guid Id,
    ForwardingRuleMinimal Rule,
    string RequestHostUrl,
    [property: DefaultValue("")]
    string RequestBody,
    DateTimeOffset FirstAttempt,
    DateTimeOffset LastAttempt,
    int AttemptCount,
    DateTimeOffset NextAttempt,
    string? LastError
);
