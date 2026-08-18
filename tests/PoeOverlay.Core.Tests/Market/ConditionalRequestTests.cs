using System.Net;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Market;
using PoeOverlay.Core.Tests.Presentation;
using Xunit;

namespace PoeOverlay.Core.Tests.Market;

/// <summary>
/// S2 11.7 M24–M28 — conditional requests and the 304 answer (HLD D24 / NFR-02).
/// </summary>
/// <remarks>
/// Every case that involves a 304 asserts on the request headers as well as the returned snapshot.
/// The return value alone cannot tell "answered from the held copy" from "fetched the body again":
/// whenever the price has not moved, both produce the same items, and S2 5.11 says so explicitly.
/// </remarks>
public sealed class ConditionalRequestTests
{
    private const string ServerETag = "W/ab89be242028f7941cde9c72d73db872";

    /// <summary>The header as poe.ninja actually sends it — a weak tag with no quotes (S2 5.11).</summary>
    private static HttpResponseMessage WithETag(string body, string? eTag)
    {
        var response = MarketTestHarness.Json(body);
        if (eTag is not null)
        {
            response.Headers.TryAddWithoutValidation("ETag", eTag);
        }

        return response;
    }

    private static CategorySnapshot Held(string? eTag, string league = "Allflame")
        => SnapshotBuilder.Category(
            ExchangeCategory.Currency,
            MarketTestHarness.Start - TimeSpan.FromHours(1),
            [SnapshotBuilder.Price("divine", 200m)]) with
        {
            League = league,
            ETag = eTag,
        };

    private static async Task<(MarketResult<CategorySnapshot> Result, List<string?> Sent)> FetchAsync(
        Func<HttpRequestMessage, int, HttpResponseMessage> responder,
        CategorySnapshot? held,
        string league = "Allflame")
    {
        var sent = new List<string?>();
        var handler = new StubHandler((request, call) =>
        {
            sent.Add(request.Headers.TryGetValues("If-None-Match", out var values)
                ? string.Join(',', values)
                : null);

            return responder(request, call);
        });

        var client = MarketTestHarness.CreateClient(handler, out var time);
        var result = await MarketTestHarness.RunAsync(
            time,
            client.FetchCategoryAsync(league, ExchangeCategory.Currency, RequestPriority.Polling, held, CancellationToken.None))
            .ConfigureAwait(false);

        return (result, sent);
    }

    [Fact]
    public async Task M24_AHeldETag_IsSentAsIfNoneMatch()
    {
        var (_, sent) = await FetchAsync(
            (_, _) => WithETag(MarketTestHarness.Fixture("currency-measured.json"), ServerETag),
            Held(ServerETag)).ConfigureAwait(false);

        Assert.Equal(ServerETag, Assert.Single(sent));
    }

    [Fact]
    public async Task M24_NothingHeld_SendsNoValidator()
    {
        var (_, sent) = await FetchAsync(
            (_, _) => WithETag(MarketTestHarness.Fixture("currency-measured.json"), ServerETag),
            held: null).ConfigureAwait(false);

        Assert.Null(Assert.Single(sent));
    }

    [Fact]
    public async Task M25_ASnapshotFromAnotherLeague_IsNotUsedAsAValidator()
    {
        var (_, sent) = await FetchAsync(
            (_, _) => WithETag(MarketTestHarness.Fixture("currency-measured.json"), ServerETag),
            Held(ServerETag, league: "Standard")).ConfigureAwait(false);

        Assert.Null(Assert.Single(sent));
    }

    [Fact]
    public async Task M26_NotModified_ReturnsTheHeldCopyReDatedToNow()
    {
        var held = Held(ServerETag);

        var (result, sent) = await FetchAsync(
            (_, _) => new HttpResponseMessage(HttpStatusCode.NotModified),
            held).ConfigureAwait(false);

        // Both halves matter: the validator went out, and what came back is the copy we already had.
        Assert.Equal(ServerETag, Assert.Single(sent));

        var snapshot = CategoryFetchTests.Value(result);
        Assert.Same(held.Items, snapshot.Items);
        Assert.Equal(held.MedianPrimaryValue, snapshot.MedianPrimaryValue);
        Assert.Equal(ServerETag, snapshot.ETag);

        // The age restarts: poe.ninja has just said these are still its current numbers.
        Assert.Equal(MarketTestHarness.Start, snapshot.FetchedAt);
        Assert.NotEqual(held.FetchedAt, snapshot.FetchedAt);
    }

    [Fact]
    public async Task M27_NotModifiedWithNothingHeld_IsAFailure()
    {
        var (result, _) = await FetchAsync(
            (_, _) => new HttpResponseMessage(HttpStatusCode.NotModified),
            held: null).ConfigureAwait(false);

        var why = CategoryFetchTests.Why(result);
        Assert.Equal(FailureKind.MappingFault, why.Kind);
        Assert.Equal("UnexpectedNotModified", why.Code);
        Assert.Equal(304, why.HttpStatus);
    }

    [Fact]
    public async Task M28_TheResponseETag_RidesOnTheSnapshot()
    {
        var (result, _) = await FetchAsync(
            (_, _) => WithETag(MarketTestHarness.Fixture("currency-measured.json"), ServerETag),
            held: null).ConfigureAwait(false);

        Assert.Equal(ServerETag, CategoryFetchTests.Value(result).ETag);
    }

    [Fact]
    public async Task M28_NoResponseETag_LeavesTheNextRequestUnconditional()
    {
        var (result, _) = await FetchAsync(
            (_, _) => WithETag(MarketTestHarness.Fixture("currency-measured.json"), eTag: null),
            held: null).ConfigureAwait(false);

        Assert.Null(CategoryFetchTests.Value(result).ETag);
    }

    [Fact]
    public async Task ARevalidationThatComesBackChanged_ReplacesTheHeldCopy()
    {
        var held = Held("W/stale");

        var (result, sent) = await FetchAsync(
            (_, _) => WithETag(MarketTestHarness.Fixture("currency-measured.json"), ServerETag),
            held).ConfigureAwait(false);

        Assert.Equal("W/stale", Assert.Single(sent));

        var snapshot = CategoryFetchTests.Value(result);
        Assert.Equal(3, snapshot.Items.Count);           // the fixture's lines, not the held one's
        Assert.Equal(ServerETag, snapshot.ETag);         // and the new validator for the next round
    }
}
