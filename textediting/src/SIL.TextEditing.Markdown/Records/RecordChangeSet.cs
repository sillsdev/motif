using System;
using System.Collections.Generic;
using System.Linq;

namespace SIL.TextEditing.Markdown.Records
{
    /// <summary>What one <see cref="RecordChange"/> does to the base. A change is addressed by the object's GUID and a field path (ADR 0031, Q63).</summary>
    public enum RecordChangeKind
    {
        /// <summary>Set a scalar field (text, notation, whole number, Boolean, GUID or a single reference) to <see cref="RecordChange.New"/>.</summary>
        Set,
        /// <summary>Clear a scalar field that held <see cref="RecordChange.Old"/>.</summary>
        Clear,
        /// <summary>Set one writing-system alternative of a plain multi field (<see cref="RecordChange.WritingSystem"/>).</summary>
        SetAlternative,
        /// <summary>Clear one writing-system alternative of a plain multi field.</summary>
        ClearAlternative,
        /// <summary>Replace a rich value (FieldWorks Markdown) with the whole new one; <see cref="RecordChange.Old"/> is its <c>expected</c> value and
        /// <see cref="RecordChange.Words"/> a word-level summary for display only. <see cref="RecordChange.WritingSystem"/> names the alternative of a multi field.</summary>
        SetRich,
        /// <summary>Clear a rich value that held <see cref="RecordChange.Old"/>.</summary>
        ClearRich,
        /// <summary>Add an owned object; <see cref="RecordChange.Subtree"/> is the whole new object. In a sequence, <see cref="RecordChange.Index"/> and
        /// <see cref="RecordChange.Anchor"/> place it.</summary>
        AddObject,
        /// <summary>Remove an owned object; <see cref="RecordChange.Subtree"/> is what the base held.</summary>
        RemoveObject,
        /// <summary>Move an owned object within its sequence.</summary>
        MoveObject,
        /// <summary>Add a reference to a sequence or collection.</summary>
        AddReference,
        /// <summary>Remove a reference from a sequence or collection.</summary>
        RemoveReference,
        /// <summary>Move a reference within its sequence.</summary>
        MoveReference,
        /// <summary>Add an item (plain text or notation) to a sequence or collection.</summary>
        AddItem,
        /// <summary>Remove an item from a sequence or collection.</summary>
        RemoveItem,
        /// <summary>Move an item within its sequence.</summary>
        MoveItem,
    }

    /// <summary>What a change set refuses to express. A refusal is typed and carries a reason; project-dependent refusals belong to the importer.</summary>
    public enum RecordRefusalKind
    {
        /// <summary>The field is computed by the host; an edit to it cannot be applied.</summary>
        ComputedField,
        /// <summary>The <c>guid:</c> of an existing object differs.</summary>
        IdentityChanged,
        /// <summary>The <c>class:</c> of an existing object differs.</summary>
        ClassChanged,
        /// <summary>An object with the same GUID appears under another owner (or another field); an owned object is not moved between owners.</summary>
        MovedToOtherOwner,
        /// <summary>The object's class names no type its field allows, so its fields cannot be read.</summary>
        UnknownClass,
        /// <summary>One GUID is on more than one object in the export or the file. None of those objects is compared, so none of their edits is lost silently.</summary>
        DuplicateGuid,

        // The kinds below are raised by a host that checks a change set against a project (SIL.TextEditing.Project's importer); RecordDiffer never raises them.

        /// <summary>The file's <c>base:</c> is not the digest of a fresh export of the record, so the project changed after the file was made. The whole file is refused.</summary>
        StaleBase,
        /// <summary>The file has no <c>base:</c>, so it cannot be shown to match the project. The whole file is refused.</summary>
        MissingBase,
        /// <summary>The file does not read as a record file of the project's schema; the reason names the finding. The whole file is refused.</summary>
        UnreadableFile,
        /// <summary>The file names a record the project does not have, or one that cannot be told from the file.</summary>
        UnknownRecord,
        /// <summary>The file names a key the record schema does not have for that object, for example a value the export left out (a timestamp). The whole file is refused.</summary>
        UnknownField,
        /// <summary>A reference names an object the project does not have, or a link that names none.</summary>
        UnknownReference,
        /// <summary>A reference names an object whose class the field cannot hold.</summary>
        WrongReferenceClass,
        /// <summary>A rich value uses a writing system the project does not have. The whole file is refused.</summary>
        UnknownWritingSystem,
        /// <summary>A rich value uses a character or paragraph style the project does not define. The whole file is refused.</summary>
        UndefinedStyle,
        /// <summary>The change touches a field whose project value the export could not hold in full (see <c>UnexportedValue</c>), so writing it would lose data.</summary>
        UnexportedValue,
        /// <summary>The file leaves out an object that has data the export did not hold (anything but its own timestamps), so deleting it would lose that data.</summary>
        DeleteHasUnexportedData,
        /// <summary>The file leaves out an object that other objects of the project refer to.</summary>
        DeleteHasIncomingReferences,
        /// <summary>The host says it cannot write this field.</summary>
        FieldNotWritable,
        /// <summary>The host says it cannot create an object of this class in this field.</summary>
        CreateNotWritable,
        /// <summary>The host says it cannot delete an object of this class from this field.</summary>
        DeleteNotWritable,
        /// <summary>A new object's GUID is already the GUID of an object in the project.</summary>
        GuidInUse,
        /// <summary>The change is placed after an added object whose own change was refused.</summary>
        DependsOnRefusedChange,
        /// <summary>The project's record could not be written as a file, so there is nothing to compare with.</summary>
        ExportFailed,
        /// <summary>The object a removal names has no GUID or is not in the project, so the delete rule cannot be checked.</summary>
        UnknownDeleteTarget,
        /// <summary>The file has no <c>base:</c>, a <c>root:</c> that names part of a record, and a GUID the project does not have. A new record must be a whole file. The whole file is refused.</summary>
        CreatePartial,
        /// <summary>The file's <c>base:</c> comes from a record the project no longer has. The whole file is refused.</summary>
        RecordGone,
        /// <summary>The file would create a record of a kind a file may not create (a list item, a phonological rule). The whole file is refused.</summary>
        CreateKindNotCreatable,
        /// <summary>A new object's GUID is the nil or all-ones GUID, not RFC 4122 variant, or not version 4, 5 or 7.</summary>
        GuidInvalid,
        /// <summary>A new object's GUID belonged to an object the project's home has deleted; re-creating it would read as an undelete. The whole file is refused.</summary>
        GuidRetired,
        /// <summary>A new record's file sets a field FieldWorks computes for it. The whole file is refused.</summary>
        CreateComputedField,
        /// <summary>A new record's file sets a field only Motif's judgment commands write. The whole file is refused.</summary>
        CreateReservedField,
    }

    /// <summary>Whether a span of a word-level summary is in both values, only the old one or only the new one.</summary>
    public enum RecordWordSpanKind
    {
        Unchanged,
        Removed,
        Added,
    }

    /// <summary>A run of words in a rich value's word-level summary. Spans cover the old and new text: unchanged and removed spans make the old text, unchanged and
    /// added spans the new.</summary>
    public sealed class RecordWordSpan
    {
        internal RecordWordSpan(RecordWordSpanKind kind, string text) { Kind = kind; Text = text; }
        public RecordWordSpanKind Kind { get; }
        public string Text { get; }
        public override string ToString() => (Kind == RecordWordSpanKind.Added ? "+" : Kind == RecordWordSpanKind.Removed ? "-" : "=") + Text;
    }

    /// <summary>What an anchor points at.</summary>
    public enum RecordAnchorKind
    {
        /// <summary>The start of the list: the item goes first.</summary>
        Start,
        /// <summary>The owned object with <see cref="RecordAnchor.ItemGuid"/>, in the list at that moment.</summary>
        Object,
        /// <summary>The <see cref="RecordAnchor.Occurrence"/>-th item (1-based) of a list of values whose text is <see cref="RecordAnchor.Text"/> (and, for a reference,
        /// whose link target is <see cref="RecordAnchor.Target"/>).</summary>
        Item,
        /// <summary>The object with no GUID that the change at <see cref="RecordAnchor.ChangeIndex"/> of the set added.</summary>
        Added,
        /// <summary>The item now at the 1-based <see cref="RecordAnchor.Position"/> in the list, counting every item in it when the change is applied.</summary>
        Position,
    }

    /// <summary>A place in a sequence, resolvable from the change data alone (no knowledge of the list's item types is needed): the item goes right after the anchored one.</summary>
    public sealed class RecordAnchor
    {
        RecordAnchor(RecordAnchorKind kind) { Kind = kind; }

        public RecordAnchorKind Kind { get; }
        public Guid? ItemGuid { get; private set; }
        public string? Text { get; private set; }
        public Guid? Target { get; private set; }
        public int Occurrence { get; private set; }
        public int ChangeIndex { get; private set; }
        public int Position { get; private set; }

        internal static readonly RecordAnchor Start = new RecordAnchor(RecordAnchorKind.Start);
        internal static RecordAnchor OfObject(Guid guid) => new RecordAnchor(RecordAnchorKind.Object) { ItemGuid = guid };
        internal static RecordAnchor OfItem(string text, Guid? target, int occurrence) => new RecordAnchor(RecordAnchorKind.Item) { Text = text, Target = target, Occurrence = occurrence };
        internal static RecordAnchor AddedBy(int changeIndex) => new RecordAnchor(RecordAnchorKind.Added) { ChangeIndex = changeIndex };
        internal static RecordAnchor AtPosition(int position) => new RecordAnchor(RecordAnchorKind.Position) { Position = position };

        public override string ToString()
            => Kind == RecordAnchorKind.Object ? "after " + ItemGuid : Kind == RecordAnchorKind.Item ? "after " + Text + "#" + Occurrence : Kind == RecordAnchorKind.Added ? "after change " + ChangeIndex
                : Kind == RecordAnchorKind.Position ? "after position " + Position : "first";
    }

    /// <summary>One change to the base: which object, which field, what kind, and the old and new values.</summary>
    public sealed class RecordChange
    {
        internal RecordChange(RecordChangeKind kind) { Kind = kind; }

        public RecordChangeKind Kind { get; }

        /// <summary>The GUID of the object the field belongs to (the nearest owner with one; for a partial file that does not carry it, the base's). Null only for
        /// a type with no GUIDs and no owner that has one.</summary>
        public Guid? ObjectGuid { get; internal set; }

        /// <summary>The name of that object's type, when known.</summary>
        public string? ObjectType { get; internal set; }

        /// <summary>The field, as a path from <see cref="ObjectGuid"/>'s object: field names, and for objects without GUIDs 1-based indices (<c>definition</c>,
        /// <c>pronunciation/1/form</c>). Writing systems are in <see cref="WritingSystem"/>.</summary>
        public string Field { get; internal set; } = string.Empty;

        /// <summary>The writing system of an alternative, or null.</summary>
        public string? WritingSystem { get; internal set; }

        /// <summary>The old value as the file spells it, or null when there was none. For a rich value this is the <c>expected</c> precondition.</summary>
        public string? Old { get; internal set; }

        /// <summary>The new value as the file spells it, or null when it is cleared.</summary>
        public string? New { get; internal set; }

        /// <summary>For a reference, the old link's target.</summary>
        public Guid? OldTarget { get; internal set; }

        /// <summary>For a reference, the new link's target.</summary>
        public Guid? NewTarget { get; internal set; }

        /// <summary>For a change to a sequence or collection: the item's key. An object's GUID (lowercase, hyphens); a reference's target GUID or an item's text,
        /// with <c>#2</c> and so on after a repeat; <c>#n</c> for a new object with no GUID, n being its 1-based index in the target.</summary>
        public string? ItemKey { get; internal set; }

        /// <summary>For an object in a sequence or collection: its GUID, when it has one.</summary>
        public Guid? ItemGuid { get; internal set; }

        /// <summary>For an add or move in a sequence: the 1-based index the item has in the target. Null for a collection. For a partial target this counts only the items
        /// the file holds, not the base's other items; rely on <see cref="Anchor"/> there.</summary>
        public int? Index { get; internal set; }

        /// <summary>For an add or move in a sequence: where the item goes, as a typed <see cref="RecordAnchor"/> (right after the anchored item, or first). A change set
        /// is applied in order: removals first, then adds and moves in ascending target order. Null for a collection, which has no order.</summary>
        public RecordAnchor? Anchor { get; internal set; }

        /// <summary>For removing an object that has no GUID: its 1-based position in the base list. Such removals come first and from the last, so the position is
        /// still right when the change is applied.</summary>
        public int? OldPosition { get; internal set; }

        /// <summary>For adding or removing an object: a detached copy of the object (the target's for an add, the base's for a remove). Changing it changes neither document.</summary>
        public RecordObject? Subtree { get; internal set; }

        /// <summary>True for every clear and removal (<see cref="RecordChangeKind.Clear"/>, <see cref="RecordChangeKind.ClearAlternative"/>, <see cref="RecordChangeKind.ClearRich"/>,
        /// <see cref="RecordChangeKind.RemoveObject"/>, <see cref="RecordChangeKind.RemoveReference"/>, <see cref="RecordChangeKind.RemoveItem"/>) of a whole-record target:
        /// the file holds no empty value, so the value is cleared because the file does not mention it. Whether that is a delete is for the importer to decide. Never set for
        /// a partial target, where absence is no change.</summary>
        public bool ByOmission { get; internal set; }

        /// <summary>For <see cref="RecordChangeKind.SetRich"/> with an old value: a word-level summary, for display only.</summary>
        public IReadOnlyList<RecordWordSpan>? Words { get; internal set; }

        /// <summary>The slot this change writes: object, field, alternative and item. At most one change in a set has a given slot.</summary>
        public string Slot => string.Join("|", ObjectGuid?.ToString("D") ?? string.Empty, Field, WritingSystem ?? string.Empty, ItemKey ?? string.Empty);

        public override string ToString() => Kind + " " + Slot + (Old != null || New != null ? " (" + Old + " -> " + New + ")" : string.Empty);
    }

    /// <summary>A change the set will not express, with the reason.</summary>
    public sealed class RecordRefusal
    {
        internal RecordRefusal(RecordRefusalKind kind, string reason) { Kind = kind; Reason = reason; }

        public RecordRefusalKind Kind { get; }
        public string Reason { get; }

        /// <summary>The object the refusal is about (see <see cref="RecordChange.ObjectGuid"/>).</summary>
        public Guid? ObjectGuid { get; internal set; }

        /// <summary>The field path from that object, empty when the refusal is about the object itself.</summary>
        public string Field { get; internal set; } = string.Empty;

        /// <summary>True when the refusal is about the whole file: the change set then has no change at all. A kind does not decide this (<see cref="RecordRefusalKind.UnknownField"/> is
        /// whole-file when the file names a key the schema lacks and per-change when a change cannot be mapped to a field); read this flag, not the kind.</summary>
        public bool WholeFile { get; internal set; }

        public string? WritingSystem { get; internal set; }
        public string? Old { get; internal set; }
        public string? New { get; internal set; }

        public override string ToString() => Kind + " " + (ObjectGuid?.ToString("D") ?? "-") + "/" + Field + ": " + Reason;
    }

    /// <summary>What <see cref="RecordDiffer"/> found: the changes, deterministically ordered and unique per slot, and what it refuses.</summary>
    public sealed class RecordChangeSet
    {
        internal RecordChangeSet(IReadOnlyList<RecordChange> changes, IReadOnlyList<RecordRefusal> refusals, bool partial)
        {
            Changes = changes;
            Refusals = refusals;
            IsPartial = partial;
        }

        public IReadOnlyList<RecordChange> Changes { get; }
        public IReadOnlyList<RecordRefusal> Refusals { get; }

        /// <summary>True when the target was a partial file, so what it omits is no change.</summary>
        public bool IsPartial { get; }

        /// <summary>True when a refusal is about the whole file (<see cref="RecordRefusal.WholeFile"/>); there is then no change.</summary>
        public bool IsRefusedWhole => Refusals.Any(r => r.WholeFile);

        /// <summary>True when there is nothing to apply and nothing refused.</summary>
        public bool IsEmpty => Changes.Count == 0 && Refusals.Count == 0;

        /// <summary>True when any change exists only because a whole-record target left something out (<see cref="RecordChange.ByOmission"/>).</summary>
        public bool HasOmissions => Changes.Any(c => c.ByOmission);
    }
}
