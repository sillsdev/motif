namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>A custom-field operation names a field the target's class does not have.</summary>
    public const string CustomFieldMissing = "custom-field.missing";
    /// <summary>A custom-field operation names a stock field, which its own kinds write.</summary>
    public const string CustomFieldNotCustom = "custom-field.not-custom";
    /// <summary>A custom-field operation's kind does not write the field's property type.</summary>
    public const string CustomFieldWrongType = "custom-field.wrong-type";
    /// <summary>A custom-field reference names an object of a class the field does not refer to.</summary>
    public const string CustomFieldWrongClass = "custom-field.wrong-class";
    /// <summary>A custom-field reference names an object outside the list the field was created over.</summary>
    public const string CustomFieldWrongList = "custom-field.wrong-list";
    /// <summary>A custom-field text names a writing system the field's selector does not allow.</summary>
    public const string CustomFieldWritingSystem = "custom-field.writing-system";
}
