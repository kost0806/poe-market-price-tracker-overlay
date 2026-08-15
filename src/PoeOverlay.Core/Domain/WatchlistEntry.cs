namespace PoeOverlay.Core.Domain;

/// <summary>
/// One watchlist row as stored in settings (S2 2.4 / S4 3.4).
/// </summary>
/// <remarks>
/// Enforced by the Settings validator, not here (D-D0): <c>Id.IsEmpty == false</c> (the sole
/// discard reason), ids unique within the list (first wins), insertion order preserved, and
/// entries whose <see cref="Category"/> is unresolved are still preserved.
/// A null <see cref="DisplayCurrency"/> means "omitted, inherit the global default" and is
/// distinct from an explicit <see cref="Domain.DisplayCurrency.Auto"/>.
/// </remarks>
public sealed record WatchlistEntry(ItemId Id, CategoryRef Category, DisplayCurrency? DisplayCurrency);
