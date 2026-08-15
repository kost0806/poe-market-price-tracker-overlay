using PoeOverlay.Core.Domain;

namespace PoeOverlay.Core.Store;

/// <summary>
/// The type-safe producer surface (S4 8.4).
/// </summary>
/// <remarks>
/// Producers call these, not <see cref="Post"/> with a hand-built command. Letting Polling
/// construct <see cref="StoreCommand"/> values itself would leak the store's internal command
/// representation and, with it, the D-ST1 encapsulation that keeps <c>RoundGeneration</c> out of
/// the store. Every overload is synchronous and merely queues.
/// </remarks>
public sealed partial class Store
{
    /// <summary>Establishes a new data world and empties the data slots (INV-8).</summary>
    public void BeginNewLeague(string league, int newDataEpoch)
        => Post(new StoreCommand.BeginNewLeague(league, newDataEpoch));

    /// <summary>Commits one category's data.</summary>
    public void CommitCategory(DataTag tag, CategorySnapshot snapshot)
        => Post(new StoreCommand.CommitCategory(tag, snapshot));

    /// <summary>Records one category failure without touching its data.</summary>
    public void RecordCategoryFailure(DataTag tag, ExchangeCategory category, FailureRecord failure)
        => Post(new StoreCommand.RecordCategoryFailure(tag, category, failure));

    /// <summary>Sets or clears the divine rate.</summary>
    public void CommitRate(DataTag tag, DivineRate? rate)
        => Post(new StoreCommand.CommitRate(tag, rate));

    /// <summary>Merges one user-fetched listing. The only overload the settings view model uses.</summary>
    public void SetFetchedListing(DataTag tag, ExchangeCategory category, CategorySnapshot snapshot)
        => Post(new StoreCommand.SetFetchedListing(tag, category, snapshot));

    /// <summary>Stores the league list, regardless of resolution state.</summary>
    public void SetLeagueList(LeagueList list)
        => Post(new StoreCommand.SetLeagueList(list));

    /// <summary>Retreats the league resolution while keeping the data (INV-5).</summary>
    public void SetLeagueUnresolved(string reasonCode)
        => Post(new StoreCommand.SetLeagueUnresolved(reasonCode));

    /// <summary>Records that a round started. Written before any early return (D20).</summary>
    public void RecordHeartbeatAttempt(int roundNumber)
        => Post(new StoreCommand.RecordHeartbeatAttempt(roundNumber));

    /// <summary>Records how a round ended, and closes the round for empty-commit accounting.</summary>
    public void RecordHeartbeatOutcome(RoundOutcome outcome)
        => Post(new StoreCommand.RecordHeartbeatOutcome(outcome));

    /// <summary>Records that the polling loop left its outermost frame.</summary>
    public void RecordLoopExit(LoopExitKind kind)
        => Post(new StoreCommand.RecordLoopExit(kind));
}
