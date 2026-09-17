using System.Collections.ObjectModel;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>One repository document linked from the completed Handoff.</summary>
public sealed record ReferenceDocument(string DisplayPath, Uri Url, string AccessibleName);

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
    private readonly IHandoffFolderPicker _folderPicker;
    private readonly IFileDragSource _dragSource;
    private string? _pendingFolder;

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
        _folderPicker = folderPicker;
        _dragSource = dragSource;
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

    /// <summary>The completed Assessment whose retained result this Handoff publishes.</summary>
    [ObservableProperty]
    private string? _invocationId;

    [ObservableProperty]
    private bool _writeFlexTextXml;

    /// <summary>Where the completed run wrote the folder, or <c>null</c> before a run has completed.</summary>
    [ObservableProperty]
    private string? _outputDirectory;

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

    protected override bool CanStartCore() => ProjectPath is not null && !string.IsNullOrWhiteSpace(InvocationId);

    protected override async Task<bool> PrepareRunAsync()
    {
        _pendingFolder = await _folderPicker.PickFolderAsync();
        return _pendingFolder is not null;
    }

    protected override void OnRunStarting()
    {
        Files.Clear();
        OutputDirectory = null;
    }

    protected override Task<CommandOutcome<HandoffCommandResponse>> ExecuteCoreAsync(
        CancellationToken cancellationToken)
    {
        var request = new HandoffRequest(
            ProjectPath!, _pendingFolder!, new SelectionRequest(false, [], [], false, null),
            WriteFlexTextXml, true, InvocationId);
        return _commandClient.HandoffAsync(request, this, cancellationToken);
    }

    protected override void OnRunSucceeded(HandoffCommandResponse response)
    {
        OutputDirectory = response.OutputDirectory;
        PopulateFiles(response);
        _pendingFolder = null;
    }

    protected override void OnReset()
    {
        InvocationId = null;
        _pendingFolder = null;
        Files.Clear();
        OutputDirectory = null;
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
