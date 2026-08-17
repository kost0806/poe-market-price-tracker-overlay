using PoeOverlay.Core.Domain;

namespace PoeOverlay.Core.Presentation.ViewModels.Rows;

/// <summary>
/// One row of the settings window's watchlist (S3 5.4.3 E14 / S4 11.3 D-DL26).
/// </summary>
/// <remarks>
/// <para>
/// The list bound <c>WatchlistEntry.Id.Value</c> — the raw slug — while the overlay rows and the
/// search results had both been moved onto the name chain. With a Korean dictionary loaded this was
/// the one list still reading <c>abandoned-wealth</c>.
/// </para>
/// <para>
/// <c>WatchlistEntry</c> carries no API name, so the chain runs ③⑤ rather than ③④⑤: an item the
/// dictionary does not have falls to the slug, which is exactly what this list drew before. Nothing
/// is lost for those items and everything else gains a name.
/// </para>
/// </remarks>
/// <param name="Id">The command parameter <c>RemoveFromWatchlistCommand</c> takes.</param>
/// <param name="DisplayName">Localised name, or the slug when the dictionary has no entry.</param>
public sealed record WatchlistRowViewModel(ItemId Id, string DisplayName);
