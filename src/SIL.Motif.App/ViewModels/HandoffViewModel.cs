using System.Collections.ObjectModel;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Projection.Usage;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// Writes an AI Handoff folder for the completed Assessment, following the same run-state-machine shape
/// as <see cref="AssessViewModel"/>: one <see cref="RunCommand"/> owns one <see cref="CancellationTokenSource"/>,
/// progress replays the command's own stages, and disposal cancels and awaits an active run.
/// </summary>
/// <remarks>
/// Every path the completed command reports is re-verified to sit inside its own output directory before
/// becoming a draggable <see cref="HandoffFileViewModel"/> row: a path a caller cannot trust is dropped
/// rather than exposed, pinned by <c>PathsThatEscapeTheOutputDirectoryDoNotBecomeDraggableRows</c>. This
/// view model never reads a Handoff file's bytes; it only hands <see cref="IFileDragSource"/> the exact,
/// already-verified paths of a drag gesture the view forwarded to it.
/// </remarks>
public sealed partial class HandoffViewModel : CommandRunViewModel<HandoffCommandResponse>
{
    protected override IDisposable BeginUsageAction() => _commandClient.BeginUsageAction("handoff",
        UsageArgumentShape.Text("fwDataPath"), UsageArgumentShape.Text("assessmentId"),
        UsageArgumentShape.Text("outputDirectory"));

    /// <summary>
    /// What leaves the machine, stated once beside the drag tiles rather than in a file a model reads (ADR
    /// 0045 decision 13): the decision belongs to the person dragging, not to whichever line of a Handoff
    /// document they happen to reach.
    /// </summary>
    public static string DataSensitivitySentence { get; } =
        "This folder holds real grammar rules, lexicon entries, and corpus sentences from this project. " +
        "Dragging it into a chat model sends that data to whoever runs it (OpenAI, Anthropic, or another " +
        "provider) — check the project's own data-sensitivity policy first.";

    private readonly ICommandClient _commandClient;
    private readonly IHandoffFolderPicker _folderPicker;
    private readonly IFileDragSource _dragSource;
    private readonly IClipboard _clipboard;
    private readonly TimeProvider _timeProvider;
    private string? _pendingFolder;

    public HandoffViewModel(
        ICommandClient commandClient, SelectionViewModel selection,
        IHandoffFolderPicker folderPicker, IFileDragSource dragSource, TimeProvider? timeProvider = null,
        IClipboard? clipboard = null)
    {
        ArgumentNullException.ThrowIfNull(commandClient);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(folderPicker);
        ArgumentNullException.ThrowIfNull(dragSource);
        _commandClient = commandClient;
        _folderPicker = folderPicker;
        _dragSource = dragSource;
        _clipboard = clipboard ?? NoDesktopServices.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The project this Handoff publishes, or <c>null</c> before a project has been chosen.</summary>
    [ObservableProperty]
    private string? _projectPath;

    /// <summary>The completed Assessment whose retained result this Handoff publishes.</summary>
    [ObservableProperty]
    private string? _invocationId;

    /// <summary>When these files were written, so the stage can say whether newer results have arrived.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WrittenAtText))]
    private DateTimeOffset? _writtenAt;

    /// <summary>The time of writing as the stage shows it, or a prompt before the first write.</summary>
    public string WrittenAtText => WrittenAt is { } written
        ? $"Last written {written.ToLocalTime():t}"
        : "Not written yet";

    // What the next write covers: the Assessment's time, words and texts; set by the workspace.
    [ObservableProperty]
    private string? _coverageText;

    /// <summary>When the last Assessment finished, set by the workspace; newer than the files means stale.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOutOfDate))]
    [NotifyPropertyChangedFor(nameof(OutOfDateText))]
    private DateTimeOffset? _latestAssessmentAt;

    /// <summary>Whether an Assessment finished after these files were written, so they no longer match Results.</summary>
    public bool IsOutOfDate => HasCompletedFiles && WrittenAt is { } written && LatestAssessmentAt > written;

    /// <summary>The sentence the AI Handoff page shows when the files are stale.</summary>
    public string OutOfDateText => LatestAssessmentAt is { } assessed && WrittenAt is { } written
        ? $"The words were parsed again at {assessed.ToLocalTime():t}, after these files were written at " +
          $"{written.ToLocalTime():t}. Write the AI Handoff again to include the new results."
        : string.Empty;

    /// <summary>Where the completed run wrote the folder, or <c>null</c> before a run has completed.</summary>
    [ObservableProperty]
    private string? _outputDirectory;

    /// <summary>The text pasted alongside the dragged files, or <c>null</c> before a run has completed.</summary>
    [ObservableProperty]
    private string? _pastedHeader;

    /// <summary>The completed run's own <c>handoff.md</c>, or <c>null</c> before a run has completed.</summary>
    [ObservableProperty]
    private string? _handoffMarkdown;

    /// <summary>The completed run's own files, each already verified to sit inside <see cref="OutputDirectory"/>.</summary>
    public ObservableCollection<HandoffFileViewModel> Files { get; } = [];

    /// <summary>Whether the completed Handoff has files that can be dragged or copied.</summary>
    public bool HasCompletedFiles => State == RunState.Completed && Files.Count > 0;

    partial void OnProjectPathChanged(string? value) => RunCommand.NotifyCanExecuteChanged();

    partial void OnInvocationIdChanged(string? value) => RunCommand.NotifyCanExecuteChanged();

    /// <summary>Copies the folder the files were written to, the keyboard's route to what a drag carries.</summary>
    public Task CopyFolderAsync() =>
        OutputDirectory is { } folder ? _clipboard.SetTextAsync(folder) : Task.CompletedTask;

    /// <summary>Copies the exact path of one Handoff file.</summary>
    public Task CopyFilePathAsync(HandoffFileViewModel file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return _clipboard.SetTextAsync(file.FullPath);
    }

    /// <summary>Copies one of the questions offered for a chat.</summary>
    public Task CopyQuestionAsync(string question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        return _clipboard.SetTextAsync(question);
    }

    /// <summary>Copies the starter prompt pasted beside the files; nothing before a run has written one.</summary>
    public Task CopyStarterPromptAsync() =>
        PastedHeader is { } header ? _clipboard.SetTextAsync(header) : Task.CompletedTask;

    /// <summary>Hands the exact path of one Handoff file to the drag adapter; never reads its bytes.</summary>
    public Task<DragDropEffects> DragFileAsync(PointerPressedEventArgs trigger, HandoffFileViewModel file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return _dragSource.StartDragAsync(trigger, [file.FullPath], DragDropEffects.Copy);
    }

    /// <summary>Hands every Handoff file's exact path to the drag adapter at once; never reads their bytes.</summary>
    public Task<DragDropEffects> DragAllFilesAsync(PointerPressedEventArgs trigger) =>
        _dragSource.StartDragAsync(trigger, Files.Select(file => file.FullPath).ToList(), DragDropEffects.Copy);

    protected override bool CanStartCore() =>
        ProjectPath is not null && (ChosenWords is not null || !string.IsNullOrWhiteSpace(InvocationId));

    // Words chosen in Compare to hand off, assessed afresh; null hands off the latest Assessment as it is.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChosenWords))]
    [NotifyPropertyChangedFor(nameof(ChosenWordsText))]
    private IReadOnlyList<string>? _chosenWords;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChosenWordsText))]
    private WordTraceResponse? _selectedTrace;

    private WarningHandoffScope? _warningScope;

    public bool HasChosenWords => ChosenWords is { Count: > 0 };

    public string ChosenWordsText => SelectedTrace is { } trace
        ? $"Keeps the displayed trace for {trace.Word} and the Baseline captured with it."
        : ChosenWords is { } words
            ? _warningScope is not null
                ? words.Count > 0
                    ? $"{words.Count:N0} word{(words.Count == 1 ? string.Empty : "s")} selected from this PanGloss finding."
                    : "No words were selected from this PanGloss finding; its message and attribution limits will be included."
                : $"Only the {words.Count:N0} word{(words.Count == 1 ? string.Empty : "s")} chosen on the Texts page, parsed again for these files."
            : string.Empty;

    /// <summary>Hands off <paramref name="words"/> rather than the whole Assessment.</summary>
    public void UseWords(IReadOnlyList<string> words, WarningHandoffScope? warningScope = null)
    {
        SelectedTrace = null;
        _warningScope = warningScope;
        ChosenWords = warningScope is not null || words.Count > 0 ? words : null;
        OnPropertyChanged(nameof(ChosenWordsText));
    }

    /// <summary>Keeps the trace already displayed on Try a Word with the Baseline recorded in its capture.</summary>
    public void UseSelectedTrace(WordTraceResponse trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        SelectedTrace = trace;
        _warningScope = null;
        ChosenWords = [trace.Word];
        InvocationId = null;
        LatestAssessmentAt = null;
        CoverageText = null;
    }

    public void UseWholeAssessment()
    {
        SelectedTrace = null;
        ChosenWords = null;
        _warningScope = null;
    }

    partial void OnChosenWordsChanged(IReadOnlyList<string>? value) => RunCommand.NotifyCanExecuteChanged();

    protected override async Task<bool> PrepareRunAsync()
    {
        _pendingFolder = await _folderPicker.PickFolderAsync();
        return _pendingFolder is not null;
    }

    protected override void OnRunStarting()
    {
        Files.Clear();
        OutputDirectory = null;
        PastedHeader = null;
        HandoffMarkdown = null;
    }

    protected override Task<CommandOutcome<HandoffCommandResponse>> ExecuteCoreAsync(
        CancellationToken cancellationToken)
    {
        var request = SelectedTrace is { } trace
            ? new HandoffRequest(ProjectPath!, _pendingFolder!,
                new SelectionRequest(false, [], [trace.Word], false, null), false)
            {
                SelectedTrace = trace,
            }
            : ChosenWords is { } words
                ? new HandoffRequest(ProjectPath!, _pendingFolder!, new SelectionRequest(false, [], words, false, null),
                    words.Count > 0, WarningScope: _warningScope)
            : new HandoffRequest(
                ProjectPath!, _pendingFolder!, new SelectionRequest(false, [], [], false, null), true, InvocationId);
        return _commandClient.HandoffAsync(request, this, cancellationToken);
    }

    protected override void OnRunSucceeded(HandoffCommandResponse response)
    {
        WrittenAt = _timeProvider.GetLocalNow();
        OutputDirectory = response.OutputDirectory;
        PastedHeader = response.PastedHeader;
        HandoffMarkdown = response.HandoffMarkdown;
        PopulateFiles(response);
        _pendingFolder = null;
    }

    protected override void OnReset()
    {
        InvocationId = null;
        ChosenWords = null;
        SelectedTrace = null;
        _warningScope = null;
        CoverageText = null;
        LatestAssessmentAt = null;
        WrittenAt = null;
        _pendingFolder = null;
        Files.Clear();
        OutputDirectory = null;
        PastedHeader = null;
        HandoffMarkdown = null;
    }

    protected override void OnRunStateChanged(RunState value) => OnPropertyChanged(nameof(HasCompletedFiles));

    // A path outside the output directory is dropped rather than exposed as a draggable row.
    private void PopulateFiles(HandoffCommandResponse response)
    {
        foreach (var relativePath in response.Files)
        {
            var candidate = Path.Combine(response.OutputDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (IsWithinOutputDirectory(candidate, response.OutputDirectory))
                Files.Add(new HandoffFileViewModel(relativePath, Path.GetFullPath(candidate)));
        }
    }

    // A trailing separator on the root keeps a sibling folder from passing the prefix check.
    private static bool IsWithinOutputDirectory(string filePath, string outputDirectory)
    {
        var fullFile = Path.GetFullPath(filePath);
        var fullRoot = Path.GetFullPath(outputDirectory);
        var prefix = fullRoot + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return fullFile.StartsWith(prefix, comparison);
    }
}
