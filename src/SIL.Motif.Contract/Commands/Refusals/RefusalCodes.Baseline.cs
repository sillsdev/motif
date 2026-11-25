namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    public const string BaselineBusy = "baseline.busy";
    public const string BaselineCopyUnloadable = "baseline.copy-unloadable";
    public const string BaselineOwnedRootViolation = "baseline.owned-root-violation";
    public const string BaselineSourceIncomplete = "baseline.source-incomplete";
    public const string BaselineTextWordsUnreadable = "baseline.text-words-unreadable";
    /// <summary>A command that needs a Baseline found none for the project.</summary>
    public const string BaselineMissing = "baseline.missing";
}
