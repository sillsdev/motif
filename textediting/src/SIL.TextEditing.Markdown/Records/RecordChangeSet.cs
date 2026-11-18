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
        /// <see cref="RecordChange.After"/> place it.</summary>
        AddObject,
        /// <summary>Remove an owned object; <see cref="RecordChange.Subtree"/> is what the base held. <see cref="RecordChange.ByOmission"/> says the target
        /// simply did not mention it.</summary>
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

        /// <summary>For an add or move in a sequence: the 1-based index the item has in the target. Null for a collection.</summary>
        public int? Index { get; internal set; }

        /// <summary>For an add or move in a sequence: the key of the item that comes just before it in the target; null for first. A change set is applied in
        /// order: removals first, then adds and moves in ascending target order, each placed right after its <see cref="After"/> item.</summary>
        public string? After { get; internal set; }

        /// <summary>For adding or removing an object: the object (the target's for an add, the base's for a remove).</summary>
        public RecordObject? Subtree { get; internal set; }

        /// <summary>For removing an object: true when the target is a whole-record file that does not mention it. Phase 2 decides whether that is a delete.</summary>
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

        /// <summary>True when there is nothing to apply and nothing refused.</summary>
        public bool IsEmpty => Changes.Count == 0 && Refusals.Count == 0;

        /// <summary>True when an object is removed only because a whole-record target left it out (<see cref="RecordChange.ByOmission"/>).</summary>
        public bool HasRemovalsByOmission => Changes.Any(c => c.Kind == RecordChangeKind.RemoveObject && c.ByOmission);
    }
}
