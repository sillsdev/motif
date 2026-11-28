namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>The rule-formula text of an affix process or phonological rule field is shown and an import does not write it.</summary>
    public const string RuleNotationReadOnly = "rule-notation.read-only";
    /// <summary>The rule-formula text is not well formed outside a bracket.</summary>
    public const string RuleNotationSyntax = "rule-notation.syntax";
    /// <summary>A repeat count in the rule-formula text is below 1 for its maximum or below its minimum.</summary>
    public const string RuleNotationBadRepeat = "rule-notation.bad-repeat";
    /// <summary>No phoneme or boundary marker of the first phoneme set has the code the rule-formula text writes.</summary>
    public const string RuleNotationUnknownPhoneme = "rule-notation.unknown-phoneme";
    /// <summary>No natural class has the abbreviation the rule-formula text writes.</summary>
    public const string RuleNotationUnknownNaturalClass = "rule-notation.unknown-natural-class";
    /// <summary>Two phonemes, markers or classes share the code or abbreviation the rule-formula text writes, so it names none.</summary>
    public const string RuleNotationAmbiguousLabel = "rule-notation.ambiguous-label";
    /// <summary>The rule-formula text uses one alpha letter for two features, two letters for one feature, or more than 24 letters.</summary>
    public const string RuleNotationAlphaMismatch = "rule-notation.alpha-mismatch";
    /// <summary>An output $n of the rule-formula text names a column beyond the input as it will stand.</summary>
    public const string RuleNotationIndexOutOfRange = "rule-notation.index-out-of-range";
    /// <summary>An input-only change would leave an output element pointing at a column it removes.</summary>
    public const string RuleNotationDanglingIndex = "rule-notation.dangling-index";
    /// <summary>The rule-formula text holds a construct its field cannot hold.</summary>
    public const string RuleNotationContextNotAllowed = "rule-notation.context-not-allowed";
    /// <summary>The rule-formula text is longer than the model allows: 16 inputs or 256 outputs.</summary>
    public const string RuleNotationLimitExceeded = "rule-notation.limit-exceeded";
    /// <summary>The rule-formula text needs an object the operation catalog cannot create.</summary>
    public const string RuleNotationUnsupportedClass = "rule-notation.unsupported-class";
    /// <summary>The project's rule-formula field holds something the text cannot show, so a change to it cannot be checked.</summary>
    public const string RuleNotationUnrenderable = "rule-notation.unrenderable";
    /// <summary>An object outside the rule refers to a context the rule-formula text would keep, change or delete.</summary>
    public const string RuleNotationSharedClass = "rule-notation.shared-class";
    /// <summary>The rule-formula text would delete a context that has a name or description, which the file cannot show.</summary>
    public const string RuleNotationContextHasData = "rule-notation.context-has-data";
    /// <summary>When an operation applies, a context it would delete is referred to by an object outside what it deletes, so it is kept.</summary>
    public const string RuleNotationContextInUse = "rule-notation.context-in-use";
}
