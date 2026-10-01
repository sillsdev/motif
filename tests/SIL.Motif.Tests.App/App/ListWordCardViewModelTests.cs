using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Xunit;
using WordRow = SIL.Motif.Contract.Responses.WordRow;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the card an opened Lists row shows: FieldWorks' analysis and PanGloss's lined up morpheme by morpheme, the
/// differing pieces marked, and one sentence saying where the two part.
/// </summary>
public sealed class ListWordCardViewModelTests
{
    private static ParserReadingMorph Morph(string form, string gloss, string? id = null) =>
        new(form, gloss, "v", null, false, null)
        {
            AllomorphId = "form-" + (id ?? form),
            GrammaticalInfoId = "msa-" + (id ?? form),
        };

    private static readonly ParserReadingMorph A = Morph("a-", "3SG");
    private static readonly ParserReadingMorph Li = Morph("li-", "PST");
    private static readonly ParserReadingMorph Kul = Morph("kul", "eat");
    private static readonly ParserReadingMorph Ku = Morph("ku-", "INF");
    private static readonly ParserReadingMorph L = Morph("l", "eat");
    private static readonly ParserReadingMorph Fv = Morph("-a", "FV");

    private static ListWordCardViewModel Card(WordRowOutcome outcome, IReadOnlyList<ParserReadingMorph> fieldWorks,
        IReadOnlyList<ParserReadingMorph> panGloss, int readings = 1)
    {
        var row = new WordRow("alikula", outcome, "Built something else", WordRowTone.Problem)
        {
            Opinion = ProjectStanding.Approved,
            FieldWorksMorphemes = fieldWorks,
            PanGlossMorphemes = panGloss,
            DifferingPositions = fieldWorks.Count == 0 ? [] : WordRowProjection.DifferingPositions(fieldWorks, panGloss),
            PanGlossReadingCount = readings,
        };
        return new ListWordCardViewModel(new WordRowViewModel(row));
    }

    private static string Shape(ListWordCardSegmentViewModel segment) =>
        $"{string.Join(" ", segment.FieldWorks.Select(morph => morph.Morph.Form))}|" +
        $"{string.Join(" ", segment.PanGloss.Select(morph => morph.Morph.Form))}";

    [Fact]
    public void ForAlikula_KulLinesUpWithKuAndL_AndOnlyThePanGlossPiecesThatDifferAreMarked()
    {
        var card = Card(WordRowOutcome.Different, [A, Li, Kul, Fv], [A, Li, Ku, L, Fv]);

        Assert.Equal(["a-|a-", "li-|li-", "kul|ku- l", "-a|-a"], card.Segments.Select(Shape));
        Assert.Equal(["ku-", "l"], card.Segments.SelectMany(segment => segment.PanGloss)
            .Where(morph => morph.IsDifferent).Select(morph => morph.Morph.Form));
        Assert.Equal(["kul"], card.Segments.SelectMany(segment => segment.FieldWorks)
            .Where(morph => morph.IsDifferent).Select(morph => morph.Morph.Form));
        Assert.True(card.ShowsPanGlossMorphemes);
    }

    [Fact]
    public void ForAlikula_OneSentenceSaysWhereTheyPart()
    {
        var card = Card(WordRowOutcome.Different, [A, Li, Kul, Fv], [A, Li, Ku, L, Fv]);

        Assert.Equal("PanGloss reads FieldWorks' kul ‘eat’ as ku- ‘INF’ + l ‘eat’. Every other morpheme matches.",
            card.WhereTheyPart);
    }

    [Fact]
    public void WhenTheyPartInSeveralPlaces_TheSentenceNamesTheFirstAndCountsTheRest()
    {
        var other = Morph("wa-", "3PL");
        var card = Card(WordRowOutcome.Different, [A, Li, Kul, Fv], [other, Li, Ku, L, Fv]);

        Assert.Equal("PanGloss reads FieldWorks' a- ‘3SG’ as wa- ‘3PL’. They part in 2 places; this is the first.",
            card.WhereTheyPart);
    }

    [Fact]
    public void AMorphemeOnlyOneSideHasIsNamedAsAddedOrLeftOut()
    {
        Assert.Equal("PanGloss adds li- ‘PST’, which FieldWorks' analysis does not have. Every other morpheme matches.",
            Card(WordRowOutcome.Different, [A, Kul, Fv], [A, Li, Kul, Fv]).WhereTheyPart);
        Assert.Equal("PanGloss leaves out FieldWorks' li- ‘PST’. Every other morpheme matches.",
            Card(WordRowOutcome.Different, [A, Li, Kul, Fv], [A, Kul, Fv]).WhereTheyPart);
    }

    [Fact]
    public void WithNoFieldWorksAnalysis_PanGlossStandsAloneAndNothingIsMarked()
    {
        var card = Card(WordRowOutcome.Different, [], [Ku, L]);

        Assert.False(card.HasFieldWorksAnalysis);
        Assert.Equal(["|ku- l"], card.Segments.Select(Shape));
        Assert.DoesNotContain(card.Segments.SelectMany(segment => segment.PanGloss), morph => morph.IsDifferent);
        Assert.Equal("FieldWorks holds no analysis of this word to compare with.", card.WhereTheyPart);
    }

    [Theory]
    [InlineData(WordRowOutcome.Same, "PanGloss builds this same analysis.")]
    [InlineData(WordRowOutcome.NoParse, "PanGloss built no analysis of this word.")]
    [InlineData(WordRowOutcome.Stopped, "PanGloss stopped at a limit before it finished this word.")]
    [InlineData(WordRowOutcome.NotParsed, "PanGloss has not parsed this word yet.")]
    public void WhenPanGlossBuiltNothingDifferent_FieldWorksStandsAloneAndTheSentenceSaysWhy(
        WordRowOutcome outcome, string sentence)
    {
        var card = Card(outcome, [A, Li, Kul, Fv], []);

        Assert.False(card.ShowsPanGlossMorphemes);
        Assert.Equal(["a-|", "li-|", "kul|", "-a|"], card.Segments.Select(Shape));
        Assert.DoesNotContain(card.Segments.SelectMany(segment => segment.FieldWorks), morph => morph.IsDifferent);
        Assert.Equal(sentence, card.WhereTheyPart);
    }

    [Fact]
    public void WhenPanGlossBuiltSeveralAnalyses_TheCardSaysItShowsTheClosest()
    {
        Assert.Equal("Closest of 3", Card(WordRowOutcome.Different, [A, Li, Kul, Fv], [A, Li, Ku, L, Fv], readings: 3)
            .PanGlossNote);
        Assert.Equal(string.Empty, Card(WordRowOutcome.Different, [A, Li, Kul, Fv], [A, Li, Ku, L, Fv]).PanGlossNote);
    }

    [Fact]
    public void CompareInTryAWordOpensTryAWordOnTheWord()
    {
        string? tried = null;
        var row = new WordRowViewModel(new WordRow("alikula", WordRowOutcome.Different, "Built something else",
            WordRowTone.Problem), new WordRowRoutes { TryWord = word => tried = word });
        var card = new ListWordCardViewModel(row);

        card.CompareCommand.Execute(null);

        Assert.Equal("Compare in Try a Word", card.CompareLabel);
        Assert.Equal("alikula", tried);
    }
}
