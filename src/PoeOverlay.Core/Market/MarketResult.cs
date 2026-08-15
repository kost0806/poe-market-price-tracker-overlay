using PoeOverlay.Core.Domain;

namespace PoeOverlay.Core.Market;

/// <summary>
/// Market's failure-as-value type (S2 1.5 / 5.6, S4 7.3).
/// </summary>
/// <remarks>
/// <para>
/// An abstract record hierarchy rather than a struct. A struct version cannot be consumed under
/// <c>WarningsAsErrors=nullable</c> without <c>!</c> (which S2 1.6 forbids), and adding
/// <c>MemberNotNullWhen</c> to make it compile produces a <c>default</c> value that claims success
/// while its <c>Value</c> is null — an analyzer-approved NullReferenceException. A closed record
/// hierarchy has no <c>default</c> and gives <c>switch</c> expressions exhaustiveness checking.
/// </para>
/// <para>Exceptions are reserved for programming errors and cancellation.</para>
/// </remarks>
/// <typeparam name="T">The value produced on success.</typeparam>
public abstract record MarketResult<T>
{
    private MarketResult()
    {
    }

    /// <summary>A successful fetch.</summary>
    public sealed record Ok(T Value) : MarketResult<T>;

    /// <summary>A classified failure. Never a thrown exception.</summary>
    public sealed record Fail(FailureRecord Why) : MarketResult<T>;
}
