using System.Diagnostics.CodeAnalysis;
using OneOf;

namespace http_forwarder_app.Utils;

public class Memoizer<TIn, TOut>(Func<TIn, TOut> func, IEqualityComparer<TIn> equalityComparer) where TIn : IEquatable<TIn>
{
    public Memoizer(Func<TIn, TOut> func) : this(func, EqualityComparer<TIn>.Default)
    { }

    private readonly Func<TIn, TOut> _func = func;
    private readonly IEqualityComparer<TIn> _equalityComparer = equalityComparer;
    private readonly Lock _lastValueLock = new();
    private KeyValuePair<TIn, TOut>? _lastValue = null;

    public TOut Memoize([NotNull] TIn @in)
    {
        ArgumentNullException.ThrowIfNull(@in, nameof(@in));

        var matches = MatchesLastValue(@in);
        if (matches.IsT0) return matches.AsT0;
        using (_lastValueLock.EnterScope())
        {
            matches = MatchesLastValue(@in);
            if (matches.IsT0) return matches.AsT0;
            var newValue = new KeyValuePair<TIn, TOut>(@in, _func(@in));
            _lastValue = newValue;
            return newValue.Value;
        }
    }

    private OneOf<TOut, bool> MatchesLastValue(TIn @in)
    {
        var last = _lastValue;
        if (last.HasValue && last.Value.Key != null && _equalityComparer.Equals(last.Value.Key, @in))
        {
            return last.Value.Value;
        }
        return false;
    }
}