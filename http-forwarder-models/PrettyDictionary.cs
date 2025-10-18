using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Serialization;

namespace http_forwarder_app.Models;

public class PrettyDictionary
{
    private readonly HashSet<string> _maskedKeys;
    public readonly IDictionary<string, string> _pairs;

    private static ConcurrentDictionary<string, HashSet<string>> MaskedKeys { get; } = new ConcurrentDictionary<string, HashSet<string>>();

    [ThreadStatic]
    private static string? _currentContext;

    public static string? CurrentContext { get { return _currentContext; } set { _currentContext = value; } }

    public static void SetMaskedKeys(IEnumerable<string> keys, string? contextName = null)
    {
        MaskedKeys[contextName ?? CurrentContext ?? string.Empty] = keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static void ResetMaskedKeys(string? contextName = null)
    {
        MaskedKeys.TryRemove(contextName ?? CurrentContext ?? string.Empty, out var _);
    }

    public PrettyDictionary(IDictionary<string, string> pairs, HashSet<string> maskedKeys)
    {
        _pairs = pairs;
        _maskedKeys = maskedKeys;
    }

    [JsonConstructor]
    public PrettyDictionary(IDictionary<string, string> pairs) : this(pairs, MaskedKeys.GetValueOrDefault(CurrentContext ?? string.Empty) ?? throw new InvalidOperationException($"{Constants.MASKED_HEADERS} not set"))
    { }

    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append('[');
        var curIndex = 0;
        foreach (var pair in _pairs.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (curIndex > 0) builder.Append(", ");
            builder.Append('{');
            builder.Append(pair.Key);
            builder.Append('=');
            var value = pair.Value;
            if (_maskedKeys.Contains(pair.Key))
            {
                value = string.Empty.PadLeft(Math.Max(pair.Value.Length, 8), '*');
            }
            builder.Append(value);
            builder.Append('}');
            curIndex++;
        }
        builder.Append(']');
        return builder.ToString();
    }

    public override bool Equals(object? obj)
    {
        if (obj is not PrettyDictionary other) return false;
        if (_pairs.Count != other._pairs.Count) return false;
        foreach (var pair in _pairs)
        {
            var key = pair.Key;
            var thisValue = pair.Value;
            var otherHasValue = other._pairs.TryGetValue(key, out var otherValue);
            if (!otherHasValue) return false;
            if (thisValue != otherValue) return false;
        }
        return true;
    }

    public override int GetHashCode()
    {
        var hashCode = new HashCode();
        foreach (var key in _pairs.Keys.Order(StringComparer.OrdinalIgnoreCase))
        {
            hashCode.Add(key);
        }
        return hashCode.ToHashCode();
    }
}
