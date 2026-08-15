namespace PoeOverlay.Core.Store;

/// <summary>
/// The commit-validation rejection codes (S4 13.4).
/// </summary>
/// <remarks>
/// These reach a Warning log entry's <c>code=</c> field and the detail of the
/// <c>CommitRejected</c> condition. They are not <c>FailureRecord.Code</c> values: a rejection
/// raises no <c>ErrorRecord</c>, because one transient rejection is not something to show a user.
/// </remarks>
public static class RejectionCodes
{
    /// <summary>No data world has been established yet.</summary>
    public const string NoBaseline = "NoBaseline";

    /// <summary>The tag's league is null or blank, which is what <c>default(DataTag)</c> looks like.</summary>
    public const string DefaultTag = "DefaultTag";

    /// <summary>The tag belongs to an earlier data epoch.</summary>
    public const string EpochMismatch = "EpochMismatch";

    /// <summary>The tag belongs to a different league.</summary>
    public const string LeagueMismatch = "LeagueMismatch";

    /// <summary>A committed snapshot carried an empty <c>ItemId</c> key.</summary>
    public const string EmptyItemId = "EmptyItemId";

    /// <summary>A derived condition was offered to the stored condition map.</summary>
    public const string DerivedCondition = "DerivedCondition";
}
