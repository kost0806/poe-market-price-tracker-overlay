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
    Task<MarketResult<CategorySnapshot>> FetchCategoryAsync(
        string league,
        ExchangeCategory category,
        RequestPriority priority,
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
