using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ProposalSidesTests
{
    [Fact]
    public async Task OpeningAProposalKeepsProjectHealthAndWarningsOnTheCurrentEvidence()
    {
        var fake = new FakeCommandClient();
        var current = SampleOverview.Populated() with
        {
            Warnings = SampleOverview.Populated().Warnings! with { Count = 27 },
        };
        var candidate = current with { Warnings = current.Warnings! with { Count = 18 } };
        fake.OnOverview((request, _) => Task.FromResult(CommandOutcome<OverviewResponse>.Success(
            request.ProposalId is null ? current : candidate)));
        var currentWarning = new GrammarWarning(GrammarDiagnosticLevel.Warning, "Current", [], [], "Current warning");
        var candidateWarning = new GrammarWarning(GrammarDiagnosticLevel.Warning, "Candidate", [], [], "Candidate warning");
        fake.StoredGrammarCheckHandler = (request, _) => Task.FromResult(CommandOutcome<StoredGrammarCheckResponse>.Success(
            new StoredGrammarCheckResponse(new GrammarCheckResponse(
                [request.ProposalId is null ? currentWarning : candidateWarning], true))));
        fake.ReadTrialCompletesWith(OverviewProposalViewTests.Trial());
        var context = WorkspaceContextTests.NewContext(fake);
        context.Diagnostics = true;
        var progress = new ProgressPageModel(context);
        var diagnostics = new DiagnosticsPageModel(context);
        await context.OpenProjectAsync("project.fwdata");

        await context.OpenProposalAsync("proposal/one", CancellationToken.None);
        await context.EvidencePublication;

        Assert.Same(current, progress.Health.Overview);
        Assert.Contains("27", progress.Health.WarningText);
        Assert.Equal([currentWarning], diagnostics.Warnings.Findings);
        Assert.DoesNotContain(fake.OverviewRequests, request => request.ProposalId is not null);
        Assert.DoesNotContain(fake.StoredGrammarCheckRequests, request => request.ProposalId is not null);
        Assert.Empty(fake.AssessRequests);
        await context.StopProjectWorkAsync();
    }
}
