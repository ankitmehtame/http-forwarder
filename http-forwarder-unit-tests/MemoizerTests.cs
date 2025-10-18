
using http_forwarder_app.Utils;
using Shouldly;

namespace http_forwarder_unit_tests;

public class MemoizerTests
{
    [Fact]
    public void Memoize_WithSameInput_ShouldCallFuncOnlyOnce()
    {
        // Arrange
        var callCount = 0;
        Func<string, int> func = s =>
        {
            callCount++;
            return s.Length;
        };
        var memoizer = new Memoizer<string, int>(func);

        // Act
        var result1 = memoizer.Memoize("test");
        var result2 = memoizer.Memoize("test");
        var result3 = memoizer.Memoize("test");
        var result4 = memoizer.Memoize("test");

        // Assert
        callCount.ShouldBe(1);
        new int[] { result1, result2, result3, result4 }.ShouldBe([4, 4, 4, 4]);
    }

    [Fact]
    public void Memoize_WithDifferentCaseInput_ShouldCallFuncOnlyOnce()
    {
        // Arrange
        var callCount = 0;
        Func<string, int> func = s =>
        {
            callCount++;
            return s.Length;
        };
        var memoizer = new Memoizer<string, int>(func, StringComparer.OrdinalIgnoreCase);

        // Act
        var result1 = memoizer.Memoize("test");
        var result2 = memoizer.Memoize("TEST");
        var result3 = memoizer.Memoize("TesT");
        var result4 = memoizer.Memoize("test");

        // Assert
        callCount.ShouldBe(1);
        new int[] { result1, result2, result3, result4 }.ShouldBe([4, 4, 4, 4]);
    }

    [Fact]
    public void Memoize_WithDifferentInputs_ShouldCallFuncForEachInput()
    {
        // Arrange
        var callCount = 0;
        Func<string, int> func = s =>
        {
            callCount++;
            return s.Length;
        };
        var memoizer = new Memoizer<string, int>(func);

        // Act
        var result1 = memoizer.Memoize("test9");
        var result2 = memoizer.Memoize("Test9");

        // Assert
        callCount.ShouldBe(2);
        result1.ShouldBe(5);
        result2.ShouldBe(5);
    }

    [Fact]
    public void Memoize_RevertingToPreviousInput_ShouldCallFuncAgain()
    {
        // Arrange
        var callCount = 0;
        Func<string, int> func = s =>
        {
            callCount++;
            return s.Length;
        };
        var memoizer = new Memoizer<string, int>(func);

        // Act
        memoizer.Memoize("testA"); // Call 1
        memoizer.Memoize("testB"); // Call 2
        memoizer.Memoize("testA"); // Call 3 (since only last value is stored)

        // Assert
        callCount.ShouldBe(3);
    }

    [Fact]
    public void Memoize_WithConcurrentCalls_ShouldBeThreadSafeAndCallFuncOnce()
    {
        // Arrange
        var callCount = 0;
        Func<string, int> func = s =>
        {
            // Simulate work
            Thread.Sleep(100);
            Interlocked.Increment(ref callCount);
            return s.Length;
        };
        var memoizer = new Memoizer<string, int>(func);
        var tasks = new List<Task<int>>();
        var input = "concurrent_test";

        // Act
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() => memoizer.Memoize(input)));
        }

        Task.WhenAll(tasks).Wait();
        var results = tasks.Select(t => t.Result).ToList();

        // Assert
        callCount.ShouldBe(1);
        results.ShouldAllBe(r => r == input.Length);
    }
}
