namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>Reading the lexicon needs a Baseline and the project has none.</summary>
    public const string LexiconFindBaselineMissing = "lexicon-find.baseline-missing";
    /// <summary>The current Baseline file is missing, so the lexicon cannot be read.</summary>
    public const string LexiconFindBaselineUnavailable = "lexicon-find.baseline-unavailable";
    /// <summary>A lexicon find request is not valid: a bad regular expression or filter.</summary>
    public const string LexiconFindInvalidRequest = "lexicon-find.invalid-request";
}
