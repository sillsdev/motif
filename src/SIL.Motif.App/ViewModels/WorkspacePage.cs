using CommunityToolkit.Mvvm.ComponentModel;

namespace SIL.Motif.App.ViewModels;

/// <summary>The window's five stages, in the order a person moves through them.</summary>
public enum WorkflowStage
{
    /// <summary>Choosing a project and capturing its Baseline.</summary>
    Project,

    /// <summary>The grammar's own findings, which do not depend on which Texts are chosen.</summary>
    Grammar,

    /// <summary>Choosing the Texts and words an Assessment will parse.</summary>
    Texts,

    /// <summary>Reading an Assessment's words and statistics.</summary>
    Results,

    /// <summary>Writing the Handoff files and passing them to a chat model.</summary>
    Handoff,
}

/// <summary>The three views of a finished Assessment that share the Results stage.</summary>
public enum ResultsView
{
    /// <summary>Every word in the matrix of what the project held against what the parser did, and the list it filters.</summary>
    Compare,

    /// <summary>What changed since the run before: the words that moved between cells of the Compare matrix.</summary>
    Difference,

    /// <summary>One row per word the parser was asked about.</summary>
    Words,

    /// <summary>The chosen Texts read in place, each occurrence compared with what the project stores there.</summary>
    InText,

    /// <summary>The grammar's own parts, counted and timed.</summary>
    Statistics,
}

/// <summary>One entry in the stage stepper: a title, a one-line state, and whether the stage is finished.</summary>
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

    /// <summary>The stage bar column this entry sits in, counting from zero.</summary>
    public int ColumnIndex => (int)Stage;

    /// <summary>The accessible name of the stepper entry that opens this stage.</summary>
    public string AutomationName => $"{Title} stage";

    /// <summary>A short line saying where this stage stands, shown when the entry is hovered.</summary>
    [ObservableProperty]
    private string _summary = string.Empty;

    /// <summary>A short count beside the title, or empty when the stage has none worth showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBadge))]
    private string _badge = string.Empty;

    /// <summary>Whether <see cref="Badge"/> has anything to show.</summary>
    public bool HasBadge => Badge.Length > 0;

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
