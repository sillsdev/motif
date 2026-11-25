namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    public const string AssessBaselineChanged = "assess.baseline-changed";
    public const string AssessCollectEmpty = "assess.collect-empty";
    public const string AssessInvalidLimit = "assess.invalid-limit";
    public const string AssessInvocationInconsistent = "assess.invocation-inconsistent";
    public const string AssessMeasurementsIncomplete = "assess.measurements-incomplete";
    /// <summary>Motif or the parser exhausted memory while assessing, including a parser memory-ceiling stop.</summary>
    public const string AssessOutOfMemory = "assess.out-of-memory";
    public const string AssessParserKilled = "assess.parser-killed";
    public const string AssessParserMemoryLimit = "assess.parser-memory-limit";
    public const string AssessParserUnavailable = "assess.parser-unavailable";
    /// <summary>The Assessment could not provide usable evidence.</summary>
    public const string AssessUnavailable = "assess.unavailable";
    public const string AssessTimingDetailSelection = "assess.timing-detail-selection";
    public const string AssessUnsupportedKind = "assess.unsupported-kind";
    public const string AssessWordsAmbiguous = "assess.words-ambiguous";
    public const string AssessWordsFileEmpty = "assess.words-file-empty";
    public const string AssessWordsMultiWord = "assess.words-multi-word";
    public const string AssessWordsFileNotText = "assess.words-file-not-text";
    public const string AssessWordsFileTooLarge = "assess.words-file-too-large";
    public const string AssessWordsFileBadRow = "assess.words-file-bad-row";
    public const string AssessWordsFileUnreadable = "assess.words-file-unreadable";
    /// <summary>A replacement Assessment was asked for without a complete Assessment of this Baseline to replace.</summary>
    public const string AssessInvalidReplacement = "assess.invalid-replacement";
}
