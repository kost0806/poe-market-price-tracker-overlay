using PoeOverlay.Core.Domain;

namespace PoeOverlay.Core.Presentation.ViewModels.Rows;

/// <summary>
/// One row of the settings window's search results (S4 11.3 D-DL16).
/// </summary>
/// <remarks>
/// <para>
/// The list used to bind <c>SearchHit</c> straight from the Store, and every hit whose
/// <c>ApiName</c> was null rendered as a blank row showing only its category. The name fallback
/// belongs to a view model exactly as it does for <see cref="PriceRowViewModel"/>: the Store may not
/// reference <c>Localization</c> (S2 1.2), so <c>SearchHit</c> cannot carry a display name, and no
/// XAML fallback reaches this — <c>TargetNullValue</c> and <c>PriorityBinding</c> both treat null as
/// a successful binding, not as an absent one.
/// </para>
/// <para>
/// <paramref name="Id"/> and <paramref name="Category"/> are what adding to the watchlist needs, so
/// the row is a complete command parameter and the view never has to reach back for the hit.
/// </para>
/// </remarks>
/// <param name="DisplayName">Localised name, API name, or the slug — the ③④⑤ chain of S2 3.4.</param>
/// <param name="CategoryLabel">
/// What the category column draws (S4 14.10). <paramref name="Category"/> stays because the
/// watchlist entry is built from it; a label cannot stand in for the value.
/// </param>
/// <param name="PriceText">
/// Empty for a row that came from the fetched cache, and <c>ui.settings.search.noPrice</c> for one
/// that came only from the shipped catalogue (S3 5.4.6, D-DL29). It is a marker, not a price: what
/// a thing costs is the overlay's column, and an unlabelled number here would have to answer "in
/// which currency" — a question this list does not exist to ask.
/// </param>
public sealed record SearchRowViewModel(
    ItemId Id,
    string DisplayName,
    ExchangeCategory Category,
    string CategoryLabel,
    string PriceText);
