using http_forwarder_app.Core;
using http_forwarder_app.Models;
using Microsoft.Extensions.Configuration;

namespace http_forwarder_app.Utils;

public static class PrettyDictionaryUtils
{
    public static PrettyDictionary CreatePrettyDictionary(this IConfiguration configuration, IDictionary<string, string> dictionary)
    {
        return new PrettyDictionary(dictionary, configuration.CreateMaskedHeaders());
    }

    public static HashSet<string> CreateMaskedHeaders(this IConfiguration configuration)
    {
        return _memoizeMaskedHeaders.Memoize(configuration.GetMaskedHeadersValue());
    }

    private static HashSet<string> CreateMaskedHeaders(string headersValue)
    {
        return headersValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly Memoizer<string, HashSet<string>> _memoizeMaskedHeaders = new(CreateMaskedHeaders);
}