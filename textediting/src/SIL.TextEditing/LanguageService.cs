using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SIL.TextEditing
{
    // The language-service hook (ADR 0030, owner decision Q58): what a host tells a field about the meaning of its text, so a box
    // whose text is a notation (a phonological environment, a morph-breakdown line) can highlight it, mark what is wrong, complete it and
    // explain it. It is shaped like the spell-check host (ADR 0020) and its data is shaped like the Language Server Protocol's, so a VS Code
    // adapter can wrap it later and an agent's findings can come from the same place. Nothing here knows Avalonia or liblcm.

    /// <summary>A place in the text a service is given, as the Language Server Protocol counts: a zero-based <see cref="Line"/> and a
    /// zero-based <see cref="Character"/>, in UTF-16 code units, on that line. The text a field gives its service is one paragraph, so the
    /// line is 0 unless the text itself holds a CR, an LF or a CR LF (the protocol's only line ends: the hard line break U+2028 is not one).</summary>
    public readonly struct LanguagePosition : IEquatable<LanguagePosition>, IComparable<LanguagePosition>
    {
        public LanguagePosition(int line, int character)
        {
            Line = line;
            Character = character;
        }

        public int Line { get; }
        public int Character { get; }

        public int CompareTo(LanguagePosition other) => Line != other.Line ? Line.CompareTo(other.Line) : Character.CompareTo(other.Character);
        public bool Equals(LanguagePosition other) => Line == other.Line && Character == other.Character;
        public override bool Equals(object? obj) => obj is LanguagePosition p && Equals(p);
        public override int GetHashCode() => Line * 397 ^ Character;
        public override string ToString() => Line + ":" + Character;
    }

    /// <summary>A stretch of the text between two <see cref="LanguagePosition"/>s: from <see cref="Start"/> up to, not including,
    /// <see cref="End"/> (the protocol's Range).</summary>
    public readonly struct LanguageRange : IEquatable<LanguageRange>
    {
        public LanguageRange(LanguagePosition start, LanguagePosition end)
        {
            Start = start;
            End = end;
        }

        /// <summary>The range of offsets [<paramref name="start"/>, <paramref name="end"/>) of a text with no line end in it.</summary>
        public LanguageRange(int start, int end) : this(new LanguagePosition(0, start), new LanguagePosition(0, end)) { }

        public LanguagePosition Start { get; }
        public LanguagePosition End { get; }
        public bool IsEmpty => Start.Equals(End);

        public bool Equals(LanguageRange other) => Start.Equals(other.Start) && End.Equals(other.End);
        public override bool Equals(object? obj) => obj is LanguageRange r && Equals(r);
        public override int GetHashCode() => Start.GetHashCode() * 31 + End.GetHashCode();
        public override string ToString() => Start + "-" + End;
    }

    /// <summary>Converts between offsets into a text and the protocol's line and character, with the protocol's line ends (CR, LF and CR LF).</summary>
    public static class LanguageText
    {
        /// <summary>The position of an offset of <paramref name="text"/>. An offset past the end gives the end.</summary>
        public static LanguagePosition PositionOf(string text, int offset)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            offset = Math.Max(0, Math.Min(offset, text.Length));
            int line = 0, lineStart = 0;
            for (int i = 0; i < offset; i++)
            {
                char c = text[i];
                if (c == '\n' || (c == '\r' && !(i + 1 < text.Length && text[i + 1] == '\n'))) { line++; lineStart = i + 1; }
            }
            return new LanguagePosition(line, offset - lineStart);
        }

        /// <summary>The offset of a position in <paramref name="text"/>. A character past the end of its line gives the end of the line (before the line end),
        /// and a line past the last gives the end of the text, as the protocol says.</summary>
        public static int OffsetOf(string text, LanguagePosition position)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            int line = 0, i = 0;
            while (line < position.Line)
            {
                while (i < text.Length && text[i] != '\n' && text[i] != '\r') i++;
                if (i >= text.Length) return text.Length;
                if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                i++;
                line++;
            }
            int end = i;
            while (end < text.Length && text[end] != '\n' && text[end] != '\r') end++;
            return Math.Max(0, Math.Min(i + Math.Max(0, position.Character), end));
        }

        public static int StartOffset(string text, LanguageRange range) => OffsetOf(text, range.Start);

        public static int EndOffset(string text, LanguageRange range) => OffsetOf(text, range.End);
    }

    /// <summary>A stretch of the text and what kind of thing it is, so the field can draw it by kind (a chip behind the glyphs). The kind is
    /// the host's word, as the protocol's semantic token types are: the field draws whatever kind its token-brush map has a style for and
    /// nothing for the others. The text itself is never changed by a token.</summary>
    public sealed class LanguageToken : IEquatable<LanguageToken>
    {
        public LanguageToken(LanguageRange range, string kind, string? target = null)
        {
            Range = range;
            Kind = kind ?? throw new ArgumentNullException(nameof(kind));
            Target = target;
        }

        public LanguageRange Range { get; }

        /// <summary>What it is: for an environment, "class", "segment", "boundary", "slot", "punct", "optional" or "unknown".</summary>
        public string Kind { get; }

        /// <summary>The host's key for the thing the token names (a natural class's id, say), or null; the host turns it into a tooltip or a link.</summary>
        public string? Target { get; }

        public bool Equals(LanguageToken? other) => other != null && Range.Equals(other.Range) && Kind == other.Kind && Target == other.Target;
        public override bool Equals(object? obj) => Equals(obj as LanguageToken);
        public override int GetHashCode() => (Range.GetHashCode() * 31 + Kind.GetHashCode()) * 31 + (Target?.GetHashCode() ?? 0);
        public override string ToString() => Kind + Range;
    }

    /// <summary>How bad a diagnostic is; the numbers are the protocol's.</summary>
    public enum DiagnosticSeverity
    {
        /// <summary>Drawn red.</summary>
        Error = 1,
        /// <summary>Drawn blue.</summary>
        Warning = 2,
        /// <summary>Drawn grey.</summary>
        Information = 3,
        /// <summary>Not drawn; the field still reports it (<c>RichTextField.LanguageDiagnostics</c>).</summary>
        Hint = 4,
    }

    /// <summary>A replacement of one range of the text (the protocol's TextEdit).</summary>
    public sealed class LanguageTextEdit : IEquatable<LanguageTextEdit>
    {
        public LanguageTextEdit(LanguageRange range, string newText)
        {
            Range = range;
            NewText = newText ?? throw new ArgumentNullException(nameof(newText));
        }

        public LanguageRange Range { get; }
        public string NewText { get; }

        public bool Equals(LanguageTextEdit? other) => other != null && Range.Equals(other.Range) && NewText == other.NewText;
        public override bool Equals(object? obj) => Equals(obj as LanguageTextEdit);
        public override int GetHashCode() => Range.GetHashCode() * 31 + NewText.GetHashCode();
        public override string ToString() => Range + " -> \"" + NewText + "\"";
    }

    /// <summary>An exact fix for a diagnostic: its edits are applied together, as one undo step (the protocol's quick fix).</summary>
    public sealed class LanguageFix : IEquatable<LanguageFix>
    {
        public LanguageFix(string title, IReadOnlyList<LanguageTextEdit> edits)
        {
            Title = title ?? throw new ArgumentNullException(nameof(title));
            Edits = edits ?? throw new ArgumentNullException(nameof(edits));
        }

        /// <summary>The caption of the menu item.</summary>
        public string Title { get; }

        /// <summary>The edits, against the text the diagnostic was made for, not overlapping.</summary>
        public IReadOnlyList<LanguageTextEdit> Edits { get; }

        public bool Equals(LanguageFix? other)
        {
            if (other == null || Title != other.Title || Edits.Count != other.Edits.Count) return false;
            for (int i = 0; i < Edits.Count; i++) if (!Edits[i].Equals(other.Edits[i])) return false;
            return true;
        }

        public override bool Equals(object? obj) => Equals(obj as LanguageFix);
        public override int GetHashCode() => Title.GetHashCode() * 31 + Edits.Count;
    }

    /// <summary>Something wrong with, or worth knowing about, a stretch of the text. A diagnostic is a view of the text: the field draws it and
    /// never writes it into the document (liblcm's own squiggle on a stored environment string is not copied).</summary>
    public sealed class LanguageDiagnostic : IEquatable<LanguageDiagnostic>
    {
        public LanguageDiagnostic(LanguageRange range, DiagnosticSeverity severity, string message, string? code = null, string? source = null, LanguageFix? fix = null)
        {
            Range = range;
            Severity = severity;
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Code = code;
            Source = source;
            Fix = fix;
        }

        public LanguageRange Range { get; }
        public DiagnosticSeverity Severity { get; }
        public string Message { get; }

        /// <summary>The finding's id, for a host that counts or filters findings: <c>fwmd.env.unknown-class</c> and the like.</summary>
        public string? Code { get; }

        /// <summary>Who found it (the service's name), or null.</summary>
        public string? Source { get; }

        /// <summary>An exact fix, or null when the service has none.</summary>
        public LanguageFix? Fix { get; }

        public bool Equals(LanguageDiagnostic? other) =>
            other != null && Range.Equals(other.Range) && Severity == other.Severity && Message == other.Message && Code == other.Code && Source == other.Source && Equals(Fix, other.Fix);
        public override bool Equals(object? obj) => Equals(obj as LanguageDiagnostic);
        public override int GetHashCode() => (Range.GetHashCode() * 31 + (int)Severity) * 31 + Message.GetHashCode();
        public override string ToString() => Severity + " " + Range + " " + Message;
    }

    /// <summary>What a service found in a text: its tokens, in order, and its diagnostics.</summary>
    public sealed class LanguageAnalysis
    {
        public static readonly LanguageAnalysis Empty = new LanguageAnalysis(new LanguageToken[0], new LanguageDiagnostic[0]);

        public LanguageAnalysis(IReadOnlyList<LanguageToken> tokens, IReadOnlyList<LanguageDiagnostic> diagnostics)
        {
            Tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public IReadOnlyList<LanguageToken> Tokens { get; }
        public IReadOnlyList<LanguageDiagnostic> Diagnostics { get; }
    }

    /// <summary>The protocol's completion item kinds, the ones a box is likely to use (the numbers are the protocol's).</summary>
    public enum CompletionItemKind
    {
        Text = 1,
        Method = 2,
        Function = 3,
        Field = 5,
        Variable = 6,
        Class = 7,
        Module = 9,
        Property = 10,
        Unit = 11,
        Value = 12,
        Enum = 13,
        Keyword = 14,
        Snippet = 15,
        Color = 16,
        Reference = 18,
        EnumMember = 20,
        Constant = 21,
        Struct = 22,
        Operator = 24,
    }

    /// <summary>One thing the completion popup offers.</summary>
    public sealed class CompletionItem
    {
        public CompletionItem(string label, CompletionItemKind kind = CompletionItemKind.Text, string? detail = null, string? insertText = null, LanguageRange? replaceRange = null, int? caretOffset = null)
        {
            Label = label ?? throw new ArgumentNullException(nameof(label));
            Kind = kind;
            Detail = detail;
            InsertText = insertText;
            ReplaceRange = replaceRange;
            CaretOffset = caretOffset;
        }

        /// <summary>What the popup shows.</summary>
        public string Label { get; }
        public CompletionItemKind Kind { get; }

        /// <summary>A few words after the label, such as the natural class's name.</summary>
        public string? Detail { get; }

        /// <summary>Longer help, shown beside the item or read by a screen reader; null for none.</summary>
        public string? Documentation { get; set; }

        /// <summary>What accepting the item types; the label when null.</summary>
        public string? InsertText { get; }

        /// <summary>The text the insertion replaces (the part of a word already typed, say). Null inserts at the caret, over the selection, if any.</summary>
        public LanguageRange? ReplaceRange { get; }

        /// <summary>Where the caret goes afterwards, in UTF-16 units from the start of the inserted text; null puts it at the end. An optional item
        /// "()" puts it between the parentheses.</summary>
        public int? CaretOffset { get; }

        /// <summary>The key the list is ordered by when it is not the order given; the label when null.</summary>
        public string? SortText { get; set; }

        public string Inserted => InsertText ?? Label;

        public override string ToString() => Label;
    }

    /// <summary>Why a completion was asked for; the numbers are the protocol's.</summary>
    public enum CompletionTriggerKind
    {
        /// <summary>The user pressed Ctrl+Space.</summary>
        Invoked = 1,
        /// <summary>The user typed one of the service's <see cref="ITextLanguageService.CompletionTriggerCharacters"/>.</summary>
        TriggerCharacter = 2,
        /// <summary>The popup was open and the text changed: ask again for the list that fits.</summary>
        Refresh = 3,
    }

    /// <summary>Help for a place in the text, shown when the pointer rests there.</summary>
    public sealed class LanguageHover
    {
        public LanguageHover(string contents, LanguageRange? range = null)
        {
            Contents = contents ?? throw new ArgumentNullException(nameof(contents));
            Range = range;
        }

        /// <summary>Plain text; a host that wants markup waits for a later version of this interface.</summary>
        public string Contents { get; }

        /// <summary>The stretch the help is about, or null for the word at the place.</summary>
        public LanguageRange? Range { get; }
    }

    /// <summary>The host's language service for one field's text (ADR 0030): what the text means to the notation it is written in. Like the
    /// spell checker (ADR 0020) the field asks it when it is idle and applies an answer only to the text it was asked about, so every
    /// answer that can take time is a task; an in-process service answers with a task that is already done, and a host whose service lives in
    /// another process (Motif's backend) answers when it can. A call may begin on the field's thread; a service that is slow does its work
    /// elsewhere. A task that is cancelled, or that fails, gives no answer: the field then keeps what it had and, when it failed, tells the host
    /// (<c>RichTextField.LanguageServiceFailed</c>). The text is the field's one paragraph; embedded objects are in it as U+FFFC.
    /// <see cref="TextLanguageService"/> is a base class that makes the common case a few overrides.</summary>
    public interface ITextLanguageService
    {
        /// <summary>The tokens and the diagnostics of a text. Asked again after every change to the paragraph, once the field is idle.</summary>
        Task<LanguageAnalysis> AnalyzeAsync(string text, CancellationToken cancellation);

        /// <summary>The completions at a position, best first (the field shows them in the order given). An empty list closes the popup.</summary>
        Task<IReadOnlyList<CompletionItem>> CompleteAsync(string text, LanguagePosition position, CompletionTriggerKind trigger, CancellationToken cancellation);

        /// <summary>The help for a position, or null for none.</summary>
        Task<LanguageHover?> HoverAsync(string text, LanguagePosition position, CancellationToken cancellation);

        /// <summary>Whether the host's insert command <paramref name="kind"/> is allowed at the selection (FLEx's <c>CanInsertItem</c> and <c>CanInsertHashMark</c>): the host's
        /// Insert menu is enabled by it. The kinds are the service's words ("natural-class", "word-boundary" for an environment). Answered at once, on the
        /// field's thread.</summary>
        bool CanInsert(string text, LanguageRange selection, string kind);

        /// <summary>The characters that open the completion popup when typed. Empty for none; Ctrl+Space always opens it.</summary>
        IReadOnlyList<char> CompletionTriggerCharacters { get; }

        /// <summary>Raised when what the service answers has changed, as the spell host's <c>DictionariesChanged</c> is (a natural class added, say): every field
        /// using the service analyses its text again. It may be raised on any thread.</summary>
        event EventHandler? Changed;
    }

    /// <summary>A base for a service that answers at once: override <see cref="Analyze"/> and whatever else the notation needs; the rest answer
    /// nothing. <see cref="RaiseChanged"/> announces a change to every field using the service, from any thread.</summary>
    public abstract class TextLanguageService : ITextLanguageService
    {
        /// <summary>The tokens and diagnostics of a text.</summary>
        protected abstract LanguageAnalysis Analyze(string text);

        protected virtual IReadOnlyList<CompletionItem> Complete(string text, LanguagePosition position, CompletionTriggerKind trigger) => new CompletionItem[0];

        protected virtual LanguageHover? Hover(string text, LanguagePosition position) => null;

        public virtual bool CanInsert(string text, LanguageRange selection, string kind) => false;

        public virtual IReadOnlyList<char> CompletionTriggerCharacters => new char[0];

        public virtual Task<LanguageAnalysis> AnalyzeAsync(string text, CancellationToken cancellation) => Task.FromResult(Analyze(text));

        public virtual Task<IReadOnlyList<CompletionItem>> CompleteAsync(string text, LanguagePosition position, CompletionTriggerKind trigger, CancellationToken cancellation) =>
            Task.FromResult(Complete(text, position, trigger));

        public virtual Task<LanguageHover?> HoverAsync(string text, LanguagePosition position, CancellationToken cancellation) => Task.FromResult(Hover(text, position));

        public event EventHandler? Changed;

        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A <see cref="TextLanguageService"/> whose work is done on the thread pool, off the field's thread: for a notation whose analysis takes long enough to be
    /// felt between keystrokes. The overrides are called on a worker thread, possibly several at once, so they must not touch the UI and must be thread-safe; the field
    /// applies the answers on its own thread, only to the text asked about, as for any service.</summary>
    public abstract class BackgroundTextLanguageService : TextLanguageService
    {
        public override Task<LanguageAnalysis> AnalyzeAsync(string text, CancellationToken cancellation) => Task.Run(() => Analyze(text), cancellation);

        public override Task<IReadOnlyList<CompletionItem>> CompleteAsync(string text, LanguagePosition position, CompletionTriggerKind trigger, CancellationToken cancellation) =>
            Task.Run(() => Complete(text, position, trigger), cancellation);

        public override Task<LanguageHover?> HoverAsync(string text, LanguagePosition position, CancellationToken cancellation) => Task.Run(() => Hover(text, position), cancellation);
    }
}
