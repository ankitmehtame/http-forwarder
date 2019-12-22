using System;
using System.Collections.Generic;
using System.IO;
using http_forwarder_app.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace http_forwarder_app.Core
{
    public class ForwardingRulesReader
    {
        private readonly ILogger<ForwardingRulesReader> _logger;

        public ForwardingRulesReader(IConfiguration configuration, ILogger<ForwardingRulesReader> logger)
        {
            Configuration = configuration;
            _logger = logger;
        }

        private IConfiguration Configuration { get; }

        public void Init()
        {
            var confPath = Configuration.GetConfDirPath();
            var confPathExists = Directory.Exists(confPath);
            if (!confPathExists)
            {
                _logger.LogError("Conf folder does not exist");
                throw new DirectoryNotFoundException($"conf dir not found {confPath}");
            }
            _logger.LogInformation($"Conf folder found at {confPath}");
            var rulesFile = Configuration.GetConfFilePath("rules.json");
            if (!File.Exists(rulesFile))
            {
                _logger.LogInformation($"Writing empty rules file at {rulesFile}");
                File.WriteAllText(rulesFile, JsonUtils.Serialize(Array.Empty<ForwardingRule>(), true));
                _logger.LogDebug($"Written successfully at {rulesFile}");
            }
            else
            {
                _logger.LogInformation($"Rules file found at {rulesFile}");    
            }
        }

        public IEnumerable<ForwardingRule> Read()
        {
            var rulesJsonFilePath = Configuration.GetConfFilePath("rules.json");
            _logger.LogInformation($"Reading rules file from {rulesJsonFilePath}");
            var rulesJson = File.ReadAllText(rulesJsonFilePath);
            var rules = JsonUtils.Deserialize<ForwardingRule[]>(rulesJson);
            _logger.LogInformation($"Read {rules.Length} forwarding rules - {JsonUtils.Serialize(rules, false)}");
            return rules;
        }
    }
}