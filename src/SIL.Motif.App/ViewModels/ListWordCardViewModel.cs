using System.Windows.Input;
using SIL.Motif.App.Controls;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// The word card an opened Lists row shows: FieldWorks' analysis and PanGloss's lined up morpheme by morpheme, the
/// pieces where they differ marked, one sentence saying where the two part, and the way to compare them in Try a Word.
/// </summary>
public sealed class ListWordCardViewModel
{
    public ListWordCardViewModel(WordRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        Row = row;
        HasFieldWorksAnalysis = row.FieldWorksMorphemes.Count > 0;
        ShowsPanGlossMorphemes = row.Outcome == ParserOutcome.Different && row.PanGlossMorphemes.Count > 0;
        Segments = ShowsPanGlossMorphemes
            ? WordRowProjection.Align(row.Row.FieldWorksMorphemes, row.Row.PanGlossMorphemes)
                .Select(segment => Segment(segment, marks: HasFieldWorksAnalysis && !segment.Shared)).ToArray()
            : row.FieldWorksMorphemes.Select(morph =>
                new ListWordCardSegmentViewModel([new ListWordCardMorphViewModel(morph, false)], [], false)).ToArray();
        WhereTheyPart = Sentence();
        var readings = row.Row.PanGlossReadingCount;
        PanGlossNote = ShowsPanGlossMorphemes && readings > 1 ? $"Closest of {readings:N0}" : string.Empty;
    }

    public WordRowViewModel Row { get; }

    public bool HasFieldWorksAnalysis { get; }

    /// <summary>Whether PanGloss's own morphemes line up under FieldWorks', which they do only when it built something different.</summary>
    public bool ShowsPanGlossMorphemes { get; }

    /// <summary>The two analyses in order, a column per segment: one shared morpheme, or one place where they part.</summary>
    public IReadOnlyList<ListWordCardSegmentViewModel> Segments { get; }

    /// <summary>One sentence, in window words, naming the first place the two analyses part, or why they cannot.</summary>
    public string WhereTheyPart { get; }

    /// <summary>A note beside PanGloss's analysis when it built several and the card shows the closest; else empty.</summary>
    public string PanGlossNote { get; }

    public bool HasPanGlossNote => PanGlossNote.Length > 0;

    /// <summary>Why FieldWorks' line is empty: no analysis at all, or an opinion with no single analysis to line up.</summary>
    public string FieldWorksAbsentText => HoldsNothing
        ? "FieldWorks holds no analysis of this word." : "FieldWorks holds no single analysis of this word to line up.";

    private bool HoldsNothing => Row.Row.Opinion is null or ProjectStanding.NotPresent;

    public Mark OutcomeMark => Row.OutcomeMark;

    public string OutcomeWord => Row.OutcomeWord;

    public string CompareLabel => "Try a Word";

    public string CompareName => $"Compare {Row.Word} in Try a Word";

    public ICommand CompareCommand => Row.TryWordCommand;

    private ListWordCardSegmentViewModel Segment(WordRowSegment segment, bool marks) => new(
        segment.FieldWorks.Select(index => new ListWordCardMorphViewModel(Row.FieldWorksMorphemes[index], marks)).ToArray(),
        segment.PanGloss.Select(index => new ListWordCardMorphViewModel(Row.PanGlossMorphemes[index].Morph, marks)).ToArray(),
        marks);

    private string Sentence()
    {
        if (!ShowsPanGlossMorphemes) return Row.Outcome switch
        {
            ParserOutcome.Same => "PanGloss builds this same analysis.",
            ParserOutcome.NoParse => "PanGloss built no analysis of this word.",
            ParserOutcome.Stopped => "PanGloss stopped at a limit before it finished this word.",
            ParserOutcome.Different => "PanGloss built something different, but kept no morphemes to show.",
            _ => "PanGloss has not parsed this word yet.",
        };
        if (!HasFieldWorksAnalysis) return HoldsNothing
            ? "FieldWorks holds no analysis of this word to compare with."
            : "FieldWorks holds no single analysis of this word to compare with.";
        var parted = Segments.Where(segment => segment.IsParted).ToArray();
        if (parted.Length == 0) return "Every morpheme matches.";
        var first = parted[0];
        var where = (first.FieldWorks.Count, first.PanGloss.Count) switch
        {
            (0, _) => $"PanGloss adds {Pieces(first.PanGloss)}, which FieldWorks' analysis does not have.",
            (_, 0) => $"PanGloss leaves out FieldWorks' {Pieces(first.FieldWorks)}.",
            _ => $"PanGloss reads FieldWorks' {Pieces(first.FieldWorks)} as {Pieces(first.PanGloss)}.",
        };
        var rest = parted.Length == 1
            ? Segments.Count > 1 ? " Every other morpheme matches." : string.Empty
            : $" They part in {parted.Length:N0} places; this is the first.";
        return where + rest;
    }

    private static string Pieces(IReadOnlyList<ListWordCardMorphViewModel> morphs) =>
        string.Join(" + ", morphs.Select(morph =>
            morph.Morph.Gloss.Length == 0 ? morph.Morph.Form : $"{morph.Morph.Form} '{morph.Morph.Gloss}'"));
}

/// <summary>One column of the aligned card: the FieldWorks and PanGloss morphemes it holds, and whether they part there.</summary>
public sealed record ListWordCardSegmentViewModel(
    IReadOnlyList<ListWordCardMorphViewModel> FieldWorks, IReadOnlyList<ListWordCardMorphViewModel> PanGloss, bool IsParted);

/// <summary>One morpheme in the aligned card, and whether it sits where the two analyses part.</summary>
public sealed record ListWordCardMorphViewModel(ParserReadingMorphViewModel Morph, bool IsDifferent);
