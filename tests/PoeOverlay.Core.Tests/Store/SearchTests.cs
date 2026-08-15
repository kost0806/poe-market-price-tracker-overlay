using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Store;
using Xunit;

namespace PoeOverlay.Core.Tests.Store;

/// <summary>
/// S2 11.8 S9–S15 — cross-category search (S2 6.7 D-ST5 / D-ST6, FR-01-1).
/// </summary>
public sealed class SearchTests
{
    private static readonly SearchOptions Default = new(200, null);

    private static async Task<StoreHarness> WithCacheAsync()
    {
        var harness = await StoreHarness.StartAsync().ConfigureAwait(false);
        harness.Store.BeginNewLeague(StoreTestHarness.League, 1);
        harness.Store.CommitCategory(
            StoreTestHarness.Tag,
            StoreTestHarness.Snapshot(
                ExchangeCategory.Currency,
                items: [
                    StoreTestHarness.Price("vivid-lifeforce", 0.064m, "Vivid Lifeforce"),
                    StoreTestHarness.Price("divine", 194.6m, "Divine Orb")
                ]));
        await harness.WaitForVersionAsync(2).ConfigureAwait(false);
        return harness;
    }

    [Fact]
    public async Task S9_TheSameIdInBothSlots_IsReportedOnceFromTheRoundCommit()
    {
        using var harness = await WithCacheAsync().ConfigureAwait(false);

        harness.Store.SetFetchedListing(
            StoreTestHarness.Tag,
            ExchangeCategory.Scarab,
            StoreTestHarness.Snapshot(
                ExchangeCategory.Scarab,
                items: [StoreTestHarness.Price("divine", 999m, "Divine Orb")]));
        await harness.WaitForVersionAsync(3).ConfigureAwait(false);

        var result = harness.Store.Search("divine", Default);

        // The round commit wins: it is the data that passed the context checks, and Source keeps
        // the provenance in the type rather than in a comment.
        var hit = Assert.Single(result.Hits);
        Assert.Equal(SearchSource.RoundCommitted, hit.Source);
        Assert.Equal(ExchangeCategory.Currency, hit.Category);
        Assert.Equal(194.6m, hit.PrimaryValue);
        Assert.Equal(SearchOutcome.Found, result.Outcome);
    }

    [Fact]
    public async Task S10_ACacheWithNoMatch_IsNotInCacheAndNamesTheUnfetchedCategories()
    {
        using var harness = await WithCacheAsync().ConfigureAwait(false);

        var result = harness.Store.Search("headhunter", Default);

        Assert.Empty(result.Hits);
        Assert.Equal(SearchOutcome.NotInCache, result.Outcome);
        Assert.DoesNotContain(ExchangeCategory.Currency, result.UnfetchedCategories);
        Assert.Contains(ExchangeCategory.Scarab, result.UnfetchedCategories);
        Assert.Equal(17, result.UnfetchedCategories.Count);
    }

    [Fact]
    public async Task S11_SearchingBeforeTheFirstRound_IsCacheEmpty()
    {
        // Saying "not in the cache" here makes the user fetch categories by hand for something
        // waiting would have delivered — traffic NFR-02 forbids, and the arriving round then
        // duplicates the same data.
        using var harness = await StoreHarness.StartAsync().ConfigureAwait(false);

        var result = harness.Store.Search("divine", Default);

        Assert.Empty(result.Hits);
        Assert.Equal(SearchOutcome.CacheEmpty, result.Outcome);
        Assert.Equal(18, result.UnfetchedCategories.Count);
    }

    [Fact]
    public async Task S12_MixedCaseQueryAgainstALowerCaseSlug_Matches()
    {
        using var harness = await WithCacheAsync().ConfigureAwait(false);

        var result = harness.Store.Search("Vivid", Default);

        // OrdinalIgnoreCase. Identity and search are different operations: a case-sensitive search
        // would find nothing here while breaking no stated rule.
        Assert.Equal(new ItemId("vivid-lifeforce"), Assert.Single(result.Hits).Id);
    }

    [Fact]
    public async Task S13_AnExtraMatchThatThrows_LosesOnlyThatItemAndWarnsOnce()
    {
        using var harness = await WithCacheAsync().ConfigureAwait(false);

        var options = new SearchOptions(200, (_, _) => throw new InvalidOperationException("bad predicate"));

        var first = harness.Store.Search("divine", options);
        var second = harness.Store.Search("divine", options);

        Assert.Single(first.Hits);
        Assert.Single(second.Hits);
        Assert.Single(harness.Logger.WithCode("ExtraMatchFault"));
    }

    [Fact]
    public async Task S13_AnExtraMatchThatSucceeds_AddsAHitTheDirectPredicateMisses()
    {
        using var harness = await WithCacheAsync().ConfigureAwait(false);

        var options = new SearchOptions(200, (id, _) => id.Value == "vivid-lifeforce");

        var result = harness.Store.Search("신성한", options);

        Assert.Equal(new ItemId("vivid-lifeforce"), Assert.Single(result.Hits).Id);
    }

    [Fact]
    public async Task S14_UnrelatedCommandsAfterAWatchlistEdit_LeaveTheResultsAlone()
    {
        // C2 regression: editing the watchlist raises roundGeneration, which never reaches the
        // store at all, so nothing about it can invalidate the cache.
        using var harness = await WithCacheAsync().ConfigureAwait(false);

        harness.Store.RecordHeartbeatAttempt(2);
        harness.Store.RecordHeartbeatOutcome(RoundOutcome.Canceled);
        await harness.WaitForVersionAsync(4).ConfigureAwait(false);

        var result = harness.Store.Search("divine", Default);

        Assert.Single(result.Hits);
        Assert.Equal(SearchOutcome.Found, result.Outcome);
    }

    [Fact]
    public async Task S15_AUserFetchDuringAWatchlistEdit_IsNotRejected()
    {
        // Only a league change moves the epoch, so a fetch begun before an edit still commits.
        using var harness = await WithCacheAsync().ConfigureAwait(false);

        harness.Store.SetFetchedListing(
            StoreTestHarness.Tag,
            ExchangeCategory.Essence,
            StoreTestHarness.Snapshot(ExchangeCategory.Essence, items: [StoreTestHarness.Price("deafening-essence", 12m)]));
        await harness.WaitForVersionAsync(3).ConfigureAwait(false);

        Assert.Equal(0, harness.Current.RejectedCommitCount);

        var result = harness.Store.Search("deafening", Default);
        var hit = Assert.Single(result.Hits);
        Assert.Equal(SearchSource.UserFetched, hit.Source);
        Assert.Equal(ExchangeCategory.Essence, hit.Category);
    }

    [Fact]
    public async Task RankingPutsExactMatchesFirstThenPrefixesThenSubstrings()
    {
        using var harness = await StoreHarness.StartAsync().ConfigureAwait(false);

        harness.Store.BeginNewLeague(StoreTestHarness.League, 1);
        harness.Store.CommitCategory(
            StoreTestHarness.Tag,
            StoreTestHarness.Snapshot(
                ExchangeCategory.Currency,
                items: [
                    StoreTestHarness.Price("wild-orb", 3m),
                    StoreTestHarness.Price("orb-of-fusing", 2m),
                    StoreTestHarness.Price("orb", 1m)
                ]));
        await harness.WaitForVersionAsync(2).ConfigureAwait(false);

        var result = harness.Store.Search("orb", Default);

        Assert.Equal(
            new[] { "orb", "orb-of-fusing", "wild-orb" },
            result.Hits.Select(h => h.Id.Value));
    }

    [Fact]
    public async Task TheLimitIsClampedAndTruncationIsReported()
    {
        using var harness = await WithCacheAsync().ConfigureAwait(false);

        var result = harness.Store.Search("i", new SearchOptions(1, null));

        Assert.Single(result.Hits);
        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task ABlankQueryReportsTheCacheStateWithoutHits()
    {
        using var harness = await WithCacheAsync().ConfigureAwait(false);

        var result = harness.Store.Search("   ", Default);

        Assert.Empty(result.Hits);
        Assert.Equal(SearchOutcome.NotInCache, result.Outcome);
    }
}
