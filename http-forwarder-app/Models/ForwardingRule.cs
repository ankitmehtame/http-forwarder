using System.Collections.Generic;
using System.ComponentModel;

namespace http_forwarder_app.Models
{
    public class ForwardingRule
    {
        public string Method { get; set; }

        public string Event { get; set; }

        public string TargetUrl { get; set; }

        [DefaultValue(true)]
        public bool HasContent { get; set; } = true;

        public string Content { get; set; } = null;

        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
    }
}
