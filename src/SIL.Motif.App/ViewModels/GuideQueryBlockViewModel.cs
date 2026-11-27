using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.GuideQueries;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Gdr;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Responses;

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
    private readonly string? _saveUnavailable;
    private readonly Func<string, string, CancellationToken, Task<GuideEvidenceResult>>? _evidence;
    private readonly Func<string, CancellationToken, Task<CommandOutcome<GdrRefreshTablesResponse>>>? _saveTable;

    internal GuideQueryBlockViewModel(string id, string title, string body, IReadOnlyList<GuideShowDirective> shows,
        GuideQueryContext context, Func<string?> projectPath,
        Func<GuideQueryRunRequest, CancellationToken, Task<CommandOutcome<FormsQueryResponse>>> run, Func<string, string, Task<string?>>? save,
        Action<string>? openWord, GuideSort? sort, string? saveUnavailable = null,
        Func<string, string, CancellationToken, Task<GuideEvidenceResult>>? evidence = null,
        Func<string, CancellationToken, Task<CommandOutcome<GdrRefreshTablesResponse>>>? saveTable = null)
    {
        _evidence = evidence;
        _saveTable = saveTable;
        _saveUnavailable = saveUnavailable;
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
        if (shows.Count == 0) EvidenceDisplays.Add(new GuideEvidenceDisplayViewModel(GuideShowKind.Table, null, null, sort));
        foreach (var show in shows) EvidenceDisplays.Add(new GuideEvidenceDisplayViewModel(show.Show, show.Limit, show.Columns, sort));
        IsEvidence = ParsedQuery(body, out _) is { } first && QueryRunLine.TryParseRun(first.Run, out var firstLine, out _) && firstLine.Corpus is not null;
        EditCommand = new RelayCommand(StartEditing, () => !IsEditing);
        CancelCommand = new RelayCommand(StopEditing, () => IsEditing);
        RunCommand = new AsyncRelayCommand(() => RunAsync(CancellationToken.None), () => !IsRunning);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => CanSave);
        SaveTableCommand = new AsyncRelayCommand(SaveTableAsync, () => !IsRunning);
    }

    /// <summary>The query's id, unique in its section.</summary>
    public string Id { get; }

    /// <summary>The query's title: what it groups, or its id when it has none.</summary>
    public string Title { get; }

    /// <summary>The block's body as the section stores it.</summary>
    public string Body { get; private set; }

    /// <summary>The displays of a v2 corpus query: one for each <c>motif-show</c>, or one table when none.</summary>
    public ObservableCollection<GuideEvidenceDisplayViewModel> EvidenceDisplays { get; } = [];

    /// <summary>Whether the run line is a v2 corpus-forms line; <see cref="EvidenceDisplays"/> shows it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRowsQuery), nameof(OffersSaveTable))]
    private bool _isEvidence;

    /// <summary>Whether the block's run line is a v1 query shown by <see cref="Displays"/>.</summary>
    public bool IsRowsQuery => !IsEvidence;

    /// <summary>The displays of the query: one for each <c>motif-show</c> naming it, or one table when none does.</summary>
    public ObservableCollection<GuideQueryDisplayViewModel> Displays { get; } = [];

    /// <summary>The text being edited.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave), nameof(SaveReason), nameof(HasSaveReason))]
    private string _draftText;

    /// <summary>Whether the query text is being edited.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReading), nameof(OffersSaveTable), nameof(ShowsQueryText), nameof(ShowsSave), nameof(CanSave), nameof(SaveReason), nameof(HasSaveReason))]
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

    /// <summary>Whether the block offers Save at all: a block shown where its text cannot be written offers none.</summary>
    public bool OffersSave => _save is not null;

    /// <summary>Whether the Save button is shown: while editing, in a block that offers Save.</summary>
    public bool ShowsSave => IsEditing && OffersSave;

    /// <summary>Whether the edited text differs from the stored text and parses as one query.</summary>
    public bool CanSave => IsEditing && _save is not null && DraftText != Body && DraftProblem is null;

    /// <summary>Why Save is not available while editing; empty when it is or when not editing.</summary>
    public string SaveReason => !IsEditing ? "" : _save is null ? _saveUnavailable ?? MotifAppStrings.GuideQuerySaveUnavailable
        : DraftProblem ?? (DraftText == Body ? MotifAppStrings.GuideQueryNothingToSave : "");

    /// <summary>Whether <see cref="SaveReason"/> says anything.</summary>
    public bool HasSaveReason => SaveReason.Length > 0;

    /// <summary>Opens the query text for editing.</summary>
    public IRelayCommand EditCommand { get; }

    /// <summary>Drops the edit and shows the stored text.</summary>
    public IRelayCommand CancelCommand { get; }

    /// <summary>Runs the query as written, edited or not, and shows its rows.</summary>
    public IAsyncRelayCommand RunCommand { get; }

    /// <summary>Whether the Save table button is shown: a v2 corpus query in a section where the Guide can be edited.</summary>
    public bool OffersSaveTable => IsEvidence && _saveTable is not null && !IsEditing;

    /// <summary>
    /// Stages the saved table of this block (a caption and at most ten rows from a live run) in the pending changes. It is the only
    /// action in the window that writes a table; showing the block never does.
    /// </summary>
    public IAsyncRelayCommand SaveTableCommand { get; }

    private async Task SaveTableAsync()
    {
        if (_saveTable is null) return;
        CommandOutcome<GdrRefreshTablesResponse> outcome;
        try { outcome = await _saveTable(Id, CancellationToken.None).ConfigureAwait(true); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Notice = string.Format(CultureInfo.CurrentCulture, MotifAppStrings.GuideQueryTableNotSavedFormat, exception.Message);
            return;
        }
        var table = outcome.Value?.Sections.SelectMany(section => section.Tables).FirstOrDefault(item => item.QueryId == Id);
        Notice = !outcome.Succeeded ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.GuideQueryTableNotSavedFormat, outcome.Refusal?.Message)
            : table is null ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.GuideQueryTableNotSavedFormat, MotifAppStrings.GuideQueryNoAnswer)
            : table.Outcome == "saved" ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.GuideQueryTableSavedFormat, table.RowsSaved, table.RowsInAll)
            : table.Outcome == "unchanged" ? MotifAppStrings.GuideQueryTableUnchanged
            : string.Format(CultureInfo.CurrentCulture, MotifAppStrings.GuideQueryTableNotSavedFormat, table.Message);
    }

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

    partial void OnIsRunningChanged(bool value)
    {
        RunCommand.NotifyCanExecuteChanged();
        SaveTableCommand.NotifyCanExecuteChanged();
    }

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
        var corpus = QueryRunLine.TryParseRun(query.Run, out var runLine, out _) && runLine.Corpus is not null;
        IsEvidence = corpus;
        if (corpus) { await RunEvidenceAsync(query, project, generation, cancellationToken).ConfigureAwait(true); return; }
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
                Fail(outcome.Refusal is { } refused ? Describe(refused) : MotifAppStrings.GuideQueryNoAnswer);
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

    // A v2 corpus line gives a table, a refusal with its code, or "parse index not built".
    private async Task RunEvidenceAsync(GuideQueryDefinition query, string project, int generation, CancellationToken cancellationToken)
    {
        if (_evidence is null) { Fail(MotifAppStrings.GuideQueryNoAnswer); return; }
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _running = cancellation;
        Notice = "";
        IsRunning = true;
        OnPropertyChanged(nameof(Status));
        try
        {
            var result = await _evidence(project, query.Run, cancellation.Token).ConfigureAwait(true);
            if (generation != _generation || cancellation.IsCancellationRequested) return;
            foreach (var display in EvidenceDisplays)
            {
                var shaped = result.Shaped(display.Columns ?? (query.Columns.Count > 0 ? query.Columns : null), display.Sort, display.Limit);
                if (shaped.Table is { } table) display.Show(table);
                else display.ShowMessage(shaped.Message);
            }
            if (result.Status != GuideEvidenceStatus.Ready)
            {
                Notice = result.Message;
                foreach (var display in EvidenceDisplays) display.ShowMessage(result.Message);
            }
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

    // An author-fixable line keeps the command's detail under the sentence; a Proposal state is the sentence alone.
    private static string Describe(Refusal refusal)
    {
        var shown = WindowRefusal.From(refusal, refusal.Message);
        return refusal.Code is RefusalCodes.QueryInvalid or RefusalCodes.GuideQueryRunInvalid or RefusalCodes.GuideQueryRunNotReadOnly
            && shown.Details is { Length: > 0 } details ? shown.Sentence + " " + details : shown.Sentence;
    }

    private void Fail(string message)
    {
        IsRunning = false;
        OnPropertyChanged(nameof(Status));
        Notice = string.Format(CultureInfo.CurrentCulture, MotifAppStrings.GuideQueryRefusedFormat, message);
        foreach (var display in Displays) display.Show(null);
        foreach (var display in EvidenceDisplays) display.Clear();
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
