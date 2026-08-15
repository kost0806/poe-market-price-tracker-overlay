using System.Globalization;
using PoeOverlay.Core.Domain;

namespace PoeOverlay.Core.Market;

/// <summary>
/// The measured endpoint templates and the HTTP client identity (S4 7.4 D-DL23 / S4 15.3).
/// </summary>
/// <remarks>
/// The <c>type=</c> query token is <see cref="ExchangeCategory"/>'s member name verbatim; S2 2.2
/// decided there is no separate mapping table, so introducing one here would be a regression.
/// </remarks>
internal static class NinjaEndpoints
{
    /// <summary>Named <c>IHttpClientFactory</c> client. Composition sets User-Agent and an infinite timeout on it.</summary>
    internal const string HttpClientName = "poe.ninja";

    /// <summary>S4 15.3 — identifiable fixed User-Agent.</summary>
    internal const string UserAgent = "PoeOverlayPriceTracker/1.0";

    /// <summary>Measured in 00-api-contract.md 1.1.</summary>
    internal const string LeaguesUrl = "https://poe.ninja/poe1/api/economy/leagues";

    private const string OverviewFormat =
        "https://poe.ninja/poe1/api/economy/exchange/current/overview?league={0}&type={1}";

    /// <summary>Builds the category overview URL, escaping the league (free-form user input, D6).</summary>
    internal static string OverviewUrl(string league, ExchangeCategory category)
        => string.Format(
            CultureInfo.InvariantCulture,
            OverviewFormat,
            Uri.EscapeDataString(league),
            category.ToString());
}
