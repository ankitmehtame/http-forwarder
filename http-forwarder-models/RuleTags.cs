namespace http_forwarder_app.Models;

public static class RuleTags
{
    public const string HomeTag = "home";
    public const string CloudTag = "cloud";


    public static bool HasHomeTag(this ForwardingRule forwardingRule) => forwardingRule.HasTag(HomeTag);

    public static bool HasCloudTag(this ForwardingRule forwardingRule) => forwardingRule.HasTag(CloudTag);

}