namespace PoeOverlay.Core.Domain;

/// <summary>
/// Failure and retry state for one category (S2 2.6 D-D4 / S4 3.5).
/// </summary>
/// <remarks>The only source for failure badges, cooldowns and the retry UI.</remarks>
/// <param name="NeverNonEmpty">True while the category has never once returned a non-empty body (S2 12-27).</param>
public sealed record CategoryStatus(
    ExchangeCategory Category,
    int ConsecutiveFailures,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? CooldownUntil,
    FailureRecord? LastFailure,
    int ConsecutiveMedianJumps,
    DateTimeOffset? LastForcedAcceptAt,
    bool NeverNonEmpty);
