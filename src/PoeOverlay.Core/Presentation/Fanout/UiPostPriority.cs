namespace PoeOverlay.Core.Presentation.Fanout;

/// <summary>
/// Presentation's own priority vocabulary for <see cref="IUiDispatcher.Post"/> (S3 7.2 B2 / S4 11.1).
/// </summary>
/// <remarks>
/// <c>DispatcherPriority</c> lives in <c>WindowsBase</c>, which is <c>net8.0-windows</c>. Naming it
/// in the interface signature would stop this <c>net8.0</c> project compiling — the same reason
/// <see cref="IUiTicker"/> does not name <c>DispatcherTimer</c> (S2 10.8). The Shell adapter maps
/// these three members onto <c>DispatcherPriority</c> one for one (S3 7.2).
/// </remarks>
public enum UiPostPriority
{
    /// <summary>Maps to <c>DispatcherPriority.Normal</c>. The default, and what <c>Republish</c> uses.</summary>
    Normal,

    /// <summary>Maps to <c>DispatcherPriority.Background</c>.</summary>
    Background,

    /// <summary>Maps to <c>DispatcherPriority.Render</c>.</summary>
    Render,
}
