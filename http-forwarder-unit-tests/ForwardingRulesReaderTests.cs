using http_forwarder_app.Core;
using http_forwarder_app.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace http_forwarder_unit_tests;

public class ForwardingRulesReaderTests
{
    [Fact]
    public void Init_WithInvalidIgnoredHeaderRegexes_WarnsForEachPatternAndStillLoadsRules()
    {
        const string json = """
        [
            {
                "method": "POST", "event": "local-event", "targetUrl": "http://example.com",
                "tags": ["local"], "ignoredRequestHeaders": ["[", "(", "*", "X-.*"]
            },
            {
                "method": "PUT", "event": "remote-event", "targetUrl": "http://example.com",
                "tags": ["remote"], "ignoreRequestHeaders": ["[", "*"]
            }
        ]
        """;

        var (state, logger) = LoadRules(json);

        state.Rules.ShouldHaveSingleItem().Event.ShouldBe("local-event");
        state.RemoteRules.ShouldHaveSingleItem().Event.ShouldBe("remote-event");
        state.RemoteRules[0].IgnoredRequestHeaders.SetEquals(["[", "*"]).ShouldBeTrue();
        var warnings = logger.Entries.Where(entry => entry.Level == LogLevel.Warning).ToArray();
        warnings.Length.ShouldBe(3);
        warnings.ShouldContain(entry => entry.Message.Contains("pattern [ for rule POST local-event"));
        warnings.ShouldContain(entry => entry.Message.Contains("pattern ( for rule POST local-event"));
        warnings.ShouldContain(entry => entry.Message.Contains("pattern [ for rule PUT remote-event"));
    }

    [Fact]
    public void Init_WithWildcardAndValidRegexes_DoesNotWarn()
    {
        const string json = """
        [{
            "method": "GET", "event": "valid-event", "targetUrl": "http://example.com",
            "tags": ["local"], "ignoredRequestHeaders": ["*", ".*", "X-.*", "Authorization"]
        }]
        """;

        var (state, logger) = LoadRules(json);

        state.Rules.ShouldHaveSingleItem().Event.ShouldBe("valid-event");
        logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
    }

    private static (AppState State, CapturingLogger<ForwardingRulesReader> Logger) LoadRules(string json)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LOCATION_TAG"] = "local"
        }).Build();
        var path = configuration.GetConfFilePath("rules.json");
        var original = File.Exists(path) ? File.ReadAllBytes(path) : null;
        Directory.CreateDirectory(configuration.GetConfDirPath());
        try
        {
            File.WriteAllText(path, json);
            var state = new AppState();
            var logger = new CapturingLogger<ForwardingRulesReader>();
            var reader = new ForwardingRulesReader(configuration, logger, state);

            reader.Init();

            return (state, logger);
        }
        finally
        {
            if (original is null) File.Delete(path);
            else File.WriteAllBytes(path, original);
        }
    }
}
