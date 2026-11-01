using SIL.Motif.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed partial class ProposalDifferenceCommandsTests
{
    private const string ParserOne = "sha256:1111111111111111111111111111111111111111111111111111111111111111";
    private const string ParserTwo = "sha256:2222222222222222222222222222222222222222222222222222222222222222";

    [Fact]
    public void TheSummaryCarriesEveryPinOfBothAssessmentsByValue()
    {
        using var fixture = new Fixture(_root, beforeParserSha: ParserOne, afterParserSha: ParserOne);

        var summary = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary;

        Assert.Equal("pangloss", summary.Assessor);
        Assert.StartsWith("sha256:", summary.ProposalIntentDigest, StringComparison.Ordinal);
        Assert.Equal(new ProposalDifferenceSidePin("0.1.0", ParserOne, "sha256:grammar", "Default",
            DefaultSelectionSha(), "whitespace", "1"), summary.Before);
        Assert.Equal(new ProposalDifferenceSidePin("0.2.0", ParserOne, "sha256:grammar", "Default",
            DefaultSelectionSha(), "whitespace", "1"), summary.After);
        Assert.Empty(summary.Warnings);
    }

    [Fact]
    public void ASummaryReadBackAfterBothAssessmentsAreGoneStillHoldsTheirPins()
    {
        using var fixture = new Fixture(_root, beforeParserSha: ParserOne, afterParserSha: ParserOne);
        var produced = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary;

        fixture.Sql("PRAGMA foreign_keys = OFF; DELETE FROM Assessments;");
        var read = ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(fixture.ProjectPath,
            DifferenceId: produced.DifferenceId));

        Assert.True(read.Succeeded, read.Refusal?.Message);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(produced, SIL.Motif.Contract.MotifJson.CreateOptions()),
            System.Text.Json.JsonSerializer.Serialize(read.Value!.Summary, SIL.Motif.Contract.MotifJson.CreateOptions()));
    }

    [Fact]
    public void ADifferingParserIsFlaggedInTheSummaryWithBothVersionsAndAMissingOneIsNot()
    {
        using var differing = new Fixture(_root, beforeParserSha: ParserOne, afterParserSha: ParserTwo);
        using var unrecorded = new Fixture(_root, beforeParserSha: ParserOne);

        var flagged = ProposalDifferenceCommands.Produce(differing.ProduceRequest()).Value!.Summary;
        var quiet = ProposalDifferenceCommands.Produce(unrecorded.ProduceRequest()).Value!.Summary;

        var warning = Assert.Single(flagged.Warnings);
        Assert.Contains("0.1.0", warning, StringComparison.Ordinal);
        Assert.Contains("0.2.0", warning, StringComparison.Ordinal);
        Assert.Contains("not the grammar", warning, StringComparison.Ordinal);
        Assert.Empty(quiet.Warnings);
        Assert.Null(quiet.After.ParserSha256);
    }

    [Fact]
    public void WordsTheTrialMeasuredThatTheTextLacksAreCountedAndNotCompared()
    {
        using var fixture = new Fixture(_root, outsideText: ["zzz", "yyy"]);

        var produced = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!;

        Assert.Equal(2, produced.Summary.MeasuredWordsOutsideText);
        Assert.Equal(2, produced.Summary.Words);
        Assert.DoesNotContain(produced.Words, word => word.Word is "zzz" or "yyy");
    }

    [Fact]
    public void LimitationCountsNameHowManyWordsASearchLimitTouched()
    {
        using var fixture = new Fixture(_root, cappedAfterMutu: true);

        var summary = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary;

        var capped = Assert.Single(summary.Limitations);
        Assert.Equal(new ProposalDifferenceLimitationCount(HumanAgreementLimitation.Capped, 1, 1, Listed: 1), capped);
    }

    [Fact]
    public void TheSavedSummaryOfAThousandsOfWordsTextStaysUnderEightKilobytes()
    {
        using var fixture = new Fixture(_root, extraWords: 3000, cappedAfterMutu: true, outsideText: ["zzz"],
            beforeParserSha: ParserOne, afterParserSha: ParserTwo);

        var produced = ProposalDifferenceCommands.Produce(fixture.ProduceRequest());

        Assert.True(produced.Succeeded, produced.Refusal?.Message);
        Assert.Equal(3002, produced.Value!.Summary.Words);
        var bytes = fixture.SummaryBytes(produced.Value.Summary.DifferenceId);
        Assert.InRange(bytes, 1, 8 * 1024);
    }

    [Fact]
    public void ProducingAgainForTheSameProposalAndTextLeavesOneDifference()
    {
        using var fixture = new Fixture(_root);
        var first = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary;
        var second = new ProposalDifferenceRepository(fixture.Database);

        second.Publish(new NewProposalDifference(first with { DifferenceId = "difference/newer" }, []));

        Assert.Equal(0, fixture.Count("ProposalDifferences", first.DifferenceId));
        Assert.Equal(0, fixture.Count("ProposalDifferenceCases", first.DifferenceId));
        Assert.Equal(1, fixture.Count("ProposalDifferences", "difference/newer"));
        Assert.Equal("difference/newer", second.FindFor(first.ProposalId, first.TextId, first.Reference)!.Summary.DifferenceId);
    }

    private static string DefaultSelectionSha() =>
        SIL.Motif.Host.Corpus.Selection.Create("Default", ["sita", "mutu"]).Sha256;

    private sealed partial class Fixture
    {
        public long SummaryBytes(string differenceId)
        {
            using var connection = Database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT LENGTH(CAST(SummaryJson AS BLOB)) + LENGTH(CAST(SummaryDigest AS BLOB)) + " +
                "LENGTH(CAST(BaselineToken AS BLOB)) FROM ProposalDifferences WHERE DifferenceId = $id;";
            command.Parameters.AddWithValue("$id", differenceId);
            return Convert.ToInt64(command.ExecuteScalar());
        }
    }
}
