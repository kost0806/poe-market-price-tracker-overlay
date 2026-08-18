using System.Globalization;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Market;
using Xunit;

namespace PoeOverlay.Core.Tests.Market;

/// <summary>
/// S2 11.7 M1–M6, M9–M11, M10′ — the structural validation order of S2 5.5.3 and the element-wise
/// deserialisation of D-MK2.
/// </summary>
public sealed class CategoryFetchTests
{
    internal static async Task<MarketResult<CategorySnapshot>> FetchAsync(string body)
    {
        var handler = new StubHandler(body);
        var client = MarketTestHarness.CreateClient(handler, out var time);

        return await MarketTestHarness.RunAsync(
            time,
            client.FetchCategoryAsync("Allflame", ExchangeCategory.Currency, RequestPriority.Polling, held: null, CancellationToken.None))
            .ConfigureAwait(false);
    }

    internal static FailureRecord Why(MarketResult<CategorySnapshot> result)
        => Assert.IsType<MarketResult<CategorySnapshot>.Fail>(result).Why;

    internal static CategorySnapshot Value(MarketResult<CategorySnapshot> result)
        => Assert.IsType<MarketResult<CategorySnapshot>.Ok>(result).Value;

    private static string ZeroValueLine(int index)
        => string.Create(CultureInfo.InvariantCulture, $$"""{"id":"item-{{index}}","primaryValue":0}""");

    [Fact]
    public async Task Measured_CurrencyBody_MapsEveryLineAndTakesTheLowerMedian()
    {
        var result = await FetchAsync(MarketTestHarness.Fixture("currency-measured.json")).ConfigureAwait(false);

        var snapshot = Value(result);
        Assert.Equal(3, snapshot.Items.Count);
        Assert.Equal(194.6m, snapshot.Items[new ItemId("divine")].PrimaryValue);
        Assert.Equal("Vivid Lifeforce", snapshot.Items[new ItemId("vivid-lifeforce")].ApiName);
        Assert.Equal(30.46, snapshot.Items[new ItemId("vivid-lifeforce")].TotalChangePercent);

        // sorted: 0.06401, 1, 194.6 -> sorted[(3-1)/2] = 1. The lower median, not the mean.
        Assert.Equal(1m, snapshot.MedianPrimaryValue);
        Assert.Equal(ExchangeCategory.Currency, snapshot.Items[new ItemId("divine")].SelfReportedCategory);
        Assert.Equal("Allflame", snapshot.League);
        Assert.Equal(MarketTestHarness.Start, snapshot.FetchedAt);
    }

    [Fact]
    public async Task M1_EmptyLines_FailsWithEmptyLines()
    {
        var result = await FetchAsync(MarketTestHarness.Fixture("empty-lines.json")).ConfigureAwait(false);

        Assert.Equal(FailureKind.EmptyLines, Why(result).Kind);
        Assert.Equal("EmptyLines", Why(result).Code);
    }

    [Fact]
    public async Task M2_FiveOfTwentyNonPositive_FailsWithAllNonPositive()
    {
        var body = MarketTestHarness.Overview(
            20,
            i => i < 5 ? ZeroValueLine(i) : MarketTestHarness.GoodLine(i));

        var why = Why(await FetchAsync(body).ConfigureAwait(false));

        Assert.Equal(FailureKind.FieldMissingRatio, why.Kind);
        Assert.Equal("AllNonPositive", why.Code);
        Assert.Equal("blank=0 nonpos=5 dup=0 fault=0", why.Detail);
    }

    [Fact]
    public async Task M3_TwoOfTwentyNonPositive_SucceedsAndKeepsTheSkippedSlugs()
    {
        var body = MarketTestHarness.Overview(
            20,
            i => i < 2 ? ZeroValueLine(i) : MarketTestHarness.GoodLine(i));

        var snapshot = Value(await FetchAsync(body).ConfigureAwait(false));

        Assert.Equal(2, snapshot.Skips.Total);
        Assert.Equal(18, snapshot.Items.Count);
        Assert.Equal(2, snapshot.SkippedIds.Count);
        Assert.False(snapshot.SkippedIdsTruncated);
        Assert.Equal(20, snapshot.RawLineCount);
    }

    [Fact]
    public async Task M4_OneOfThreeInvalid_SucceedsUnderTheSmallSampleException()
    {
        var body = MarketTestHarness.Overview(3, i => i == 0 ? ZeroValueLine(i) : MarketTestHarness.GoodLine(i));

        var snapshot = Value(await FetchAsync(body).ConfigureAwait(false));

        Assert.Equal(2, snapshot.Items.Count);
        Assert.Equal(1, snapshot.Skips.NonPositiveValue);
    }

    [Fact]
    public async Task M5_PrimaryIsDivine_FailsBeforeLinesAreEvenLookedAt()
    {
        // The body is also empty of lines: were step 4 to run first this would report EmptyLines,
        // filing a collapsed premise as an ordinary empty market.
        var body = MarketTestHarness.Overview(0, _ => string.Empty, primary: "divine");

        var why = Why(await FetchAsync(body).ConfigureAwait(false));

        Assert.Equal(FailureKind.PrimaryCurrencyMismatch, why.Kind);
        Assert.Equal("PrimaryCurrencyMismatch", why.Code);
    }

    [Fact]
    public async Task M6_LineWithNoMatchingNameTableEntry_KeepsThePriceAndCountsTheJoinMiss()
    {
        // Five lines, but only three name table rows: two lines cannot be joined.
        var body = MarketTestHarness.Overview(5, MarketTestHarness.GoodLine, itemCount: 3);

        var snapshot = Value(await FetchAsync(body).ConfigureAwait(false));

        Assert.Equal(5, snapshot.Items.Count);
        Assert.Equal(2, snapshot.JoinMissCount);
        Assert.Null(snapshot.Items[new ItemId("item-4")].ApiName);
        Assert.Equal("Item 0", snapshot.Items[new ItemId("item-0")].ApiName);
    }

    [Fact]
    public async Task M9_EveryLineMissingItsId_FailsWithMissingIdRatio()
    {
        var body = MarketTestHarness.Overview(
            20,
            i => string.Create(CultureInfo.InvariantCulture, $$"""{"primaryValue":{{i + 1}}}"""));

        var why = Why(await FetchAsync(body).ConfigureAwait(false));

        Assert.Equal(FailureKind.FieldMissingRatio, why.Kind);
        Assert.Equal("MissingIdRatio", why.Code);
        Assert.Equal("blank=20 nonpos=0 dup=0 fault=0", why.Detail);
    }

    [Fact]
    public async Task M10_SecondLineHasAStringNumber_TheFirstLineSurvives()
    {
        // The regression that element-wise deserialisation exists for: under NumberHandling.Strict
        // one string-typed number kills the whole document, healthy lines included.
        var body = """
            {"core":{"primary":"chaos","secondary":"divine","items":[{"id":"item-0","name":"Item 0"}]},
             "lines":[{"id":"item-0","primaryValue":1.5},{"id":"item-1","primaryValue":"0.5"}]}
            """;

        var snapshot = Value(await FetchAsync(body).ConfigureAwait(false));

        Assert.Single(snapshot.Items);
        Assert.Equal(1.5m, snapshot.Items[new ItemId("item-0")].PrimaryValue);
        Assert.Equal(1, snapshot.Skips.ElementFault);
        Assert.Equal(2, snapshot.RawLineCount);
    }

    [Fact]
    public async Task M10Prime_EveryLineHasAStringNumber_FailsWithElementFaultRatio()
    {
        var body = MarketTestHarness.Overview(
            6,
            i => string.Create(CultureInfo.InvariantCulture, $$"""{"id":"item-{{i}}","primaryValue":"0.5"}"""));

        var why = Why(await FetchAsync(body).ConfigureAwait(false));

        Assert.Equal(FailureKind.FieldMissingRatio, why.Kind);
        Assert.Equal("ElementFaultRatio", why.Code);
        Assert.Equal("blank=0 nonpos=0 dup=0 fault=6", why.Detail);
    }

    [Fact]
    public async Task M11_UnknownMemberOnALine_IsIgnoredBecauseAddingFieldsIsNormalEvolution()
    {
        var body = MarketTestHarness.Overview(
            5,
            i => string.Create(
                CultureInfo.InvariantCulture,
                $$$"""{"id":"item-{{{i}}}","primaryValue":{{{i + 1}}},"newField":{"nested":true}}"""));

        var snapshot = Value(await FetchAsync(body).ConfigureAwait(false));

        Assert.Equal(5, snapshot.Items.Count);
        Assert.Equal(0, snapshot.Skips.Total);
    }

    [Fact]
    public async Task DuplicateId_FirstWins_AndTheSecondIsCounted()
    {
        var body = """
            {"core":{"primary":"chaos","items":[{"id":"item-0","name":"Item 0"}]},
             "lines":[{"id":"item-0","primaryValue":1},{"id":"item-0","primaryValue":99}]}
            """;

        var snapshot = Value(await FetchAsync(body).ConfigureAwait(false));

        Assert.Equal(1m, snapshot.Items[new ItemId("item-0")].PrimaryValue);
        Assert.Equal(1, snapshot.Skips.Duplicate);
    }

    [Fact]
    public async Task EveryLineNonPositiveInASmallSample_ReportsNoPricedLinesRatherThanARatio()
    {
        // Step 8 is reachable precisely because step 7 no longer folds mapped == 0 into itself:
        // "no listings priced yet" is a normal market state, not a schema collapse, and merging the
        // two would put it into an exponential cooldown.
        var body = MarketTestHarness.Overview(3, ZeroValueLine);

        var why = Why(await FetchAsync(body).ConfigureAwait(false));

        Assert.Equal(FailureKind.NoPricedLines, why.Kind);
        Assert.Equal("NoPricedLines", why.Code);
    }
}
