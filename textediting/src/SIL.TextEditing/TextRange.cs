using System;

namespace SIL.TextEditing
{
    /// <summary>A stretch of text between two positions of a document: <see cref="Start"/> up to, not including,
    /// <see cref="End"/>. A find match, a word, a run and a selection are each one. A match lies in one paragraph.</summary>
    public readonly struct TextRange : IEquatable<TextRange>
    {
        public TextRange(TextPosition start, TextPosition end)
        {
            Start = start;
            End = end;
        }

        public TextRange(int paragraph, int start, int end) : this(new TextPosition(paragraph, start), new TextPosition(paragraph, end)) { }

        public TextPosition Start { get; }
        public TextPosition End { get; }
        public bool IsEmpty => Start.Equals(End);

        public bool Equals(TextRange other) => Start.Equals(other.Start) && End.Equals(other.End);
        public override bool Equals(object? obj) => obj is TextRange r && Equals(r);
        public override int GetHashCode() => Start.GetHashCode() * 31 + End.GetHashCode();
        public override string ToString() => Start + "-" + End;
    }
}
