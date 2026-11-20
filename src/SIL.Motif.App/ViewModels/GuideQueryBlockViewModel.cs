using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.GuideQueries;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Gdr;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// One <c>motif-query</c> block of a Guide section: its title, its displays, and the Edit, Run and Save of its text. A run
/// shows its rows in the displays and is dropped with them; only the query text is ever saved.
/// </summary>
public sealed partial class GuideQueryBlockViewModel : ObservableObject
{
    private readonly GuideQueryContext _context;
    private readonly Func<string?> _projectPath;
    private readonly Func<GuideQueryRunRequest, CancellationToken, Task<CommandOutcome<FormsQueryResponse>>> _run;
    private readonly Func<string, string, Task<string?>>? _save;
    private CancellationTokenSource? _running;
    private int _generation;

    internal GuideQueryBlockViewModel(string id, string title, string body, IReadOnlyList<GuideShowDirective> shows,
        GuideQueryContext context, Func<string?> projectPath,
        Func<GuideQueryRunRequest, CancellationToken, Task<CommandOutcome<FormsQueryResponse>>> run, Func<string, string, Task<string?>>? save,
        Action<string>? openWord, GuideSort? sort)
    {
        Id = id;
        Title = title.Length > 0 ? title : id;
        Body = body;
        _draftText = body;
        _context = context;
        _projectPath = projectPath;
        _run = run;
        _save = save;
        if (shows.Count == 0) Displays.Add(Display(new GuideQueryDisplayViewModel(GuideShowKind.Table, null, context.BothSides, openWord), sort));
        foreach (var show in shows)
            Displays.Add(Display(new GuideQueryDisplayViewModel(show.Show, show.Limit, context.BothSides, openWord), sort));
        EditCommand = new RelayCommand(StartEditing, () => !IsEditing);
        CancelCommand = new RelayCommand(StopEditing, () => IsEditing);
        RunCommand = new AsyncRelayCommand(() => RunAsync(CancellationToken.None), () => !IsRunning);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => CanSave);
    }

    /// <summary>The query's id, unique in its section.</summary>
    public string Id { get; }

    /// <summary>The query's title: what it groups, or its id when it has none.</summary>
    public string Title { get; }

    /// <summary>The block's body as the section stores it.</summary>
    public string Body { get; private set; }

    /// <summary>The displays of the query: one for each <c>motif-show</c> naming it, or one table when none does.</summary>
    public ObservableCollection<GuideQueryDisplayViewModel> Displays { get; } = [];

    /// <summary>The text being edited.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave), nameof(SaveReason), nameof(HasSaveReason))]
    private string _draftText;

    /// <summary>Whether the query text is being edited.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReading), nameof(ShowsQueryText), nameof(CanSave), nameof(SaveReason), nameof(HasSaveReason))]
    private bool _isEditing;

    /// <summary>Whether a run is under way.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private bool _isRunning;

    /// <summary>Why the query did not run, or what saving did; empty when there is nothing to say.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice), nameof(ShowsQueryText))]
    private string _notice = "";

    /// <summary>Whether the query text is shown read-only so a notice about the run can be understood without editing.</summary>
    public bool ShowsQueryText => IsReading && HasNotice;

    /// <summary>Whether the block is shown for reading rather than editing.</summary>
    public bool IsReading => !IsEditing;

    /// <summary>Whether <see cref="Notice"/> says anything.</summary>
    public bool HasNotice => Notice.Length > 0;

    /// <summary>The line saying a run is under way.</summary>
    public string Status => IsRunning ? MotifAppStrings.GuideQueryRunning : "";

    /// <summary>Whether <see cref="Status"/> says anything.</summary>
    public bool HasStatus => IsRunning;

    /// <summary>Whether the edited text differs from the stored text and parses as one query.</summary>
    public bool CanSave => IsEditing && _save is not null && DraftText != Body && DraftProblem is null;

    /// <summary>Why Save is not available while editing; empty when it is or when not editing.</summary>
    public string SaveReason => !IsEditing ? "" : _save is null ? MotifAppStrings.GuideQuerySaveUnavailable
        : DraftProblem ?? (DraftText == Body ? MotifAppStrings.GuideQueryNothingToSave : "");

    /// <summary>Whether <see cref="SaveReason"/> says anything.</summary>
    public bool HasSaveReason => SaveReason.Length > 0;

    /// <summary>Opens the query text for editing.</summary>
    public IRelayCommand EditCommand { get; }

    /// <summary>Drops the edit and shows the stored text.</summary>
    public IRelayCommand CancelCommand { get; }

    /// <summary>Runs the query as written, edited or not, and shows its rows.</summary>
    public IAsyncRelayCommand RunCommand { get; }

    /// <summary>Saves the edited query text to the pending changes; no rows are saved.</summary>
    public IAsyncRelayCommand SaveCommand { get; }

    private string? DraftProblem => Parse(DraftText).Problem;

    partial void OnDraftTextChanged(string value) => SaveCommand.NotifyCanExecuteChanged();

    partial void OnIsEditingChanged(bool value)
    {
        EditCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsRunningChanged(bool value) => RunCommand.NotifyCanExecuteChanged();

    private void StartEditing()
    {
        DraftText = Body;
        Notice = "";
        IsEditing = true;
    }

    private void StopEditing()
    {
        IsEditing = false;
        DraftText = Body;
        Notice = "";
    }

    private static GuideQueryDefinition? ParsedQuery(string body, out string? problem)
    {
        var blocks = GuideQueryBlocks.Parse("```" + GdrTemplate.QueryBlockTag + "\n" + body + "\n```");
        problem = null;
        if (blocks.Diagnostics.Count > 0) problem = string.Join(" ", blocks.Diagnostics.Select(item => item.Message));
        if (blocks.Queries.Count != 1 && problem is null) problem = MotifAppStrings.GuideQueryOneQuery;
        return blocks.Queries.Count == 1 ? blocks.Queries[0] : null;
    }

    private (GuideQueryDefinition? Query, string? Problem) Parse(string body)
    {
        var query = ParsedQuery(body, out var problem);
        return (query, problem);
    }

    /// <summary>Runs the query text shown (the draft while editing) and puts its rows in the displays.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _running?.Cancel();
        var generation = ++_generation;
        var (query, problem) = Parse(IsEditing ? DraftText : Body);
        if (problem is not null || query is null) { Fail(problem ?? MotifAppStrings.GuideQueryOneQuery); return; }
        var (line, refusal) = GuideQueryRunLines.Prepare(query.Run, _context);
        if (line is null) { Fail(refusal!); return; }
        if (_projectPath() is not { } project) { Fail(MotifAppStrings.GuideNoProject); return; }
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _running = cancellation;
        Notice = "";
        IsRunning = true;
        OnPropertyChanged(nameof(Status));
        try
        {
            var outcome = await _run(new GuideQueryRunRequest(project, line), cancellation.Token).ConfigureAwait(true);
            if (generation != _generation || cancellation.IsCancellationRequested) return;
            if (!outcome.Succeeded || outcome.Value is not { } response)
            {
                Fail(outcome.Refusal?.Message ?? MotifAppStrings.GuideQueryNoAnswer);
                return;
            }
            var (run, unreadable) = GuideQueryRun.From(response);
            if (run is null) { Fail(unreadable ?? MotifAppStrings.GuideQueryNoAnswer); return; }
            foreach (var display in Displays) display.Show(run);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            if (generation == _generation)
            {
                IsRunning = false;
                OnPropertyChanged(nameof(Status));
            }
            if (ReferenceEquals(_running, cancellation)) _running = null;
            cancellation.Dispose();
        }
    }

    /// <summary>Cancel drops the answer when it arrives; it does not interrupt the command.</summary>
    public void Stop()
    {
        _generation++;
        _running?.Cancel();
        IsRunning = false;
        OnPropertyChanged(nameof(Status));
    }

    private void Fail(string message)
    {
        IsRunning = false;
        OnPropertyChanged(nameof(Status));
        Notice = string.Format(CultureInfo.CurrentCulture, MotifAppStrings.GuideQueryRefusedFormat, message);
        foreach (var display in Displays) display.Show(null);
    }

    private async Task SaveAsync()
    {
        if (!CanSave || _save is null) return;
        var body = DraftText;
        string? refusal;
        try { refusal = await _save(Id, body).ConfigureAwait(true); }
        catch (Exception exception) when (exception is not OperationCanceledException) { refusal = exception.Message; }
        if (refusal is not null)
        {
            Notice = string.Format(CultureInfo.CurrentCulture, MotifAppStrings.GuideQuerySaveFailedFormat, refusal);
            return;
        }
        Body = body;
        IsEditing = false;
        OnPropertyChanged(nameof(Body));
        await RunAsync(CancellationToken.None).ConfigureAwait(true);
        Notice = Notice.Length == 0 ? MotifAppStrings.GuideQuerySaved : MotifAppStrings.GuideQuerySaved + " " + Notice;
    }

    private static GuideQueryDisplayViewModel Display(GuideQueryDisplayViewModel display, GuideSort? sort)
    {
        display.SortBy(sort);
        return display;
    }
}
