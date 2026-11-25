namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>A reference write names an object outside the possibility list FieldWorks offers for the field.</summary>
    public const string ReferenceWrongList = "reference.wrong-list";
    /// <summary>A reference write names an object of a class the field does not refer to.</summary>
    public const string ReferenceWrongClass = "reference.wrong-class";
    /// <summary>A reference write names an object the project does not contain.</summary>
    public const string ReferenceUnknown = "reference.unknown";
}
