using System.Text.Json;
using System.Text.Json.Serialization;
using PoeOverlay.Core.Market.Dtos;
using Xunit;

namespace PoeOverlay.Core.Tests.Market;

/// <summary>
/// S2 11.7 M22 — the five strictness options of S2 5.3 / S4 7.2, asserted on the generated context.
/// </summary>
/// <remarks>
/// All five are .NET 8 defaults, so the documentation fixes nothing on its own. The actual risk is
/// somebody reaching for <c>JsonSerializerDefaults.Web</c>, which turns
/// <c>PropertyNameCaseInsensitive</c> on and quietly removes D8-b's ability to notice a renamed
/// field. This test is the only thing standing in the way.
/// </remarks>
public sealed class JsonContextOptionsTests
{
    private static readonly JsonSerializerOptions Options = NinjaJsonContext.Default.Options;

    [Fact]
    public void M22_PropertyNameCaseInsensitiveIsOff()
    {
        Assert.False(Options.PropertyNameCaseInsensitive);
    }

    [Fact]
    public void M22_NumberHandlingIsStrict()
    {
        Assert.Equal(JsonNumberHandling.Strict, Options.NumberHandling);
    }

    [Fact]
    public void M22_TrailingCommasAndCommentsAreRefused()
    {
        Assert.False(Options.AllowTrailingCommas);
        Assert.Equal(JsonCommentHandling.Disallow, Options.ReadCommentHandling);
    }

    [Fact]
    public void M22_UnmappedMembersAreSkippedBecauseAddingFieldsIsNormalEvolution()
    {
        Assert.Equal(JsonUnmappedMemberHandling.Skip, Options.UnmappedMemberHandling);
    }

    [Fact]
    public void M22_DefaultIgnoreConditionIsNeverSoAMissingFieldStaysVisible()
    {
        Assert.Equal(JsonIgnoreCondition.Never, Options.DefaultIgnoreCondition);
    }

    [Fact]
    public void M22_ACasedFieldNameIsNotSilentlyAccepted()
    {
        // The behavioural half of the assertion: strictness that only lives in a property value can
        // be reinstated by accident, but this line fails the moment case-insensitivity returns.
        var dto = JsonSerializer.Deserialize("""{"PrimaryValue":1.5,"id":"a"}""", NinjaJsonContext.Default.LineDto);

        Assert.NotNull(dto);
        Assert.Null(dto.PrimaryValue);
    }
}
