using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

// A Proposal keeps one Difference per Text and reference, so they sit side by side and each says which it is.
public sealed partial class ProposalDifferenceCommandsTests
{
    private static CommandOutcome<ProposalDifferenceResponse> ProduceAsTheWindowDoes(
        ProposalDifferenceProduceRequest request) => ProposalDifferenceCommands.Produce(request, null, default);

    [Fact]
    public void ADifferenceAgainstEachReferenceIsKeptForTheSameText()
    {
        LoadAgentEvaluationKind();
        using var fixture = new Fixture(_root);
        fixture.StageAppliedAgentApproval(SitaAnalysis);
        var human = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary;

        var sourceValidated = ProduceSourceValidated(fixture.ProduceRequest()).Value!.Summary;

        Assert.NotEqual(human.DifferenceId, sourceValidated.DifferenceId);
        Assert.Equal(2, fixture.DifferenceRows());
        Assert.Equal(2, fixture.Count("ProposalDifferenceCases", human.DifferenceId));
        Assert.Equal(1, fixture.Count("ProposalDifferences", human.DifferenceId));
    }

    [Fact]
    public void ProducingAgainstOneReferenceReplacesOnlyThatReferencesDifference()
    {
        LoadAgentEvaluationKind();
        using var fixture = new Fixture(_root);
        var human = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary;
        var first = ProduceSourceValidated(fixture.ProduceRequest()).Value!.Summary;
        fixture.StageAppliedAgentApproval(SitaAnalysis);

        var second = ProduceSourceValidated(fixture.ProduceRequest()).Value!.Summary;

        Assert.NotEqual(first.DifferenceId, second.DifferenceId);
        Assert.Equal(0, fixture.Count("ProposalDifferences", first.DifferenceId));
        Assert.Equal(1, fixture.Count("ProposalDifferences", human.DifferenceId));
        Assert.Equal(2, fixture.DifferenceRows());
    }

    [Fact]
    public void ShowReadsTheDifferenceOfTheReferenceAskedForAndListsEverySavedOneWithItsTextAndReference()
    {
        LoadAgentEvaluationKind();
        using var fixture = new Fixture(_root);
        var human = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary;
        var sourceValidated = ProduceSourceValidated(fixture.ProduceRequest()).Value!.Summary;

        var newest = ProposalDifferenceCommands.Show(fixture.ShowRequest()).Value!;
        var asked = ProposalDifferenceCommands.Show(fixture.ShowRequest() with
        {
            Reference = ProposalDifferenceReferences.Human, TextId = fixture.TextId.ToString("D"),
        }).Value!;

        Assert.Equal(sourceValidated.DifferenceId, newest.Summary.DifferenceId);
        Assert.Equal(human.DifferenceId, asked.Summary.DifferenceId);
        Assert.Equal(2, asked.SavedTotal);
        Assert.Equal([ProposalDifferenceReferences.SourceValidated, ProposalDifferenceReferences.Human],
            asked.Saved!.Select(saved => saved.Reference));
        Assert.All(asked.Saved!, saved =>
        {
            Assert.Equal(fixture.TextId.ToString("D"), saved.TextId);
            Assert.False(string.IsNullOrEmpty(saved.TextTitle));
            Assert.True(saved.WordDetailKept);
        });
    }

    [Fact]
    public void ShowRefusesATextOrReferenceTheProposalHasNoDifferenceFor()
    {
        using var fixture = new Fixture(_root);
        ProposalDifferenceCommands.Produce(fixture.ProduceRequest());

        var otherReference = ProposalDifferenceCommands.Show(fixture.ShowRequest() with
        { Reference = ProposalDifferenceReferences.SourceValidated });
        var otherText = ProposalDifferenceCommands.Show(fixture.ShowRequest() with { TextId = Guid.NewGuid().ToString("D") });
        var badReference = ProposalDifferenceCommands.Show(fixture.ShowRequest() with { Reference = "both" });
        var badText = ProposalDifferenceCommands.Show(fixture.ShowRequest() with { TextId = "not-a-guid" });
        var withoutProposal = ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(
            fixture.ProjectPath, DifferenceId: "difference/x", Reference: ProposalDifferenceReferences.Human));

        Assert.Equal("difference.none", otherReference.Refusal?.Code);
        Assert.Contains("other saved Differences", otherReference.Refusal!.Message, StringComparison.Ordinal);
        Assert.Equal("difference.none", otherText.Refusal?.Code);
        Assert.Equal("difference.invalid-reference", badReference.Refusal?.Code);
        Assert.Equal("difference.invalid-text", badText.Refusal?.Code);
        Assert.Equal("difference.text-needs-proposal", withoutProposal.Refusal?.Code);
    }

    [Fact]
    public void ADifferenceForAnotherTextSitsBesideTheFirstAndReplacesNothing()
    {
        using var fixture = new Fixture(_root);
        var first = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary;
        var otherText = Guid.NewGuid().ToString("D");

        new ProposalDifferenceRepository(fixture.Database).Publish(new NewProposalDifference(
            first with { DifferenceId = "difference/other", TextId = otherText, TextTitle = "Another Text" }, []));

        var listed = ProposalDifferenceCommands.Show(fixture.ShowRequest()).Value!;
        Assert.Equal(2, listed.SavedTotal);
        Assert.Contains(listed.Saved!, saved => saved.TextId == otherText && saved.TextTitle == "Another Text");
        Assert.Contains(listed.Saved!, saved => saved.DifferenceId == first.DifferenceId);
        Assert.Equal(first.DifferenceId, ProposalDifferenceCommands.Show(fixture.ShowRequest() with
        { TextId = fixture.TextId.ToString("D") }).Value!.Summary.DifferenceId);
    }

    [Fact]
    public void ASiblingSummaryThatNoLongerMatchesItsDigestIsCountedAndTheShowStillSucceeds()
    {
        using var fixture = new Fixture(_root);
        var first = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary;
        var repository = new ProposalDifferenceRepository(fixture.Database);
        foreach (var id in new[] { "difference/bad", "difference/broken" })
            repository.Publish(new NewProposalDifference(first with
            {
                DifferenceId = id, TextId = Guid.NewGuid().ToString("D"), CreatedUtc = "2020-01-01T00:00:00Z",
            }, []));
        using (var connection = fixture.Database.OpenConnection())
        {
            using var damage = connection.CreateCommand();
            damage.CommandText = "UPDATE ProposalDifferences SET SummaryJson = SummaryJson || ' ' WHERE DifferenceId = 'difference/bad';" +
                "UPDATE ProposalDifferences SET SummaryJson = '{', SummaryDigest = $digest WHERE DifferenceId = 'difference/broken';";
            damage.Parameters.AddWithValue("$digest",
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("{"u8)).ToLowerInvariant());
            damage.ExecuteNonQuery();
        }

        var shown = ProposalDifferenceCommands.Show(fixture.ShowRequest());

        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        Assert.Equal(first.DifferenceId, shown.Value!.Summary.DifferenceId);
        Assert.Equal(3, shown.Value.SavedTotal);
        Assert.Equal(2, shown.Value.SavedUnreadable);
        Assert.Equal(first.DifferenceId, Assert.Single(shown.Value.Saved!).DifferenceId);
    }

    [Fact]
    public void TheListOfSavedDifferencesIsBoundedAndCountsTheRest()
    {
        using var fixture = new Fixture(_root);
        var first = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary;
        var repository = new ProposalDifferenceRepository(fixture.Database);
        for (var index = 0; index < ProposalDifferenceRepository.MostListed + 5; index++)
            repository.Publish(new NewProposalDifference(first with
            {
                DifferenceId = "difference/many-" + index, TextId = Guid.NewGuid().ToString("D"),
            }, []));

        var listed = ProposalDifferenceCommands.Show(fixture.ShowRequest()).Value!;

        Assert.Equal(ProposalDifferenceRepository.MostListed, listed.Saved!.Count);
        Assert.Equal(ProposalDifferenceRepository.MostListed + 6, listed.SavedTotal);
    }

    // The window makes a Difference against a person's decisions and never touches one a program's decisions made.
    [Fact]
    public void TheWindowOverloadMakesAHumanDifferenceBesideASavedSourceValidatedOne()
    {
        LoadAgentEvaluationKind();
        using var fixture = new Fixture(_root);
        fixture.StageAppliedAgentApproval(SitaAnalysis);
        var saved = ProduceSourceValidated(fixture.ProduceRequest()).Value!.Summary;

        var made = ProduceAsTheWindowDoes(fixture.ProduceRequest());

        Assert.True(made.Succeeded, made.Refusal?.Message);
        Assert.Equal(ProposalDifferenceReferences.Human, made.Value!.Summary.Reference);
        Assert.Equal(1, fixture.Count("ProposalDifferences", saved.DifferenceId));
        Assert.Equal(2, made.Value.SavedTotal);
    }

    [Fact]
    public void TheWindowOverloadAnswersFromASavedHumanDifferenceWhenNothingChanged()
    {
        using var fixture = new Fixture(_root);
        var saved = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary;

        var again = ProduceAsTheWindowDoes(fixture.ProduceRequest());

        Assert.True(again.Succeeded, again.Refusal?.Message);
        Assert.Equal(saved.DifferenceId, again.Value!.Summary.DifferenceId);
    }

    [Fact]
    public void AnUnknownReferenceNameIsStillRefused()
    {
        using var fixture = new Fixture(_root);

        var refused = ProposalDifferenceCommands.Produce(fixture.ProduceRequest(), "both");

        Assert.Equal("difference.invalid-reference", refused.Refusal!.Code);
    }
}
