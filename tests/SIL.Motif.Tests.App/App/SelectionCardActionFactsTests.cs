using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class SelectionCardActionFactsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapturedCardChoicesRespectTheReadersExactTargetCapabilities(bool available)
    {
        var first = Guid.NewGuid();
        var reading = new ParseAnalysis([]);
        var result = new AssessmentWordResult("same", "analysed", false, "Complete", 1, null)
        {
            Morphology = new ParseWordEvidence("fixture", 0, "same", 1, false, false, false, [reading], []),
            ProjectStanding = ProjectStanding.NotPresent, StoredAnalysesAvailable = true,
        };
        var facts = new SelectionWordActionFacts(false, false, false, available, available, [])
        {
            CandidateWordformIds = available ? [first] : [first, Guid.NewGuid()],
        };
        using var token = new ResultsTokenViewModel("Selection", 0,
            new TextToken("same", "same", null, null) { WordformId = available ? first : null }, result,
            actionFacts: facts);
        Assert.NotEmpty(token.Marking.FixChoices);
        Assert.All(token.BoundFixChoices, choice => Assert.Equal(available, choice.IsAvailable));
        Assert.Equal(available, token.IncorrectSpellingAction.IsAvailable);
        Assert.All(token.Disposition.Readings.SelectMany(row => row.Tiles),
            tile =>
            {
                Assert.Equal(available, Assert.IsType<WordMarkingChoice>(tile.Action).IsAvailable);
                Assert.Equal(available, tile.IsAvailable);
            });
    }

    [Fact]
    public void AcceptNewSetTargetRetainsTheParserReadingsThatSuppliedItsChoice()
    {
        var first = new ParseAnalysis([]);
        var second = new ParseAnalysis([]);
        var source = new AssessmentWordResult("same", "analysed", false, "Complete", 1, null)
        {
            Morphology = new ParseWordEvidence("fixture", 0, "same", 1, false, false, false, [first, second], []),
        };
        var facts = new SelectionWordActionFacts(false, false, false, true, true, [])
        {
            Classification = new SelectionAnalysisClassification(SelectionAnalysisClass.Different,
                [new(0, [], []), new(1, [], [])]),
        };
        using var token = new ResultsTokenViewModel("Selection", 0, new TextToken("same", "same", null, null), source,
            actionFacts: facts);
        var captured = token.CaptureActionTarget(null);
        token.BindActionFacts(facts with
        {
            Classification = new SelectionAnalysisClassification(SelectionAnalysisClass.NotAssessed, []),
        });
        Assert.Equal([0, 1], captured.ParserOnlyReadings.Select(reading => reading.Index));
        Assert.Same(first, captured.ParserOnlyReadings[0].Analysis);
        Assert.Same(second, captured.ParserOnlyReadings[1].Analysis);
        Assert.Empty(token.CaptureActionTarget(null).ParserOnlyReadings);
    }
}
