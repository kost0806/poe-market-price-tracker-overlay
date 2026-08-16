using System.Collections.Frozen;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Market.Dtos;

namespace PoeOverlay.Core.Market;

/// <summary>
/// Structural validation and mapping — steps 2 to 9 of S2 5.5.3.
/// </summary>
public sealed partial class MarketClient
{
    /// <summary>
    /// S4 15.3 D-PR8 — the lower bound on a usable price.
    /// </summary>
    /// <remarks>
    /// Duplicated from <c>Pricing.NumberFormatter.MinPrice</c> because S2 1.2 forbids
    /// <c>Market → Pricing</c>. The two literals must stay equal; there is no shared home for the
    /// constant that both modules are allowed to reference.
    /// </remarks>
    internal const decimal MinPrice = 1e-9m;

    /// <summary>S2 5.5.4 — the D8-b threshold.</summary>
    internal const double FieldMissingThreshold = 0.20;

    /// <summary>S2 5.5.4 — below this many raw lines the ratio is not consulted.</summary>
    internal const int SmallSampleFloor = 5;

    /// <summary>S2 2.6 — skipped slugs are preserved up to this many.</summary>
    internal const int SkippedIdCap = 200;

    private const string ExpectedPrimaryCurrency = "chaos";

    /// <summary>S4 13.2 D-DL11 — which reason dominates, with ties refusing to differentiate.</summary>
    internal static string DetermineFieldMissingCode(SkipCounts skips)
    {
        // Duplicate is deliberately not a candidate: it has no code of its own.
        var max = Math.Max(skips.NonPositiveValue, Math.Max(skips.ElementFault, skips.BlankId));
        if (max == 0)
        {
            return "FieldMissingRatio";
        }

        var winners = 0;
        if (skips.NonPositiveValue == max)
        {
            winners++;
        }

        if (skips.ElementFault == max)
        {
            winners++;
        }

        if (skips.BlankId == max)
        {
            winners++;
        }

        if (winners > 1)
        {
            return "FieldMissingRatio";
        }

        if (skips.NonPositiveValue == max)
        {
            return "AllNonPositive";
        }

        return skips.ElementFault == max ? "ElementFaultRatio" : "MissingIdRatio";
    }

    private static string DescribeSkips(SkipCounts skips)
        => Invariant(
            $"blank={skips.BlankId} nonpos={skips.NonPositiveValue} dup={skips.Duplicate} fault={skips.ElementFault}");

    private static decimal LowerMedian(List<decimal> values)
    {
        values.Sort();

        // The lower median, never the mean of two: an average invents a value that never existed,
        // and D8-e only compares magnitudes.
        return values[(values.Count - 1) / 2];
    }

    private MarketResult<CategorySnapshot> MapCategory(string body, ExchangeCategory category, string league)
    {
        // Step 2 — skeleton deserialisation, strict.
        NinjaOverviewDto? doc;
        try
        {
            doc = JsonSerializer.Deserialize(body, NinjaJsonContext.Default.NinjaOverviewDto);
        }
        catch (JsonException ex)
        {
            Log(LogLevel.Warning, "Deserialization", "The response skeleton could not be parsed.", ex);
            return FailCategory(FailureKind.Deserialization, "Deserialization", null, ex.GetType().Name);
        }

        // Step 2' — the skeleton null check. Without it {"lines":null} deserialises happily and
        // step 4's .Length throws NullReferenceException, which no catch in Market sees.
        if (doc is null
            || doc.Core is null
            || doc.Core.Items is null
            || doc.Lines is null
            || string.IsNullOrEmpty(doc.Core.Primary))
        {
            Log(LogLevel.Warning, "Deserialization", "The response skeleton was present but null in a required member.");
            return FailCategory(FailureKind.Deserialization, "Deserialization", "skeleton", null);
        }

        // Step 3 — d before a, so that a broken premise is not filed as an empty response.
        if (!string.Equals(doc.Core.Primary.Trim(), ExpectedPrimaryCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return FailCategory(
                FailureKind.PrimaryCurrencyMismatch,
                "PrimaryCurrencyMismatch",
                Invariant($"primary={doc.Core.Primary}"),
                null);
        }

        // Step 4.
        if (doc.Lines.Length == 0)
        {
            return FailCategory(FailureKind.EmptyLines, "EmptyLines", category.ToString(), null);
        }

        // Step 5 — two joins, one build pass per response (contract A2). A linear scan over hundreds
        // of items for each of hundreds of lines is O(n²) on a path that reaches the UI thread.
        //
        // The two arrays are different things and neither can do the other's job: the root items is
        // the name table (one entry per line, 959/959), core.items is the rate basis ([chaos,
        // divine], 2/959 and its category is the only one that equals the query type). S2 5.4.
        JoinDictionaryBuildCount++;
        var namesById = BuildJoinDictionary(doc.Items);
        var rateBasisById = BuildJoinDictionary(doc.Core.Items);

        // Step 6.
        var mapped = new Dictionary<ItemId, ItemPrice>(doc.Lines.Length);
        var seen = new HashSet<ItemId>();
        var skippedIds = new List<ItemId>();
        var skippedIdsTruncated = false;
        var blankId = 0;
        var nonPositive = 0;
        var duplicate = 0;
        var elementFault = 0;
        var joinMiss = 0;
        var values = new List<decimal>(doc.Lines.Length);

        foreach (var element in doc.Lines)
        {
            LineDto? line;
            try
            {
                line = JsonSerializer.Deserialize(element, NinjaJsonContext.Default.LineDto);
            }
            catch (JsonException)
            {
                // S2 9.5 row 4: the observable result is the tally, which D8-b then measures. One
                // malformed element loses only itself — under Strict a document-wide parse would
                // take every healthy line with it.
                elementFault++;
                continue;
            }

            if (line is null)
            {
                elementFault++;
                continue;
            }

            if (!ItemId.TryCreate(line.Id, out var id))
            {
                blankId++;
                continue;
            }

            if (!seen.Add(id))
            {
                duplicate++;
                RecordSkipped(id);
                continue;
            }

            if (line.PrimaryValue is not { } value || value <= 0m || value < MinPrice)
            {
                nonPositive++;
                RecordSkipped(id);
                continue;
            }

            string? apiName = null;
            if (namesById.TryGetValue(id.Value, out var named))
            {
                apiName = named.Name;
            }
            else
            {
                // A join miss is not a failure: it only shortens the name fallback chain.
                joinMiss++;
            }

            // A6 reads the rate basis, which is where the category that matches the query type
            // lives. Most lines are absent from it — that is its normal shape, not a miss, so it is
            // not counted.
            ExchangeCategory? selfReported = null;
            if (rateBasisById.TryGetValue(id.Value, out var basis))
            {
                selfReported = ParseCategory(basis.Category);
                if (selfReported is { } reported && reported != category)
                {
                    // Contract A6 disagreement is reported, never a reason to discard data —
                    // dropping on this axis would turn A6 from a benefit into a hazard.
                    LogOnce(
                        LogLevel.Warning,
                        "market.categoryMismatch",
                        Invariant($"{category}:{reported}"),
                        "CategoryMismatch",
                        Invariant($"core.items reported category {reported} inside the {category} response."));
                }
            }

            ReportUnknownCurrency(line.MaxVolumeCurrency);

            mapped.Add(id, new ItemPrice(
                id,
                apiName,
                value,
                line.VolumePrimaryValue,
                line.MaxVolumeCurrency,
                line.MaxVolumeRate,
                line.Sparkline?.TotalChange,
                selfReported));
            values.Add(value);
        }

        var rawLineCount = doc.Lines.Length;
        var skips = new SkipCounts(blankId, nonPositive, duplicate, elementFault);
        var detail = DescribeSkips(skips);

        // Step 7 — the ratio only. "Nothing priced at all" is step 8's event, and merging the two
        // would put a normal market state (no listings yet) into an ×8 cooldown.
        if (rawLineCount >= SmallSampleFloor
            && (double)skips.Total / rawLineCount > FieldMissingThreshold)
        {
            return FailCategory(FailureKind.FieldMissingRatio, DetermineFieldMissingCode(skips), detail, null);
        }

        // Step 8.
        if (mapped.Count == 0)
        {
            return FailCategory(FailureKind.NoPricedLines, "NoPricedLines", category.ToString(), null);
        }

        // Step 9. FetchedAt is the mapping-completion instant, not the request instant.
        return new MarketResult<CategorySnapshot>.Ok(new CategorySnapshot(
            category,
            mapped.ToFrozenDictionary(),
            LowerMedian(values),
            _timeProvider.GetUtcNow(),
            league,
            0,
            rawLineCount,
            skips,
            skippedIds,
            skippedIdsTruncated,
            joinMiss,
            false));

        void RecordSkipped(ItemId id)
        {
            if (skippedIds.Count >= SkippedIdCap)
            {
                skippedIdsTruncated = true;
                return;
            }

            skippedIds.Add(id);
        }
    }

    private MarketResult<LeagueList> MapLeagues(string body)
    {
        LeagueDto[]? dtos;
        try
        {
            dtos = JsonSerializer.Deserialize(body, NinjaJsonContext.Default.LeagueDtoArray);
        }
        catch (JsonException ex)
        {
            Log(LogLevel.Warning, "Deserialization", "The league list could not be parsed.", ex);
            return FailedLeagueList("Deserialization");
        }

        if (dtos is null)
        {
            return FailedLeagueList("Deserialization");
        }

        // Order is preserved absolutely: the array order is the only signal of which league is the
        // current challenge league (contract A4). Sorting destroys the only thing it tells us.
        var entries = new List<LeagueEntry>(dtos.Length);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dto in dtos)
        {
            if (string.IsNullOrWhiteSpace(dto.Id) || !seen.Add(dto.Id))
            {
                continue;
            }

            entries.Add(new LeagueEntry(dto.Id, string.IsNullOrWhiteSpace(dto.Name) ? dto.Id : dto.Name));
        }

        if (entries.Count == 0)
        {
            // An empty list cannot populate the dropdown either, so Suspicious would be a lie.
            return FailedLeagueList("EmptyLeagueList");
        }

        var head = entries[0].Id;
        if (string.Equals(head, "Standard", StringComparison.Ordinal)
            || string.Equals(head, "Hardcore", StringComparison.Ordinal))
        {
            LogOnce(
                LogLevel.Warning,
                "market.leagueOrderAnomaly",
                head,
                "LeagueOrderAnomaly",
                Invariant($"The league list starts with {head}; the challenge-league convention may have changed."));

            // Suspicious still carries its entries — the manual selection dropdown must not be empty.
            return new MarketResult<LeagueList>.Ok(
                new LeagueList(entries, _timeProvider.GetUtcNow(), LeagueListStatus.Suspicious, null));
        }

        return new MarketResult<LeagueList>.Ok(
            new LeagueList(entries, _timeProvider.GetUtcNow(), LeagueListStatus.Ok, null));
    }

    private static ExchangeCategory? ParseCategory(string? raw)
        => Enum.TryParse<ExchangeCategory>(raw, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : null;

    /// <summary>
    /// Indexes one item array by slug. The caller counts the build pass, not this method: both
    /// dictionaries are built in the same step, and the counter measures "once per response".
    /// </summary>
    private static FrozenDictionary<string, ItemDto> BuildJoinDictionary(ItemDto[]? items)
    {
        if (items is null || items.Length == 0)
        {
            return FrozenDictionary<string, ItemDto>.Empty;
        }

        var builder = new Dictionary<string, ItemDto>(items.Length, StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (!string.IsNullOrEmpty(item.Id))
            {
                // First entry wins, matching the line-level duplicate rule.
                builder.TryAdd(item.Id, item);
            }
        }

        return builder.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private void ReportUnknownCurrency(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var trimmed = token.Trim();
        if (string.Equals(trimmed, "chaos", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "divine", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Market records, Pricing judges (D-C4) — and both must use Trim + OrdinalIgnoreCase or the
        // same token is normal on one side and unknown on the other. The suppression key is folded
        // to lower case for the same reason: the registry compares keys ordinally, so " MIRROR "
        // and "mirror" would otherwise be reported as two distinct unknown tokens.
        LogOnce(
            LogLevel.Information,
            "market.unknownMaxVolumeCurrency",
            trimmed.ToLowerInvariant(),
            "UnknownMaxVolumeCurrency",
            Invariant($"Unknown maxVolumeCurrency token '{trimmed}'; the row falls back to chaos."));
    }

    private MarketResult<LeagueList> FailedLeagueList(string code)
        => new MarketResult<LeagueList>.Ok(
            new LeagueList([], _timeProvider.GetUtcNow(), LeagueListStatus.Failed, code));

    private MarketResult<CategorySnapshot> FailCategory(
        FailureKind kind,
        string code,
        string? detail,
        string? exceptionType)
        => new MarketResult<CategorySnapshot>.Fail(
            new FailureRecord(kind, code, _timeProvider.GetUtcNow(), null, detail, exceptionType));
}
