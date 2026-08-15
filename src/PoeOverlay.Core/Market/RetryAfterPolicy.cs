using System.Globalization;

namespace PoeOverlay.Core.Market;

/// <summary>
/// Turns a <c>Retry-After</c> header into a wait (S2 5.8 / S4 15.3).
/// </summary>
/// <remarks>
/// <para>
/// The server's instruction is a floor, not a ceiling: the actual wait is
/// <c>max(header, backoff)</c>. Treating it as a ceiling would let a server shorten our own
/// backoff, which is the opposite of what a 429 means.
/// </para>
/// <para>
/// The raw header string is parsed here rather than
/// <c>HttpResponseMessage.Headers.RetryAfter</c>: the typed header refuses to represent
/// <c>Retry-After: -5</c> at all (it parses as absent), and M18 requires a negative delta to
/// clamp to zero rather than to fall back to the exponential backoff.
/// </para>
/// </remarks>
internal static class RetryAfterPolicy
{
    /// <summary>S4 15.3 — Retry-After is clamped into [0, 60s].</summary>
    internal static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The clamped delay a <c>Retry-After</c> value asks for, or <c>null</c> when the header is
    /// absent or unparseable (in which case the exponential backoff stands alone).
    /// </summary>
    internal static TimeSpan? HeaderDelay(string? headerValue, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return null;
        }

        var raw = headerValue.Trim();

        if (long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var deltaSeconds))
        {
            return Clamp(TimeSpan.FromSeconds(deltaSeconds));
        }

        if (DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var httpDate))
        {
            return Clamp(httpDate - now);
        }

        return null;
    }

    /// <summary>The wait actually taken: the header is a floor under the exponential backoff.</summary>
    internal static TimeSpan Wait(TimeSpan? headerDelay, TimeSpan backoff)
        => headerDelay is null ? backoff : (headerDelay.Value > backoff ? headerDelay.Value : backoff);

    private static TimeSpan Clamp(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return value > MaxRetryAfter ? MaxRetryAfter : value;
    }
}
