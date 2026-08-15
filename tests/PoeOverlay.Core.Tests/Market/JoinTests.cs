using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Market;
using Xunit;

namespace PoeOverlay.Core.Tests.Market;

/// <summary>
/// S2 11.7 M6–M7 — the <c>core.items</c> join of contract A1/A2, and E1's counter that stops M7
/// from passing vacuously.
/// </summary>
public sealed class JoinTests
{
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
    public async Task DuplicateCoreItemId_FirstEntryWins()
    {
        var body = """
            {"core":{"primary":"chaos","items":[
                {"id":"item-0","name":"First"},
                {"id":"item-0","name":"Second"}]},
             "lines":[{"id":"item-0","primaryValue":1}]}
            """;

        var snapshot = CategoryFetchTests.Value(await CategoryFetchTests.FetchAsync(body).ConfigureAwait(false));

        Assert.Equal("First", snapshot.Items[new ItemId("item-0")].ApiName);
    }

    [Fact]
    public async Task CoreItemsWithNoMatchingLine_AreIgnored()
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
