using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using http_forwarder_app.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace http_forwarder_app.Core
{
    public class ForwardingRulesReader
    {
        private readonly ILogger<ForwardingRulesReader> _logger;
        private readonly string _locationTag;

        public ForwardingRulesReader(IConfiguration configuration, ILogger<ForwardingRulesReader> logger, AppState appState)
        {
            Configuration = configuration;
            _logger = logger;
            AppState = appState;
            var locationTag = configuration.GetValue<string?>(Constants.LOCATION_TAG);
            if (string.IsNullOrEmpty(locationTag)) throw new ArgumentNullException(nameof(locationTag), $"Property {Constants.LOCATION_TAG} should be set");
            _locationTag = locationTag;
        }

        private IConfiguration Configuration { get; }
        private AppState AppState { get; }

        public void Init()
        {
            var confPath = Configuration.GetConfDirPath();
            var confPathExists = Directory.Exists(confPath);
            if (!confPathExists)
            {
                _logger.LogError("Conf folder does not exist");
                throw new DirectoryNotFoundException($"conf dir not found {confPath}");
            }
            _logger.LogInformation("Conf folder found at {confPath}", confPath);
            var rulesFile = Configuration.GetConfFilePath("rules.json");
            if (!File.Exists(rulesFile))
            {
                _logger.LogInformation("Writing empty rules file at {rulesFile}", rulesFile);
                File.WriteAllText(rulesFile, JsonUtils.Serialize(Array.Empty<ForwardingRule>(), true));
                _logger.LogDebug("Written successfully at {rulesFile}", rulesFile);
            }
            else
            {
                _logger.LogInformation("Rules file found at {rulesFile}", rulesFile);
            }
            var rules = Read();
            AppState.Rules = rules?.ToArray() ?? [];
        }

        public IEnumerable<ForwardingRule> Read()
        {
            var rulesJsonFilePath = Configuration.GetConfFilePath("rules.json");
            _logger.LogInformation("Reading rules file from {rulesJsonFilePath}", rulesJsonFilePath);
            var rulesJson = File.ReadAllText(rulesJsonFilePath);
            var rules = JsonUtils.Deserialize<ForwardingRule[]>(rulesJson) ?? [];
            var validRules = rules.Where(rule => rule.HasTag(_locationTag)).ToArray();
            _logger.LogInformation("Read {validRulesCount}/{rulesLength} forwarding rules valid for location '{location}' - {rulesJson}", validRules.Length, rules.Length, _locationTag, validRules);
            return validRules;
        }

        public ForwardingRule? Find(string method, string eventName)
        {
            var rule = SimpleFind(method, eventName);
            if (rule != null)
            {
                return rule;
            }
            var rules = Read();
            AppState.Rules = rules?.ToArray() ?? [];
            rule = SimpleFind(method, eventName);
            return rule;
        }

        private ForwardingRule? SimpleFind(string method, string eventName)
        {
            var rule = AppState.Rules.FirstOrDefault(r => r.Method == method && string.Equals(r.Event, eventName, StringComparison.OrdinalIgnoreCase));
            return rule;
        }
    }
}