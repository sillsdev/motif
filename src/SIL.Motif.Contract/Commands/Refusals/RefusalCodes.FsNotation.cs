namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>The feature-structure text is not a bracketed list of feature:value pairs.</summary>
    public const string FsNotationSyntax = "fs-notation.syntax";
    /// <summary>The feature-structure text names a feature the project's feature system does not have.</summary>
    public const string FsNotationUnknownFeature = "fs-notation.unknown-feature";
    /// <summary>The feature-structure text gives a feature a value it does not have.</summary>
    public const string FsNotationUnknownValue = "fs-notation.unknown-value";
    /// <summary>More than one feature, or more than one value of a feature, has the label, so it names none.</summary>
    public const string FsNotationAmbiguousLabel = "fs-notation.ambiguous-label";
    /// <summary>The feature-structure text names one feature twice.</summary>
    public const string FsNotationRepeatedFeature = "fs-notation.repeated-feature";
    /// <summary>The feature-structure text names a complex feature, which the notation does not write.</summary>
    public const string FsNotationUnsupportedFeature = "fs-notation.unsupported-feature";
    /// <summary>The project's feature structure holds something the text cannot show, so a change to it cannot be checked.</summary>
    public const string FsNotationUnrenderable = "fs-notation.unrenderable";
}
