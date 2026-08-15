namespace PoeOverlay.Core.Domain;

/// <summary>
/// The chaos-per-divine rate in force for a data world (S2 2.7 / S4 3.6).
/// </summary>
/// <remarks>
/// <c>ChaosPerDivine &gt; 0</c>, taken from the Currency response line with <c>id="divine"</c>;
/// the reciprocal of <c>core.rates.divine</c> is forbidden (D1).
/// <para>
/// The most important invariant of this record: inheritance rewrites <see cref="Inherited"/> and
/// nothing else. Refreshing <see cref="AcquiredAt"/> on inheritance postpones the staleness
/// verdict forever and neutralises D9 and D16 entirely. <see cref="League"/> is likewise not
/// rewritten, and <see cref="Inherited"/> stays true until a fresh rate replaces the record.
/// </para>
/// </remarks>
public sealed record DivineRate(decimal ChaosPerDivine, DateTimeOffset AcquiredAt, string League, bool Inherited);
