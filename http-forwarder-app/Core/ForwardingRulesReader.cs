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

        public IEnumerable<ForwardingRule> Read()
        {
            var rulesJsonFilePath = Configuration.GetConfFilePath("rules.json");
            _logger.LogInformation($"Found rules file at location {rulesJsonFilePath}");
            var rulesJson = File.ReadAllText(rulesJsonFilePath);
            var rules = JsonUtils.Deserialize<ForwardingRule[]>(rulesJson);
            _logger.LogInformation($"Read {rules.Length} forwarding rules - {JsonUtils.Serialize(rules, false)}");
            return rules;
        }
    }
}