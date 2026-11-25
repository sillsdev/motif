namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    public const string TextsWordsCancelled = "texts.words-cancelled";
    public const string TextsEvidenceChanged = "texts.evidence-changed";
    /// <summary>Words from different saved Selections were marked together.</summary>
    public const string TextsMixedEvidence = "texts.mixed-evidence";
    /// <summary>The exact source occurrence is absent from the Baseline.</summary>
    public const string TextsOccurrenceNotFound = "texts.occurrence-not-found";
}
