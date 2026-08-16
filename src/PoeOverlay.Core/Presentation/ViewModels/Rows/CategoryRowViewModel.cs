using PoeOverlay.Core.Domain;

namespace PoeOverlay.Core.Presentation.ViewModels.Rows;

/// <summary>
/// One of the "not fetched yet" category buttons under the search results (S4 11.3 D-DL26).
/// </summary>
/// <remarks>
/// The buttons bound the bare <see cref="ExchangeCategory"/>, so their captions were enum members —
/// <c>AllflameEmber</c>, <c>DivinationCard</c> — which no dictionary translates because nothing
/// looked them up (S3 5.4.3 E13). The row carries both halves: the label to draw and the value
/// <c>FetchCategoryCommand</c> takes as its parameter.
/// </remarks>
public sealed record CategoryRowViewModel(ExchangeCategory Category, string Label);
