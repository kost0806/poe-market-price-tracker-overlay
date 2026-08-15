namespace PoeOverlay.Core.Domain;

/// <summary>
/// The closed set of eighteen poe.ninja exchange categories (S2 2.2 / S4 3.3).
/// </summary>
/// <remarks>
/// The member name is itself the <c>type=</c> query token; there is no separate mapping table.
/// The numeric values are pinned because they appear in logs and drive the deterministic sort
/// order — reordering them makes past logs lie.
/// </remarks>
public enum ExchangeCategory
{
    Currency = 1,
    Fragment = 2,
    Runegraft = 3,
    AllflameEmber = 4,
    Tattoo = 5,
    Omen = 6,
    DjinnCoin = 7,
    Ducat = 8,
    EnshroudingCrystal = 9,
    DivinationCard = 10,
    Artifact = 11,
    Oil = 12,
    DeliriumOrb = 13,
    Scarab = 14,
    Astrolabe = 15,
    Fossil = 16,
    Resonator = 17,
    Essence = 18,
}

/// <summary>User intent for the displayed currency (S2 2.3).</summary>
public enum DisplayCurrency
{
    Auto,
    Chaos,
    Divine,
}

/// <summary>Result of resolving <see cref="DisplayCurrency"/>; deliberately cannot carry Auto (S2 2.3).</summary>
public enum ResolvedCurrency
{
    Chaos,
    Divine,
}

/// <summary>Direction of a price change. The glyph belongs to Pricing, the colour to the View (S2 2.14).</summary>
public enum ChangeDirection
{
    Up,
    Down,
    Flat,
    Unknown,
}

/// <summary>Row display state. Loading is not an absorbing state (S2 2.14, HLD 6.5).</summary>
public enum DisplayState
{
    Loading,
    Ready,
    Failed,
}

/// <summary>Gateway scheduling priority (S2 2.14, D13).</summary>
public enum RequestPriority
{
    Polling,
    UserInitiated,
}

/// <summary>The eight shapes a formatted price can take (S2 2.14).</summary>
public enum PriceForm
{
    ChaosOnly,
    ChaosWithDivine,
    ChaosReciprocal,
    DivineOnly,
    DivineReciprocal,
    ChaosRatePending,
    RatePending,
    Unavailable,
}

/// <summary>Overlay height policy (S4 3.3).</summary>
public enum HeightMode
{
    Auto,
    Explicit,
}

/// <summary>
/// Application-level conditions surfaced as banners and tray state (S2 2.11 / S4 3.3).
/// </summary>
/// <remarks>
/// Two groups. The first is stored in <see cref="MarketSnapshot.Conditions"/> and accepted by
/// <c>Store.SetCondition</c>. The second is derived at display time; the Store rejects those
/// members even in Release builds.
/// <para>
/// Values are a deterministic order that reaches the log, so reordering is forbidden. The one
/// re-ordering is the settled S2 5th ed. revision (S4 19.8) that moved <see cref="FetchFailed"/>
/// from the stored group to the head of the derived group; it lands before any log exists to be
/// made to lie by it.
/// </para>
/// </remarks>
public enum AppConditionKind
{
    // ── stored ───────────────────────────────────────────────────────────────────
    LeagueUnresolved,
    CommitRejected,
    SettingsWriteFailed,
    SettingsCorrupt,
    SettingsReadOnly,
    SettingsUnreadable,
    TrayUnavailable,
    LoggingUnavailable,
    ViewModelRefreshFailing,

    // ── derived at display time; never stored ────────────────────────────────────

    /// <summary>
    /// Derived, never stored (S2 2.11 5th ed. / S4 19.8). The failure list is derived from
    /// <c>CategoryStatuses</c> at display time and never touched <c>Conditions</c>, so
    /// <c>snapshot.Conditions[FetchFailed]</c> was always absent. The member stays so that the
    /// banner and tooltip aggregators can treat stored and derived conditions on one axis;
    /// <c>Store.SetCondition</c> rejects it, in Release builds too.
    /// </summary>
    FetchFailed,
    RatePending,
    RateInherited,
    PollingStopped,
    ItemUnresolved,
    ItemDropped,
}
