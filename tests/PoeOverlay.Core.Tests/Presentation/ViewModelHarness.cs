using System.Text.Json;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Localization;
using PoeOverlay.Core.Presentation.Overlay;
using PoeOverlay.Core.Settings;

namespace PoeOverlay.Core.Tests.Presentation;

/// <summary>
/// A localizer over the real embedded dictionary.
/// </summary>
/// <remarks>
/// Not a stub returning the key: the point of most of these assertions is the rendered English, and
/// a stub would make every fallback-chain defect invisible. Only the item-name path is synthetic.
/// </remarks>
internal sealed class FakeLocalizer : ILocalizer
{
    private readonly Dictionary<string, string> _entries;

    public FakeLocalizer()
    {
        var assembly = typeof(ILocalizer).Assembly;
        using var stream = assembly.GetManifestResourceStream("PoeOverlay.Core.Localization.en.json")
            ?? throw new InvalidOperationException("the embedded dictionary is missing");

        _entries = new Dictionary<string, string>(
            JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!,
            StringComparer.Ordinal);
    }

    public event EventHandler? LanguageChanged;

    public IReadOnlyList<LanguageInfo> Languages { get; } = [];

    public string CurrentLanguage { get; private set; } = "en";

    /// <summary>Set to make <see cref="ItemName"/> throw, standing in for a broken row path.</summary>
    public bool ThrowOnItemName { get; set; }

    /// <summary>
    /// Prepended to everything this localizer resolves — a stand-in for "a different language".
    /// </summary>
    /// <remarks>
    /// Empty by default, so every existing assertion still sees plain English. Setting it before
    /// <see cref="SetLanguage"/> is what lets a test tell "re-resolved" from "still holding the
    /// string it resolved at construction", which asserting on the English alone cannot do.
    /// </remarks>
    public string Marker { get; set; } = string.Empty;

    public bool TryGetTemplate(string key, out string template)
    {
        if (_entries.TryGetValue(key, out var found) && !string.IsNullOrWhiteSpace(found))
        {
            template = found;
            return true;
        }

        template = key;
        return false;
    }

    public string Ui(string key, params string[] args)
    {
        var template = TryGetTemplate(key, out var found) ? found : key;
        try
        {
            return Marker + string.Format(System.Globalization.CultureInfo.InvariantCulture, template, args);
        }
        catch (FormatException)
        {
            return key;
        }
    }

    public string ItemName(ItemId id, string? apiName)
        => ThrowOnItemName
            ? throw new InvalidOperationException("the name path is broken")
            : Marker + (apiName ?? id.Value);

    public void SetLanguage(string tag)
    {
        CurrentLanguage = tag;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>An in-memory settings source; <c>FlushAsync</c> and the debounce are not what is under test here.</summary>
internal sealed class FakeSettingsSource(AppSettings? initial = null) : ISettingsSource
{
    public event SettingsChangedHandler? Changed;

    public AppSettings Current { get; private set; } = initial ?? AppSettings.Default;

    public WriteBlockReason BlockReason { get; set; } = WriteBlockReason.None;

    public int UpdateCount { get; private set; }

    public void Update(AppSettings next)
    {
        var old = Current;
        Current = next;
        UpdateCount++;
        Changed?.Invoke(old, next);
    }

    public Task FlushAsync(CancellationToken ct) => Task.CompletedTask;

    public void Acknowledge()
    {
        if (BlockReason == WriteBlockReason.Corrupt)
        {
            BlockReason = WriteBlockReason.None;
        }
    }
}

/// <summary>A move-mode service that only tracks the flag; the Shell owns the ordering.</summary>
internal sealed class FakeOverlayModeService : IOverlayModeService
{
    public event EventHandler? StateChanged;

    public bool IsActive { get; private set; }

    public MoveModeExitReason? LastExitReason { get; private set; }

    public void EnterMoveMode()
    {
        IsActive = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ExitMoveMode(MoveModeExitReason reason)
    {
        IsActive = false;
        LastExitReason = reason;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>Records which geometry command was issued.</summary>
internal sealed class FakeOverlayGeometryService : IOverlayGeometryService
{
    public int ResetCount { get; private set; }

    public int RevertCount { get; private set; }

    public void ResetPlacement() => ResetCount++;

    public void RevertHeightToAuto() => RevertCount++;
}

/// <summary>Builders for the snapshots the view-model tests drive.</summary>
internal static class SnapshotBuilder
{
    internal static readonly DateTimeOffset Now = new(2026, 8, 16, 7, 0, 0, TimeSpan.Zero);

    internal const string League = "Allflame";

    internal static MarketSnapshot Empty() => PoeOverlay.Core.Store.Store.CreateInitialSnapshot();

    internal static MarketSnapshot WithConditions(params (AppConditionKind Kind, string? Detail)[] active)
    {
        var conditions = new Dictionary<AppConditionKind, ConditionState>();
        foreach (var (kind, detail) in active)
        {
            conditions[kind] = new ConditionState(true, Now.AddMinutes(-2), detail);
        }

        return Empty() with { Conditions = conditions };
    }

    internal static CategorySnapshot Category(
        ExchangeCategory category,
        DateTimeOffset fetchedAt,
        IEnumerable<ItemPrice> items,
        IReadOnlyList<ItemId>? skipped = null)
    {
        var list = items.ToArray();
        return new CategorySnapshot(
            category,
            list.ToDictionary(p => p.Id),
            list.Length > 0 ? list[0].PrimaryValue : 1m,
            fetchedAt,
            League,
            1,
            list.Length,
            default,
            skipped ?? [],
            false,
            0,
            false);
    }

    internal static ItemPrice Price(string id, decimal value, string? apiName = null)
        => new(new ItemId(id), apiName, value, null, "chaos", null, null, null);
}
