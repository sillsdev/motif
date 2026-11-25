namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>The Baseline changed before the word was read.</summary>
    public const string WordContextBaselineChanged = "word-context.baseline-changed";
    /// <summary>The exact Baseline file is missing, so the word's analyses cannot be read.</summary>
    public const string WordContextBaselineUnavailable = "word-context.baseline-unavailable";
    /// <summary>A word-context request was empty.</summary>
    public const string WordContextEmptyWord = "word-context.empty-word";
}
