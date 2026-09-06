using System.Text.Json;
using Avalonia.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins <see cref="HandoffWorkspaceViewModel"/>'s composition: choosing a project loads Baseline and Text
/// state and propagates the project to every child; a completed Assessment feeds
/// <see cref="BaselineViewModel.HasAssessment"/> and <see cref="StatisticsViewModel.SummaryMarkdown"/>; a
/// Refresh that replaces an already-assessed Baseline offers a rerun, which Accept and Dismiss resolve;
/// and choosing another project clears what the previous one displayed. The last test exercises the whole
/// agreed workflow end to end.
/// </summary>
public sealed class HandoffWorkspaceViewModelTests
{
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string BundleDigest = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string ProjectPath = @"C:\projects\one.fwdata";

    private static readonly Guid TextId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static BaselineToken NewToken(string capturedUtc = "2026-09-05T00:00:00Z") =>
        new("project-1", Digest, "1", capturedUtc, BundleDigest);

    private static AssessCommandResponse NewAssessResponse(string summary) => new(
        new BaselineCaptureResponse(NewToken(), ProjectPath, DateTimeOffset.UtcNow, false, false),
        new SelectionProjection([], []), ["assessment/one"], summary);

    private static (FakeCommandClient Fake, FakeProjectPicker ProjectPicker, FakeFolderPicker FolderPicker,
        FakeDragSource DragSource, HandoffWorkspaceViewModel Workspace) NewWorkspace()
    {
        var fake = new FakeCommandClient();
        var projectPicker = new FakeProjectPicker();
        var folderPicker = new FakeFolderPicker();
        var dragSource = new FakeDragSource();
        var selection = new SelectionViewModel(fake);
        var workspace = new HandoffWorkspaceViewModel(
            new ProjectViewModel(fake, projectPicker),
            new BaselineViewModel(fake),
            selection,
            new AssessViewModel(fake, selection),
            new StatisticsViewModel(fake),
            new HandoffViewModel(fake, selection, folderPicker, dragSource));
        return (fake, projectPicker, folderPicker, dragSource, workspace);
    }

    private static async Task ChooseProjectAsync(
        FakeCommandClient fake, FakeProjectPicker projectPicker, HandoffWorkspaceViewModel workspace,
        string projectPath, BaselineToken? token = null)
    {
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(token, token is null ? null : DateTimeOffset.UtcNow, false));
        fake.ListTextsCompletesWith(new TextInventoryResponse([]));
        projectPicker.PathToReturn = projectPath;
        await workspace.Project.BrowseCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task ChoosingAProjectLoadsBaselineAndTextStateAndPropagatesTheProjectToEveryChild()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(NewToken(), DateTimeOffset.UtcNow, false));
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")]));
        projectPicker.PathToReturn = ProjectPath;

        await workspace.Project.BrowseCommand.ExecuteAsync(null);

        Assert.True(workspace.Baseline.HasBaseline);
        Assert.Single(workspace.Selection.Texts);
        Assert.Equal(ProjectPath, workspace.Assess.ProjectPath);
        Assert.Equal(ProjectPath, workspace.Statistics.ProjectPath);
        Assert.Equal(ProjectPath, workspace.Handoff.ProjectPath);
    }

    [Fact]
    public async Task ACompletedAssessmentFeedsBaselineHasAssessmentAndTheStatisticsSummary()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        workspace.Selection.AllWordforms = true;
        fake.AssessCompletesWith(NewAssessResponse("(summary)"));

        await workspace.Assess.RunCommand.ExecuteAsync(null);

        Assert.True(workspace.Baseline.HasAssessment);
        Assert.Equal("(summary)", workspace.Statistics.SummaryMarkdown);
        Assert.True(workspace.HasEverAssessed);
        Assert.False(workspace.NotYetAssessed);
    }

    [Fact]
    public async Task RefreshingAnAssessedBaselineOffersARerunAndAcceptingItRunsTheAssessmentAgain()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        workspace.Selection.AllWordforms = true;
        fake.AssessCompletesWith(NewAssessResponse("(first)"));
        await workspace.Assess.RunCommand.ExecuteAsync(null);

        fake.CaptureBaselineCompletesWith(new BaselineCaptureResponse(
            NewToken("2026-09-06T00:00:00Z"), ProjectPath, DateTimeOffset.UtcNow, false, false));
        await workspace.Baseline.RefreshCommand.ExecuteAsync(null);

        Assert.True(workspace.RerunOffered);

        fake.AssessCompletesWith(NewAssessResponse("(second)"));
        await workspace.AcceptRerunCommand.ExecuteAsync(null);

        Assert.False(workspace.RerunOffered);
        Assert.Equal(2, fake.AssessRequests.Count);
        Assert.Equal("(second)", workspace.Statistics.SummaryMarkdown);
    }

    [Fact]
    public async Task DismissingARerunOfferClearsItWithoutRunningAnything()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        workspace.Selection.AllWordforms = true;
        fake.AssessCompletesWith(NewAssessResponse("(first)"));
        await workspace.Assess.RunCommand.ExecuteAsync(null);
        fake.CaptureBaselineCompletesWith(new BaselineCaptureResponse(
            NewToken("2026-09-06T00:00:00Z"), ProjectPath, DateTimeOffset.UtcNow, false, false));
        await workspace.Baseline.RefreshCommand.ExecuteAsync(null);

        workspace.DismissRerunCommand.Execute(null);

        Assert.False(workspace.RerunOffered);
        Assert.Single(fake.AssessRequests);
    }

    [Fact]
    public async Task ChoosingAnotherProjectClearsThePreviousProjectsAssessmentAndStatisticsState()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        workspace.Selection.AllWordforms = true;
        fake.AssessCompletesWith(NewAssessResponse("(summary)"));
        await workspace.Assess.RunCommand.ExecuteAsync(null);
        fake.StatsCompletesWith(new StatsCommandResponse(
            "assessment/one", "grammar.json", "cache.sqlite", null,
            [JsonDocument.Parse("""{"kind":"word"}""").RootElement.Clone()]));
        await workspace.Statistics.LoadCommand.ExecuteAsync(null);

        await ChooseProjectAsync(fake, projectPicker, workspace, @"C:\projects\two.fwdata");

        Assert.False(workspace.HasEverAssessed);
        Assert.True(workspace.NotYetAssessed);
        Assert.Null(workspace.Statistics.SummaryMarkdown);
        Assert.Empty(workspace.Statistics.Rows);
        Assert.Equal(AssessRunState.Idle, workspace.Assess.State);
        Assert.Null(workspace.Assess.Result);
        Assert.Empty(workspace.Handoff.Files);
    }

    [Fact]
    public async Task TheFullAgreedWorkflowCompletesAndProducesDraggableHandoffFiles()
    {
        var (fake, projectPicker, folderPicker, dragSource, workspace) = NewWorkspace();
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(NewToken(), DateTimeOffset.UtcNow, false));
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")]));
        projectPicker.PathToReturn = ProjectPath;
        await workspace.Project.BrowseCommand.ExecuteAsync(null);

        workspace.Selection.Texts[0].IsChecked = true;
        workspace.Selection.PastedWords = "one\ntwo";
        Assert.True(workspace.Selection.CanAssess);

        fake.AssessCompletesWith(NewAssessResponse("(summary)"));
        await workspace.Assess.RunCommand.ExecuteAsync(null);
        Assert.Equal(AssessRunState.Completed, workspace.Assess.State);

        fake.StatsCompletesWith(new StatsCommandResponse(
            "assessment/one", "grammar.json", "cache.sqlite", null,
            [JsonDocument.Parse("""{"kind":"word","attempts":1}""").RootElement.Clone()]));
        workspace.Statistics.SelectedGroup = workspace.Statistics.Groups[1];
        await workspace.Statistics.LoadCommand.ExecuteAsync(null);
        workspace.Statistics.FilterText = "word";
        workspace.Statistics.SortBy("attempts");
        Assert.Single(workspace.Statistics.Rows);
        Assert.Equal(workspace.Statistics.Groups[1], Assert.Single(fake.StatsRequests).ForwardedArguments[1]);

        fake.CaptureBaselineCompletesWith(new BaselineCaptureResponse(
            NewToken("2026-09-06T00:00:00Z"), ProjectPath, DateTimeOffset.UtcNow, false, false));
        await workspace.Baseline.RefreshCommand.ExecuteAsync(null);
        Assert.True(workspace.RerunOffered);
        fake.AssessCompletesWith(NewAssessResponse("(rerun)"));
        await workspace.AcceptRerunCommand.ExecuteAsync(null);
        Assert.False(workspace.RerunOffered);

        folderPicker.PathToReturn = @"C:\out";
        fake.HandoffCompletesWith(new HandoffCommandResponse(
            @"C:\out",
            new BaselineCaptureResponse(NewToken(), ProjectPath, DateTimeOffset.UtcNow, false, false),
            new SelectionProjection([], []), ["grammar.json"], ["assessment/two"]));
        await workspace.Handoff.RunCommand.ExecuteAsync(null);

        Assert.Equal(HandoffRunState.Completed, workspace.Handoff.State);
        var file = Assert.Single(workspace.Handoff.Files);
        await workspace.Handoff.DragFileAsync(null!, file);
        Assert.Equal([file.FullPath], dragSource.LastPaths);
    }

    private sealed class FakeProjectPicker : IProjectPicker
    {
        public string? PathToReturn { get; set; }

        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(PathToReturn);
    }

    private sealed class FakeFolderPicker : IHandoffFolderPicker
    {
        public string? PathToReturn { get; set; } = @"C:\out";

        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(PathToReturn);
    }

    private sealed class FakeDragSource : IFileDragSource
    {
        public IReadOnlyList<string>? LastPaths { get; private set; }

        public Task<DragDropEffects> StartDragAsync(
            PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths, DragDropEffects allowedEffects)
        {
            LastPaths = filePaths;
            return Task.FromResult(allowedEffects);
        }
    }
}
