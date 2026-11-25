namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>A named morpheme is not a morphosyntactic analysis of an entry.</summary>
    public const string AdhocProhibitionInvalidMorpheme = "adhoc-prohibition.invalid-morpheme";
    /// <summary>A named allomorph is not a form of an entry.</summary>
    public const string AdhocProhibitionInvalidAllomorph = "adhoc-prohibition.invalid-allomorph";
    /// <summary>The same prohibition already exists in the project or earlier in the Draft.</summary>
    public const string AdhocProhibitionDuplicate = "adhoc-prohibition.duplicate";
    /// <summary>The first morpheme or allomorph also appears in the rest.</summary>
    public const string AdhocProhibitionSelf = "adhoc-prohibition.self";
    /// <summary>The project has no morphology data to own the prohibition.</summary>
    public const string AdhocProhibitionNoMorphologyData = "adhoc-prohibition.no-morphology-data";
}
