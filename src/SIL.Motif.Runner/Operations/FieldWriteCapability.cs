namespace SIL.Motif.Runner.Operations;

/// <summary>Field edits supplied by a Runner handler or requested by a record importer.</summary>
public enum RecordWriteVerb
{
    Set,
    Clear,
    SetAlternative,
    ClearAlternative,
    /// <summary>Set inline rich text whose value can be represented without formatting or writing-system runs.</summary>
    SetPlainRich,
    /// <summary>Set formatted inline text or replace the paragraphs of a structured text.</summary>
    SetRich,
    ClearRich,
    AddObject,
    RemoveObject,
    MoveObject,
    /// <summary>Add a collection member or append a sequence member.</summary>
    AddReference,
    /// <summary>Add a reference using identity-relative placement in an ordered sequence.</summary>
    InsertReference,
    RemoveReference,
    MoveReference,
    AddItem,
    RemoveItem,
    MoveItem,
}

/// <summary>How a kind uses identity-relative placement on the field it writes.</summary>
public enum FieldWritePlacement
{
    Forbidden,
    /// <summary>Placement is required when the sequence already has a member.</summary>
    RequiredWhenNonempty,
    Required,
}

/// <summary>A child content shape that a lowerer must confirm before selecting a restricted kind.</summary>
public enum FieldWriteShape
{
    Unspecified,
    /// <summary>A variant LexEntryRef with exactly one component and one variant type, without complex-form links.</summary>
    VariantReference,
}

/// <summary>The <c>after</c> shape of an owning create that a record-file lowerer may author without a hand-written composer.</summary>
public enum OwningCreatePayload
{
    /// <summary>The kind has its own payload, so only its composer authors it.</summary>
    Unspecified,
    /// <summary>The generic creation: <c>{}</c>.</summary>
    Empty,
    /// <summary>The generic creation of a field that holds several concrete classes: <c>{ "class": "..." }</c>.</summary>
    ClassProperty,
}

/// <summary>
/// A direct edit supported by a Runner registration. The declaring class is where the field originates in LibLCM.
/// An owner restriction narrows an inherited field. Child classes are concrete and an empty list accepts every
/// child allowed by the field. A fallback serves fields that also have a dedicated kind for the same edit.
/// </summary>
public sealed record FieldWriteCapability(
    string DeclaringClass, string Field, RecordWriteVerb Verb,
    FieldWritePlacement Placement = FieldWritePlacement.Forbidden,
    string? OwnerClass = null, FieldWriteShape Shape = FieldWriteShape.Unspecified,
    bool Fallback = false)
{
    /// <summary>Concrete child classes accepted by an owning edit.</summary>
    public IReadOnlyList<string> ChildClasses { get; init; } = [];

    /// <summary>For an owning create, the generic payload shape its handler accepts; <see cref="OwningCreatePayload.Unspecified"/> for any other edit.</summary>
    public OwningCreatePayload CreatePayload { get; init; }

    internal static FieldWriteCapability ForAddress(string address, RecordWriteVerb verb, bool fallback = false)
    {
        var separator = address.IndexOf('.');
        return new(address[..separator], address[(separator + 1)..], verb, Fallback: fallback);
    }
}
