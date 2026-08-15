using PoeOverlay.Core.Domain;

namespace PoeOverlay.Core.Store;

/// <summary>
/// The read path every consumer of application state uses (S2 10.1 / S4 8.1).
/// </summary>
public interface IMarketSnapshotSource
{
    /// <summary>
    /// The current immutable snapshot.
    /// </summary>
    /// <remarks>
    /// The <c>Volatile.Read</c> lives inside the accessor so that a caller cannot forget it. Its
    /// pair is the <c>Volatile.Write</c> on the publish side; one half alone lets a reader observe
    /// an object whose fields are not yet initialised — which happens to work on x86-64 and breaks
    /// on ARM64, because <c>readonly</c> gives no publication guarantee in ECMA-335.
    /// </remarks>
    MarketSnapshot Current { get; }

    /// <summary>A signal only. It carries no data, so a late handler cannot act on a stale payload.</summary>
    event EventHandler? SnapshotChanged;
}
