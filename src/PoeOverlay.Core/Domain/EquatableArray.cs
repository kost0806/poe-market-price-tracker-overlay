using System.Collections;

namespace PoeOverlay.Core.Domain;

/// <summary>
/// An immutable list with value equality, so that records holding one keep record semantics
/// (S2 2.4 D-D2 / S4 3.4).
/// </summary>
/// <remarks>
/// <para>
/// All of <see cref="Equals(EquatableArray{T})"/>, <see cref="Equals(object?)"/>,
/// <see cref="GetHashCode"/>, <c>==</c> and <c>!=</c> must exist. Overriding only
/// <see cref="Equals(EquatableArray{T})"/> leaves two equal instances with different hashes, so
/// <c>HashSet</c> stops de-duplicating and <c>w1.Equals((object)w2)</c> returns false.
/// </para>
/// <para>
/// The constructor copies. Without the copy an external holder of the source array can mutate an
/// element after construction and the cached hash starts lying.
/// </para>
/// <para>
/// The load-bearing detail is that the *declared* type of the holding property is
/// <c>EquatableArray&lt;T&gt;</c>. Widening it back to <c>IReadOnlyList&lt;T&gt;</c> compiles
/// cleanly and silently restores the infinite re-entry D-D2 exists to prevent.
/// </para>
/// </remarks>
public sealed class EquatableArray<T> : IReadOnlyList<T>, IEquatable<EquatableArray<T>>
    where T : IEquatable<T>
{
    private readonly T[] _items;
    private int _hash;
    private bool _hashComputed;

    /// <summary>Copies <paramref name="items"/> into a private array.</summary>
    public EquatableArray(IEnumerable<T> items)
    {
        _items = items.ToArray();
    }

    /// <inheritdoc />
    public int Count => _items.Length;

    /// <inheritdoc />
    public T this[int index] => _items[index];

    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    /// <inheritdoc />
    public bool Equals(EquatableArray<T>? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || other._items.Length != _items.Length)
        {
            return false;
        }

        var comparer = EqualityComparer<T>.Default;
        for (var i = 0; i < _items.Length; i++)
        {
            if (!comparer.Equals(_items[i], other._items[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as EquatableArray<T>);

    /// <summary>Computed once and cached; the contents cannot change after construction.</summary>
    public override int GetHashCode()
    {
        if (!_hashComputed)
        {
            var hash = new HashCode();
            hash.Add(_items.Length);
            foreach (var item in _items)
            {
                hash.Add(item);
            }

            _hash = hash.ToHashCode();
            _hashComputed = true;
        }

        return _hash;
    }

    /// <summary>Value equality; two nulls are equal.</summary>
    public static bool operator ==(EquatableArray<T>? left, EquatableArray<T>? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Negation of <see cref="op_Equality"/>.</summary>
    public static bool operator !=(EquatableArray<T>? left, EquatableArray<T>? right) => !(left == right);
}
