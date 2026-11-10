using SIL.Motif.Runner.Operations;

namespace SIL.Motif.App.Compare;

/// <summary>Who made a change: the person, FieldWorks, or both of them to the same words.</summary>
public enum ChangeAuthor
{
    Mine,
    FieldWorks,
    Both,
}

/// <summary>What a change is.</summary>
public enum ChangeKind
{
    /// <summary>Words added, removed or replaced.</summary>
    Wording,

    /// <summary>The same words with other formatting.</summary>
    Formatting,

    /// <summary>The same words and formatting in another writing system.</summary>
    WritingSystem,

    /// <summary>A whole paragraph added.</summary>
    ParagraphAdded,

    /// <summary>A whole paragraph removed, or removed on one side and changed on the other.</summary>
    ParagraphRemoved,

    /// <summary>A paragraph's style or paragraph properties.</summary>
    ParagraphFormatting,

    /// <summary>More than half a paragraph's words changed on both sides: one choice for the whole paragraph.</summary>
    Rewritten,
}

/// <summary>Which version of a change's words the result takes.</summary>
public enum Choice
{
    Mine,
    FieldWorks,

    /// <summary>The person's words, then the FieldWorks words; offered only where <see cref="TextChange.CanUseBoth"/>.</summary>
    Both,
}

/// <summary>
/// One change the sort found, with the starting words and each side's version of them. A change made on one side only
/// starts out included; one both sides made to the same words needs the person's choice.
/// </summary>
public sealed class TextChange
{
    internal TextChange(int id, ChangeAuthor author, ChangeKind kind, int paragraph, string location,
        IReadOnlyList<Token> starting, IReadOnlyList<Token> mine, IReadOnlyList<Token> fieldWorks, string? whyNotBoth)
    {
        Id = id;
        Author = author;
        Kind = kind;
        Paragraph = paragraph;
        Location = location;
        Starting = starting;
        Mine = mine;
        FieldWorks = fieldWorks;
        WhyNotBoth = whyNotBoth;
    }

    /// <summary>The change's number, in reading order from 0.</summary>
    public int Id { get; }

    public ChangeAuthor Author { get; }

    public ChangeKind Kind { get; }

    /// <summary>The index of the <see cref="SortedParagraph"/> the change is in.</summary>
    public int Paragraph { get; }

    /// <summary>Where the change is, in words: "paragraph 2", or the label the caller gave a one-string field.</summary>
    public string Location { get; }

    /// <summary>The starting words the change replaces; empty for an insertion.</summary>
    public IReadOnlyList<Token> Starting { get; }

    /// <summary>The person's version of those words.</summary>
    public IReadOnlyList<Token> Mine { get; }

    /// <summary>The FieldWorks version of those words.</summary>
    public IReadOnlyList<Token> FieldWorks { get; }

    /// <summary>Whether Use both is possible: both sides inserted at the same point, and nothing was replaced.</summary>
    public bool CanUseBoth => Author == ChangeAuthor.Both && WhyNotBoth is null;

    /// <summary>Why Use both is not possible for a change both sides made; null for one-sided changes and when it is possible.</summary>
    public string? WhyNotBoth { get; }

    /// <summary>The choice the change starts with: its author's side, or none when both made it.</summary>
    public Choice? Initial => Author switch
    {
        ChangeAuthor.Mine => Choice.Mine,
        ChangeAuthor.FieldWorks => Choice.FieldWorks,
        _ => null,
    };

    /// <summary>Whether the change covers a whole paragraph rather than words inside one.</summary>
    public bool IsWholeParagraph => Kind is ChangeKind.ParagraphAdded or ChangeKind.ParagraphRemoved or ChangeKind.Rewritten;

    /// <summary>The change in words, as the changes list and a screen reader say it.</summary>
    public string Description => ChangeWords.Describe(this);

    public override string ToString() => Description;
}

/// <summary>One stretch of a paragraph: words both sides left as they began (or changed alike), or one change.</summary>
public abstract record Piece;

/// <summary>Words that read the same in both versions.</summary>
public sealed record UnchangedPiece(IReadOnlyList<Token> Tokens) : Piece;

/// <summary>A change inside a paragraph.</summary>
public sealed record ChangePiece(TextChange Change) : Piece;

/// <summary>
/// One paragraph of the combined text, in reading order: the person's and the FieldWorks version (either may be
/// absent), and either one whole-paragraph change or the pieces it reads as, with any paragraph formatting change.
/// </summary>
public sealed class SortedParagraph
{
    internal SortedParagraph(int index, string? key, ParagraphVersion? mine, ParagraphVersion? fieldWorks)
    {
        Index = index;
        Key = key;
        Mine = mine;
        FieldWorks = fieldWorks;
    }

    public int Index { get; }

    /// <summary>The paragraph's key on whichever side has one; null for a paragraph the person added.</summary>
    public string? Key { get; }

    public ParagraphVersion? Mine { get; }

    public ParagraphVersion? FieldWorks { get; }

    /// <summary>A change of the whole paragraph, or null when the paragraph reads as <see cref="Pieces"/>.</summary>
    public TextChange? Whole { get; internal set; }

    /// <summary>A change of the paragraph's style or properties, or null.</summary>
    public TextChange? Format { get; internal set; }

    /// <summary>The paragraph's stretches, when it is not one whole change.</summary>
    public IReadOnlyList<Piece> Pieces { get; internal set; } = [];
}

/// <summary>The sort's answer: the combined text's paragraphs in reading order and every change in it.</summary>
public sealed class SortedText
{
    internal SortedText(IReadOnlyList<SortedParagraph> paragraphs, IReadOnlyList<TextChange> changes)
    {
        Paragraphs = paragraphs;
        Changes = changes;
    }

    public IReadOnlyList<SortedParagraph> Paragraphs { get; }

    public IReadOnlyList<TextChange> Changes { get; }
}

/// <summary>
/// The three-way sort. Each version is compared with the starting text: paragraphs by key, and inside a paragraph
/// word by word. A change made on one side only is the author's; changes on both sides overlap, and need a choice,
/// when the starting words they replace share a token, when both insert at the same point, or when one removes or
/// rewords words the other reformats. A formatting change and a wording change to different words never overlap.
/// </summary>
public static class TextSort
{
    /// <summary>When both sides changed more than this share of a paragraph's starting words, it is "rewritten in both places".</summary>
    public const double RewrittenShare = 0.5;

    internal const string ReplacesSameText = "the two versions replace the same text";
    internal const string RemovesReformatted = "one version changes words the other reformats";
    internal const string FormatsDiffer = "the two versions format the same words differently";
    internal const string ParagraphWhole = "a whole paragraph is one version or the other";

    /// <summary>
    /// Sorts <paramref name="versions"/>. <paramref name="location"/>, when given, names every change's place, for a
    /// field that is one formatted string; otherwise a change's place is its paragraph's number.
    /// </summary>
    public static SortedText Sort(TextVersions versions, string? location = null)
    {
        ArgumentNullException.ThrowIfNull(versions);
        var builder = new Builder(location);
        var starting = versions.Starting.GroupBy(paragraph => paragraph.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var (mine, fieldWorks) in Slots(versions))
        {
            var key = mine?.Key ?? fieldWorks?.Key;
            var paragraph = builder.Paragraph(key, mine, fieldWorks);
            var start = key is null ? null : starting.GetValueOrDefault(key);
            if (start is null)
            {
                builder.Whole(paragraph, mine is null ? ChangeAuthor.FieldWorks : ChangeAuthor.Mine, ChangeKind.ParagraphAdded);
                continue;
            }
            if (mine is null || fieldWorks is null)
            {
                var kept = mine ?? fieldWorks!;
                var untouched = start.Exact is { } exact ? exact.SameContent(kept) : TextVersions.Same(kept.Text, start.Text) && kept.Style == start.Style;
                builder.Whole(paragraph, untouched ? (mine is null ? ChangeAuthor.Mine : ChangeAuthor.FieldWorks) : ChangeAuthor.Both,
                    ChangeKind.ParagraphRemoved);
                continue;
            }
            builder.Inside(paragraph, start, mine, fieldWorks);
        }
        return builder.Done();
    }

    // FieldWorks' order, each paragraph only the person has following its predecessor in the person's order.
    private static List<(ParagraphVersion? Mine, ParagraphVersion? FieldWorks)> Slots(TextVersions versions)
    {
        var mineByKey = versions.Mine.Where(paragraph => paragraph.Key is not null)
            .GroupBy(paragraph => paragraph.Key!, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var slots = versions.FieldWorks
            .Select(paragraph => (Mine: paragraph.Key is null ? null : mineByKey.GetValueOrDefault(paragraph.Key), FieldWorks: (ParagraphVersion?)paragraph))
            .ToList();
        var fieldWorksKeys = versions.FieldWorks.Where(paragraph => paragraph.Key is not null).Select(paragraph => paragraph.Key!)
            .ToHashSet(StringComparer.Ordinal);
        var insertAt = 0;
        foreach (var mine in versions.Mine)
        {
            if (mine.Key is { } key && fieldWorksKeys.Contains(key))
            {
                insertAt = slots.FindIndex(slot => slot.FieldWorks?.Key == key) + 1;
                continue;
            }
            slots.Insert(insertAt++, (mine, null));
        }
        return slots;
    }

    private sealed class Builder(string? location)
    {
        private readonly List<SortedParagraph> _paragraphs = [];
        private readonly List<TextChange> _changes = [];

        public SortedParagraph Paragraph(string? key, ParagraphVersion? mine, ParagraphVersion? fieldWorks)
        {
            var paragraph = new SortedParagraph(_paragraphs.Count, key, mine, fieldWorks);
            _paragraphs.Add(paragraph);
            return paragraph;
        }

        public SortedText Done() => new(_paragraphs, _changes);

        private string Where(SortedParagraph paragraph) => location ?? $"paragraph {paragraph.Index + 1}";

        private TextChange Add(ChangeAuthor author, ChangeKind kind, SortedParagraph paragraph, IReadOnlyList<Token> starting,
            IReadOnlyList<Token> mine, IReadOnlyList<Token> fieldWorks, string? whyNotBoth)
        {
            var change = new TextChange(_changes.Count, author, kind, paragraph.Index, Where(paragraph), starting, mine, fieldWorks,
                author == ChangeAuthor.Both ? whyNotBoth : null);
            _changes.Add(change);
            return change;
        }

        public void Whole(SortedParagraph paragraph, ChangeAuthor author, ChangeKind kind)
        {
            var mine = paragraph.Mine is null ? [] : Tokens.Of(paragraph.Mine.Runs);
            var fieldWorks = paragraph.FieldWorks is null ? [] : Tokens.Of(paragraph.FieldWorks.Runs);
            paragraph.Whole = Add(author, kind, paragraph, [], mine, fieldWorks, ParagraphWhole);
        }

        public void Inside(SortedParagraph paragraph, StartingParagraph start, ParagraphVersion mine, ParagraphVersion fieldWorks)
        {
            var known = start.Exact is not null;
            var baseTokens = known ? Tokens.Of(start.Exact!.Runs) : Tokens.Of([new TextRun(start.Text, "")]);
            var mineTokens = Tokens.Of(mine.Runs);
            var fieldWorksTokens = Tokens.Of(fieldWorks.Runs);
            var toMine = Alignment.Match(baseTokens, mineTokens);
            var toFieldWorks = Alignment.Match(baseTokens, fieldWorksTokens);
            var mineHunks = Hunk.From(Side.Mine, baseTokens, mineTokens, toMine, known);
            var fieldWorksHunks = Hunk.From(Side.FieldWorks, baseTokens, fieldWorksTokens, toFieldWorks, known);
            if (!known) fieldWorksHunks.AddRange(UnknownStartFormatting(baseTokens, mineTokens, fieldWorksTokens, toMine, toFieldWorks, mineHunks, fieldWorksHunks));

            var groups = Grouped(mineHunks, fieldWorksHunks);
            if (groups.Any(group => group.Both && !group.Same) && Share(baseTokens, mineHunks) > RewrittenShare &&
                Share(baseTokens, fieldWorksHunks) > RewrittenShare)
            {
                paragraph.Whole = Add(ChangeAuthor.Both, ChangeKind.Rewritten, paragraph, baseTokens, mineTokens, fieldWorksTokens, ParagraphWhole);
                return;
            }
            ParagraphFormat(paragraph, start, mine, fieldWorks);
            paragraph.Pieces = Pieces(paragraph, baseTokens, mineTokens, toMine, groups);
        }

        private void ParagraphFormat(SortedParagraph paragraph, StartingParagraph start, ParagraphVersion mine, ParagraphVersion fieldWorks)
        {
            string Of(ParagraphVersion version) => version.Style + "\u0001" + TextVersions.Signature(version.Props);
            var (mineFormat, fieldWorksFormat) = (Of(mine), Of(fieldWorks));
            if (mineFormat == fieldWorksFormat) return;
            // Only the style of the start is remembered when its runs are not, so its other properties are unknown.
            var startFormat = start.Exact is { } exact ? Of(exact) : null;
            var author = startFormat == mineFormat ? ChangeAuthor.FieldWorks
                : startFormat == fieldWorksFormat ? ChangeAuthor.Mine
                : startFormat is null && mine.Style == fieldWorks.Style ? ChangeAuthor.Both
                : startFormat is null && mine.Style == start.Style ? ChangeAuthor.FieldWorks
                : startFormat is null && fieldWorks.Style == start.Style ? ChangeAuthor.Mine
                : ChangeAuthor.Both;
            paragraph.Format = Add(author, ChangeKind.ParagraphFormatting, paragraph, [], [], [], ParagraphWhole);
        }

        private List<Piece> Pieces(SortedParagraph paragraph, IReadOnlyList<Token> baseTokens, IReadOnlyList<Token> mineTokens,
            int[] toMine, List<Group> groups)
        {
            var pieces = new List<Piece>();
            var unchanged = new List<Token>();
            void Flush()
            {
                if (unchanged.Count == 0) return;
                pieces.Add(new UnchangedPiece(unchanged.ToArray()));
                unchanged.Clear();
            }
            var byStart = groups.OrderBy(group => group.Start).ThenBy(group => group.End).ThenBy(group => group.IsInsertion ? 0 : 1).ToList();
            var next = 0;
            var at = 0;
            while (at <= baseTokens.Count)
            {
                // Insertions at this point come first, then a change that replaces from here, then the starting token.
                while (next < byStart.Count && byStart[next].Start == at && byStart[next].IsInsertion)
                    Emit(byStart[next++]);
                if (next < byStart.Count && byStart[next].Start == at)
                {
                    var group = byStart[next++];
                    Emit(group);
                    at = group.End;
                    continue;
                }
                if (at == baseTokens.Count) break;
                unchanged.Add(toMine[at] >= 0 ? mineTokens[toMine[at]] : baseTokens[at]);
                at++;
            }
            Flush();
            return pieces;

            void Emit(Group group)
            {
                if (group.Same)
                {
                    unchanged.AddRange(group.Version(Side.Mine, baseTokens));
                    return;
                }
                Flush();
                var starting = baseTokens.Skip(group.Start).Take(group.End - group.Start).ToArray();
                var author = group.Both ? ChangeAuthor.Both : group.Hunks[0].Side == Side.Mine ? ChangeAuthor.Mine : ChangeAuthor.FieldWorks;
                var kind = group.Hunks.All(hunk => hunk.Kind == ChangeKind.WritingSystem) ? ChangeKind.WritingSystem
                    : group.Hunks.All(hunk => hunk.Kind != ChangeKind.Wording) ? ChangeKind.Formatting
                    : ChangeKind.Wording;
                var whyNot = group.Hunks.Any(hunk => hunk.Kind == ChangeKind.Wording) && group.Hunks.Any(hunk => hunk.Kind != ChangeKind.Wording)
                    ? RemovesReformatted
                    : group.Hunks.All(hunk => hunk.Kind != ChangeKind.Wording) ? FormatsDiffer
                    : group.Hunks.All(hunk => hunk.IsInsertion) && group.Hunks.Count == 2 ? null
                    : ReplacesSameText;
                pieces.Add(new ChangePiece(Add(author, kind, paragraph, starting, group.Version(Side.Mine, baseTokens),
                    group.Version(Side.FieldWorks, baseTokens), whyNot)));
            }
        }

        // Without the starting runs, a formatting difference between the sides cannot be given to either.
        private static IEnumerable<Hunk> UnknownStartFormatting(IReadOnlyList<Token> baseTokens, IReadOnlyList<Token> mine,
            IReadOnlyList<Token> fieldWorks, int[] toMine, int[] toFieldWorks, List<Hunk> mineHunks, List<Hunk> fieldWorksHunks)
        {
            Hunk? open = null;
            for (var i = 0; i < baseTokens.Count; i++)
            {
                var (m, f) = (toMine[i], toFieldWorks[i]);
                var differs = m >= 0 && f >= 0 && mine[m].Format != fieldWorks[f].Format &&
                    !mineHunks.Concat(fieldWorksHunks).Any(hunk => hunk.Start <= i && i < hunk.End || hunk.IsInsertion && hunk.Start == i);
                if (!differs)
                {
                    if (open is not null) yield return open;
                    open = null;
                    continue;
                }
                var kind = mine[m].FormatWithoutWritingSystem == fieldWorks[f].FormatWithoutWritingSystem ? ChangeKind.WritingSystem : ChangeKind.Formatting;
                if (open is not null && open.End == i && open.Kind == kind)
                {
                    open = open.Extend(fieldWorks[f], mine[m]);
                    continue;
                }
                if (open is not null) yield return open;
                open = new Hunk(Side.FieldWorks, i, i + 1, [fieldWorks[f]], kind) { Unknown = [mine[m]] };
            }
            if (open is not null) yield return open;
        }

        private static double Share(IReadOnlyList<Token> baseTokens, List<Hunk> hunks)
        {
            var words = baseTokens.Count(token => token.Kind == TokenKind.Word);
            if (words == 0) return 0;
            var touched = Enumerable.Range(0, baseTokens.Count)
                .Count(i => baseTokens[i].Kind == TokenKind.Word && hunks.Any(hunk => hunk.Start <= i && i < hunk.End));
            return (double)touched / words;
        }
    }

    internal enum Side
    {
        Mine,
        FieldWorks,
    }

    // A stretch of starting tokens [Start, End) one side replaced with Tokens; Start == End is an insertion.
    internal sealed record Hunk(Side Side, int Start, int End, IReadOnlyList<Token> Tokens, ChangeKind Kind)
    {
        public bool IsInsertion => Start == End;

        // The person's own version of a formatting difference found without the starting runs.
        public IReadOnlyList<Token>? Unknown { get; init; }

        public Hunk Extend(Token token, Token? unknown) => this with
        {
            End = End + 1,
            Tokens = [.. Tokens, token],
            Unknown = unknown is null ? Unknown : [.. Unknown ?? [], unknown],
        };

        public static List<Hunk> From(Side side, IReadOnlyList<Token> baseTokens, IReadOnlyList<Token> sideTokens, int[] map, bool formatKnown)
        {
            var hunks = new List<Hunk>();
            var (previousBase, previousSide) = (0, 0);
            for (var i = 0; i <= baseTokens.Count; i++)
            {
                if (i < baseTokens.Count && map[i] < 0) continue;
                var j = i < baseTokens.Count ? map[i] : sideTokens.Count;
                if (i > previousBase || j > previousSide)
                    hunks.Add(new Hunk(side, previousBase, i, sideTokens.Skip(previousSide).Take(j - previousSide).ToArray(), ChangeKind.Wording));
                if (i < baseTokens.Count && formatKnown && baseTokens[i].Format != sideTokens[j].Format)
                {
                    var kind = baseTokens[i].FormatWithoutWritingSystem == sideTokens[j].FormatWithoutWritingSystem
                        ? ChangeKind.WritingSystem : ChangeKind.Formatting;
                    if (hunks.Count > 0 && hunks[^1] is { Kind: not ChangeKind.Wording } last && last.End == i && last.Kind == kind)
                        hunks[^1] = last.Extend(sideTokens[j], null);
                    else
                        hunks.Add(new Hunk(side, i, i + 1, [sideTokens[j]], kind));
                }
                previousBase = i + 1;
                previousSide = j + 1;
            }
            return hunks;
        }
    }

    // Hunks that overlap, joined across the two sides.
    internal sealed class Group(List<Hunk> hunks)
    {
        public List<Hunk> Hunks { get; } = hunks;

        public int Start => Hunks.Min(hunk => hunk.Start);

        public int End => Hunks.Max(hunk => hunk.End);

        public bool IsInsertion => Start == End;

        public bool Both => Hunks.Any(hunk => hunk.Side == Side.Mine) && Hunks.Any(hunk => hunk.Side == Side.FieldWorks) ||
            Hunks.Any(hunk => hunk.Unknown is not null);

        // Both sides made the same change: shown once, unmarked.
        public bool Same => Both && Hunks.All(hunk => hunk.Unknown is null) && Hunks.Count == 2 &&
            Hunks[0].Start == Hunks[1].Start && Hunks[0].End == Hunks[1].End &&
            Hunks[0].Tokens.Select(token => token.Text + "\u0001" + token.Format)
                .SequenceEqual(Hunks[1].Tokens.Select(token => token.Text + "\u0001" + token.Format));

        // One side's version of the starting tokens this group covers.
        public IReadOnlyList<Token> Version(Side side, IReadOnlyList<Token> baseTokens)
        {
            var own = Hunks.Where(hunk => hunk.Side == side && hunk.Unknown is null).OrderBy(hunk => hunk.Start).ThenBy(hunk => hunk.End).ToList();
            if (side == Side.Mine && Hunks.FirstOrDefault(hunk => hunk.Unknown is not null) is { } unknown) return unknown.Unknown!;
            if (side == Side.FieldWorks && Hunks.FirstOrDefault(hunk => hunk.Unknown is not null) is { } theirs) return theirs.Tokens;
            var tokens = new List<Token>();
            var at = Start;
            var next = 0;
            while (at <= End)
            {
                while (next < own.Count && own[next].Start == at && own[next].IsInsertion) tokens.AddRange(own[next++].Tokens);
                if (next < own.Count && own[next].Start == at)
                {
                    tokens.AddRange(own[next].Tokens);
                    at = own[next++].End;
                    continue;
                }
                if (at == End) break;
                tokens.Add(baseTokens[at]);
                at++;
            }
            return tokens;
        }
    }

    private static List<Group> Grouped(List<Hunk> mine, List<Hunk> fieldWorks)
    {
        var groups = new List<Group>();
        foreach (var hunk in mine.Concat(fieldWorks))
        {
            var touching = groups.Where(group => group.Hunks.Any(other => other.Side != hunk.Side && Overlap(hunk, other)) ||
                group.Hunks.Any(other => other.Unknown is not null && Overlap(hunk, other))).ToList();
            var joined = new List<Hunk> { hunk };
            foreach (var group in touching)
            {
                joined.AddRange(group.Hunks);
                groups.Remove(group);
            }
            groups.Add(new Group(joined));
        }
        return groups;
    }

    // Shared starting tokens, one insertion point, or an insertion strictly inside the other's stretch.
    internal static bool Overlap(Hunk left, Hunk right)
    {
        if (left.IsInsertion && right.IsInsertion) return left.Start == right.Start;
        if (left.IsInsertion) return right.Start < left.Start && left.Start < right.End;
        if (right.IsInsertion) return left.Start < right.Start && right.Start < left.End;
        return Math.Max(left.Start, right.Start) < Math.Min(left.End, right.End);
    }
}
