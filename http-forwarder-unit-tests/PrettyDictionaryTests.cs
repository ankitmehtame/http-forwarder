using http_forwarder_app.Models;
using Shouldly;

namespace http_forwarder_unit_tests;

public class PrettyDictionaryTests : IDisposable
{
    public PrettyDictionaryTests()
    {
        var currentContext = GetType().Name;
        PrettyDictionary.CurrentContext = currentContext;
        // Ensure static state is clean before each test
        PrettyDictionary.SetMaskedKeys([]);
    }

    public void Dispose()
    {
        // Cleanup static state after each test
        PrettyDictionary.ResetMaskedKeys();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ToString_WithNoMaskedKeys_ShouldReturnFormattedString()
    {
        // Arrange
        var dict = new Dictionary<string, string>
        {
            { "Content-Type", "application/json" },
            { "Accept", "*/*" }
        };
        var prettyDict = new PrettyDictionary(dict);

        // Act
        var result = prettyDict.ToString();

        // Assert
        result.ShouldBe("[{Accept=*/*}, {Content-Type=application/json}]");
    }

    [Fact]
    public void ToString_WithMaskedKeys_ShouldMaskValues()
    {
        // Arrange
        PrettyDictionary.SetMaskedKeys(["Authorization"]);
        var dict = new Dictionary<string, string>
        {
            { "Authorization", "Bearer some_secret_token" },
            { "Content-Type", "application/json" }
        };
        var prettyDict = new PrettyDictionary(dict);

        // Act
        var result = prettyDict.ToString();

        // Assert
        result.ShouldBe("[{Authorization=************************}, {Content-Type=application/json}]");
    }

    [Fact]
    public void ToString_WithMaskedKeys_ShouldBeCaseInsensitive()
    {
        // Arrange
        PrettyDictionary.SetMaskedKeys(["authorization"]); // lowercase
        var dict = new Dictionary<string, string>
        {
            { "Authorization", "Bearer token" }, // title case
            { "X-API-KEY", "secret" } // uppercase
        };
        var prettyDict = new PrettyDictionary(dict);

        // Act
        var result = prettyDict.ToString();

        // Assert
        result.ShouldBe("[{Authorization=************}, {X-API-KEY=secret}]");
    }

    [Fact]
    public void ToString_MaskedValueLength_ShouldBeAtLeast8Chars()
    {
        // Arrange
        PrettyDictionary.SetMaskedKeys(["X-Api-Key"]);
        var dict = new Dictionary<string, string> { { "X-Api-Key", "short" } }; // value length is 5
        var prettyDict = new PrettyDictionary(dict);

        // Act
        var result = prettyDict.ToString();

        // Assert
        // Masked value should be 8 asterisks since 'short'.Length < 8
        result.ShouldBe("[{X-Api-Key=********}]");
    }

    [Fact]
    public void ToString_ForEmptyDictionary_ShouldReturnEmptyBrackets()
    {
        // Arrange
        var prettyDict = new PrettyDictionary(new Dictionary<string, string>());

        // Act
        var result = prettyDict.ToString();

        // Assert
        result.ShouldBe("[]");
    }

    [Fact]
    public void Constructor_WhenMaskedKeysNotSet_ShouldThrowInvalidOperationException()
    {
        // Arrange
        // Static state is reset to empty list in constructor, so we need to reset it here
        PrettyDictionary.ResetMaskedKeys();
        var dict = new Dictionary<string, string> { { "key", "value" } };

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => new PrettyDictionary(dict));
    }
}
