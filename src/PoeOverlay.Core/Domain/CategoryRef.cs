namespace PoeOverlay.Core.Domain;

/// <summary>
/// A category as it appears in the watchlist: the raw stored token plus its resolution, if any
/// (S2 2.2 D-D1 / S4 3.2).
/// </summary>
/// <remarks>
/// Invariant: <c>Known is not null =&gt; Raw == Known.Value.ToString()</c>. Used only by the
/// Settings watchlist; Market, Store and Polling deal in <see cref="ExchangeCategory"/> alone.
/// Unknown strings are preserved rather than collapsed into a single <c>Unknown</c> member.
/// </remarks>
public readonly record struct CategoryRef(string Raw, ExchangeCategory? Known)
{
    /// <summary>True when the stored token does not name a known category.</summary>
    public bool IsUnresolved => Known is null;
}
