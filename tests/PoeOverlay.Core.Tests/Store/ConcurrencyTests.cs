using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Store;
using Xunit;

namespace PoeOverlay.Core.Tests.Store;

/// <summary>
/// S2 11.8 S5 — many producers, one consumer, nothing lost and nothing merged.
/// </summary>
public sealed class ConcurrencyTests
{
    private const int Producers = 4;
    private const int CommandsEach = 250;

    [Fact]
    public async Task S5_FourProducersOfTwoHundredAndFiftyCommands_PublishExactlyOneThousandSnapshots()
    {
        using var harness = await StoreHarness.StartAsync().ConfigureAwait(false);

        harness.Store.BeginNewLeague(StoreTestHarness.League, 1);
        await harness.WaitForVersionAsync(1).ConfigureAwait(false);

        var categories = new[]
        {
            ExchangeCategory.Currency,
            ExchangeCategory.Scarab,
            ExchangeCategory.Essence,
            ExchangeCategory.Fossil,
        };

        var producers = categories.Select(category => Task.Run(() =>
        {
            for (var i = 1; i <= CommandsEach; i++)
            {
                harness.Store.CommitCategory(
                    StoreTestHarness.Tag,
                    StoreTestHarness.Snapshot(category, value: i));
            }
        })).ToArray();

        await Task.WhenAll(producers).ConfigureAwait(false);
        await harness.WaitForVersionAsync(1 + (Producers * CommandsEach)).ConfigureAwait(false);

        var snapshot = harness.Current;

        // One command, one snapshot, one version. No merge optimisation: merging would make Version
        // timing-dependent, which is the single thing that field exists to rule out.
        Assert.Equal(1 + (Producers * CommandsEach), snapshot.Version);
        Assert.Equal(Producers, snapshot.Categories.Count);

        foreach (var category in categories)
        {
            // Per-producer ordering is preserved, so the surviving value is that producer's last.
            Assert.Equal(CommandsEach, snapshot.Categories[category].MedianPrimaryValue);
        }

        Assert.Equal(0, snapshot.RejectedCommitCount);
        Assert.Equal(1 + (Producers * CommandsEach), harness.Events);
    }
}
