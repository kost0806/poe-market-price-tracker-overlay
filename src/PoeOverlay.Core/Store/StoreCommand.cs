using PoeOverlay.Core.Domain;

namespace PoeOverlay.Core.Store;

/// <summary>
/// The data world a command belongs to (S2 6.2 D-ST1 / S4 8.2).
/// </summary>
/// <remarks>
/// Commands carry this rather than a whole <see cref="RoundContext"/>. Handing the Store the round
/// context would let it see <c>RoundGeneration</c>, and anything it can see it will eventually
/// validate against — at which point cancellation is counted as a rejection and the two events D9
/// insists on separating are fused again.
/// <para>
/// <c>default(DataTag)</c> is <c>(null, 0)</c>, which passes both <c>!=</c> comparisons against a
/// freshly started store, so the blank check in <c>Validate</c> has to run first and has to
/// survive in Release — <c>Debug.Assert</c> is not a defence.
/// </para>
/// </remarks>
public readonly record struct DataTag(string League, int DataEpoch);

/// <summary>
/// Everything that can change store state (S2 6.2 / S4 8.2).
/// </summary>
/// <remarks>
/// A closed hierarchy: the consumer loop's <c>switch</c> is checked for exhaustiveness, and no
/// <c>default</c> command can exist.
/// </remarks>
public abstract record StoreCommand
{
    private StoreCommand()
    {
    }

    /// <summary>
    /// Establishes a new data world.
    /// </summary>
    /// <remarks>
    /// Not validated — it is the command that <em>sets</em> the yardstick. It moves
    /// <c>DataLeague</c>, <c>DataEpoch</c> and <c>LeagueResolution</c> together (INV-8) and empties
    /// the data slots, so no state can exist in which only two of the three moved. The first
    /// edition validated commits against <c>LeagueResolution.League</c>, which no producer ever
    /// set, so every commit of the first round was rejected and the app sat in Loading forever.
    /// </remarks>
    public sealed record BeginNewLeague(string League, int NewDataEpoch) : StoreCommand;

    /// <summary>Replaces one category's data. Validated.</summary>
    public sealed record CommitCategory(DataTag Tag, CategorySnapshot Snapshot) : StoreCommand;

    /// <summary>Records a category failure. Validated. Touches only the status — never the data (D-D4).</summary>
    public sealed record RecordCategoryFailure(DataTag Tag, ExchangeCategory Category, FailureRecord Failure) : StoreCommand;

    /// <summary>Sets or clears the divine rate slot. Validated.</summary>
    public sealed record CommitRate(DataTag Tag, DivineRate? Rate) : StoreCommand;

    /// <summary>Merges one user-fetched category listing. Validated.</summary>
    public sealed record SetFetchedListing(DataTag Tag, ExchangeCategory Category, CategorySnapshot Snapshot) : StoreCommand;

    /// <summary>
    /// Stores the league list. Not validated.
    /// </summary>
    /// <remarks>
    /// The league list is not data belonging to a league; it is the data used to <em>choose</em>
    /// one. Validating it would empty the manual-selection dropdown exactly when the user needs it.
    /// </remarks>
    public sealed record SetLeagueList(LeagueList List) : StoreCommand;

    /// <summary>Retreats the league resolution. Not validated; data stays (INV-5).</summary>
    public sealed record SetLeagueUnresolved(string ReasonCode) : StoreCommand;

    /// <summary>Round liveness. Not validated — a heartbeat is a survival signal, not data.</summary>
    public sealed record RecordHeartbeatAttempt(int RoundNumber) : StoreCommand;

    /// <summary>Round outcome, and the point at which an empty commit round is counted.</summary>
    public sealed record RecordHeartbeatOutcome(RoundOutcome Outcome) : StoreCommand;

    /// <summary>The polling loop left its outermost frame.</summary>
    public sealed record RecordLoopExit(LoopExitKind Kind) : StoreCommand;

    /// <summary>Sets the banner error. Named apart from <c>IErrorSink.Report</c> on purpose (D-DL5).</summary>
    public sealed record SetLastErrorCmd(ErrorRecord Error) : StoreCommand;

    /// <summary>Sets or clears a stored condition. Named apart from <c>IConditionSink.Set</c> (D-DL5).</summary>
    public sealed record SetConditionCmd(AppConditionKind Kind, bool Active, string? Detail) : StoreCommand;
}
