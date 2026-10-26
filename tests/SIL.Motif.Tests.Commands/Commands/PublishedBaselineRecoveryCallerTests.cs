using System.IO;
using System.Threading;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class PublishedBaselineRecoveryCallerTests : IDisposable
{
    private readonly PristineProjectFixture _pristine;
    private readonly string _managedRootsParent = Path.Combine(Path.GetTempPath(),
        "SIL.Motif.PublishedBaselineRecoveryCallerTests", Guid.NewGuid().ToString("N"));

    public PublishedBaselineRecoveryCallerTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        Directory.CreateDirectory(_managedRootsParent);
    }

    public void Dispose()
    {
        try { Directory.Delete(_managedRootsParent, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void SelectionCanSaveItsInputsWhenThePublishedCopyIsGone()
    {
        var project = CaptureSeededProject();
        RemovePublishedCopy(project);

        var saved = SelectionCommands.SetDefault(new SetDefaultSelectionRequest(
            project.Path, "Default", [project.Seed.TextId], []));

        Assert.True(saved.Succeeded, saved.Refusal?.Message);
        Assert.Equal([project.Seed.TextId], saved.Value!.Selection!.TextIds);
        AssertMissingPublishedCopyPreservedHistory(project);
    }

    [Fact]
    public void PendingChangePutReturnsItsTypedMissingBaselineRefusalBeforeOpeningTheCopy()
    {
        var project = CaptureSeededProject();
        var created = ProposalCommands.New(new NewDraftRequest(project.Path, MotifProductVersion.CurrentText,
            "recovery-proposal", "Recovery caller fixture"));
        Assert.True(created.Succeeded, created.Refusal?.Message);
        var proposalId = created.Value!.ProposalId;
        var before = PendingChanges.Load(new PendingChangesRequest(
            project.Path, MotifProductVersion.CurrentText, proposalId));
        Assert.True(before.Succeeded, before.Refusal?.Message);
        RemovePublishedCopy(project);

        var attempted = PendingChanges.Put(new PutPendingChangeRequest(
            project.Path, MotifProductVersion.CurrentText, before.Value!.Revision,
            new ChangeIntent("missing-baseline-change", "incorrect-spelling",
                CanonicalId.FromGuid(project.Seed.AnalysedWordformId).Value,
                SeededProject.AnalysedWordForm), proposalId));

        Assert.False(attempted.Succeeded);
        Assert.Equal("change.baseline-missing", attempted.Refusal!.Code);
        var after = PendingChanges.Load(new PendingChangesRequest(
            project.Path, MotifProductVersion.CurrentText, proposalId));
        Assert.True(after.Succeeded, after.Refusal?.Message);
        Assert.Equal(before.Value.Revision, after.Value!.Revision);
        Assert.Empty(after.Value.Changes);
        AssertMissingPublishedCopyPreservedHistory(project);
    }

    [Fact]
    public void GrammarCheckRefusesWithoutCallingPanGlossWhenThePublishedCopyIsGone()
    {
        var project = CaptureSeededProject();
        var invoker = new FakeInvoker
        {
            Respond = _ => throw new InvalidOperationException("Grammar Check must not run without a Baseline."),
        };
        RemovePublishedCopy(project);

        var outcome = GrammarCheckQuery.Query(new GrammarCheckRequest(project.Path), invoker, CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("baseline.missing", outcome.Refusal!.Code);
        Assert.Empty(invoker.Requests);
        AssertMissingPublishedCopyPreservedHistory(project);
    }

    [Fact]
    public void WordTraceRefusesWithoutCallingPanGlossWhenThePublishedCopyIsGone()
    {
        var project = CaptureSeededProject();
        var invoker = new FakeInvoker
        {
            Respond = _ => throw new InvalidOperationException("Word Trace must not run without a Baseline."),
        };
        RemovePublishedCopy(project);

        var outcome = WordTraceQuery.Query(new WordTraceRequest(project.Path, SeededProject.AnalysedWordForm),
            new PanGlossTracer(invoker), CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("wordtrace.no-baseline", outcome.Refusal!.Code);
        Assert.Empty(invoker.Requests);
        AssertMissingPublishedCopyPreservedHistory(project);
    }

    private CapturedProject CaptureSeededProject()
    {
        string path;
        SeededText seed;
        using (var scratch = _pristine.NewScratch())
        {
            seed = SeededProject.SeedText(scratch, _pristine.Seed);
            new FwDataProjectLoader().Save(scratch);
            path = scratch.ProjectId.Path;
        }

        var managedRoot = Path.Combine(_managedRootsParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(managedRoot);
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), managedRoot);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        return new CapturedProject(path, captured.Value!, seed);
    }

    private static void RemovePublishedCopy(CapturedProject project)
    {
        Assert.True(File.Exists(project.Baseline.FwDataPath));
        File.Delete(project.Baseline.FwDataPath);
        Assert.False(File.Exists(project.Baseline.FwDataPath));
    }

    private static void AssertMissingPublishedCopyPreservedHistory(CapturedProject project)
    {
        var state = ProjectStoreCommand.Run(project.Path, MotifProductVersion.CurrentText, (database, locator) =>
        {
            var repository = new BaselineRepository(database);
            var key = ProjectWorkspaceKey.Compute(locator);
            return CommandOutcome<BaselineStoreState>.Success(new BaselineStoreState(
                repository.GetCurrent(key)?.Token,
                repository.GetCurrentPublished(key)?.Token));
        });

        Assert.True(state.Succeeded, state.Refusal?.Message);
        Assert.Equal(project.Baseline.Token, state.Value!.RecordedToken);
        Assert.Null(state.Value.PublishedToken);
    }

    private sealed record CapturedProject(string Path, SIL.Motif.Contract.Responses.BaselineCaptureResponse Baseline,
        SeededText Seed);

    private sealed record BaselineStoreState(BaselineToken? RecordedToken, BaselineToken? PublishedToken);
}
