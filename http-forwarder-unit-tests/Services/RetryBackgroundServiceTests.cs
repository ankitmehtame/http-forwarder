using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Moq;
using http_forwarder_app.Models;
using http_forwarder_app.Services;
using OneOf;

namespace http_forwarder_unit_tests;

public class RetryBackgroundServiceTests
{
    private readonly Mock<IFailedRequestStorage> _storageMock;
    private readonly Mock<IForwardingService> _forwardingServiceMock;
    private readonly Mock<ISystemClock> _clockMock;
    private readonly Mock<ILogger<RetryBackgroundService>> _loggerMock;
    private readonly RetryBackgroundService _service;
    private readonly CancellationTokenSource _cts;

    public RetryBackgroundServiceTests()
    {
        _storageMock = new Mock<IFailedRequestStorage>();
        _forwardingServiceMock = new Mock<IForwardingService>();
        _clockMock = new Mock<ISystemClock>();
        _loggerMock = new Mock<ILogger<RetryBackgroundService>>();
        _cts = new CancellationTokenSource();

        var currentTime = DateTimeOffset.UtcNow;
        _clockMock.Setup(x => x.UtcNow).Returns(currentTime);

        _service = new RetryBackgroundService(
            _storageMock.Object,
            _forwardingServiceMock.Object,
            _clockMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRetryFailedRequests()
    {
        // Arrange
        var request = CreateTestRequest();
        _storageMock.Setup(x => x.GetPendingRequests())
            .Returns(new List<FailedRequest> { request });

        var successResult = new HttpResponseRuleResult(
            new HttpResponseMessage(System.Net.HttpStatusCode.OK),
            request.Rule);

        _forwardingServiceMock.Setup(x => x.ProcessPostEvent(
            request.Rule.Event,
            It.IsAny<string>(),
            request.Rule.Content!))
            .ReturnsAsync(successResult);

        // Act
        _cts.CancelAfter(TimeSpan.FromMilliseconds(100));
        await _service.StartAsync(_cts.Token);

        // Assert
        _storageMock.Verify(x => x.Remove(request.Id), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldUpdateRetryInfoOnFailure()
    {
        // Arrange
        var request = CreateTestRequest();
        _storageMock.Setup(x => x.GetPendingRequests())
            .Returns(new List<FailedRequest> { request });

        var failureResult = new HttpResponseRuleResult(
            new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError),
            request.Rule);

        _forwardingServiceMock.Setup(x => x.ProcessPostEvent(
            request.Rule.Event,
            It.IsAny<string>(),
            request.Rule.Content!))
            .ReturnsAsync(failureResult);

        // Act
        _cts.CancelAfter(TimeSpan.FromMilliseconds(100));
        await _service.StartAsync(_cts.Token);

        // Assert
        _storageMock.Verify(x => x.Store(It.Is<FailedRequest>(r =>
            r.Id == request.Id &&
            r.AttemptCount == request.AttemptCount + 1)),
            Times.Once);
    }

    private FailedRequest CreateTestRequest()
    {
        var rule = new ForwardingRule(
            Method: "POST",
            Event: "test-event",
            TargetUrl: "http://test.com",
            HasContent: true,
            Content: "test-content",
            IsRetryable: true);

        return new FailedRequest(
            Id: Guid.NewGuid(),
            Rule: rule,
            RequestHostUrl: "http://localhost:5000",
            FirstAttempt: _clockMock.Object.UtcNow.AddMinutes(-5),
            LastAttempt: _clockMock.Object.UtcNow.AddMinutes(-5),
            AttemptCount: 1,
            NextAttempt: _clockMock.Object.UtcNow.AddMinutes(-1),
            LastError: "test error");
    }
}
