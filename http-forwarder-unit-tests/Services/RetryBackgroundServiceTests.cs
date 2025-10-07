using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Moq;
using http_forwarder_app.Models;
using http_forwarder_app.Services;
using Shouldly;
using http_forwarder_app;

namespace http_forwarder_unit_tests;

public class RetryBackgroundServiceTests
{
    private readonly Mock<IFailedRequestStorage> _storageMock;
    private readonly Mock<IForwardingService> _forwardingServiceMock;
    private readonly Mock<ISystemClock> _clockMock;
    private readonly Mock<ITimeDelayService> _delayServiceMock;
    private readonly Mock<ILogger<RetryBackgroundService>> _loggerMock;
    private readonly TestableRetryBackgroundService _service;
    private readonly Mock<IConfiguration> _configMock;
    private readonly CancellationTokenSource _cts;
    private readonly DateTimeOffset _startTime;


    public RetryBackgroundServiceTests()
    {
        _storageMock = new Mock<IFailedRequestStorage>();
        _forwardingServiceMock = new Mock<IForwardingService>();
        _clockMock = new Mock<ISystemClock>();
        _delayServiceMock = new Mock<ITimeDelayService>();
        _loggerMock = new Mock<ILogger<RetryBackgroundService>>();
        _configMock = new Mock<IConfiguration>(MockBehavior.Loose);
        _cts = new CancellationTokenSource();
        _startTime = DateTimeOffset.UtcNow;

        _clockMock.Setup(x => x.UtcNow).Returns(_startTime);
        var configSectionMock = new Mock<IConfigurationSection>(MockBehavior.Loose);
        configSectionMock.Setup(x => x.Value).Returns("1");
        _configMock.Setup(x => x.GetSection(Constants.RETRY_POLICY_MAX_CONCURRENCY))
            .Returns(configSectionMock.Object);

        _service = new TestableRetryBackgroundService(
            _storageMock.Object,
            _forwardingServiceMock.Object,
            _clockMock.Object,
            _delayServiceMock.Object,
            _loggerMock.Object,
            _configMock.Object);
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldRetryFailedRequestsAndRemoveOnSuccess()
    {
        // Arrange
        var rule = CreateRule();
        var request = CreateTestRequest(rule);
        _storageMock.Setup(x => x.GetRequestsDue(It.IsAny<DateTimeOffset>()))
            .Returns([request]);

        var successResult = new HttpResponseRuleResult(
            new HttpResponseMessage(System.Net.HttpStatusCode.OK),
            rule);

        _forwardingServiceMock.Setup(x => x.ProcessPostEvent(
            request.Rule.Event,
            It.IsAny<string>(),
            request.RequestBody))
            .ReturnsAsync(successResult);

        // Act
        _cts.CancelAfter(TimeSpan.FromMilliseconds(100));
        await _service.ProcessPendingAsync(_startTime.AddSeconds(1), _cts.Token);

        // Assert
        _storageMock.Verify(x => x.Remove(request.Id), Times.Once);
        _storageMock.Verify(x => x.Store(It.IsAny<FailedRequest>()), Times.Never);
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldUpdateRetryInfoOnServerErrorFailure()
    {
        // Arrange
        var rule = CreateRule();
        var request = CreateTestRequest(rule);
        _storageMock.Setup(x => x.GetRequestsDue(It.IsAny<DateTimeOffset>()))
            .Returns([request]);

        var failureResult = new HttpResponseRuleResult(
            new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError),
            rule);

        _forwardingServiceMock.Setup(x => x.ProcessPostEvent(
            request.Rule.Event,
            It.IsAny<string>(),
            request.RequestBody))
            .ReturnsAsync(failureResult);

        // Act
        _cts.CancelAfter(TimeSpan.FromMilliseconds(100));
        await _service.ProcessPendingAsync(_startTime.AddSeconds(1), _cts.Token);

        // Assert
        _storageMock.Verify(x => x.Store(It.Is<FailedRequest>(r =>
            r.Id == request.Id &&
            r.AttemptCount == request.AttemptCount + 1)),
            Times.Once);
        _storageMock.Verify(x => x.Remove(request.Id), Times.Never);
    }


    [Fact]
    public async Task ProcessPendingAsync_ShouldUpdateRetryInfoOnClientErrorFailure()
    {
        // Arrange
        var rule = CreateRule();
        var request = CreateTestRequest(rule);
        _storageMock.Setup(x => x.GetRequestsDue(It.IsAny<DateTimeOffset>()))
            .Returns([request]);

        var failureResult = new HttpResponseRuleResult(
            new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest),
            rule);

        _forwardingServiceMock.Setup(x => x.ProcessPostEvent(
            request.Rule.Event,
            It.IsAny<string>(),
            request.RequestBody))
            .ReturnsAsync(failureResult);

        // Act
        _cts.CancelAfter(TimeSpan.FromMilliseconds(100));
        await _service.ProcessPendingAsync(_startTime.AddSeconds(1), _cts.Token);

        // Assert
        _storageMock.Verify(x => x.Store(It.IsAny<FailedRequest>()), Times.Never);
        _storageMock.Verify(x => x.Remove(request.Id), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenNoRequests_ShouldWaitAndLoop()
    {
        // Arrange
        _storageMock.Setup(s => s.GetAllRequests()).Returns([]);
        _storageMock.Setup(s => s.GetRequestsDue(It.IsAny<DateTimeOffset>())).Returns([]);
        _storageMock.Setup(s => s.StorageHash).Returns(1);

        _delayServiceMock.Setup(d => d.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Callback(() => _cts.Cancel()) // Cancel on first delay to exit loop
            .Returns(Task.CompletedTask);

        // Act
        await _service.ExecuteAsync(_cts.Token);

        // Assert
        _storageMock.Verify(s => s.GetRequestsDue(It.IsAny<DateTimeOffset>()), Times.Once);
        _storageMock.Verify(s => s.GetAllRequests(), Times.Once);
        _storageMock.Verify(x => x.Remove(It.IsAny<Guid>()), Times.Never);
        _storageMock.Verify(x => x.Store(It.IsAny<FailedRequest>()), Times.Never);
        _delayServiceMock.Verify(d => d.DelayAsync(TimeSpan.FromHours(1), It.IsAny<CancellationToken>()), Times.Once);
        _forwardingServiceMock.Verify(f => f.ProcessPostEvent(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRequestsAreDue_ShouldProcessThem()
    {
        // Arrange
        var rule = CreateRule();
        var request = CreateTestRequest(rule);
        _storageMock.Setup(s => s.GetRequestsDue(It.IsAny<DateTimeOffset>())).Returns([request]);
        _storageMock.Setup(s => s.GetAllRequests()).Returns([]); // No more requests after processing
        _storageMock.Setup(s => s.StorageHash).Returns(1);

        var successResult = new HttpResponseRuleResult(new(System.Net.HttpStatusCode.OK), rule);
        _forwardingServiceMock.Setup(f => f.ProcessPostEvent(request.Rule.Event, request.RequestHostUrl, request.RequestBody))
            .ReturnsAsync(successResult);

        _delayServiceMock.Setup(d => d.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Callback(() => _cts.Cancel())
            .Returns(Task.CompletedTask);

        // Act
        await _service.ExecuteAsync(_cts.Token);

        // Assert
        _storageMock.Verify(s => s.GetRequestsDue(It.IsAny<DateTimeOffset>()), Times.Once);
        _forwardingServiceMock.Verify(f => f.ProcessPostEvent(request.Rule.Event, request.RequestHostUrl, request.RequestBody), Times.Once);
        _storageMock.Verify(s => s.Remove(request.Id), Times.Once);
        _delayServiceMock.Verify(d => d.DelayAsync(TimeSpan.FromHours(1), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenStorageChanges_ShouldReEvaluate()
    {
        // Arrange
        var rule = CreateRule();
        var request = CreateTestRequest(rule);
        var nextAttemptTime = _startTime.AddMinutes(10);
        var requestInFuture = request with { NextAttempt = nextAttemptTime };

        var storageHash = 1;
        _storageMock.Setup(s => s.StorageHash).Returns(() => storageHash);
        _storageMock.SetupSequence(s => s.GetAllRequests())
            .Returns([]) // First call, no requests
            .Returns([requestInFuture]); // Second call, after hash change

        _storageMock.Setup(s => s.GetRequestsDue(It.IsAny<DateTimeOffset>())).Returns([]);

        var delayCallCount = 0;
        _delayServiceMock.Setup(d => d.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Callback<TimeSpan, CancellationToken>((delay, token) =>
            {
                delayCallCount++;
                if (delayCallCount == 1)
                {
                    // After first loop, simulate storage change
                    storageHash = 2;
                }
                else
                {
                    _cts.Cancel();
                }
            })
            .Returns(Task.CompletedTask);

        // Act
        await _service.ExecuteAsync(_cts.Token);

        // Assert
        _storageMock.Verify(s => s.GetRequestsDue(It.IsAny<DateTimeOffset>()), Times.Exactly(2));
        _storageMock.Verify(s => s.GetAllRequests(), Times.Exactly(2));
        _delayServiceMock.Verify(d => d.DelayAsync(TimeSpan.FromHours(1), It.IsAny<CancellationToken>()), Times.Once);
        delayCallCount.ShouldBe(2);
    }

    private FailedRequest CreateTestRequest(ForwardingRule rule)
    {
        return new FailedRequest(
            Id: Guid.NewGuid(),
            Rule: rule.ToMinimal(),
            RequestBody: "test-body",
            RequestHostUrl: "http://localhost:5000",
            FirstAttempt: _startTime.AddMinutes(-5),
            LastAttempt: _startTime.AddMinutes(-5),
            AttemptCount: 1,
            NextAttempt: _startTime.AddMinutes(-1),
            LastError: "test error");
    }

    private static ForwardingRule CreateRule()
    {
        return new ForwardingRule(
            Method: "POST",
            Event: "test-event",
            TargetUrl: "http://test.com",
            HasContent: true,
            Content: "test-content",
            Retry: RuleRetry.AllowedDefault);
    }

    // Helper class to expose ExecuteAsync for testing
    private class TestableRetryBackgroundService : RetryBackgroundService
    {
        public TestableRetryBackgroundService(
            IFailedRequestStorage storage,
            IForwardingService forwardingService,
            ISystemClock clock,
            ITimeDelayService timeDelayService,
            ILogger<RetryBackgroundService> logger,
            IConfiguration configuration)
            : base(storage, forwardingService, clock, timeDelayService, logger, configuration)
        {
        }
        public new Task ExecuteAsync(CancellationToken stoppingToken) => base.ExecuteAsync(stoppingToken);

        public new Task ProcessPendingAsync(DateTimeOffset asOf, CancellationToken stoppingToken) => base.ProcessPendingAsync(asOf, stoppingToken);
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldRemoveRequestOnNoRuleFound()
    {
        // Arrange
        var rule = CreateRule();
        var request = CreateTestRequest(rule);
        _storageMock.Setup(x => x.GetRequestsDue(It.IsAny<DateTimeOffset>()))
            .Returns(new List<FailedRequest> { request });

        NoMatchingRuleResult noRuleResult = new();

        _forwardingServiceMock.Setup(x => x.ProcessPostEvent(
            request.Rule.Event,
            It.IsAny<string>(),
            request.RequestBody))
            .ReturnsAsync(noRuleResult);

        // Act
        _cts.CancelAfter(TimeSpan.FromMilliseconds(100));
        await _service.ProcessPendingAsync(_startTime.AddSeconds(1), _cts.Token);

        // Assert
        _storageMock.Verify(x => x.Store(It.IsAny<FailedRequest>()), Times.Never);
        _storageMock.Verify(x => x.Remove(request.Id), Times.Once);
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldRemoveRequestOnNoBody()
    {
        // Arrange
        var rule = CreateRule();
        var request = CreateTestRequest(rule);
        _storageMock.Setup(x => x.GetRequestsDue(It.IsAny<DateTimeOffset>()))
            .Returns(new List<FailedRequest> { request });

        NoBodyRuleResult noBodyResult = new();

        _forwardingServiceMock.Setup(x => x.ProcessPostEvent(
            request.Rule.Event,
            It.IsAny<string>(),
            request.RequestBody))
            .ReturnsAsync(noBodyResult);

        // Act
        _cts.CancelAfter(TimeSpan.FromMilliseconds(100));
        await _service.ProcessPendingAsync(_startTime.AddSeconds(1), _cts.Token);

        // Assert
        _storageMock.Verify(x => x.Store(It.IsAny<FailedRequest>()), Times.Never);
        _storageMock.Verify(x => x.Remove(request.Id), Times.Once);
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldRemoveRequestOnRemoteRule()
    {
        // Arrange
        var rule = CreateRule();
        var request = CreateTestRequest(rule);
        _storageMock.Setup(x => x.GetRequestsDue(It.IsAny<DateTimeOffset>()))
            .Returns([request]);

        RemoteRuleFoundResult remoteRuleResult = new(rule);

        _forwardingServiceMock.Setup(x => x.ProcessPostEvent(
            request.Rule.Event,
            It.IsAny<string>(),
            request.RequestBody))
            .ReturnsAsync(remoteRuleResult);

        // Act
        _cts.CancelAfter(TimeSpan.FromMilliseconds(100));
        await _service.ProcessPendingAsync(_startTime.AddSeconds(1), _cts.Token);

        // Assert
        _storageMock.Verify(x => x.Store(It.IsAny<FailedRequest>()), Times.Never);
        _storageMock.Verify(x => x.Remove(request.Id), Times.Once);
    }
}
