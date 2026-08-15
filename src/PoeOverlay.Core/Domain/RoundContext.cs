namespace PoeOverlay.Core.Domain;

/// <summary>
/// Identity of one polling round (S2 2.8 / S4 3.6).
/// </summary>
/// <remarks>
/// Only created once a league is settled, so <see cref="League"/> is never blank.
/// <see cref="DataEpoch"/> is the only tag that attaches to data and rises only on a league
/// change. <see cref="RoundGeneration"/> attaches to no data at all — it is read inside Polling
/// and never reaches the Store.
/// </remarks>
public sealed record RoundContext(
    string League,
    int DataEpoch,
    int RoundGeneration,
    int RoundNumber,
    DateTimeOffset StartedAt);
