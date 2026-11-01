using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Commands;

// The window never names a reference: the store keeps the one saved for the Proposal and Text at the write.
public sealed partial class ProposalDifferenceCommandsTests
{
    private static CommandOutcome<ProposalDifferenceResponse> ProduceAsTheWindowDoes(
        ProposalDifferenceProduceRequest request) => ProposalDifferenceCommands.Produce(request, null, default);

    // The window may still show an older human copy; it sends nothing, so the store keeps what is saved.
    [Fact]
    public void TheWindowOverloadKeepsASavedSourceValidatedDifferenceSourceValidated()
    {
        LoadAgentEvaluationKind();
        using var fixture = new Fixture(_root);
        fixture.StageAppliedAgentApproval(SitaAnalysis);
        var saved = ProduceSourceValidated(fixture.ProduceRequest()).Value!.Summary;

        fixture.StageAppliedAgentApproval(MutuWordform);
        var again = ProduceAsTheWindowDoes(fixture.ProduceRequest());

        Assert.True(again.Succeeded, again.Refusal?.Message);
        Assert.Equal(ProposalDifferenceReferences.SourceValidated, again.Value!.Summary.Reference);
        Assert.NotEqual(saved.DecisionsDigest, again.Value.Summary.DecisionsDigest);
        Assert.Equal(ProposalDifferenceReferences.SourceValidated, ProposalDifferenceCommands.Show(
            new ProposalDifferenceShowRequest(fixture.ProjectPath, fixture.ProposalId)).Value!.Summary.Reference);
    }

    [Fact]
    public void TheWindowOverloadAnswersFromASavedSourceValidatedDifferenceWhenNothingChanged()
    {
        LoadAgentEvaluationKind();
        using var fixture = new Fixture(_root);
        fixture.StageAppliedAgentApproval(SitaAnalysis);
        var saved = ProduceSourceValidated(fixture.ProduceRequest()).Value!.Summary;

        var again = ProduceAsTheWindowDoes(fixture.ProduceRequest());

        Assert.Equal(saved.DifferenceId, again.Value!.Summary.DifferenceId);
        Assert.Equal(ProposalDifferenceReferences.SourceValidated, again.Value.Summary.Reference);
    }

    [Fact]
    public void TheWindowOverloadKeepsASavedHumanDifferenceHuman()
    {
        using var fixture = new Fixture(_root);
        var saved = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary;

        var again = ProduceAsTheWindowDoes(fixture.ProduceRequest());

        Assert.True(again.Succeeded, again.Refusal?.Message);
        Assert.Equal(ProposalDifferenceReferences.Human, again.Value!.Summary.Reference);
        Assert.Equal(saved.DifferenceId, again.Value.Summary.DifferenceId);
    }

    [Fact]
    public void TheWindowOverloadWithNothingSavedIsAgainstAPersonsDecisions()
    {
        LoadAgentEvaluationKind();
        using var fixture = new Fixture(_root);
        fixture.StageAppliedAgentApproval(SitaAnalysis);

        var made = ProduceAsTheWindowDoes(fixture.ProduceRequest());

        Assert.True(made.Succeeded, made.Refusal?.Message);
        Assert.Equal(ProposalDifferenceReferences.Human, made.Value!.Summary.Reference);
    }

    [Fact]
    public void AnExplicitReferenceStillReplacesASavedOneAndAnUnknownNameIsStillRefused()
    {
        LoadAgentEvaluationKind();
        using var fixture = new Fixture(_root);
        ProduceSourceValidated(fixture.ProduceRequest());

        var human = ProposalDifferenceCommands.Produce(fixture.ProduceRequest());
        var refused = ProposalDifferenceCommands.Produce(fixture.ProduceRequest(), "both");

        Assert.Equal(ProposalDifferenceReferences.Human, human.Value!.Summary.Reference);
        Assert.Equal("difference.invalid-reference", refused.Refusal!.Code);
    }
}
