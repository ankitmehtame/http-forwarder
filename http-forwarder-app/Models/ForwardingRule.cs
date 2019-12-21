namespace http_forwarder_app.Models
{
    public class ForwardingRule
    {
        public string Method { get; set; }

        public string Event { get; set; }

        public string TargetUrl { get; set; }
    }
}
