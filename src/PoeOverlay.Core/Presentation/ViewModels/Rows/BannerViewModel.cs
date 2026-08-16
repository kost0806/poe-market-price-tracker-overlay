using PoeOverlay.Core.Domain;

namespace PoeOverlay.Core.Presentation.ViewModels.Rows;

/// <summary>How loudly a banner should read (S4 11.3).</summary>
public enum BannerSeverity
{
    /// <summary>Informational; nothing is broken.</summary>
    Info,

    /// <summary>Something is degraded and may recover on its own.</summary>
    Warning,

    /// <summary>The user has to act, or the state will not clear.</summary>
    Error,
}

/// <summary>
/// One banner line (S4 11.3).
/// </summary>
/// <param name="Kind">The condition behind it, so the view keys off the enum rather than the text.</param>
/// <param name="Text">Already localised and formatted.</param>
/// <param name="Duration">
/// How long the condition has been active, from <c>ConditionState.Since</c> for stored conditions
/// and from the derived threshold for the rest. <see cref="TimeSpan.Zero"/> when unknown.
/// </param>
public sealed record BannerViewModel(AppConditionKind Kind, string Text, TimeSpan Duration, BannerSeverity Severity);
