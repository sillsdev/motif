namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>A rich-text field no longer holds the text its edit began from.</summary>
    public const string TextFieldStale = "text.field-stale";
    /// <summary>A rich-text edit names a paragraph or character style the project does not have.</summary>
    public const string TextStyleUnknown = "text.style-unknown";
}
