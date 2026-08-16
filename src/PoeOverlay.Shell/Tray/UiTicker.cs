using System.Windows.Threading;
using PoeOverlay.Core.Presentation.Fanout;

namespace PoeOverlay.Tray;

/// <summary>
/// The one periodic tick, over <c>DispatcherTimer</c> (S3 1.3 / S4 12.4).
/// </summary>
/// <remarks>
/// This is the one component allowed to touch the wall clock directly rather than a
/// <c>TimeProvider</c> — being a wall-clock timer on the UI thread is the whole reason it exists.
/// </remarks>
internal sealed class UiTicker : IUiTicker
{
    private readonly DispatcherTimer _timer;

    /// <summary>Creates a stopped ticker bound to <paramref name="dispatcher"/>.</summary>
    /// <param name="dispatcher">The UI thread's dispatcher.</param>
    internal UiTicker(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher);
        _timer.Tick += OnTimerTick;
    }

    /// <inheritdoc />
    public event EventHandler? Tick;

    /// <inheritdoc />
    public void Start(TimeSpan period)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(period, TimeSpan.Zero);
        _timer.Interval = period;
        _timer.Start();
    }

    /// <inheritdoc />
    public void Stop() => _timer.Stop();

    private void OnTimerTick(object? sender, EventArgs e) => Tick?.Invoke(this, EventArgs.Empty);
}
