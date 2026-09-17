using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>The Handoff panel's own state machine for one synchronous folder-writing run.</summary>
public enum HandoffRunState
{
    /// <summary>No run has started, or the previous one already reached a terminal state.</summary>
    Idle,

    /// <summary>The command is running; Handoff is disabled and Cancel is available.</summary>
    Running,

    /// <summary>Cancel was requested; waiting for the command to unwind and return its refusal.</summary>
    Cancelling,

    /// <summary>The command returned a typed success.</summary>
    Completed,

    /// <summary>The command returned <c>handoff.cancelled</c> after a requested cancellation.</summary>
    Cancelled,

    /// <summary>The command returned a refusal other than cancellation.</summary>
    Refused,
}

/// <summary>One repository document linked from the completed Handoff.</summary>
public sealed record ReferenceDocument(string DisplayPath, Uri Url, string AccessibleName);

/// <summary>
/// Writes an AI Handoff folder for the current Selection, following the same run-state-machine shape as
/// <see cref="AssessViewModel"/>: one <see cref="RunCommand"/> owns one <see cref="CancellationTokenSource"/>,
/// progress replays the command's own stages, and disposal cancels and awaits an active run.
/// </summary>
/// <remarks>
/// Every path the completed command reports is re-verified to sit inside its own output directory before
/// becoming a draggable <see cref="HandoffFileViewModel"/> row: a path a caller cannot trust is dropped
/// rather than exposed, pinned by <c>PathsThatEscapeTheOutputDirectoryDoNotBecomeDraggableRows</c>. This
/// view model never reads a Handoff file's bytes; it only hands <see cref="IFileDragSource"/> the exact,
/// already-verified paths of a drag gesture the view forwarded to it.
/// </remarks>
public sealed partial class HandoffViewModel : ObservableObject, IProgress<AssessmentProgress>, IAsyncDisposable
{
    private const string CancelledRefusalCode = "handoff.cancelled";
    private const string InstructionsResourceName = "SIL.Motif.Commands.Handoff.Assets.instructions.md";
    private const string StarterPromptResourceName = "SIL.Motif.Commands.Handoff.Assets.starter-prompt.md";

    public const string RepositoryUrlBase = "https://github.com/sillsdev/motif";
    public const string IntroductionText =
        "Drag these files into your chat (Claude, ChatGPT or Gemini). Start with instructions.md. " +
        "This folder is as of FieldWorks' last save. Reference documents in Motif's repository are " +
        "docs/handoff/grammar-format.md, docs/handoff/flextext-json-format.md, " +
        "docs/handoff/hc-mechanics.md, and src/SIL.Motif.Commands/Handoff/Assets/read_handoff.py " +
        "at https://github.com/sillsdev/motif on main.";

    private readonly ICommandClient _commandClient;
    private readonly SelectionViewModel _selection;
    private readonly IHandoffFolderPicker _folderPicker;
    private readonly IFileDragSource _dragSource;
    private CancellationTokenSource? _cts;

    /// <summary>Repository documents that explain the files in a completed Handoff.</summary>
    public IReadOnlyList<ReferenceDocument> ReferenceDocuments { get; } =
    [
        new("docs/handoff/grammar-format.md",
            new Uri($"{RepositoryUrlBase}/blob/main/docs/handoff/grammar-format.md"),
            "Open grammar format reference"),
        new("docs/handoff/flextext-json-format.md",
            new Uri($"{RepositoryUrlBase}/blob/main/docs/handoff/flextext-json-format.md"),
            "Open FlexText JSON format reference"),
        new("docs/handoff/hc-mechanics.md",
            new Uri($"{RepositoryUrlBase}/blob/main/docs/handoff/hc-mechanics.md"),
            "Open HC mechanics reference"),
        new("src/SIL.Motif.Commands/Handoff/Assets/read_handoff.py",
            new Uri($"{RepositoryUrlBase}/blob/main/src/SIL.Motif.Commands/Handoff/Assets/read_handoff.py"),
            "Open read_handoff.py reference"),
    ];

    public HandoffViewModel(
        ICommandClient commandClient, SelectionViewModel selection,
        IHandoffFolderPicker folderPicker, IFileDragSource dragSource)
    {
        ArgumentNullException.ThrowIfNull(commandClient);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(folderPicker);
        ArgumentNullException.ThrowIfNull(dragSource);
        _commandClient = commandClient;
        _selection = selection;
        _folderPicker = folderPicker;
        _dragSource = dragSource;
        _selection.PropertyChanged += OnSelectionPropertyChanged;
        RunCommand = new AsyncRelayCommand(RunAsync, CanRun);
        CancelCommand = new RelayCommand(Cancel, CanCancel);
    }

    /// <summary>The Handoff's own read-this-first prose, rendered in full in an expandable preview.</summary>
    public static string InstructionsMarkdown { get; } = ReadEmbeddedInstructions();

    /// <summary>The short first message a user can paste alongside the completed Handoff files.</summary>
    public static string StarterPromptMarkdown { get; } = ReadEmbeddedAsset(StarterPromptResourceName);

    /// <summary>
    /// The instructions' own sentence naming where an uploaded folder's data goes, shown once above the
    /// file list rather than composed anew here.
    /// </summary>
    /// <remarks>
    /// Held as a literal and pinned against the shipped asset by
    /// <c>DataSensitivitySentenceIsTakenVerbatimFromTheInstructionsAsset</c>, the same way the Baseline
    /// freshness wording is pinned wherever it appears. Parsing it out of the prose at run time instead
    /// would turn a reworded paragraph into a crash while the type initialises.
    /// </remarks>
    public static string DataSensitivitySentence { get; } =
        "Uploading it to a chat model sends that data to whoever runs it " +
        "(OpenAI, Anthropic, or another provider).";

    public string Introduction => IntroductionText;

    /// <summary>The project this Handoff publishes, or <c>null</c> before a project has been chosen.</summary>
    [ObservableProperty]
    private string? _projectPath;

    [ObservableProperty]
    private bool _writeFlexTextXml;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    private HandoffRunState _state = HandoffRunState.Idle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIndeterminate))]
    [NotifyPropertyChangedFor(nameof(ProgressFraction))]
    private AssessmentProgress? _progress;

    // The command's typed success, once State reaches HandoffRunState.Completed.
    [ObservableProperty]
    private HandoffCommandResponse? _result;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RefusalFacts))]
    private Refusal? _refusal;

    /// <summary>Where the completed run wrote the folder, or <c>null</c> before a run has completed.</summary>
    [ObservableProperty]
    private string? _outputDirectory;

    public IAsyncRelayCommand RunCommand { get; }

    public IRelayCommand CancelCommand { get; }

    /// <summary>The completed run's own files, each already verified to sit inside <see cref="OutputDirectory"/>.</summary>
    public ObservableCollection<HandoffFileViewModel> Files { get; } = [];

    /// <summary>Whether the completed Handoff has files that can be dragged or copied.</summary>
    public bool HasCompletedFiles => State == HandoffRunState.Completed && Files.Count > 0;

    /// <summary>Whether project and Selection controls should show disabled while a run is in flight.</summary>
    public bool IsActive => State is HandoffRunState.Running or HandoffRunState.Cancelling;

    /// <summary>
    /// Whether the progress bar must render indeterminate: the command's own stages do not all carry a
    /// total, and a missing <see cref="AssessmentProgress.Total"/> is never replaced with a fabricated one.
    /// </summary>
    public bool IsIndeterminate => Progress?.Total is null;

    /// <summary>The current stage's own completion fraction, meaningful only when <see cref="IsIndeterminate"/> is false.</summary>
    public double ProgressFraction =>
        Progress is { Total: { } total } && total > 0 ? (double)Progress.Completed / total : 0d;

    /// <summary>The current <see cref="Refusal"/>'s facts, rendered as one line per entry for an expandable details list.</summary>
    public IReadOnlyList<string> RefusalFacts =>
        Refusal is null ? [] : Refusal.Facts.Select(fact => $"{fact.Key}: {fact.Value}").ToList();

    partial void OnProjectPathChanged(string? value) => RunCommand.NotifyCanExecuteChanged();

    partial void OnStateChanged(HandoffRunState value)
    {
        RunCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasCompletedFiles));
    }

    void IProgress<AssessmentProgress>.Report(AssessmentProgress value) => Progress = value;

    /// <summary>Hands the exact path of one Handoff file to the drag adapter; never reads its bytes.</summary>
    public Task<DragDropEffects> DragFileAsync(PointerPressedEventArgs trigger, HandoffFileViewModel file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return _dragSource.StartDragAsync(trigger, [file.FullPath], DragDropEffects.Copy);
    }

    /// <summary>Hands every Handoff file's exact path to the drag adapter at once; never reads their bytes.</summary>
    public Task<DragDropEffects> DragAllFilesAsync(PointerPressedEventArgs trigger) =>
        _dragSource.StartDragAsync(trigger, Files.Select(file => file.FullPath).ToList(), DragDropEffects.Copy);

    private void OnSelectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectionViewModel.CanAssess)) RunCommand.NotifyCanExecuteChanged();
    }

    private bool CanRun() =>
        ProjectPath is not null && _selection.CanAssess
        && State is HandoffRunState.Idle or HandoffRunState.Completed or HandoffRunState.Cancelled
            or HandoffRunState.Refused;

    private bool CanCancel() => State == HandoffRunState.Running;

    private async Task RunAsync()
    {
        if (ProjectPath is null) return;

        // Backing out of the folder dialog leaves State untouched; no run exists yet for Cancel to unwind.
        var folder = await _folderPicker.PickFolderAsync();
        if (folder is null) return;

        var cts = new CancellationTokenSource();
        _cts = cts;
        Progress = null;
        Result = null;
        Refusal = null;
        Files.Clear();
        OutputDirectory = null;
        State = HandoffRunState.Running;

        try
        {
            var request = new HandoffRequest(ProjectPath, folder, _selection.BuildRequest(), WriteFlexTextXml, true);
            var outcome = await _commandClient.HandoffAsync(request, this, cts.Token);
            if (outcome.Succeeded)
            {
                Result = outcome.Value;
                OutputDirectory = outcome.Value!.OutputDirectory;
                PopulateFiles(outcome.Value!);
                State = HandoffRunState.Completed;
            }
            else
            {
                Refusal = outcome.Refusal;
                State = outcome.Refusal!.Code == CancelledRefusalCode
                    ? HandoffRunState.Cancelled
                    : HandoffRunState.Refused;
            }
        }
        finally
        {
            _cts = null;
            cts.Dispose();
        }
    }

    private void Cancel()
    {
        if (_cts is null) return;
        State = HandoffRunState.Cancelling;
        _cts.Cancel();
    }

    /// <summary>Cancels an in-flight run and awaits it, so nothing keeps running past this view model's lifetime.</summary>
    public async ValueTask DisposeAsync()
    {
        Cancel();
        if (RunCommand.ExecutionTask is { } running) await running.ConfigureAwait(false);
    }

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

    private static string ReadEmbeddedInstructions()
    {
        return ReadEmbeddedAsset(InstructionsResourceName);
    }

    private static string ReadEmbeddedAsset(string resourceName)
    {
        using var stream = typeof(HandoffCommand).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"The Handoff asset '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // Extracts one sentence instead of composing a new one, so wording never drifts from instructions.md.
}
