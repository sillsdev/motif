using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ProposalEvidenceColumnsTests
{
    [UiThreadFact]
    public async Task MechanicalFactsDoNotRenameOrAddOverlappingAdvisoryBuckets()
    {
        var fixture = new ProposalDifferenceFixture(3) { Warnings = [] };
        var summary = fixture.Summary with { UnboundOccurrences = 0, Limitations = [], WordsListed = 3 };
        var words = new[]
        {
            Word("fills", 0, 1),
            Word("changes", 1, 2) with
            {
                Facts = Facts(1, 2) with { GainedIdentities = [new HumanAgreementProducedIdentity([])] },
            },
            Word("same", 1, 1),
        };
        var difference = new ProposalDifferenceViewModel((_, _) => Task.FromResult(
            CommandOutcome<ProposalDifferenceResponse>.Success(new(summary, words, words.Length, null, true))), _ => { });
        var columns = new ProposalEvidenceColumnsViewModel(difference);
        await difference.LoadAsync("project", "proposal/one");
        columns.SetCurrent(true);

        Assert.Equal("fills", Assert.Single(columns.WillFillIn).Word);
        Assert.Equal("changes", Assert.Single(columns.WillChange).Word);
        Assert.True(columns.Complete);
        Assert.False(columns.HasApprovedPlaces);
        Assert.Contains("Genesis 1", columns.Scope);
        Assert.DoesNotContain(summary.ImprovedWords.ToString(), columns.FillInCount);
    }

    [UiThreadFact]
    public async Task BoundedIncompleteAndExpiredEvidenceNeverClaimsZeroOrApprovedPlaces()
    {
        var fixture = new ProposalDifferenceFixture(1) { Cut = 900, Warnings = [] };
        var difference = new ProposalDifferenceViewModel(fixture.Read, _ => { });
        var columns = new ProposalEvidenceColumnsViewModel(difference);
        await difference.LoadAsync("project", "proposal/one");
        columns.SetCurrent(true);
        Assert.False(columns.Complete);
        Assert.False(columns.ShowsZeroFillIn);
        Assert.False(columns.HasApprovedPlaces);
        Assert.Empty(columns.WillFillIn);
        fixture.WordDetailKept = false;
        await difference.LoadAsync("project", "proposal/one");
        Assert.False(columns.CanShowFacts);
        Assert.False(columns.HasFillIn);
        Assert.False(columns.HasChange);
        fixture.WordDetailKept = true;
        fixture.WordRowsDamaged = true;
        await difference.LoadAsync("project", "proposal/one");
        Assert.False(columns.CanShowFacts);
        Assert.False(columns.ShowsZeroChange);
    }

    [UiThreadFact]
    public async Task CompleteCompatibleEmptyTextEstablishesZeroAndInvalidationRemovesIt()
    {
        var fixture = new ProposalDifferenceFixture(0) { Warnings = [] };
        var summary = fixture.Summary with { UnboundOccurrences = 0, Limitations = [], WordsListed = 0 };
        var difference = new ProposalDifferenceViewModel((_, _) => Task.FromResult(
            CommandOutcome<ProposalDifferenceResponse>.Success(new(summary, [], 0, null, true))), _ => { });
        var columns = new ProposalEvidenceColumnsViewModel(difference);
        await difference.LoadAsync("project", "proposal/one");
        columns.SetCurrent(true);
        Assert.True(columns.ShowsZeroFillIn);
        Assert.True(columns.ShowsZeroChange);
        columns.SetCurrent(false);
        Assert.False(columns.HasFillIn);
        Assert.False(columns.HasChange);
        Assert.Empty(columns.WillFillIn);
    }

    [UiThreadTheory]
    [InlineData(HumanAgreementSpellingAcceptance.Unavailable)]
    [InlineData(HumanAgreementSpellingAcceptance.Incomplete)]
    public async Task ACompleteParserSearchCannotEstablishZeroWhenSpellingEvidenceIsMissing(
        HumanAgreementSpellingAcceptance spelling)
    {
        var fixture = new ProposalDifferenceFixture(1) { Warnings = [] };
        var summary = fixture.Summary with { UnboundOccurrences = 0, Limitations = [], WordsListed = 1 };
        var word = Word("same", 1, 1) with
        {
            Facts = Facts(1, 1) with { Before = Side(1) with { SpellingAcceptance = spelling } },
        };
        var difference = new ProposalDifferenceViewModel((_, _) => Task.FromResult(
            CommandOutcome<ProposalDifferenceResponse>.Success(new(summary, [word], 1, null, true))), _ => { });
        var columns = new ProposalEvidenceColumnsViewModel(difference);
        await difference.LoadAsync("project", "proposal/one");
        columns.SetCurrent(true);
        Assert.False(columns.Complete);
        Assert.False(columns.ShowsZeroChange);
        Assert.False(columns.ShowsZeroFillIn);
        Assert.NotEmpty(columns.Availability);
        Assert.Contains("approved places are unavailable", columns.Availability);
        Assert.False(columns.ShowsPlacesNotice);
    }

    [UiThreadFact]
    public async Task DifferentContentTrialOrParserCannotPublishCurrentEffects()
    {
        var fixture = new ProposalDifferenceFixture(0) { Warnings = [] };
        var baseline = WorkspaceContextTests.Assessment().Baseline.Token;
        var summary = fixture.Summary with { ProposalIntentDigest = "intent/one",
            BaselineToken = System.Text.Json.JsonSerializer.Serialize(baseline) };
        var difference = new ProposalDifferenceViewModel((_, _) => Task.FromResult(
            CommandOutcome<ProposalDifferenceResponse>.Success(new(summary, [], 0, null, true))), _ => { });
        var columns = new ProposalEvidenceColumnsViewModel(difference);
        await difference.LoadAsync("project", "proposal/one");
        var detail = new ProposalDetailProjection("proposal/one", "proposed", "One", null, "intent/one", []);
        columns.SetCurrent(true, detail, summary.TrialJobId, baseline);
        Assert.True(columns.CanShowFacts);
        columns.SetCurrent(true, detail with { CurrentIntentDigest = "intent/other" }, summary.TrialJobId, baseline);
        Assert.False(columns.CanShowFacts);
        columns.SetCurrent(true, detail, "job/other", baseline);
        Assert.False(columns.CanShowFacts);
        columns.SetCurrent(true, detail, summary.TrialJobId, baseline);
        summary = summary with { After = summary.After with { ParserSha256 = "different" } };
        await difference.LoadAsync("project", "proposal/one");
        Assert.False(columns.CanShowFacts);
        summary = summary with { After = summary.Before, BaselineToken = "{}" };
        await difference.LoadAsync("project", "proposal/one");
        Assert.False(columns.CanShowFacts);
    }

    [UiThreadTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EvidenceForAnotherProposalIsRefusedBeforePublication(bool produced)
    {
        var fixture = new ProposalDifferenceFixture(0);
        var foreign = new ProposalDifferenceResponse(fixture.Summary, [], 0, null, true);
        var difference = new ProposalDifferenceViewModel((_, _) => Task.FromResult(produced
            ? CommandOutcome<ProposalDifferenceResponse>.Refused(new Refusal(RefusalCodes.DifferenceNone,
                FailureReason.NotFound, "None"))
            : CommandOutcome<ProposalDifferenceResponse>.Success(foreign)), _ => { },
            (_, _, _) => Task.FromResult(CommandOutcome<ProposalDifferenceResponse>.Success(foreign)),
            () => [new(Guid.NewGuid(), "A Text")]);
        await difference.LoadAsync("project", "proposal/other");
        if (produced) await difference.Maker.RunCommand.ExecuteAsync(null);
        Assert.Equal(ProposalDifferenceState.Refused, difference.State);
        Assert.Null(difference.Summary);
        Assert.NotNull(difference.ShownRefusal);
        await difference.StopAsync();
    }

    private static ProposalDifferenceWord Word(string word, int before, int after) =>
        new(word, 1, "no-parse", "approved-unique", [],
            [ProposalDifferenceBucket.Regression, ProposalDifferenceBucket.AnalysisChanged,
             ProposalDifferenceBucket.ImprovedUnique], Facts(before, after));

    private static ProposalDifferenceCaseFacts Facts(int before, int after) =>
        new(new ProposalDifferencePolicy(), Side(before), Side(after), true, [], [], [], [], [], []);
    private static HumanAgreementFacts Side(int count) =>
        new(HumanAgreementEvidenceAvailability.Available, HumanAgreementSearchCompletion.Complete, [],
            count == 0 ? HumanAgreementApprovalState.NoParse : HumanAgreementApprovalState.ApprovedUnique,
            count, 0, 0, [], [], [], [], [], [], [], HumanAgreementSpellingAcceptance.NotApplicable, []);
}
