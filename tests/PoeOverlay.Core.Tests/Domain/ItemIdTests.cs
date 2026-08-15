using PoeOverlay.Core.Domain;
using Xunit;

namespace PoeOverlay.Core.Tests.Domain;

/// <summary>
/// S2 1.6 / 2.1, S4 3.1 — a struct cannot forbid <c>default</c>, so <c>default</c> must be a
/// defined, harmless state: <c>ToString()</c> never returns null and <c>IsEmpty</c> is reachable.
/// </summary>
public sealed class ItemIdTests
{
    [Fact]
    public void Default_ToString_IsNotNull()
    {
        var id = default(ItemId);

        Assert.NotNull(id.ToString());
        Assert.Equal(string.Empty, id.ToString());
    }

    [Fact]
    public void Default_IsEmpty_IsTrue()
    {
        Assert.True(default(ItemId).IsEmpty);
    }

    [Fact]
    public void Default_InAnInterpolatedString_RendersAsEmptyRatherThanBrackets()
    {
        Assert.Equal(">|<", $">|{default(ItemId)}<");
    }

    [Fact]
    public void Default_UsedAsADictionaryKey_DoesNotThrow()
    {
        var map = new Dictionary<ItemId, int> { [default] = 1 };

        Assert.Equal(1, map[default]);
        Assert.Equal(0, default(ItemId).GetHashCode());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void IsEmpty_ForBlankValues_IsTrue(string value)
    {
        Assert.True(new ItemId(value).IsEmpty);
    }

    [Fact]
    public void IsEmpty_ForARealSlug_IsFalse()
    {
        Assert.False(new ItemId("divine").IsEmpty);
    }

    [Fact]
    public void TryCreate_Null_FailsAndYieldsAnEmptyIdRatherThanANullValue()
    {
        Assert.False(ItemId.TryCreate(null, out var id));
        Assert.True(id.IsEmpty);
        Assert.Equal(string.Empty, id.Value);
        Assert.Equal(string.Empty, id.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryCreate_Blank_Fails(string raw)
    {
        Assert.False(ItemId.TryCreate(raw, out var id));
        Assert.True(id.IsEmpty);
    }

    [Fact]
    public void TryCreate_RealSlug_SucceedsAndPreservesTheValue()
    {
        Assert.True(ItemId.TryCreate("divine", out var id));
        Assert.Equal("divine", id.Value);
    }

    [Fact]
    public void TryCreate_DoesNotNormalise()
    {
        Assert.True(ItemId.TryCreate("  divine  ", out var id));
        Assert.Equal("  divine  ", id.Value);
    }

    [Fact]
    public void Equality_IsOrdinalAndCaseSensitive()
    {
        Assert.NotEqual(new ItemId("Divine"), new ItemId("divine"));
        Assert.Equal(new ItemId("divine"), new ItemId("divine"));
    }

    [Fact]
    public void ToString_ForARealSlug_IsTheValue()
    {
        Assert.Equal("divine", new ItemId("divine").ToString());
    }
}
