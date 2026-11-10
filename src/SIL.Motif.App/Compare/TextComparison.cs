using SIL.Motif.Runner.Operations;

namespace SIL.Motif.App.Compare;

/// <summary>
/// A sorted text and the person's choices: every change made on one side starts out included, and every change both
/// made waits for a choice. <see cref="Result"/> is the text those choices make. A paragraph rewritten in the
/// side-by-side view replaces the choices inside it until it is set back.
/// </summary>
public sealed class TextComparison
{
    private readonly Dictionary<int, Choice> _choices = [];
    private readonly Dictionary<int, IReadOnlyList<ParagraphVersion>> _rewritten = [];

    /// <summary>Starts comparing <paramref name="sorted"/>, with each one-sided change at its author's side.</summary>
    public TextComparison(SortedText sorted)
    {
        Sorted = sorted ?? throw new ArgumentNullException(nameof(sorted));
        foreach (var change in sorted.Changes)
            if (change.Initial is { } initial) _choices[change.Id] = initial;
    }

    public SortedText Sorted { get; }

    /// <summary>Raised whenever a choice or a rewritten paragraph changes.</summary>
    public event EventHandler? Changed;

    /// <summary>The choice made for <paramref name="change"/>, or null while one both made waits.</summary>
    public Choice? ChoiceFor(TextChange change) => _choices.TryGetValue(change.Id, out var choice) ? choice : null;

    /// <summary>Chooses <paramref name="choice"/> for <paramref name="change"/>; false, and nothing changes, when it is not offered.</summary>
    public bool Choose(TextChange change, Choice choice)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (choice == Choice.Both && !change.CanUseBoth) return false;
        if (_choices.TryGetValue(change.Id, out var was) && was == choice) return true;
        _choices[change.Id] = choice;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Takes one side for every change, both-made ones included.</summary>
    public void ChooseAll(Choice side)
    {
        if (side == Choice.Both) throw new ArgumentOutOfRangeException(nameof(side));
        foreach (var change in Sorted.Changes) _choices[change.Id] = side;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The changes both made that still wait for a choice, in reading order, outside paragraphs rewritten side by side.</summary>
    public IReadOnlyList<TextChange> Undecided => Sorted.Changes
        .Where(change => !_choices.ContainsKey(change.Id) && !_rewritten.ContainsKey(change.Paragraph)).ToArray();

    /// <summary>Whether every change both made has a choice, so Done can make the result.</summary>
    public bool IsDecided => Undecided.Count == 0;

    /// <summary>The paragraphs the person wrote in the side-by-side view in place of sorted paragraph <paramref name="paragraph"/>, or null.</summary>
    public IReadOnlyList<ParagraphVersion>? RewrittenAt(int paragraph) => _rewritten.GetValueOrDefault(paragraph);

    /// <summary>
    /// Puts <paramref name="paragraphs"/> in place of sorted paragraph <paramref name="paragraph"/>, as typed on the
    /// result side of the side-by-side view; null sets it back to the choices.
    /// </summary>
    public void Rewrite(int paragraph, IReadOnlyList<ParagraphVersion>? paragraphs)
    {
        if (paragraphs is null)
        {
            if (!_rewritten.Remove(paragraph)) return;
        }
        else
        {
            _rewritten[paragraph] = paragraphs;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>How many changes each side made and how many need a choice, as the summary line says.</summary>
    public string Summary
    {
        get
        {
            var changes = Sorted.Changes;
            var mine = changes.Count(change => change.Author == ChangeAuthor.Mine);
            var fieldWorks = changes.Count(change => change.Author == ChangeAuthor.FieldWorks);
            var both = Undecided.Count;
            return $"{Count(changes.Count, "change")}: {mine} yours · {fieldWorks} from FieldWorks · " +
                   (both == 1 ? "1 needs your choice" : $"{both} need your choice");
        }
    }

    /// <summary>Why Done waits, or null when it does not.</summary>
    public string? Waiting
    {
        get
        {
            var undecided = Undecided;
            if (undecided.Count == 0) return null;
            var places = string.Join(", ", undecided.Select(change => change.Location).Distinct());
            return undecided.Count == 1 ? $"1 change still needs a choice ({places})" : $"{undecided.Count} changes still need a choice ({places})";
        }
    }

    /// <summary>
    /// The text the choices make, paragraph by paragraph. A paragraph's key is the one it had on the side it comes
    /// from; a change both made that waits counts as the person's version.
    /// </summary>
    public IReadOnlyList<ParagraphVersion> Result(IReadOnlyDictionary<int, IReadOnlyList<ParagraphVersion>>? rows = null)
    {
        var result = new List<ParagraphVersion>();
        foreach (var paragraph in Sorted.Paragraphs)
        {
            if (rows is not null && rows.TryGetValue(paragraph.Index, out var row))
            {
                result.AddRange(row);
                continue;
            }
            if (_rewritten.TryGetValue(paragraph.Index, out var rewritten))
            {
                result.AddRange(rewritten);
                continue;
            }
            if (paragraph.Whole is { } whole)
            {
                if ((ChoiceFor(whole) == Choice.FieldWorks ? paragraph.FieldWorks : paragraph.Mine) is { } version) result.Add(version);
                continue;
            }
            result.Add(Paragraph(paragraph));
        }
        return result;
    }

    /// <summary>One sorted paragraph as the choices make it, ignoring a rewrite in side by side.</summary>
    public ParagraphVersion Paragraph(SortedParagraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        if (paragraph.Whole is { } whole)
            return (ChoiceFor(whole) == Choice.FieldWorks ? paragraph.FieldWorks : paragraph.Mine) ??
                   paragraph.Mine ?? paragraph.FieldWorks!;
        var tokens = new List<Token>();
        foreach (var piece in paragraph.Pieces)
        {
            switch (piece)
            {
                case UnchangedPiece unchanged:
                    tokens.AddRange(unchanged.Tokens);
                    break;
                case ChangePiece { Change: var change }:
                    tokens.AddRange(Words(change, ChoiceFor(change)));
                    break;
            }
        }
        var format = paragraph.Format is { } formatChange && ChoiceFor(formatChange) == Choice.FieldWorks ? paragraph.FieldWorks! : paragraph.Mine!;
        var key = paragraph.Mine?.Key ?? paragraph.FieldWorks?.Key;
        return new ParagraphVersion(key, format.Style, format.Props, Tokens.Runs(tokens));
    }

    /// <summary>The words <paramref name="change"/> puts in the text under <paramref name="choice"/>; none chosen reads as the person's.</summary>
    public static IReadOnlyList<Token> Words(TextChange change, Choice? choice)
    {
        ArgumentNullException.ThrowIfNull(change);
        return choice switch
        {
            Choice.FieldWorks => change.FieldWorks,
            Choice.Both => [.. change.Mine, .. change.FieldWorks],
            _ => change.Mine,
        };
    }

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}

/// <summary>
/// Turns a compared result back into the payload a person's pending edit carries, measured against the text FieldWorks
/// now holds, so it fits again. Null means the result is the FieldWorks text and nothing is left to change.
/// </summary>
public static class ComparedPayloads
{
    /// <summary>
    /// The <c>text/setParagraphs</c> payload that turns <paramref name="live"/> into <paramref name="result"/>. A result
    /// paragraph whose key FieldWorks no longer has goes back as a new paragraph; kept paragraphs keep FieldWorks' order.
    /// </summary>
    public static TextParagraphsPayload? Paragraphs(string field, IReadOnlyList<ParagraphVersion> result,
        IReadOnlyList<Contract.Responses.GdrParagraph> live)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(live);
        var liveVersions = live.Where(paragraph => paragraph.Key is not null)
            .ToDictionary(paragraph => paragraph.Key!, paragraph => (Gdr: paragraph, Version: TextVersions.Paragraph(paragraph)), StringComparer.Ordinal);
        var liveOrder = live.Select(paragraph => paragraph.Key).OfType<string>().ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var lastKept = -1;
        var order = new List<string?>();
        var changed = new List<TextParagraphChange>();
        foreach (var paragraph in result)
        {
            // A key kept twice, out of FieldWorks' order, or gone from FieldWorks makes a new paragraph.
            var key = paragraph.Key is { } candidate && liveVersions.ContainsKey(candidate) && liveOrder.IndexOf(candidate) > lastKept &&
                      seen.Add(candidate) ? candidate : null;
            if (key is not null) lastKept = liveOrder.IndexOf(key);
            if (key is null || !liveVersions[key].Version.SameContent(paragraph))
                changed.Add(new TextParagraphChange(order.Count, key, paragraph.Style.Length > 0 ? paragraph.Style : null,
                    Tokens.Join(paragraph.Runs), paragraph.Props));
            order.Add(key);
        }
        var removed = liveOrder.Where(key => !seen.Contains(key)).ToArray();
        if (changed.Count == 0 && removed.Length == 0) return null;
        var expected = changed.Where(change => change.Key is not null).Select(change => change.Key!).Concat(removed)
            .ToDictionary(key => key, key =>
            {
                var gdr = liveVersions[key].Gdr;
                return new TextParagraphExpected(string.Concat(gdr.Runs.Select(run => run.Text)), gdr.Style ?? "", Contract.Responses.TextRunsDigest.Of(gdr));
            }, StringComparer.Ordinal);
        return new TextParagraphsPayload(field, order, changed, removed, expected);
    }

    /// <summary>The <c>text/setRichString</c> payload that turns <paramref name="live"/> into <paramref name="result"/>.</summary>
    public static TextRichStringPayload? RichString(string field, string? ws, ParagraphVersion result, IReadOnlyList<Contract.Responses.GdrRun> live)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(live);
        var runs = Tokens.Join(result.Runs);
        var liveDigest = Contract.Responses.TextRunsDigest.OfRuns(live);
        if (TextVersions.Digest(runs) == liveDigest) return null;
        return new TextRichStringPayload(field, ws, runs, new TextRichStringExpected(string.Concat(live.Select(run => run.Text)), liveDigest));
    }
}
