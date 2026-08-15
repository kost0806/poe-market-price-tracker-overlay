namespace PoeOverlay.Core.Diagnostics;

/// <summary>
/// The last N warning-or-worse log entries, for the settings window (S2 9.3 / S4 4.4).
/// </summary>
/// <remarks>
/// Owned by Diagnostics, not by the Store. Putting it in the Store would give the Store a logging
/// concern and force every module to push errors into it.
/// <para>
/// This is a different thing from <c>Store.LastError</c>: that is one <c>ErrorRecord</c> ("the one
/// to put on the banner right now"), this is N <see cref="LogEntry"/> values ("the recent list to
/// read in the settings window").
/// </para>
/// <para>
/// A fixed array with an interlocked index; reads take a copy.
/// </para>
/// </remarks>
public sealed class RecentErrorRing
{
    /// <summary>S4 15.4: ring size. Admitting Debug/Info would fill all 64 slots with noise.</summary>
    public const int DefaultCapacity = 64;

    private readonly LogEntry?[] _slots;
    private long _next;

    /// <summary>Creates a ring of <paramref name="capacity"/> slots.</summary>
    public RecentErrorRing(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _slots = new LogEntry?[capacity];
    }

    /// <summary>Slot count.</summary>
    public int Capacity => _slots.Length;

    /// <summary>Adds one entry, overwriting the oldest. Callers filter to Warning and above (S2 9.3).</summary>
    public void Add(LogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var index = Interlocked.Increment(ref _next) - 1;
        Volatile.Write(ref _slots[(int)(index % _slots.Length)], entry);
    }

    /// <summary>A copy, oldest first.</summary>
    public IReadOnlyList<LogEntry> Snapshot()
    {
        var written = Interlocked.Read(ref _next);
        var count = (int)Math.Min(written, _slots.Length);
        var start = written - count;

        var result = new List<LogEntry>(count);
        for (var i = 0L; i < count; i++)
        {
            var entry = Volatile.Read(ref _slots[(int)((start + i) % _slots.Length)]);
            if (entry is not null)
            {
                result.Add(entry);
            }
        }

        return result;
    }
}
