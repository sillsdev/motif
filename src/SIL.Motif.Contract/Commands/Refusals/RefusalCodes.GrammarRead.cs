namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>Reading grammar objects needs a Baseline and the project has none.</summary>
    public const string GrammarReadBaselineMissing = "grammar-read.baseline-missing";
    /// <summary>The current Baseline file is missing, so grammar objects cannot be read.</summary>
    public const string GrammarReadBaselineUnavailable = "grammar-read.baseline-unavailable";
    /// <summary>A grammar read request is not valid.</summary>
    public const string GrammarReadInvalidRequest = "grammar-read.invalid-request";
}
