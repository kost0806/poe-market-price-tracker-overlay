using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PoeOverlay.Core.Diagnostics;

/// <summary>
/// "Report this once per session" bookkeeping (S2 9.4 D-DG2 / S4 4.5, channel literals in S4 14.8).
/// </summary>
/// <remarks>
/// <para>
/// Every deliberate discard is reported. A channel holds at most
/// <see cref="DefaultPerChannelCapacity"/> distinct suppression keys; on reaching that cap the
/// registry logs once for that channel, because the first edition simply went quiet and the fact
/// of going quiet was itself unrecorded.
/// </para>
/// <para>
/// <see cref="DumpTotals"/> exists because the useful reading of "once per session" is "the total
/// is knowable" — without the total, suppression is just concealment. The shutdown path writes
/// one Info line per channel.
/// </para>
/// </remarks>
public sealed class SessionSuppressionRegistry
{
    /// <summary>S4 15.4: distinct suppression keys retained per channel.</summary>
    public const int DefaultPerChannelCapacity = 512;

    private readonly ILogger _logger;
    private readonly int _perChannelCapacity;
    private readonly ConcurrentDictionary<string, ChannelState> _channels = new(StringComparer.Ordinal);

    /// <summary>Creates a registry that logs saturation through <paramref name="logger"/>.</summary>
    public SessionSuppressionRegistry(ILogger logger, int perChannelCapacity = DefaultPerChannelCapacity)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThan(perChannelCapacity, 1);

        _logger = logger;
        _perChannelCapacity = perChannelCapacity;
    }

    /// <summary>
    /// True when this is the first occurrence of <paramref name="suppressionKey"/> on
    /// <paramref name="channel"/>, meaning the caller should record it.
    /// </summary>
    /// <remarks>
    /// Once a channel holds <see cref="DefaultPerChannelCapacity"/> keys it reports saturation
    /// once and then answers false for every further unseen key — the occurrence is still counted
    /// in <see cref="DumpTotals"/>.
    /// </remarks>
    public bool ShouldReport(string channel, string suppressionKey)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(suppressionKey);

        var state = _channels.GetOrAdd(channel, static _ => new ChannelState());
        Interlocked.Increment(ref state.Occurrences);

        if (state.Keys.Count >= _perChannelCapacity && !state.Keys.ContainsKey(suppressionKey))
        {
            ReportChannelSaturated(channel);
            return false;
        }

        return state.Keys.TryAdd(suppressionKey, 0);
    }

    /// <summary>Logs, at most once per channel, that the channel stopped tracking new keys.</summary>
    public void ReportChannelSaturated(string channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        var state = _channels.GetOrAdd(channel, static _ => new ChannelState());
        if (Interlocked.Exchange(ref state.SaturationReported, 1) == 1)
        {
            return;
        }

        _logger.Log(
            LogLevel.Warning,
            new EventId(0, "SuppressionChannelSaturated"),
            FormattableString.Invariant(
                $"Suppression channel '{channel}' reached its cap of {_perChannelCapacity} keys; further distinct keys are no longer reported."),
            exception: null,
            static (state, _) => state);
    }

    /// <summary>Total occurrences seen per channel, for the shutdown dump.</summary>
    public IReadOnlyDictionary<string, int> DumpTotals()
    {
        var totals = new Dictionary<string, int>(_channels.Count, StringComparer.Ordinal);
        foreach (var pair in _channels)
        {
            totals[pair.Key] = Volatile.Read(ref pair.Value.Occurrences);
        }

        return totals;
    }

    /// <summary>Distinct suppression keys currently tracked on <paramref name="channel"/>.</summary>
    public int DistinctKeyCount(string channel)
        => _channels.TryGetValue(channel, out var state) ? state.Keys.Count : 0;

    private sealed class ChannelState
    {
        public readonly ConcurrentDictionary<string, byte> Keys = new(StringComparer.Ordinal);
        public int Occurrences;
        public int SaturationReported;
    }
}
