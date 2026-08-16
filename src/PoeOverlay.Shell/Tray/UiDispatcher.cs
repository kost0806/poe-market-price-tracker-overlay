using System.Windows.Threading;
using PoeOverlay.Core.Presentation.Fanout;

namespace PoeOverlay.Tray;

/// <summary>
/// The Shell half of <see cref="IUiDispatcher"/> (S3 7.2 / S4 12.4).
/// </summary>
/// <remarks>
/// Presentation is <c>net8.0</c> and <c>DispatcherPriority</c> lives in <c>WindowsBase</c>, so the
/// enum crossing this boundary is Presentation's own. The mapping is one for one; this adapter is
/// the only place it exists.
/// </remarks>
internal sealed class UiDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher;

    /// <summary>Wraps a WPF dispatcher.</summary>
    /// <param name="dispatcher">The UI thread's dispatcher.</param>
    internal UiDispatcher(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
    }

    /// <inheritdoc />
    public bool HasShutdownStarted => _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished;

    /// <inheritdoc />
    public bool CheckAccess() => _dispatcher.CheckAccess();

    /// <inheritdoc />
    public void Post(Action action, UiPostPriority priority = UiPostPriority.Normal)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (HasShutdownStarted)
        {
            return;
        }

        _ = _dispatcher.BeginInvoke(Map(priority), action);
    }

    /// <summary>Maps Presentation's priority vocabulary onto WPF's.</summary>
    /// <param name="priority">The Presentation-side value.</param>
    /// <returns>The WPF equivalent.</returns>
    internal static DispatcherPriority Map(UiPostPriority priority) => priority switch
    {
        UiPostPriority.Normal => DispatcherPriority.Normal,
        UiPostPriority.Background => DispatcherPriority.Background,
        UiPostPriority.Render => DispatcherPriority.Render,
        _ => throw new ArgumentOutOfRangeException(nameof(priority), priority, "Unmapped UI post priority."),
    };
}
