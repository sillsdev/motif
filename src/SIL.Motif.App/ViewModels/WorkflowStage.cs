using CommunityToolkit.Mvvm.ComponentModel;

namespace SIL.Motif.App.ViewModels;

/// <summary>The window's four stages, in the order a person moves through them.</summary>
public enum WorkflowStage
{
    /// <summary>Choosing a project and capturing its Baseline.</summary>
    Project,

    /// <summary>Choosing the Texts and words an Assessment will parse.</summary>
    Selection,

    /// <summary>Reading an Assessment's summary, grammar warnings, words, and statistics.</summary>
    Results,

    /// <summary>Writing the Handoff files and passing them to a chat model.</summary>
    Handoff,
}

/// <summary>One entry in the stage rail: a title, a one-line state, and whether the stage is finished.</summary>
public sealed partial class StageViewModel : ObservableObject
{
    public StageViewModel(WorkflowStage stage, string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Stage = stage;
        Title = title;
    }

    public WorkflowStage Stage { get; }

    public string Title { get; }

    /// <summary>The position shown in the stage's marker, counting from one.</summary>
    public int Number => (int)Stage + 1;

    /// <summary>The accessible name of the rail entry that opens this stage.</summary>
    public string AutomationName => $"{Title} stage";

    /// <summary>A short line under the title saying where this stage stands.</summary>
    [ObservableProperty]
    private string _summary = string.Empty;

    /// <summary>Whether this stage has what the next one needs.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsCheck))]
    private bool _isDone;

    /// <summary>Whether this is the stage the window is showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsCheck))]
    private bool _isCurrent;

    /// <summary>Whether the marker shows a check; the stage on screen keeps its number.</summary>
    public bool ShowsCheck => IsDone && !IsCurrent;
}
