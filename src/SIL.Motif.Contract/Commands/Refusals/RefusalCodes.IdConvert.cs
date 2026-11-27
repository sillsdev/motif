namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>An id conversion names no id, or more ids than one call takes.</summary>
    public const string IdConvertInvalidRequest = "id-convert.invalid-request";
    /// <summary>An <c>id convert</c> argument is neither a canonical id nor a guid; it is reported on its own item.</summary>
    public const string IdConvertInvalidId = "id.invalid";
}
