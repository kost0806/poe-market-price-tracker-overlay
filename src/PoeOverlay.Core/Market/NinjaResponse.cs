namespace PoeOverlay.Core.Market;

/// <summary>
/// One answer from poe.ninja, before anything is parsed (S2 5.11).
/// </summary>
/// <remarks>
/// <c>304</c> is a third kind of answer at the transport layer only. Above it the distinction is
/// gone again: <c>FetchCategoryAsync</c> turns a not-modified response into the held snapshot
/// re-dated to now, so <c>MarketResult</c> keeps its two cases and Polling's commit path keeps its
/// one shape (D24).
/// </remarks>
/// <param name="Body">The response body, empty when <paramref name="NotModified"/> — a 304 has none.</param>
/// <param name="ETag">The raw validator header, or null when the response carried none.</param>
internal readonly record struct NinjaResponse(string Body, string? ETag, bool NotModified);
