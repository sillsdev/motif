using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Parsimony;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Pins that a Parsimony group says nothing is new only when every measure in it computed.</summary>
public sealed class ProposalParsimonyGroupNoteTests
{
    private const string Project = "/tmp/parsimony-group-note.fwdata";

    [Fact]
    public async Task AGroupWhoseMeasuresAllComputedSaysNoFindingsAreNewOrChanged()
    {
        var note = await NoteAsync(Group(ParsimonyMeasureStatus.Computed));

        Assert.Equal("No new or changed Findings in this group.", note);
    }

    [Theory]
    [InlineData(ParsimonyMeasureStatus.NotAvailable)]
    [InlineData(ParsimonyMeasureStatus.Failed)]
    [InlineData(ParsimonyMeasureStatus.Inconclusive)]
    [InlineData(ParsimonyMeasureStatus.NotRun)]
    public async Task AGroupWithAMeasureThatDidNotComputeLimitsTheAllClearToTheMeasuresThatGaveAResult(
        ParsimonyMeasureStatus status)
    {
        var note = await NoteAsync(Group(status));

        Assert.Equal("The measures that gave a result found no new or changed Findings in this group.", note);
        Assert.DoesNotContain("No new or changed", note);
    }

    [Fact]
    public async Task AMeasureThatDidNotComputeAndHasNoCountsStillLimitsTheAllClear()
    {
        var group = Group(ParsimonyMeasureStatus.Computed) with
        {
            MeasureRuns =
            [
                .. Group(ParsimonyMeasureStatus.Computed).MeasureRuns,
                new ParsimonyMeasureRun(ProposalParsimonyStatusFixtures.Unavailable, ParsimonyMeasureStatus.NotAvailable,
                    null, null, "statement", null, null, "Missing table."),
            ],
        };

        var note = await NoteAsync(group);

        Assert.StartsWith("The measures that gave a result", note);
    }

    // Both measures report no new or changed Finding; the second one's run carries the given status.
    private static ProposalParsimonyGroup Group(ParsimonyMeasureStatus second) => ProposalParsimonyStatusFixtures.Measured() with
    {
        MeasureRuns =
        [
            new ParsimonyMeasureRun(ProposalParsimonyStatusFixtures.Computed, ParsimonyMeasureStatus.Computed, 10, 2, "x", null, null, null),
            new ParsimonyMeasureRun(ProposalParsimonyStatusFixtures.Second, second, 8, 0, "x", null, null, null),
        ],
        Changes =
        [
            new ProposalParsimonyMeasureChange(ProposalParsimonyStatusFixtures.Computed, 0, 0, 8, 0, "r1", "r2"),
            new ProposalParsimonyMeasureChange(ProposalParsimonyStatusFixtures.Second, 0, 0, 8, 0, "r3", "r4"),
        ],
    };

    private static async Task<string> NoteAsync(ProposalParsimonyGroup group)
    {
        var fake = new FakeCommandClient();
        ProposalParsimonyPresentationTests.ConfigureStoredReports(fake);
        var read = await new ProposalParsimonyFindings(fake.ReadParsimonyReportAsync, fake.ReadParsimonyViewAsync)
            .ReadAsync(Project, group, CancellationToken.None);
        return read.Note;
    }
}
