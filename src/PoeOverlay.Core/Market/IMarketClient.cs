using PoeOverlay.Core.Domain;

namespace PoeOverlay.Core.Market;

/// <summary>
/// The only way into poe.ninja (S4 7.4).
/// </summary>
/// <remarks>
/// Both methods are the "category / league entry points" that own the D-MK4 boundary catch, so
/// neither ever throws except for <see cref="OperationCanceledException"/>, which is control flow
/// and propagates unchanged.
/// </remarks>
public interface IMarketClient
{
    /// <summary>
    /// Fetches, validates and maps one category overview.
    /// </summary>
    /// <remarks>
    /// The returned snapshot carries <see cref="CategorySnapshot.League"/> from
    /// <paramref name="league"/> and <see cref="CategorySnapshot.DataEpoch"/> as <c>0</c>: this
    /// signature has no epoch parameter, and Market has no other way to learn one. The caller
    /// (Polling) must stamp the round's epoch before committing.
    /// </remarks>
    /// <param name="held">
    /// What the caller already has for this category, or null. Its <see cref="CategorySnapshot.ETag"/>
    /// becomes the request's <c>If-None-Match</c> (D24), and a <c>304</c> returns it with its
    /// <see cref="CategorySnapshot.FetchedAt"/> moved to now — the server has just said that copy is
    /// still current. A <c>304</c> with nothing held is a failure: no validator was sent, so the
    /// answer is not one we can act on.
    /// </param>
    Task<MarketResult<CategorySnapshot>> FetchCategoryAsync(
        string league,
        ExchangeCategory category,
        RequestPriority priority,
        CategorySnapshot? held,
        CancellationToken ct);

    /// <summary>
    /// Fetches the league list and renders the S2 5.9 verdict without acting on it.
    /// </summary>
    /// <remarks>
    /// Transport and parse failures come back as an <c>Ok</c> holding a
    /// <see cref="LeagueListStatus.Failed"/> list carrying the failure code, per S2 5.9's judge
    /// table and S4 13.3. <c>Fail</c> is reserved for the D-MK4 boundary catch.
    /// </remarks>
    Task<MarketResult<LeagueList>> FetchLeaguesAsync(RequestPriority priority, CancellationToken ct);
}
