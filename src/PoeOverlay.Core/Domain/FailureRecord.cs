namespace PoeOverlay.Core.Domain;

/// <summary>
/// Classified category-level failure kinds (S2 2.12 / S4 3.8).
/// </summary>
/// <remarks>
/// There is deliberately no <c>Canceled</c> member: cancellation is control flow, not failure.
/// <para>
/// There is likewise no <c>ElementFault</c> member. S2 5th ed. removed it on the S4 19.2 finding:
/// element-level faults are tallied into <see cref="SkipCounts.ElementFault"/> and surface at
/// category level as a <see cref="FieldMissingRatio"/> failure carrying the <c>ElementFaultRatio</c>
/// code (S4 13.1). No decision path ever produced the Kind, so nothing referred to it.
/// </para>
/// </remarks>
public enum FailureKind
{
    Network,
    Timeout,
    HttpStatus,
    RateLimited,
    Deserialization,
    EmptyLines,
    NoPricedLines,
    FieldMissingRatio,
    PrimaryCurrencyMismatch,
    DivineLineMissing,
    MedianJump,
    LeagueListInvalid,
    MappingFault,
}

/// <summary>
/// A single classified failure (S2 2.12 / S4 3.8).
/// </summary>
/// <param name="Code">A literal from the S4 13 catalogue, sharing a string space with <see cref="ErrorRecord.Code"/>.</param>
public sealed record FailureRecord(
    FailureKind Kind,
    string Code,
    DateTimeOffset At,
    int? HttpStatus,
    string? Detail,
    string? ExceptionType);
