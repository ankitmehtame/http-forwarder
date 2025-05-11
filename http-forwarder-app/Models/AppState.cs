using System.Threading;

namespace http_forwarder_app.Models
{
    public class AppState
    {
        private ForwardingRule[] _rules = [];
        public ForwardingRule[] Rules
        {
            get { return _rules; }
            set { Interlocked.Exchange(ref _rules, value); }
        }

        private ForwardingRule[] _remoteRules = [];
        public ForwardingRule[] RemoteRules
        {
            get { return _remoteRules; }
            set { Interlocked.Exchange(ref _remoteRules, value); }
        }
    }
}