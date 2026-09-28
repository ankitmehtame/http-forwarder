using http_forwarder_app.Models;
using http_forwarder_app.Models.Services;
using http_forwarder_app.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shouldly;

namespace http_forwarder_unit_tests;

public class ForwardingOrchestratorTests
{
    private readonly Mock<IForwardingService> _forwarding = new();
    private readonly Mock<IFailedRequestStorage> _storage = new();
    private readonly Mock<ISystemClock> _clock = new();

    private ForwardingOrchestrator Create(bool publisher = false)
    {
        _clock.Setup(x => x.UtcNow).Returns(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["PUBLISHER_ENABLED"] = publisher.ToString(), ["LOCATION_TAG"] = "local" }).Build();
        var publishing = new RemoteRulePublishingService(config, NullLogger<RemoteRulePublishingService>.Instance, Mock.Of<IPublishingService>());
        return new ForwardingOrchestrator(_forwarding.Object, publishing, _storage.Object, config, _clock.Object);
    }

    [Fact]
    public async Task MissingRuleAndBodyReturnDistinctOutcomes()
    {
        _forwarding.Setup(x => x.ProcessPostEvent("missing", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IDictionary<string, string>>()))
            .ReturnsAsync(NoMatchingRuleResult.Instance);
        _forwarding.Setup(x => x.ProcessPostEvent("required", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IDictionary<string, string>>()))
            .ReturnsAsync(NoBodyRuleResult.Instance);
        var service = Create();
        using var missing = await service.ForwardAsync("POST", "missing", "", new Dictionary<string, string>(), "http://localhost");
        using var required = await service.ForwardAsync("POST", "required", "", new Dictionary<string, string>(), "http://localhost");
        missing.Kind.ShouldBe(ForwardingOutcomeKind.NoMatchingRule);
        required.Kind.ShouldBe(ForwardingOutcomeKind.NoBody);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    public async Task RetryableFailurePersistsOriginalMethod(string method)
    {
        var rule = new ForwardingRule(method, "failed", "/target") { Retry = RuleRetry.AllowedDefault };
        var response = new HttpResponseRuleResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)
        { Content = new StringContent("backend failed") }, rule);
        if (method == "POST")
            _forwarding.Setup(x => x.ProcessPostEvent("failed", "http://localhost", "raw", It.IsAny<IDictionary<string, string>>())).ReturnsAsync(response);
        else
            _forwarding.Setup(x => x.ProcessPutEvent("failed", "http://localhost", "raw", It.IsAny<IDictionary<string, string>>())).ReturnsAsync(response);

        using var outcome = await Create().ForwardAsync(method, "failed", "raw", new Dictionary<string, string>(), "http://localhost");
        outcome.StatusCode.ShouldBe(202);
        outcome.RetryId.ShouldNotBeNull();
        _storage.Verify(x => x.Store(It.Is<FailedRequest>(r => r.Id == outcome.RetryId && r.Rule.Method == method && r.AttemptCount == 1 && r.LastError == "backend failed")), Times.Once);
    }

    [Fact]
    public async Task RemoteGetIsNotPublished()
    {
        var rule = new ForwardingRule("GET", "remote", "/target");
        _forwarding.Setup(x => x.ProcessGetEvent("remote", It.IsAny<string>(), It.IsAny<IDictionary<string, string>>()))
            .ReturnsAsync(new RemoteRuleFoundResult(rule));
        using var outcome = await Create(true).ForwardAsync("GET", "remote", null, new Dictionary<string, string>(), "http://localhost");
        outcome.Kind.ShouldBe(ForwardingOutcomeKind.RemoteRule);
        outcome.Response.ShouldBeNull();
    }

    [Theory]
    [InlineData(false, 406)]
    [InlineData(true, 500)]
    public async Task RemotePostReturnsPublishingAvailabilityOrFailure(bool publisher, int status)
    {
        var rule = new ForwardingRule("POST", "remote", "/target")
        { Tags = System.Collections.Immutable.ImmutableHashSet.Create("cloud") };
        _forwarding.Setup(x => x.ProcessPostEvent("remote", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IDictionary<string, string>>()))
            .ReturnsAsync(new RemoteRuleFoundResult(rule));
        using var outcome = await Create(publisher).ForwardAsync("POST", "remote", "raw", new Dictionary<string, string>(), "http://localhost");
        outcome.Kind.ShouldBe(ForwardingOutcomeKind.RemoteRule);
        outcome.StatusCode.ShouldBe(status);
        _storage.Verify(x => x.Store(It.IsAny<FailedRequest>()), Times.Never);
    }
}
