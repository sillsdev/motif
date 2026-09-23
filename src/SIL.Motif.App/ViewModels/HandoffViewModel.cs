using System.Collections.ObjectModel;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

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
    private string? _pendingFolder;

    public HandoffViewModel(
        ICommandClient commandClient, SelectionViewModel selection,
        IHandoffFolderPicker folderPicker, IFileDragSource dragSource)
    {
        ArgumentNullException.ThrowIfNull(commandClient);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(folderPicker);
        ArgumentNullException.ThrowIfNull(dragSource);
        _commandClient = commandClient;
        _folderPicker = folderPicker;
        _dragSource = dragSource;
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

    /// <summary>The sentence the stage shows when the files are stale.</summary>
    public string OutOfDateText => LatestAssessmentAt is { } assessed && WrittenAt is { } written
        ? $"The Assessment was run again at {assessed.ToLocalTime():t}, after these files were written at " +
          $"{written.ToLocalTime():t}. Write the Handoff again to include the new results."
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

    public bool HasChosenWords => ChosenWords is { Count: > 0 };

    public string ChosenWordsText => ChosenWords is { } words
        ? $"Only the {words.Count:N0} word{(words.Count == 1 ? string.Empty : "s")} chosen in Compare, assessed again for these files."
        : string.Empty;

    /// <summary>Hands off <paramref name="words"/> rather than the whole Assessment.</summary>
    public void UseWords(IReadOnlyList<string> words) => ChosenWords = words.Count > 0 ? words : null;

    public void UseWholeAssessment() => ChosenWords = null;

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
        var request = ChosenWords is { } words
            ? new HandoffRequest(ProjectPath!, _pendingFolder!, new SelectionRequest(false, [], words, false, null), true)
            : new HandoffRequest(
                ProjectPath!, _pendingFolder!, new SelectionRequest(false, [], [], false, null), true, InvocationId);
        return _commandClient.HandoffAsync(request, this, cancellationToken);
    }

    protected override void OnRunSucceeded(HandoffCommandResponse response)
    {
        WrittenAt = DateTimeOffset.Now;
        OutputDirectory = response.OutputDirectory;
        PastedHeader = response.PastedHeader;
        HandoffMarkdown = response.HandoffMarkdown;
        PopulateFiles(response);
        _pendingFolder = null;
    }

    protected override void OnReset()
    {
        InvocationId = null;
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

    // A trailing separator on the root keeps a bare prefix match from letting "C:\out-evil" pass "C:\out".
    private static bool IsWithinOutputDirectory(string filePath, string outputDirectory)
    {
        var fullFile = Path.GetFullPath(filePath);
        var fullRoot = Path.GetFullPath(outputDirectory);
        var prefix = fullRoot + Path.DirectorySeparatorChar;
        return fullFile.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
