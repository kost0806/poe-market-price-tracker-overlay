using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace PoeOverlay.Core.Presentation.Fanout;

/// <summary>
/// The re-entrancy guard of D-PS4 (S3 8.4).
/// </summary>
/// <remarks>
/// <para>
/// Thread-static and ambient because the code it constrains — a view model's <c>Refresh</c> — has
/// no reference to the fan-out. It is raised only around <c>Republish</c>'s subscriber loop, never
/// around the deferred flush: <c>IConditionSink.Set</c> and <c>IErrorSink.Report</c> are supposed
/// to be called from the flush, and an exemption there would be a different thing from moving the
/// call out of the guarded window (S3 8.4 P1).
/// </para>
/// <para>
/// A violation is not a data-integrity failure, so Release builds record it and carry on rather
/// than shaking the process; Debug builds assert (S3 8.4 N3).
/// </para>
/// </remarks>
internal static class UiPassGuard
{
    [ThreadStatic]
    private static int _depth;

    /// <summary>True while the calling thread is inside the subscriber loop of a fan-out pass.</summary>
    public static bool IsInPass => _depth > 0;

    /// <summary>Raises the guard for the calling thread.</summary>
    public static void Enter() => _depth++;

    /// <summary>Lowers the guard for the calling thread.</summary>
    public static void Exit()
    {
        if (_depth > 0)
        {
            _depth--;
        }
    }

    /// <summary>
    /// Reports a state-changing call made from inside a pass.
    /// </summary>
    /// <returns><see langword="true"/> when the call was legal (the guard was down).</returns>
    public static bool CheckNotInPass(ILogger logger, string operation)
    {
        if (!IsInPass)
        {
            return true;
        }

        Debug.Assert(false, $"{operation} was called from inside a SnapshotFanout pass (S3 8.4 D-PS4).");

        logger?.Log(
            LogLevel.Error,
            new EventId(0, "UiPassReentrancy"),
            $"{operation} was called from inside a SnapshotFanout pass.",
            null,
            static (state, _) => state);

        return false;
    }
}
