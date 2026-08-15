using PoeOverlay.Core.Domain;
using Xunit;

namespace PoeOverlay.Core.Tests.Settings;

/// <summary>
/// S2 11.10 SE13 / SE13' / SE13" (S4 16.6) — the four equality members plus copy-on-construction.
/// </summary>
/// <remarks>
/// Overriding only <c>Equals(EquatableArray&lt;T&gt;)</c> leaves two equal instances with different
/// hashes, so <c>HashSet</c> stops de-duplicating (SE13) and <c>w1.Equals((object)w2)</c> returns
/// false (SE13'). Without the copy, an external holder of the source array can mutate an element
/// and the cached hash starts lying (SE13").
/// <para>
/// <see cref="SettingsLike"/> stands in for <c>AppSettings</c>, which arrives in a later stage.
/// It reproduces the load-bearing shape: a record whose declared property type is
/// <see cref="EquatableArray{T}"/>, which is what makes record equality behave.
/// </para>
/// </remarks>
public sealed class EquatableArrayTests
{
    private sealed record SettingsLike(EquatableArray<WatchlistEntry> Watchlist, int RefreshIntervalMinutes);

    private static WatchlistEntry Entry(string id, string category = "Currency")
        => new(
            new ItemId(id),
            new CategoryRef(category, Enum.TryParse<ExchangeCategory>(category, out var known) ? known : null),
            DisplayCurrency.Auto);

    private static EquatableArray<WatchlistEntry> Array(params string[] ids)
        => new(ids.Select(id => Entry(id)));

    [Fact]
    public void SE13_HashSetOfTwoSettingsWithEqualContent_HoldsOne()
    {
        var set = new HashSet<SettingsLike>
        {
            new(Array("divine", "chaos"), 5),
            new(Array("divine", "chaos"), 5),
        };

        Assert.Single(set);
    }

    [Fact]
    public void SE13_TwoArraysWithEqualContent_HaveEqualHashCodes()
    {
        Assert.Equal(Array("divine", "chaos").GetHashCode(), Array("divine", "chaos").GetHashCode());
    }

    [Fact]
    public void SE13Prime_EqualsThroughObject_IsTrue()
    {
        var w1 = Array("divine", "chaos");
        var w2 = Array("divine", "chaos");

        Assert.True(w1.Equals((object)w2));
    }

    [Fact]
    public void SE13Prime_EqualsThroughObject_IsFalseForAnUnrelatedType()
    {
        Assert.False(Array("divine").Equals((object)"divine"));
        Assert.False(Array("divine").Equals(null));
    }

    [Fact]
    public void SE13DoublePrime_MutatingTheSourceArrayAfterConstruction_DoesNotChangeTheCopy()
    {
        var source = new[] { Entry("divine"), Entry("chaos") };
        var array = new EquatableArray<WatchlistEntry>(source);
        var hashBefore = array.GetHashCode();

        source[0] = Entry("mirror");

        Assert.Equal(new ItemId("divine"), array[0].Id);
        Assert.Equal(hashBefore, array.GetHashCode());
        Assert.Equal(Array("divine", "chaos"), array);
    }

    [Fact]
    public void SE13DoublePrime_MutatingTheSourceArray_DoesNotBreakDeduplication()
    {
        var source = new[] { Entry("divine") };
        var array = new EquatableArray<WatchlistEntry>(source);
        var set = new HashSet<EquatableArray<WatchlistEntry>> { array };

        source[0] = Entry("mirror");

        Assert.Contains(Array("divine"), set);
        Assert.DoesNotContain(Array("mirror"), set);
    }

    [Fact]
    public void Equals_DifferentLength_IsFalse()
    {
        Assert.NotEqual(Array("divine"), Array("divine", "chaos"));
    }

    [Fact]
    public void Equals_SameLengthDifferentOrder_IsFalse()
    {
        Assert.NotEqual(Array("divine", "chaos"), Array("chaos", "divine"));
    }

    [Fact]
    public void Equals_OneDifferingElement_IsFalse()
    {
        Assert.NotEqual(Array("divine", "chaos"), Array("divine", "mirror"));
    }

    [Fact]
    public void OperatorEquality_ComparesByValueAndHandlesNulls()
    {
        var a = Array("divine");
        var b = Array("divine");
        var c = Array("chaos");
        EquatableArray<WatchlistEntry>? none = null;

        Assert.True(a == b);
        Assert.False(a != b);
        Assert.True(a != c);
        Assert.False(a == c);
        Assert.True(none == null);
        Assert.False(none != null);
        Assert.False(a == none);
        Assert.True(a != none);
    }

    [Fact]
    public void RecordEquality_IsDrivenByTheArrayContentNotTheReference()
    {
        var settings = new SettingsLike(Array("divine"), 5);

        Assert.Equal(settings, new SettingsLike(Array("divine"), 5));
        Assert.NotEqual(settings, new SettingsLike(Array("chaos"), 5));
        Assert.NotEqual(settings, new SettingsLike(Array("divine"), 10));
    }

    [Fact]
    public void ReadOnlyListSurface_ExposesCountIndexerAndEnumerator()
    {
        var array = Array("divine", "chaos", "mirror");

        Assert.Equal(3, array.Count);
        Assert.Equal(new ItemId("chaos"), array[1].Id);
        Assert.Equal(
            new[] { "divine", "chaos", "mirror" },
            array.Select(e => e.Id.Value).ToArray());
    }

    [Fact]
    public void EmptyArrays_AreEqualAndShareAHashCode()
    {
        var a = new EquatableArray<WatchlistEntry>([]);
        var b = new EquatableArray<WatchlistEntry>([]);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Empty(a);
    }

    [Fact]
    public void GetHashCode_IsStableAcrossRepeatedCalls()
    {
        var array = Array("divine", "chaos");

        Assert.Equal(array.GetHashCode(), array.GetHashCode());
    }

    [Fact]
    public void Equals_SameInstance_IsTrue()
    {
        var array = Array("divine");

        Assert.True(array.Equals(array));
    }
}
