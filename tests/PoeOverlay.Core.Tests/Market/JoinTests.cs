using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Market;
using Xunit;

namespace PoeOverlay.Core.Tests.Market;

/// <summary>
/// S2 11.7 M6–M7 — the two joins of contract A1/A2/A6, and E1's counter that stops M7 from passing
/// vacuously.
/// </summary>
public sealed class JoinTests
{
    /// <summary>
    /// The honest response shape: a populated root name table, and a two-entry rate basis.
    /// </summary>
    /// <remarks>
    /// The regression this exists for shipped: names were read from <c>core.items</c>, which holds
    /// exactly <c>[chaos, divine]</c> in all 18 categories, so six of seven watchlist rows rendered
    /// as raw slugs. Against that binding "vivid-lifeforce" below resolves to nothing.
    /// </remarks>
    private const string ThreeKeyBody = """
        {"core":{"primary":"chaos","secondary":"divine","items":[
            {"id":"chaos","name":"Chaos Orb","category":"Currency"},
            {"id":"divine","name":"Divine Orb","category":"Currency"}]},
         "lines":[{"id":"vivid-lifeforce","primaryValue":0.06401},
                  {"id":"divine","primaryValue":194.6}],
         "items":[{"id":"vivid-lifeforce","name":"Vivid Lifeforce","category":"Currency"}]}
        """;

    [Fact]
    public async Task NamesComeFromTheRootItemsTable_NotFromCoreItems()
    {
        var snapshot = CategoryFetchTests.Value(await CategoryFetchTests.FetchAsync(ThreeKeyBody).ConfigureAwait(false));

        // Present in the name table only.
        Assert.Equal("Vivid Lifeforce", snapshot.Items[new ItemId("vivid-lifeforce")].ApiName);

        // Present in core.items only — and core.items is not a name table. Reading names from it is
        // exactly the shipped bug, so this is the assertion that tells the two bindings apart: the
        // old one answers "Divine Orb" here and nothing at all above.
        Assert.Null(snapshot.Items[new ItemId("divine")].ApiName);
        Assert.Equal(1, snapshot.JoinMissCount);
    }

    [Fact]
    public async Task SelfReportedCategoryStillComesFromCoreItems()
    {
        // A6 is the one thing core.items is for: its category equals the query type, while the root
        // items[].category is a display grouping that would disagree on nearly every response.
        var snapshot = CategoryFetchTests.Value(await CategoryFetchTests.FetchAsync(ThreeKeyBody).ConfigureAwait(false));

        Assert.Equal(ExchangeCategory.Currency, snapshot.Items[new ItemId("divine")].SelfReportedCategory);

        // Absent from the rate basis, so there is nothing self-describing to report — and its
        // absence is the normal shape of that array, never a mismatch.
        Assert.Null(snapshot.Items[new ItemId("vivid-lifeforce")].SelfReportedCategory);
    }

    [Fact]
    public async Task RootItemsCategoryIsNeverReadAsTheSelfDescribingOne()
    {
        // The display grouping and the query type genuinely differ in the measured data
        // (Fragments/Cards/Essences/Catalysts/Ancestor/Delve). Were A6 pointed at the root array,
        // this body would raise a mismatch warning — which is what it would do on almost every
        // real response.
        var body = """
            {"core":{"primary":"chaos","items":[{"id":"chaos","name":"Chaos Orb","category":"Currency"}]},
             "lines":[{"id":"rusted-breach","primaryValue":5}],
             "items":[{"id":"rusted-breach","name":"Rusted Breachstone","category":"Fragments"}]}
            """;

        var handler = new StubHandler(body);
        var client = MarketTestHarness.CreateClient(handler, out var time, out var logger);

        var result = await MarketTestHarness.RunAsync(
            time,
            client.FetchCategoryAsync("Allflame", ExchangeCategory.Currency, RequestPriority.Polling, CancellationToken.None))
            .ConfigureAwait(false);

        var snapshot = CategoryFetchTests.Value(result);
        Assert.Equal("Rusted Breachstone", snapshot.Items[new ItemId("rusted-breach")].ApiName);
        Assert.Null(snapshot.Items[new ItemId("rusted-breach")].SelfReportedCategory);
        Assert.Empty(logger.WithCode("CategoryMismatch"));
    }

    [Fact]
    public async Task NoRootItemsAtAll_LosesTheNamesWithoutLosingThePrices()
    {
        // A missing name table shortens the fallback chain; it is not a schema collapse, and the
        // count is the observable that says so.
        var body = """
            {"core":{"primary":"chaos","items":[{"id":"chaos","name":"Chaos Orb","category":"Currency"}]},
             "lines":[{"id":"a","primaryValue":1},{"id":"b","primaryValue":2}]}
            """;

        var snapshot = CategoryFetchTests.Value(await CategoryFetchTests.FetchAsync(body).ConfigureAwait(false));

        Assert.Equal(2, snapshot.Items.Count);
        Assert.Equal(2, snapshot.JoinMissCount);
        Assert.Null(snapshot.Items[new ItemId("a")].ApiName);
    }

    [Fact]
    public async Task M7_FiveHundredItemsByFiveHundredLines_BuildsTheJoinDictionaryExactlyOnce()
    {
        var body = MarketTestHarness.Overview(500, MarketTestHarness.GoodLine);
        var handler = new StubHandler(body);
        var client = MarketTestHarness.CreateClient(handler, out var time);

        var result = await MarketTestHarness.RunAsync(
            time,
            client.FetchCategoryAsync("Allflame", ExchangeCategory.Currency, RequestPriority.Polling, CancellationToken.None))
            .ConfigureAwait(false);

        Assert.Equal(500, CategoryFetchTests.Value(result).Items.Count);

        // A per-item rebuild makes this grow with the item count; a linear scan makes it stop
        // growing. Either regression moves the number away from one.
        Assert.Equal(1, client.JoinDictionaryBuildCount);
    }

    [Fact]
    public async Task DuplicateNameTableId_FirstEntryWins()
    {
        var body = """
            {"core":{"primary":"chaos","items":[{"id":"chaos","name":"Chaos Orb","category":"Currency"}]},
             "lines":[{"id":"item-0","primaryValue":1}],
             "items":[{"id":"item-0","name":"First"},
                      {"id":"item-0","name":"Second"}]}
            """;

        var snapshot = CategoryFetchTests.Value(await CategoryFetchTests.FetchAsync(body).ConfigureAwait(false));

        Assert.Equal("First", snapshot.Items[new ItemId("item-0")].ApiName);
    }

    [Fact]
    public async Task NameTableEntriesWithNoMatchingLine_AreIgnored()
    {
        var body = MarketTestHarness.Overview(2, MarketTestHarness.GoodLine, itemCount: 40);

        var snapshot = CategoryFetchTests.Value(await CategoryFetchTests.FetchAsync(body).ConfigureAwait(false));

        Assert.Equal(2, snapshot.Items.Count);
        Assert.Equal(0, snapshot.JoinMissCount);
    }

    [Fact]
    public async Task SelfReportedCategoryDisagreement_IsReportedRatherThanRejected()
    {
        // Contract A6 is a benefit only while a disagreement costs nothing: discarding data on this
        // axis would turn the self-describing category into a hazard.
        var body = """
            {"core":{"primary":"chaos","items":[{"id":"item-0","name":"Item 0","category":"Scarab"}]},
             "lines":[{"id":"item-0","primaryValue":1}]}
            """;

        var handler = new StubHandler(body);
        var client = MarketTestHarness.CreateClient(handler, out var time, out var logger);

        var result = await MarketTestHarness.RunAsync(
            time,
            client.FetchCategoryAsync("Allflame", ExchangeCategory.Currency, RequestPriority.Polling, CancellationToken.None))
            .ConfigureAwait(false);

        var snapshot = CategoryFetchTests.Value(result);
        Assert.Equal(ExchangeCategory.Scarab, snapshot.Items[new ItemId("item-0")].SelfReportedCategory);
        Assert.Single(logger.WithCode("CategoryMismatch"));
    }

    [Fact]
    public async Task UnknownMaxVolumeCurrency_IsRecordedOncePerSession()
    {
        var body = """
            {"core":{"primary":"chaos","items":[]},
             "lines":[{"id":"a","primaryValue":1,"maxVolumeCurrency":"mirror"},
                      {"id":"b","primaryValue":2,"maxVolumeCurrency":" MIRROR "},
                      {"id":"c","primaryValue":3,"maxVolumeCurrency":"Divine"}]}
            """;

        var handler = new StubHandler(body);
        var client = MarketTestHarness.CreateClient(handler, out var time, out var logger);

        await MarketTestHarness.RunAsync(
            time,
            client.FetchCategoryAsync("Allflame", ExchangeCategory.Currency, RequestPriority.Polling, CancellationToken.None))
            .ConfigureAwait(false);

        // Trim + OrdinalIgnoreCase, the same predicate Pricing resolves with (D-C4): "mirror" and
        // " MIRROR " are one token, and "Divine" is not unknown at all.
        Assert.Single(logger.WithCode("UnknownMaxVolumeCurrency"));
    }
}
