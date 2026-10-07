using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ComparisonRowTests
{
    private static readonly ParserReadingMorph HomographA =
        new("x", "thing", "n", null, false, null)
        {
            AllomorphId = "aaaaaaaa-0000-0000-0000-000000000001",
            GrammaticalInfoId = "cccccccc-0000-0000-0000-000000000001",
        };

    private static readonly ParserReadingMorph HomographB = HomographA with
    {
        AllomorphId = "bbbbbbbb-0000-0000-0000-000000000001",
    };

    private static SIL.Motif.Contract.Responses.WordRow Project(
        IReadOnlyList<ParserReadingMorph> stored, IReadOnlyList<ParserReadingMorph> parsed)
    {
        var expected = new ParserReading(stored)
        {
            StoredAnalysisId = "stored-analysis", StoredAnalysisOpinion = ReadingGrade.Approved,
            Identity = new ApprovedMorphology(stored.Select(morph =>
                new ApprovedMorph(morph.AllomorphId!, morph.GrammaticalInfoId!, null, [])).ToArray()),
        };
        return WordRowProjection.Of(new AssessmentWordResult("xx", "analysed", false, "Finished", 1, null)
        {
            ProjectStanding = ProjectStanding.Approved,
            ExpectedAnalysis = expected, StoredAnalyses = [expected], Readings = [new ParserReading(parsed)],
            Morphology = new ParseWordEvidence("v1", 0, "xx", 1, false, false, false,
                [new ParseAnalysis(parsed.Select(morph =>
                    new ParseMorph(morph.AllomorphId ?? "", morph.GrammaticalInfoId, null, null)).ToArray())], []),
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedHomographsExplainTheSequenceWithoutInventingAnIdentityDifference(bool alternateSpelling)
    {
        var second = alternateSpelling ? HomographB with
        {
            AllomorphId = Guid.Parse(HomographB.AllomorphId!).ToString("B").ToUpperInvariant(),
            GrammaticalInfoId = Guid.Parse(HomographB.GrammaticalInfoId!).ToString("N").ToUpperInvariant(),
        } : HomographB;
        var row = Project([HomographA, HomographB], [HomographB, second]);
        Assert.Equal([2], row.DifferingPositions);
        var model = new WordRowViewModel(row);
        Assert.DoesNotContain("different morpheme identity", model.IdentityDetail);
        Assert.Contains("morpheme sequence differs", model.IdentityDetail);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingIdentityIsExplainedAsMissingEvidence(string? missingId)
    {
        var model = new WordRowViewModel(Project([HomographA], [HomographA with { AllomorphId = missingId }]));
        Assert.Contains("identity not recorded", model.IdentityDetail);
        Assert.DoesNotContain("different morpheme identity", model.IdentityDetail);
    }

    [Fact]
    public void CanonicalGuidSpellingDoesNotCreateAnIdentityDifference()
    {
        var same = HomographA with { AllomorphId = Guid.Parse(HomographA.AllomorphId!).ToString("B").ToUpperInvariant() };
        var row = Project([HomographA], [same]);
        Assert.Equal(WordRowOutcome.Same, row.Outcome);
        Assert.False(new WordRowViewModel(row).HasIdentityDetail);
    }

    [Fact]
    public void IdenticalFormAndGlossStillExplainTheIdentityDifference()
    {
        var sameText = new ParserReadingMorph("tabu", "book", "n", null, false, null)
        { AllomorphId = "stored", GrammaticalInfoId = "noun", Entry = "stored" };
        var model = new WordRowViewModel(Project([sameText],
            [sameText with { AllomorphId = "parser", Entry = "parser" }]));
        Assert.Contains("Same form and gloss", model.IdentityDetail);
        Assert.Contains("different morpheme identity", model.IdentityDetail);
        Assert.Equal("Different entry: parser instead of stored", model.PanGlossMorphemes[0].EntryDifferenceTip);

    }

    [Fact]
    public void ReadTextKeepsTheWordsSharedComparison()
    {
        var comparison = new WordComparison(ProjectStanding.Approved, WordRowOutcome.Different,
            "approved-different", "Built something else", WordRowTone.Problem);
        var result = new AssessmentWordResult("word", "analysed", false, "Finished", 1, null)
        { Comparison = comparison, ProjectStanding = ProjectStanding.Approved };
        var token = new SIL.Motif.Commands.Queries.TextToken("word", "word", null, "unanalysed");
        var shown = new ResultsTokenViewModel("Text", 1, token, result);
        Assert.Same(comparison, shown.Comparison);
    }


}
