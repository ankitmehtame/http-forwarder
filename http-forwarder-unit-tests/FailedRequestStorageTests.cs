using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Moq;
using http_forwarder_app.Models;
using Microsoft.Extensions.Internal;
using http_forwarder_app.Services;
using Shouldly;

namespace http_forwarder_unit_tests;

public class FailedRequestStorageTests : IDisposable
{
    private readonly string _testFilePath;
    private readonly FailedRequestStorage _storage;

    public FailedRequestStorageTests()
    {
        _testFilePath = Path.Combine(Path.GetTempPath(), $"test_failed_requests_{Guid.NewGuid()}.json");
        var configMock = new Mock<IConfiguration>();
        configMock.Setup(x => x["RetryStorage:FilePath"]).Returns(_testFilePath);
        _storage = new FailedRequestStorage(configMock.Object);
    }

    [Fact]
    public void Store_ShouldPersistRequest()
    {
        // Arrange
        var request = CreateTestRequest();

        // Act
        _storage.Store(request);

        // Assert
        var stored = File.ReadAllText(_testFilePath);
        stored.ShouldContain(request.Id.ToString());
        stored.ShouldContain(request.Rule.Event);
    }

    [Fact]
    public void Store_ShouldLoadRequests()
    {
        // Arrange
        var request1 = CreateTestRequest();
        var request2 = CreateTestRequest();

        // Act
        _storage.Store(request1);
        _storage.Store(request2);

        // Assert
        var stored = _storage.GetAllRequests();
        stored.Count.ShouldBe(2);
        stored.ShouldContain(r => r.Id == request1.Id);
        stored.ShouldContain(r => r.Id == request2.Id);
    }

    [Fact]
    public void GetPendingRequests_ShouldReturnRequestsDueForRetry()
    {
        // Arrange
        var pastRequest = CreateTestRequest(DateTimeOffset.UtcNow.AddMinutes(-5));
        var futureRequest = CreateTestRequest(DateTimeOffset.UtcNow.AddMinutes(5));
        _storage.Store(pastRequest);
        _storage.Store(futureRequest);

        // Act
        var pending = _storage.GetPendingRequests();

        // Assert
        pending.Count.ShouldBe(1);
        pending[0].Id.ShouldBe(pastRequest.Id);
    }

    [Fact]
    public void Remove_ShouldDeleteRequest()
    {
        // Arrange
        var pastTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        var request1 = CreateTestRequest(pastTime);
        var request2 = CreateTestRequest(pastTime);
        _storage.Store(request1);
        _storage.Store(request2);

        // Act
        _storage.Remove(request1.Id);

        // Assert
        var remaining = _storage.GetAllRequests();
        remaining.Count.ShouldBe(1);
        remaining[0].Id.ShouldBe(request2.Id);
    }

    private static FailedRequest CreateTestRequest(DateTimeOffset? nextAttempt = null)
    {
        var rule = new ForwardingRule(
            Method: "POST",
            Event: "test-event",
            TargetUrl: "http://test.com",
            HasContent: true,
            Content: "test-content",
            IsRetryable: true
        );

        return new FailedRequest(
            Id: Guid.NewGuid(),
            Rule: rule,
            FirstAttempt: DateTimeOffset.UtcNow,
            LastAttempt: DateTimeOffset.UtcNow,
            AttemptCount: 1,
            NextAttempt: nextAttempt ?? DateTimeOffset.UtcNow.AddMinutes(1),
            LastError: "test error"
        );
    }

    public void Dispose()
    {
        if (File.Exists(_testFilePath))
        {
            File.Delete(_testFilePath);
        }
    }
}
