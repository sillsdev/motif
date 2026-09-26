using Avalonia.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins what coming back to the window does with the stored evidence: it re-reads it and shows an Assessment
/// recorded meanwhile, as when an agent ran one from the command line, and it never starts a run, never replaces a
/// run this window already shows, and never reloads the words on screen when nothing new was recorded.
/// </summary>
public sealed class StoredEvidenceOnReturnTests
{
    private const string ProjectPath = @"C:\projects\one.fwdata";

    private static readonly DateTimeOffset Saved = new(2026, 9, 5, 10, 58, 0, TimeSpan.Zero);

    private static readonly BaselineToken Token = new(
        "project-1", "sha256:" + new string('a', 64), "1", "2026-09-05T11:02:00Z", "sha256:" + new string('b', 64));

    [Fact]
    public async Task ReturningToTheWindowShowsAnAssessmentRecordedMeanwhile()
    {
        var (fake, workspace) = NewWorkspace();
        fake.ReadCurrentEvidenceCompletesWith(Stored("assessment-first", "dogs", Saved.AddMinutes(10)));
        await workspace.SetProjectAsync(ProjectPath);
        Assert.Equal("dogs", Assert.Single(workspace.Assess.Words.AllRows).Word);

        fake.ReadCurrentEvidenceCompletesWith(Stored("assessment-later", "cats", Saved.AddMinutes(40)));
        await workspace.CheckFreshnessAsync();

        Assert.Equal("cats", Assert.Single(workspace.Assess.Words.AllRows).Word);
        Assert.Equal("assessment-later", workspace.Context.Evidence.ParseTimeAssessmentId);
        Assert.Empty(fake.AssessRequests);
    }

    [Fact]
    public async Task ReturningToTheWindowKeepsTheRunItShowsWhenTheStoreHoldsThatRun()
    {
        var (fake, workspace) = NewWorkspace();
        fake.ReadCurrentEvidenceCompletesWith(Stored("assessment-first", "dogs", Saved.AddMinutes(10)));
        await workspace.SetProjectAsync(ProjectPath);
        workspace.Selection.AllWordforms = true;
        fake.AssessCompletesWith(Run("assessment-run", "birds"));
        await workspace.Assess.RunCommand.ExecuteAsync(null);
        var shown = workspace.Context.Evidence.Assessment;

        fake.ReadCurrentEvidenceCompletesWith(Stored("assessment-run", "birds", DateTimeOffset.UtcNow));
        await workspace.CheckFreshnessAsync();

        Assert.Same(shown, workspace.Context.Evidence.Assessment);
        Assert.False(workspace.Context.Evidence.Assessment!.IsStored);
        Assert.Single(fake.AssessRequests);
    }

    [Fact]
    public async Task ReturningToTheWindowLeavesTheWordsAloneWhenNothingNewWasRecorded()
    {
        var (fake, workspace) = NewWorkspace();
        fake.ReadCurrentEvidenceCompletesWith(Stored("assessment-first", "dogs", Saved.AddMinutes(10)));
        await workspace.SetProjectAsync(ProjectPath);
        var result = workspace.Assess.Result;
        workspace.Assess.Words.SelectedRow = workspace.Assess.Words.Rows.Single();

        fake.ReadCurrentEvidenceCompletesWith(Stored("assessment-first", "dogs", Saved.AddMinutes(10)));
        await workspace.CheckFreshnessAsync();

        Assert.Same(result, workspace.Assess.Result);
        Assert.Equal("dogs", workspace.Assess.Words.SelectedRow?.Word);
        Assert.Equal(2, fake.CurrentEvidenceRequests.Count);
    }

    private static CurrentEvidenceSnapshot Stored(string assessmentId, string word, DateTimeOffset savedUtc) =>
        new("one", Saved, Saved, EvidenceFreshness.Current,
            new BaselineRecord("project-1", Token, "root", ProjectPath, Saved, Saved), null, null, null,
            new AssessmentRecord(assessmentId, null, null, "pangloss", AssessmentKind.ParseTime.ToStoredKind(),
                "{}", "sha256:scope", "whitespace", "1", "{}", Selection.Create("Default", [word]), null, null,
                "sha256:grammar", null, null, null, savedUtc.ToString("O"),
                Words: [new AssessedWord(word, "analysed", [], 10) { ProjectStanding = ProjectStanding.Approved }])
            {
                Invocation = new BatchInvocationEvidence("invocation/" + assessmentId, "source", "digest", "digest",
                    "words", "digest", "tsv", "digest", "stderr", "digest", 1000,
                    SIL.Motif.Contract.Assess.StepCap.Default, 1, false),
            });

    private static AssessCommandResponse Run(string assessmentId, string word) => new(
        new BaselineCaptureResponse(Token, ProjectPath, Saved, false, true), new SelectionProjection([word], []),
        [assessmentId], "summary")
    {
        InvocationId = "invocation/" + assessmentId,
        Words = [new AssessmentWordResult(word, "analysed", false, "Search completed", 10, null)],
        Measurements = [new ProducedAssessmentReference(assessmentId, AssessmentKinds.ParseTime,
            "invocation/" + assessmentId)],
    };

    private static (FakeCommandClient Fake, WorkspaceShellViewModel Workspace) NewWorkspace()
    {
        var fake = new FakeCommandClient();
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(Token, Saved, false)
        {
            ProjectLastWriteUtc = Saved,
        });
        fake.ListTextsCompletesWith(new TextInventoryResponse([], HasBaseline: true));
        var selection = new SelectionViewModel(fake);
        var workspace = new WorkspaceShellViewModel(
            new ProjectViewModel(fake, new ProjectPicker()), new BaselineViewModel(fake), selection,
            new AssessViewModel(fake, selection), new FolderPicker(), new DragSource(), fake);
        return (fake, workspace);
    }

    private sealed class ProjectPicker : IProjectPicker
    {
        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class FolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class DragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(
            PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) =>
            Task.FromResult(allowedEffects);
    }
}
