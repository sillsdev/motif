using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SIL.TextEditing.Playground
{
    /// <summary>A natural class of the sample's fake phonology: the abbreviation an environment names it by, and its name.</summary>
    public sealed class SampleNaturalClass
    {
        public SampleNaturalClass(string abbreviation, string name)
        {
            Abbreviation = abbreviation;
            Name = name;
        }

        public string Abbreviation { get; }
        public string Name { get; }
    }

    /// <summary>The playground's sample language service (ADR 0030): the notation of a FieldWorks phonological environment, <c>/ [V] _ #</c>,
    /// over a fake list of phonemes and natural classes. It is written from what FieldWorks does with an environment string (what it accepts,
    /// which problems it reports and what its Insert menu allows), not from FieldWorks' code (ADR 0010), and it is not shipped: the service
    /// that goes with the real project lives with liblcm's phonology data, in a project of its own, later.
    /// <para>The notation: a slash, then the left context, an underscore (where the allomorph stands) and the right context; at least one of the contexts
    /// is not empty. A context is a sequence of items, each a segment (a phoneme, or several in a row written together), a natural class in square brackets
    /// (the abbreviation, up to four words, optionally ending in a caret and a digit for reduplication), or either of those alone in parentheses
    /// (optional). A word boundary <c>#</c> may stand first in the left context and last in the right one. Spaces between items mean nothing.</para></summary>
    public sealed class SampleEnvironmentService : TextLanguageService
    {
        /// <summary>The token kinds this service gives.</summary>
        public static class Kinds
        {
            public const string Class = "class";
            public const string Segment = "segment";
            public const string Boundary = "boundary";
            public const string Slot = "slot";
            public const string Punct = "punct";
            public const string Optional = "optional";
            public const string Unknown = "unknown";
            public static readonly IReadOnlyList<string> All = new[] { Class, Segment, Boundary, Slot, Punct, Optional, Unknown };
        }

        /// <summary>The finding ids this service gives (the FieldWorks error kinds).</summary>
        public static class Codes
        {
            public const string Syntax = "fwmd.env.syntax";
            public const string UnknownClass = "fwmd.env.unknown-class";
            public const string UnknownSegment = "fwmd.env.unknown-segment";
            public const string MissingCloseParen = "fwmd.env.missing-close-paren";
            public const string MissingOpenParen = "fwmd.env.missing-open-paren";
            public const string MissingCloseBracket = "fwmd.env.missing-close-bracket";
            public const string MissingOpenBracket = "fwmd.env.missing-open-bracket";
        }

        /// <summary>The insert commands <see cref="CanInsert"/> answers for.</summary>
        public static class InsertKinds
        {
            public const string Slash = "slash";
            public const string Bar = "bar";
            public const string NaturalClass = "natural-class";
            public const string OptionalItem = "optional-item";
            public const string WordBoundary = "word-boundary";
        }

        public const string SourceName = "environment";

        readonly List<string> _segments;
        readonly List<SampleNaturalClass> _classes;
        HashSet<string> _segmentSet = new HashSet<string>(StringComparer.Ordinal);

        public SampleEnvironmentService() : this(DefaultSegments, DefaultClasses) { }

        public SampleEnvironmentService(IEnumerable<string> segments, IEnumerable<SampleNaturalClass> classes)
        {
            _segments = segments.ToList();
            _classes = classes.ToList();
            RebuildSegmentSet();
        }

        /// <summary>The fake phonology: a dozen consonants, five vowels, and four natural classes (one with a comma and a space in its abbreviation, as FieldWorks'
        /// feature-based ones have).</summary>
        public static readonly string[] DefaultSegments =
        {
            "p", "t", "k", "b", "d", "g", "m", "n", "s", "l", "r", "h", "ŋ", "a", "e", "i", "o", "u", "ai", "ts",
        };

        public static readonly SampleNaturalClass[] DefaultClasses =
        {
            new SampleNaturalClass("C", "Consonants"),
            new SampleNaturalClass("V", "Vowels"),
            new SampleNaturalClass("+son", "Sonorants"),
            new SampleNaturalClass("+lab, +vd", "Voiced labials"),
        };

        public IReadOnlyList<string> Segments => _segments;
        public IReadOnlyList<SampleNaturalClass> Classes => _classes;

        void RebuildSegmentSet() => _segmentSet = new HashSet<string>(_segments.Select(Nfc), StringComparer.Ordinal);

        /// <summary>Adds a natural class and tells every field using the service (<see cref="TextLanguageService.RaiseChanged"/>), which analyses its text again.</summary>
        public void AddClass(string abbreviation, string name)
        {
            _classes.Add(new SampleNaturalClass(abbreviation, name));
            RaiseChanged();
        }

        /// <summary>Removes a natural class (a diagnostic then appears for every environment that names it).</summary>
        public void RemoveClass(string abbreviation)
        {
            _classes.RemoveAll(c => c.Abbreviation == abbreviation);
            RaiseChanged();
        }

        public override IReadOnlyList<char> CompletionTriggerCharacters { get; } = new[] { '[' };

        static string Nfc(string s)
        {
            try { return s.Normalize(NormalizationForm.FormC); }
            catch (ArgumentException) { return s; }
        }

        // ------------------------------------------------------------------ the lexical pass

        enum ItemKind { Slash, Bar, Hash, LParen, RParen, Class, ClassOpen, ClassUnknown, CloseBracket, Segment, SegmentUnknown, ClassEmpty, ClassTooLong }

        struct Item
        {
            public ItemKind Kind;
            public int Start, End;
            /// <summary>For a class: the abbreviation, words joined by one space. For a segment: the text.</summary>
            public string Text;
            /// <summary>For an unknown class or segment: where the problem starts; for an open class, the end of its words.</summary>
            public int Problem;
            public int WordsStart, WordsEnd;
        }

        static bool IsSpace(char c) => c == ' ' || c == '\t' || c == '\r' || c == '\n';

        static bool IsPunct(char c) => c == '/' || c == '[' || c == ']' || c == '(' || c == ')' || c == '_' || c == '#';

        struct Word
        {
            public int Start, End;
        }

        static List<Word> Words(string text, out List<int> punct)
        {
            // Words and punctuation in order: a punctuation mark is a Word of length one, told apart by the list of their starts.
            var result = new List<Word>();
            punct = new List<int>();
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (IsSpace(c)) { i++; continue; }
                if (IsPunct(c)) { result.Add(new Word { Start = i, End = i + 1 }); punct.Add(i); i++; continue; }
                int s = i;
                while (i < text.Length && !IsSpace(text[i]) && !IsPunct(text[i])) i++;
                result.Add(new Word { Start = s, End = i });
            }
            return result;
        }

        List<Item> Lex(string text)
        {
            var words = Words(text, out var punct);
            var punctStarts = new HashSet<int>(punct);
            var items = new List<Item>();
            for (int w = 0; w < words.Count; w++)
            {
                var word = words[w];
                if (punctStarts.Contains(word.Start))
                {
                    char c = text[word.Start];
                    switch (c)
                    {
                        case '/': items.Add(new Item { Kind = ItemKind.Slash, Start = word.Start, End = word.End }); break;
                        case '_': items.Add(new Item { Kind = ItemKind.Bar, Start = word.Start, End = word.End }); break;
                        case '#': items.Add(new Item { Kind = ItemKind.Hash, Start = word.Start, End = word.End }); break;
                        case '(': items.Add(new Item { Kind = ItemKind.LParen, Start = word.Start, End = word.End }); break;
                        case ')': items.Add(new Item { Kind = ItemKind.RParen, Start = word.Start, End = word.End }); break;
                        case ']': items.Add(new Item { Kind = ItemKind.CloseBracket, Start = word.Start, End = word.End }); break;
                        case '[':
                        {
                            int first = w + 1, n = 0;
                            while (first + n < words.Count && !punctStarts.Contains(words[first + n].Start)) n++;
                            bool closed = first + n < words.Count && text[words[first + n].Start] == ']';
                            if (n == 0)
                            {
                                // "[]" or a bracket with nothing after it that closes.
                                int end = closed ? words[first].End : word.End;
                                items.Add(new Item { Kind = closed ? ItemKind.ClassEmpty : ItemKind.ClassOpen, Start = word.Start, End = end, Text = string.Empty, WordsStart = word.End, WordsEnd = word.End, Problem = word.End });
                                if (closed) w = first;
                                break;
                            }
                            int wordsStart = words[first].Start, wordsEnd = words[first + n - 1].End;
                            var name = string.Join(" ", Enumerable.Range(first, n).Select(k => text.Substring(words[k].Start, words[k].End - words[k].Start)));
                            if (!closed)
                            {
                                items.Add(new Item { Kind = ItemKind.ClassOpen, Start = word.Start, End = wordsEnd, Text = name, WordsStart = wordsStart, WordsEnd = wordsEnd, Problem = wordsEnd });
                                w = first + n - 1;
                                break;
                            }
                            int close = first + n;
                            if (n > 4)
                            {
                                items.Add(new Item { Kind = ItemKind.ClassTooLong, Start = word.Start, End = words[close].End, Text = name, WordsStart = wordsStart, WordsEnd = wordsEnd, Problem = words[first + 4].Start });
                                w = close;
                                break;
                            }
                            bool known = FindClass(name) != null;
                            items.Add(new Item { Kind = known ? ItemKind.Class : ItemKind.ClassUnknown, Start = word.Start, End = words[close].End, Text = name, WordsStart = wordsStart, WordsEnd = wordsEnd, Problem = wordsStart });
                            w = close;
                            break;
                        }
                    }
                    continue;
                }
                string ident = text.Substring(word.Start, word.End - word.Start);
                int reach = FurthestSegmentation(ident, out bool whole);
                items.Add(new Item { Kind = whole ? ItemKind.Segment : ItemKind.SegmentUnknown, Start = word.Start, End = word.End, Text = ident, Problem = word.Start + reach });
            }
            return items;
        }

        /// <summary>The natural class an abbreviation names: the abbreviation as it is, or without a trailing caret and digit (reduplication), else null.</summary>
        SampleNaturalClass? FindClass(string name)
        {
            string canonical = Nfc(name);
            var found = _classes.FirstOrDefault(c => Nfc(c.Abbreviation) == canonical);
            if (found != null) return found;
            int caret = name.Length - 2;
            if (caret > 0 && name[caret] == '^' && name[caret + 1] >= '0' && name[caret + 1] <= '9')
            {
                string bare = Nfc(name.Substring(0, caret));
                return _classes.FirstOrDefault(c => Nfc(c.Abbreviation) == bare);
            }
            return null;
        }

        /// <summary>How far into the word the phonemes carry it (a word written together can be several), and whether they carry it all the way.
        /// Phonemes are compared in Unicode form NFC, so a letter and its accent written apart match the one written together.</summary>
        int FurthestSegmentation(string word, out bool whole)
        {
            var reach = new bool[word.Length + 1];
            reach[0] = true;
            int furthest = 0;
            for (int i = 0; i < word.Length; i++)
            {
                if (!reach[i]) continue;
                for (int length = 1; i + length <= word.Length; length++)
                    if (_segmentSet.Contains(Nfc(word.Substring(i, length)))) { reach[i + length] = true; furthest = Math.Max(furthest, i + length); }
            }
            whole = reach[word.Length];
            return whole ? word.Length : furthest;
        }

        // ------------------------------------------------------------------ the grammar pass

        sealed class Problem
        {
            public string Code = string.Empty;
            public int Start, End;
            public string Message = string.Empty;
            public LanguageFix? Fix;
        }

        /// <summary>The first thing wrong with the environment, reading left to right as FieldWorks does (it reports one problem), or null.</summary>
        Problem? Check(string text, List<Item> items)
        {
            if (items.Count == 0)
                return text.Length == 0 ? null : new Problem { Code = Codes.Syntax, Start = 0, End = text.Length, Message = "An environment starts with a slash (/)." };
            int at = 0;
            Item? Peek() => at < items.Count ? items[at] : (Item?)null;
            Problem SyntaxAt(Item? item, string detail)
            {
                // At the end of the text the last item is the one that is missing something after it.
                var shown = item ?? items[items.Count - 1];
                return new Problem { Code = Codes.Syntax, Start = shown.Start, End = shown.End, Message = "Something is wrong around here: " + detail + " An environment is a slash, then the left context, an underscore and the right context; a word boundary # may only stand first on the left or last on the right." };
            }

            if (items[0].Kind != ItemKind.Slash)
            {
                var first = items[0];
                return new Problem
                {
                    Code = Codes.Syntax, Start = first.Start, End = first.End,
                    Message = "An environment starts with a slash (/).",
                    Fix = new LanguageFix("Insert the slash", new[] { new LanguageTextEdit(new LanguageRange(first.Start, first.Start), "/ ") }),
                };
            }
            at = 1;

            // A term: a segment, a class, or one of them in parentheses. Returns a problem, or null with the term taken.
            Problem? Term()
            {
                var item = items[at];
                if (item.Kind == ItemKind.LParen)
                {
                    int open = at;
                    at++;
                    var inner = Peek();
                    if (inner == null || !(inner.Value.Kind == ItemKind.Segment || inner.Value.Kind == ItemKind.SegmentUnknown || inner.Value.Kind == ItemKind.Class || IsBadClass(inner.Value.Kind)))
                        return SyntaxAt(inner, "an optional item holds one segment or natural class between its parentheses.");
                    var problem = Single();
                    if (problem != null) return problem;
                    var close = Peek();
                    if (close == null || close.Value.Kind != ItemKind.RParen)
                    {
                        var last = items[at - 1];
                        return new Problem
                        {
                            Code = Codes.MissingCloseParen, Start = items[open].Start, End = last.End,
                            Message = "There is a missing closing parenthesis ) after '" + Slice(last) + "'.",
                            Fix = new LanguageFix("Insert the closing parenthesis", new[] { new LanguageTextEdit(new LanguageRange(last.End, last.End), ")") }),
                        };
                    }
                    at++;
                    return null;
                }
                var p = Single();
                if (p != null) return p;
                var after = Peek();
                if (after != null && after.Value.Kind == ItemKind.RParen)
                {
                    var seg = items[at - 1];
                    return new Problem
                    {
                        Code = Codes.MissingOpenParen, Start = seg.Start, End = after.Value.End,
                        Message = "There is a missing opening parenthesis ( before '" + Slice(seg) + "'.",
                        Fix = new LanguageFix("Insert the opening parenthesis", new[] { new LanguageTextEdit(new LanguageRange(seg.Start, seg.Start), "(") }),
                    };
                }
                return null;
            }

            string Slice(Item item) => text.Substring(item.Start, item.End - item.Start);

            Problem MissingOpenBracket(Item word, Item close) => new Problem
            {
                Code = Codes.MissingOpenBracket, Start = word.Start, End = close.End,
                Message = "There is a missing opening square bracket [ before '" + Slice(word) + "'.",
                Fix = new LanguageFix("Insert the opening bracket", new[] { new LanguageTextEdit(new LanguageRange(word.Start, word.Start), "[") }),
            };

            bool IsBadClass(ItemKind kind) => kind == ItemKind.ClassUnknown || kind == ItemKind.ClassOpen || kind == ItemKind.ClassEmpty || kind == ItemKind.ClassTooLong;

            // One segment or class, with what is wrong with it.
            Problem? Single()
            {
                var item = items[at];
                switch (item.Kind)
                {
                    case ItemKind.Class: at++; return null;
                    case ItemKind.Segment:
                    case ItemKind.SegmentUnknown:
                        // A word followed by a closing bracket is a class missing its opening one, whether or not the word is a phoneme.
                        if (at + 1 < items.Count && items[at + 1].Kind == ItemKind.CloseBracket) return MissingOpenBracket(item, items[at + 1]);
                        if (item.Kind == ItemKind.Segment) { at++; return null; }
                        return new Problem
                        {
                            Code = Codes.UnknownSegment, Start = item.Problem, End = item.End,
                            Message = "The phoneme that begins '" + text.Substring(item.Problem, item.End - item.Problem) + "' is not one of the project's phonemes.",
                        };
                    case ItemKind.ClassUnknown:
                    {
                        var near = _classes.Where(c => string.Equals(c.Abbreviation, item.Text, StringComparison.OrdinalIgnoreCase)).ToList();
                        return new Problem
                        {
                            Code = Codes.UnknownClass, Start = item.WordsStart, End = item.WordsEnd,
                            Message = "The abbreviation for the class '" + item.Text + "' is not one of the project's natural classes.",
                            Fix = near.Count == 1 ? new LanguageFix("Change to " + near[0].Abbreviation, new[] { new LanguageTextEdit(new LanguageRange(item.WordsStart, item.WordsEnd), near[0].Abbreviation) }) : null,
                        };
                    }
                    case ItemKind.ClassOpen when item.Text.Length == 0:
                        return new Problem { Code = Codes.Syntax, Start = item.Start, End = item.End, Message = "Something is wrong around here: a natural class needs its abbreviation between square brackets." };
                    case ItemKind.ClassOpen:
                        return new Problem
                        {
                            Code = Codes.MissingCloseBracket, Start = item.Start, End = item.End,
                            Message = "There is a missing closing square bracket ] after '" + Slice(item) + "'.",
                            Fix = new LanguageFix("Insert the closing bracket", new[] { new LanguageTextEdit(new LanguageRange(item.End, item.End), "]") }),
                        };
                    case ItemKind.ClassEmpty:
                        return new Problem { Code = Codes.Syntax, Start = item.Start, End = item.End, Message = "Something is wrong around here: a natural class needs its abbreviation between the brackets." };
                    case ItemKind.ClassTooLong:
                        return new Problem { Code = Codes.Syntax, Start = item.Problem, End = item.WordsEnd, Message = "Something is wrong around here: a natural class's abbreviation has at most four words." };
                    default:
                        return SyntaxAt(item, "an item was expected here.");
                }
            }

            bool StartsTerm(Item item) => item.Kind == ItemKind.Segment || item.Kind == ItemKind.SegmentUnknown || item.Kind == ItemKind.Class || IsBadClass(item.Kind) || item.Kind == ItemKind.LParen
                || item.Kind == ItemKind.RParen || item.Kind == ItemKind.CloseBracket;

            // Stray closers where a term would begin.
            Problem? StrayCloser()
            {
                var item = items[at];
                if (item.Kind == ItemKind.RParen)
                    return new Problem { Code = Codes.MissingOpenParen, Start = item.Start, End = item.End, Message = "There is a missing opening parenthesis ( before this closing one." };
                if (item.Kind == ItemKind.CloseBracket)
                    return new Problem { Code = Codes.MissingOpenBracket, Start = item.Start, End = item.End, Message = "There is a missing opening square bracket [ before this closing one." };
                return null;
            }

            // Left context.
            int leftTerms = 0;
            bool leftHash = false;
            if (at < items.Count && items[at].Kind == ItemKind.Hash) { leftHash = true; at++; }
            while (at < items.Count && StartsTerm(items[at]))
            {
                var stray = StrayCloser();
                if (stray != null) return stray;
                var p = Term();
                if (p != null) return p;
                leftTerms++;
            }
            if (at >= items.Count) return SyntaxAt(null, "the underscore _, where the allomorph stands, is missing.");
            if (items[at].Kind != ItemKind.Bar)
            {
                var item = items[at];
                return SyntaxAt(item, item.Kind == ItemKind.Hash ? "a word boundary # goes first on the left or last on the right." : item.Kind == ItemKind.Slash ? "there is more than one slash." : "the underscore _ is missing.");
            }
            int barAt = at;
            at++;

            // Right context.
            int rightTerms = 0;
            bool rightHash = false;
            if (at < items.Count && items[at].Kind == ItemKind.Hash) { rightHash = true; at++; }
            else
            {
                while (at < items.Count && StartsTerm(items[at]))
                {
                    var stray = StrayCloser();
                    if (stray != null) return stray;
                    var p = Term();
                    if (p != null) return p;
                    rightTerms++;
                }
                if (rightTerms > 0 && at < items.Count && items[at].Kind == ItemKind.Hash) { rightHash = true; at++; }
            }
            if (at < items.Count)
            {
                var item = items[at];
                return SyntaxAt(item, item.Kind == ItemKind.Bar ? "there is more than one underscore." : item.Kind == ItemKind.Hash ? "only one word boundary # may stand at the end." : item.Kind == ItemKind.Slash ? "there is more than one slash." : "nothing may follow a word boundary #.");
            }
            if (!leftHash && leftTerms == 0 && !rightHash && rightTerms == 0)
                return SyntaxAt(items[barAt], "the underscore has nothing before or after it.");
            return null;
        }

        // ------------------------------------------------------------------ the service

        /// <summary>A range of offsets in the protocol's line and character: the text may hold CR or LF (the notation treats them as spaces).</summary>
        static LanguageRange At(string text, int start, int end) => new LanguageRange(LanguageText.PositionOf(text, start), LanguageText.PositionOf(text, end));

        static LanguageFix? Remap(string text, LanguageFix? fix) =>
            fix == null ? null : new LanguageFix(fix.Title, fix.Edits.Select(e => new LanguageTextEdit(At(text, e.Range.Start.Character, e.Range.End.Character), e.NewText)).ToList());

        protected override LanguageAnalysis Analyze(string text)
        {
            var items = Lex(text);
            var tokens = new List<LanguageToken>();
            foreach (var item in items)
            {
                string kind;
                string? target = null;
                switch (item.Kind)
                {
                    case ItemKind.Slash: kind = Kinds.Punct; break;
                    case ItemKind.Bar: kind = Kinds.Slot; break;
                    case ItemKind.Hash: kind = Kinds.Boundary; break;
                    case ItemKind.LParen:
                    case ItemKind.RParen: kind = Kinds.Optional; break;
                    case ItemKind.Class: kind = Kinds.Class; target = FindClass(item.Text)?.Abbreviation; break;
                    case ItemKind.Segment: kind = Kinds.Segment; target = item.Text; break;
                    default: kind = Kinds.Unknown; break;
                }
                tokens.Add(new LanguageToken(At(text, item.Start, item.End), kind, target));
            }
            var diagnostics = new List<LanguageDiagnostic>();
            var problem = Check(text, items);
            if (problem != null)
                diagnostics.Add(new LanguageDiagnostic(At(text, problem.Start, Math.Max(problem.End, problem.Start)), DiagnosticSeverity.Error, problem.Message, problem.Code, SourceName, Remap(text, problem.Fix)));
            return new LanguageAnalysis(tokens, diagnostics);
        }

        protected override LanguageHover? Hover(string text, LanguagePosition position)
        {
            int offset = LanguageText.OffsetOf(text, position);
            foreach (var item in Lex(text))
            {
                if (offset < item.Start || offset >= item.End) continue;
                var range = At(text, item.Start, item.End);
                switch (item.Kind)
                {
                    case ItemKind.Slash: return new LanguageHover("Starts the environment: what follows is where the allomorph may occur.", range);
                    case ItemKind.Bar: return new LanguageHover("Where the allomorph stands.", range);
                    case ItemKind.Hash: return new LanguageHover("Word boundary.", range);
                    case ItemKind.LParen:
                    case ItemKind.RParen: return new LanguageHover("An optional item.", range);
                    case ItemKind.Class:
                    {
                        var c = FindClass(item.Text);
                        return c == null ? null : new LanguageHover("Natural class " + c.Abbreviation + ": " + c.Name + ".", range);
                    }
                    case ItemKind.Segment: return new LanguageHover("Segment " + item.Text + ".", range);
                    default: return null;
                }
            }
            return null;
        }

        // ------------------------------------------------------------------ insert rules (FieldWorks' Insert menu)

        public override bool CanInsert(string text, LanguageRange selection, string kind)
        {
            int a = LanguageText.StartOffset(text, selection), b = LanguageText.EndOffset(text, selection);
            if (a > b) { var t = a; a = b; b = t; }
            switch (kind)
            {
                case InsertKinds.Slash: return text.IndexOf('/') < 0;
                case InsertKinds.Bar: return CanInsertBar(text, a, b);
                case InsertKinds.NaturalClass:
                case InsertKinds.OptionalItem: return CanInsertItem(text, a, b);
                case InsertKinds.WordBoundary: return CanInsertBoundary(text, a, b);
                default: return false;
            }
        }

        static bool OnlySpace(string text, int from, int to)
        {
            for (int i = Math.Max(from, 0); i < to && i < text.Length; i++) if (!char.IsWhiteSpace(text[i])) return false;
            return true;
        }

        static bool CanInsertBar(string text, int a, int b)
        {
            int slash = text.IndexOf('/');
            return slash >= 0 && a > slash && b > slash && text.IndexOf('_') < 0;
        }

        /// <summary>An item may go where the selection is after the slash and does not lie wholly outside a word boundary that closes the context it would join:
        /// before the one on the left, or after the one on the right.</summary>
        static bool CanInsertItem(string text, int a, int b)
        {
            int slash = text.IndexOf('/');
            if (slash < 0 || a <= slash || b <= slash) return false;
            int first = text.IndexOf('#');
            if (first < 0) return true;
            int second = text.IndexOf('#', first + 1);
            int bar = text.IndexOf('_');
            int leftHash = -1, rightHash = -1;
            if (second >= 0) { leftHash = first; rightHash = second; }
            else if (bar >= 0) { if (first < bar) leftHash = first; else rightHash = first; }
            if (leftHash >= 0 && b <= leftHash) return false;
            if (rightHash >= 0 && a > rightHash) return false;
            return true;
        }

        // The word boundary rule, written from EnvironmentServiceTests ("A_word_boundary_goes_in_at_the_edge_of_a_context_once") and the notation
        // in ADR 0030, not from any other implementation:
        //
        //   A # may be typed only after the slash. Imagine the selection replaced by a # (a selection is typed over). In the text that results,
        //   every # after the slash must stand at an edge of the environment: either it opens it (only spaces between the slash and it) or it
        //   closes it (only spaces after it). With an underscore, an opening # must lie before it and a closing # after it. There may be one # at
        //   the opening edge and one at the closing edge, so at most two, and when there are two the first opens and the second closes.
        //   The insert is allowed exactly when the text that results obeys this.
        //
        // The tests decide: / a _ b at both ends (yes), / # _ b at the start (no, a second one) and at the end (yes), / a _ # at the start (yes) and
        // the end (no), / # _ # anywhere (no), /a_b in the middle (no), /_# between the underscore and the boundary (no), before the slash (no).
        // Where there is no underscore (once the selection is gone) and one boundary stays, that boundary claims its own edge, so the new one goes
        // only at the opposite edge: an existing one that opens (only spaces between the slash and it; this is looked at first, so "/#" opens) leaves
        // the very end, one that closes leaves the place right after the slash, and one in the middle leaves nowhere.
        // The tests are silent about a selection that covers an existing boundary; the rule, being about the text that results, lets a # replace a
        // boundary it covers (a selection over "# " at the start of "/ # _ b" may become one #).

        static bool CanInsertBoundary(string text, int a, int b)
        {
            int slash = text.IndexOf('/');
            if (slash < 0 || a <= slash) return false;
            string result = text.Substring(0, a) + "#" + text.Substring(b);
            int bar = result.IndexOf('_', slash + 1);
            var hashes = new List<int>();
            for (int i = slash + 1; i < result.Length; i++) if (result[i] == '#') hashes.Add(i);
            if (hashes.Count > 2) return false;
            bool Opens(int h) => OnlySpace(result, slash + 1, h) && (bar < 0 || h < bar);
            bool Closes(int h) => OnlySpace(result, h + 1, result.Length) && (bar < 0 || h > bar);
            if (hashes.Count == 1) return Opens(hashes[0]) || Closes(hashes[0]);
            if (!(Opens(hashes[0]) && Closes(hashes[1]))) return false;
            if (bar >= 0) return true;
            // No underscore: the boundary that was there first keeps its edge, so the new one is at the other (the first of the two is the new one
            // exactly when it is at a, and the existing one opens if only spaces lie between the slash and it).
            bool newIsFirst = hashes[0] == a;
            string rest = text.Remove(a, b - a);
            int existing = newIsFirst ? hashes[1] - 1 : hashes[0];
            bool existingOpens = OnlySpace(rest, slash + 1, existing);
            return existingOpens ? !newIsFirst : newIsFirst;
        }

        // ------------------------------------------------------------------ completion

        protected override IReadOnlyList<CompletionItem> Complete(string text, LanguagePosition position, CompletionTriggerKind trigger) =>
            CompleteCore(text, position, trigger).Select(i => i.ReplaceRange is LanguageRange r
                ? new CompletionItem(i.Label, i.Kind, i.Detail, i.InsertText, At(text, r.Start.Character, r.End.Character), i.CaretOffset)
                : i).ToList();

        IReadOnlyList<CompletionItem> CompleteCore(string text, LanguagePosition position, CompletionTriggerKind trigger)
        {
            int caret = LanguageText.OffsetOf(text, position);
            var caretRange = At(text, caret, caret);
            var result = new List<CompletionItem>();

            // Inside square brackets: the classes whose abbreviation starts with what is typed.
            int open = caret > 0 ? text.LastIndexOf('[', caret - 1) : -1;
            if (open >= 0 && text.IndexOf(']', open, caret - open) < 0 && text.IndexOfAny(new[] { '/', '_', '#', '(', ')' }, open, caret - open) < 0)
            {
                string typed = text.Substring(open + 1, caret - open - 1).TrimStart();
                int end = caret;
                if (end < text.Length && text[end] == ']') end++;   // an accepted class replaces its closing bracket too, so none is doubled
                var replace = new LanguageRange(open, end);
                foreach (var c in _classes.Where(c => c.Abbreviation.StartsWith(typed, StringComparison.OrdinalIgnoreCase)))
                    result.Add(new CompletionItem("[" + c.Abbreviation + "]", CompletionItemKind.Class, c.Name, "[" + c.Abbreviation + "]", replace));
                return result;
            }

            // A word being typed: the phonemes that start with it.
            int wordStart = caret;
            while (wordStart > 0 && !IsSpace(text[wordStart - 1]) && !IsPunct(text[wordStart - 1])) wordStart--;
            string prefix = text.Substring(wordStart, caret - wordStart);
            var wordRange = new LanguageRange(wordStart, caret);
            var selection = caretRange;

            void Structure(string kind, string label, CompletionItemKind itemKind, string detail, string insert, int? caretOffset = null)
            {
                if (prefix.Length == 0 && CanInsert(text, selection, kind))
                    result.Add(new CompletionItem(label, itemKind, detail, insert, null, caretOffset));
            }

            Structure(InsertKinds.Slash, "/", CompletionItemKind.Operator, "starts the environment", "/ ");
            Structure(InsertKinds.Bar, "_", CompletionItemKind.Operator, "where the allomorph stands", "_");
            Structure(InsertKinds.WordBoundary, "#", CompletionItemKind.Operator, "word boundary", "#");
            Structure(InsertKinds.OptionalItem, "()", CompletionItemKind.Snippet, "optional item", "()", 1);
            if (prefix.Length == 0 && CanInsert(text, selection, InsertKinds.NaturalClass))
                foreach (var c in _classes)
                    result.Add(new CompletionItem("[" + c.Abbreviation + "]", CompletionItemKind.Class, c.Name, "[" + c.Abbreviation + "]"));
            if (CanInsert(text, selection, InsertKinds.NaturalClass) || (text.IndexOf('/') >= 0 && prefix.Length > 0))
                foreach (var s in _segments.Where(s => prefix.Length == 0 || s.StartsWith(prefix, StringComparison.Ordinal)))
                    result.Add(new CompletionItem(s, CompletionItemKind.Value, "segment", s, prefix.Length == 0 ? (LanguageRange?)null : wordRange));
            return result;
        }
    }
}
