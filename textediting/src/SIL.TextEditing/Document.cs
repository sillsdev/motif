using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SIL.TextEditing
{
    /// <summary>A position in a document: paragraph index and UTF-16 offset inside it.</summary>
    public struct TextPosition : IEquatable<TextPosition>, IComparable<TextPosition>
    {
        public TextPosition(int paragraph, int offset)
        {
            Paragraph = paragraph;
            Offset = offset;
        }

        public int Paragraph;
        public int Offset;

        public bool Equals(TextPosition other) => Paragraph == other.Paragraph && Offset == other.Offset;
        public override bool Equals(object? obj) => obj is TextPosition p && Equals(p);
        public override int GetHashCode() => Paragraph * 100003 + Offset;
        public int CompareTo(TextPosition other) => Paragraph != other.Paragraph ? Paragraph.CompareTo(other.Paragraph) : Offset.CompareTo(other.Offset);
        public override string ToString() => Paragraph + ":" + Offset;
    }

    public sealed class Document
    {
        public List<Paragraph> Paragraphs { get; } = new List<Paragraph>();

        /// <summary>The Unicode form the host stores (<see cref="NormalizationForm.FormD"/> for FieldWorks). Typed, composed and
        /// pasted text is converted to it before insertion. Null (the default) leaves text as it arrives.</summary>
        public NormalizationForm? Normalization { get; set; }

        public Document Clone()
        {
            var d = new Document { Normalization = Normalization };
            foreach (var p in Paragraphs) d.Paragraphs.Add(p.Clone());
            return d;
        }

        public string PlainText() => string.Join("\n", Paragraphs.Select(p => p.Text));
    }

    /// <summary>What an edit replaced: the text from <see cref="Start"/> to <see cref="OldEnd"/> (positions before the edit) became the text from
    /// <see cref="Start"/> to <see cref="NewEnd"/> (positions after it). Paragraphs <c>Start.Paragraph</c> to <c>OldEnd.Paragraph</c> became
    /// paragraphs <c>Start.Paragraph</c> to <c>NewEnd.Paragraph</c>. Text before Start is unchanged and text after OldEnd is unchanged, moved to
    /// follow NewEnd. <see cref="FirstOldStamp"/> and <see cref="LastOldStamp"/> are the <see cref="Paragraph.ChangeStamp"/> of the first and last
    /// paragraph before the edit, so a listener can tell whether what it remembered was about that text.</summary>
    public readonly struct TextEdit
    {
        public TextEdit(TextPosition start, TextPosition oldEnd, TextPosition newEnd, long firstOldStamp, long lastOldStamp, bool propertiesKept = true)
        {
            Start = start;
            OldEnd = oldEnd;
            NewEnd = newEnd;
            FirstOldStamp = firstOldStamp;
            LastOldStamp = lastOldStamp;
            PropertiesKept = propertiesKept;
        }

        public TextPosition Start { get; }
        public TextPosition OldEnd { get; }
        public TextPosition NewEnd { get; }
        public long FirstOldStamp { get; }
        public long LastOldStamp { get; }

        /// <summary>True when the text after <see cref="OldEnd"/> keeps the style and properties of the paragraph it was in, so what was
        /// remembered about it (spelling marks, which depend on the style) still holds; false when a join or paste gave it another paragraph's.</summary>
        public bool PropertiesKept { get; }
    }

    /// <summary>A reversible change: paragraphs [Start, Start + Before.Count) were replaced by After.</summary>
    public sealed class EditDelta
    {
        public int Start { get; set; }
        public List<Paragraph> Before { get; set; } = new List<Paragraph>();
        public List<Paragraph> After { get; set; } = new List<Paragraph>();
        public TextPosition CaretBefore { get; set; }
        public TextPosition CaretAfter { get; set; }
        public string Label { get; set; } = string.Empty;
        /// <summary>The undo step this change belongs to (ADR 0023), numbered from 1 per editor; 0 means a change that was
        /// not made by the editor and is a step of its own.</summary>
        public long UndoGroup { get; set; }
        /// <summary>True on the first delta of an undo step: the host opens an undo task here.</summary>
        public bool StartsUndoGroup { get; set; }

        public EditDelta Inverse()
        {
            return new EditDelta { Start = Start, Before = After, After = Before, CaretBefore = CaretAfter, CaretAfter = CaretBefore, Label = "undo " + Label };
        }
    }
}
