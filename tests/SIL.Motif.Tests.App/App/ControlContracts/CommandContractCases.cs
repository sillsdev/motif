using System.Reflection;
using System.Windows.Input;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;

namespace SIL.Motif.Tests.App;

internal sealed record CommandContract(
    Type OwnerType,
    string PropertyName,
    Type? ParameterType,
    object? Parameter,
    bool CanExecute,
    string SideEffectOwner,
    string? AliasOf = null,
    Func<WorkspaceShellViewModel, object?>? ParameterFactory = null)
{
    public string Key => $"{OwnerType.Name}.{PropertyName}";
}

internal sealed record CommandProperty(string Key, ICommand Command, Type? ParameterType);

internal static class CommandContractCases
{
    private static readonly DateTimeOffset Saved = new(2026, 9, 5, 10, 58, 0, TimeSpan.Zero);

    private static readonly BaselineToken Token = new(
        "project-1", "sha256:" + new string('a', 64), "1", "2026-09-05T11:02:00Z",
        "sha256:" + new string('b', 64));

    public static IReadOnlyList<CommandContract> Authored { get; } =
    [
        new(typeof(WorkspaceShellViewModel), nameof(WorkspaceShellViewModel.ShowPageCommand),
            typeof(WorkspacePage), WorkspacePage.Overview, true,
            "WorkspacePageTests.ARunOpensTheMatrixAndARerunWithMovesOpensWhatChanged"),
        new(typeof(WorkspaceShellViewModel), nameof(WorkspaceShellViewModel.SelectNewProjectCommand),
            null, null, true,
            "ProjectViewModelTests.BrowseCommandRaisesProjectChosenWithThePickedPath"),
        new(typeof(WorkspaceShellViewModel), nameof(WorkspaceShellViewModel.OpenRecentProjectCommand),
            typeof(RecentProjectViewModel), new RecentProjectViewModel(@"C:\projects\one.fwdata"), true,
            "RecentProjectsOpenTests.TheRecentListStillOffersTheProjectBeingOpenedUntilItsOpenCommandCompletes"),
        new(typeof(WorkspaceShellViewModel), nameof(WorkspaceShellViewModel.ConfigureCommand),
            null, null, false,
            "WorkspaceShellViewModelTests.ConfigureOpensSetupWithoutNavigatingAwayFromTheCurrentPage"),
        new(typeof(WorkspaceShellViewModel), nameof(WorkspaceShellViewModel.DeleteRefusedStoreCommand),
            typeof(WindowRefusal), null, false,
            "StoreDeletionFlowTests.OnlyAStoreFromAnotherVersionOffersDeletion"),
        new(typeof(WorkspaceShellViewModel), nameof(WorkspaceShellViewModel.ConfirmStoreDeletionCommand),
            null, null, false,
            "StoreDeletionFlowTests.ConfirmingDeletesThatProjectsStoreAndReopensIt"),
        new(typeof(WorkspaceShellViewModel), nameof(WorkspaceShellViewModel.CancelStoreDeletionCommand),
            null, null, false,
            "StoreDeletionFlowTests.DeletingAsksFirstAndCancelDeletesNothing"),
        new(typeof(WorkspaceShellViewModel), nameof(WorkspaceShellViewModel.RefreshCommand),
            null, null, false,
            "WorkspacePageTests.WhileRefreshingTheTopRowHidesBothActionsUntilCaptureFinishes"),
        new(typeof(WorkspaceShellViewModel), nameof(WorkspaceShellViewModel.SeeWhatChangedCommand),
            null, null, false,
            "WorkspacePageTests.ARunOpensTheMatrixAndARerunWithMovesOpensWhatChanged"),
        new(typeof(WorkspaceShellViewModel), nameof(WorkspaceShellViewModel.ParseAllWordsCommand),
            null, null, false,
            "WorkspaceShellViewModelTests.RefreshOffersParseAllWordsAndRunsTheSavedSelectionWithItsSavedLimits"),
        new(typeof(ProjectViewModel), nameof(ProjectViewModel.BrowseCommand), null, null, true,
            "ProjectViewModelTests.BrowseCommandRaisesProjectChosenWithThePickedPath"),
        new(typeof(BaselineViewModel), nameof(BaselineViewModel.RefreshCommand), null, null, false,
            "BaselineViewModelTests.RefreshCommandUpdatesStateOnSuccess"),
        new(typeof(WorkspaceContext), nameof(WorkspaceContext.BrowseForProjectCommand), null, null, true,
            "ProjectViewModelTests.BrowseCommandRaisesProjectChosenWithThePickedPath",
            nameof(ProjectViewModel) + "." + nameof(ProjectViewModel.BrowseCommand)),
        new(typeof(WorkspaceContext), nameof(WorkspaceContext.OpenProjectCommand), typeof(string),
            @"C:\projects\one.fwdata", true,
            "ProjectOpenFailureTests.AFailureWhileOpeningShowsARefusalAndKeepsThePickerUsable"),
        new(typeof(WorkspaceContext), nameof(WorkspaceContext.RefreshProjectCommand), null, null, false,
            "WorkspacePageTests.WhileRefreshingTheTopRowHidesBothActionsUntilCaptureFinishes",
            nameof(WorkspaceShellViewModel) + "." + nameof(WorkspaceShellViewModel.RefreshCommand)),
        new(typeof(WorkspaceContext), nameof(WorkspaceContext.ParseAllWordsCommand), null, null, false,
            "WorkspaceShellViewModelTests.RefreshOffersParseAllWordsAndRunsTheSavedSelectionWithItsSavedLimits",
            nameof(WorkspaceShellViewModel) + "." + nameof(WorkspaceShellViewModel.ParseAllWordsCommand)),
        new(typeof(WorkspaceContext), nameof(WorkspaceContext.ConfigureCommand), null, null, false,
            "WorkspaceShellViewModelTests.ConfigureOpensSetupWithoutNavigatingAwayFromTheCurrentPage",
            nameof(WorkspaceShellViewModel) + "." + nameof(WorkspaceShellViewModel.ConfigureCommand)),
        new(typeof(WorkspaceContext), nameof(WorkspaceContext.ParsePromptActionCommand), null, null, false,
            "WorkspaceShellViewModelTests.ConfigureOpensSetupWithoutNavigatingAwayFromTheCurrentPage",
            nameof(WorkspaceShellViewModel) + "." + nameof(WorkspaceShellViewModel.ConfigureCommand)),
        new(typeof(AssessViewModel), nameof(AssessViewModel.RunCommand), null, null, false,
            "AssessViewModelTests.CancellingWhileRunningReachesCancelledWithTheCommandsOwnRefusalCode"),
        new(typeof(AssessViewModel), nameof(AssessViewModel.CancelCommand), null, null, false,
            "AssessViewModelTests.CancellingWhileRunningReachesCancelledWithTheCommandsOwnRefusalCode"),
        new(typeof(AssessWordsViewModel), nameof(AssessWordsViewModel.SetFilterCommand),
            typeof(ResultsWordFilter), ResultsWordFilter.All, true,
            "AssessWordsViewModelTests.EachFilterChipIsItsOwnBucketNotAPartitionOfTheOthers"),
        new(typeof(CompareViewModel), nameof(CompareViewModel.ClearSelectionCommand), null, null, true,
            "CompareViewModelTests.AClickChoosesOneCellCtrlClickAddsAndClickingTheOnlyChoiceClearsIt"),
        new(typeof(CompareViewModel), nameof(CompareViewModel.FocusFixFirstCommand),
            typeof(CompareFixFirstViewModel), null, true,
            "CompareViewModelTests.FocusingAFixFirstWordMatchesTheExactForm"),
        new(typeof(CompareViewModel), nameof(CompareViewModel.HandOffCommand), null, null, false,
            "CompareActionsTests.HandingOffPassesTheListedWords"),
        new(typeof(CompareViewModel), nameof(CompareViewModel.OpenWordCommand),
            typeof(CompareWordViewModel), null, true,
            "CompareViewModelTests.OpeningAListedWordHandsItToTheWordsView"),
        new(typeof(CompareViewModel), nameof(CompareViewModel.ProposeCommand), typeof(string),
            "incorrect-spelling", false, "CompareActionsTests.MarkingCheckedSpellingsRecordsOneActionForAllWords"),
        new(typeof(CompareViewModel), nameof(CompareViewModel.RerunCommand), null, null, false,
            "CompareActionsTests.RerunningHandsTheWordsAndTheLongerLimitToItsOwner"),
        new(typeof(CompareViewModel), nameof(CompareViewModel.SelectColumnCommand),
            typeof(CompareColumnViewModel), null, true,
            "CompareViewModelTests.AClickChoosesOneCellCtrlClickAddsAndClickingTheOnlyChoiceClearsIt",
            ParameterFactory: workspace => workspace.Context.Assess.Compare.Columns[0]),
        new(typeof(CompareViewModel), nameof(CompareViewModel.SelectPresetCommand),
            typeof(ComparePresetViewModel), null, true,
            "CompareViewModelTests.AClickChoosesOneCellCtrlClickAddsAndClickingTheOnlyChoiceClearsIt",
            ParameterFactory: workspace => workspace.Context.Assess.Compare.Presets[0]),
        new(typeof(CompareViewModel), nameof(CompareViewModel.SelectRowCommand),
            typeof(CompareRowViewModel), null, true,
            "CompareViewModelTests.AClickChoosesOneCellCtrlClickAddsAndClickingTheOnlyChoiceClearsIt",
            ParameterFactory: workspace => workspace.Context.Assess.Compare.Rows[0]),
        new(typeof(ChangesViewModel), nameof(ChangesViewModel.RemoveCommand), typeof(ChangeViewModel), null,
            true, "ReviewUndoRealClientTests.RemoveAnalysisStagesAsRemovedAndUndoUsesTheRealClient"),
        new(typeof(DifferenceViewModel), nameof(DifferenceViewModel.ClearCommand), null, null, true,
            "DifferenceViewModelTests.ARerunIsComparedWithTheRunItFoldedInto"),
        new(typeof(DifferenceViewModel), nameof(DifferenceViewModel.OpenWordCommand),
            typeof(MovedWordViewModel), null, true,
            "DifferenceViewModelTests.ChoosingAMoveOutlinesItsCellsInBothMatrices"),
        new(typeof(SetupViewModel), nameof(SetupViewModel.SkipCommand), null, null, true,
            "WorkspaceShellViewModelTests.SkippingFirstSetupDoesNotSaveADefaultSelection"),
        new(typeof(SetupViewModel), nameof(SetupViewModel.BackCommand), null, null, false,
            "WorkspaceShellViewModelTests.FirstRunCannotStartBeforeTheLastSetupStep"),
        new(typeof(SetupViewModel), nameof(SetupViewModel.NextCommand), null, null, true,
            "WorkspaceShellViewModelTests.FirstRunCannotStartBeforeTheLastSetupStep"),
        new(typeof(SetupViewModel), nameof(SetupViewModel.FinishCommand), null, null, false,
            "WorkspaceShellViewModelTests.FirstRunSavesTheSelectionAndUsesItWithTheChosenStepLimit"),
        new(typeof(TraceWordViewModel), nameof(TraceWordViewModel.CancelCommand), null, null, false,
            "TraceWordViewModelTests.ChoosingAnotherWordCancelsTheTraceStillRunning"),
        new(typeof(TraceWordViewModel), nameof(TraceWordViewModel.SelectStopGroupCommand),
            typeof(TraceStopGroupViewModel), null, true,
            "TraceWordViewModelTests.FailedAttemptsAreGroupedByTheRuleThatStoppedThemClosestFirstAndFilterable"),
        new(typeof(TraceWordViewModel), nameof(TraceWordViewModel.SetViewCommand), typeof(TraceView),
            TraceView.Candidates, true, "TraceWordViewModelTests.SetWordFillsTheBoxWithoutStartingATrace"),
        new(typeof(TraceWordViewModel), nameof(TraceWordViewModel.ShowEveryAttemptCommand), null, null, true,
            "TraceWordViewModelTests.FailedAttemptsAreGroupedByTheRuleThatStoppedThemClosestFirstAndFilterable"),
        new(typeof(TraceWordViewModel), nameof(TraceWordViewModel.TryCommand), null, null, false,
            "TraceWordViewModelTests.WithNoProjectTheCommandCannotRun"),
    ];

    public static (FakeCommandClient Client, WorkspaceShellViewModel Workspace) CreateWorkspace()
    {
        var client = new FakeCommandClient();
        client.CurrentBaselineCompletesWith(new CurrentBaselineResponse(Token, Saved, false)
        {
            ProjectLastWriteUtc = Saved,
        });
        client.ListTextsCompletesWith(new TextInventoryResponse([], HasBaseline: true));
        var selection = new SelectionViewModel(client);
        var workspace = new WorkspaceShellViewModel(
            new ProjectViewModel(client, new ProjectPicker()), new BaselineViewModel(client), selection,
            new AssessViewModel(client, selection), new FolderPicker(), new DragSource(), client);
        return (client, workspace);
    }

    public static IReadOnlyList<CommandProperty> Discover(WorkspaceShellViewModel workspace)
    {
        var owners = new object[]
        {
            workspace,
            workspace.Project,
            workspace.Baseline,
            workspace.Context,
            workspace.Context.Assess,
            workspace.Context.Changes,
            workspace.Context.Selection,
            workspace.Context.Setup!,
        }
            .Concat(workspace.Pages)
            .Append(workspace.Context.Assess.Compare)
            .Append(workspace.Context.Assess.Words)
            .Append(workspace.Context.Assess.Difference)
            .Append(workspace.Context.Assess.Trace);

        return owners.SelectMany(owner => owner.GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.GetIndexParameters().Length == 0 &&
                                   typeof(ICommand).IsAssignableFrom(property.PropertyType))
                .Select(property => (Owner: owner, Property: property,
                    Command: property.GetValue(owner) as ICommand)))
            .Where(entry => entry.Command is not null)
            .Select(entry => new CommandProperty(
                $"{entry.Owner.GetType().Name}.{entry.Property.Name}", entry.Command!,
                ParameterType(entry.Command!)))
            .ToArray();
    }

    public static Type? ParameterType(ICommand command) => command.GetType().GetInterfaces()
        .Where(type => type.IsGenericType)
        .Select(type => type.GetGenericTypeDefinition())
        .Zip(command.GetType().GetInterfaces().Where(type => type.IsGenericType),
            (definition, type) => (definition, type))
        .Where(pair => pair.definition == typeof(IRelayCommand<>) ||
                       pair.definition == typeof(IAsyncRelayCommand<>))
        .Select(pair => pair.type.GetGenericArguments()[0])
        .Distinct()
        .SingleOrDefault();

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
            Task.FromResult(DragDropEffects.None);
    }
}
