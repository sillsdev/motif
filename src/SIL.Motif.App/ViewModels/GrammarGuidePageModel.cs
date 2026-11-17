using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Preview;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Gdr;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// Reads the project's Grammar Design Records from its Baseline as the Grammar Guide: parts and sections from the
/// records' own hierarchy, a section's headings and the grammar objects its text links to from its stored text. Edits go
/// through the <see cref="IGdrEditStore"/> the editor is given; this model never writes the project.
/// </summary>
public sealed partial class GrammarGuidePageModel : PageModel
{
    private int _generation;
    private CancellationTokenSource? _loadCancellation;
    private readonly List<Task> _loadTasks = [];
    private CancellationTokenSource? _showCancellation;
    private readonly List<Task> _showTasks = [];
    private IGdrEditStore? _editStore;
    private string? _editStoreProject;
    private Func<string, string>? _writingSystemName;
    private bool _reloading;

    public GrammarGuidePageModel(WorkspaceContext context) : base(context)
    {
        OpenInFieldWorksCommand = new AsyncRelayCommand<Uri>(OpenInFieldWorksAsync,
            uri => uri is not null && Context.UriLauncher is not null);
    }

    /// <summary>The records in their stored order.</summary>
    public ObservableCollection<GdrSummary> Records { get; } = [];

    /// <summary>The parts at the top of the project's list, each with its sections.</summary>
    public ObservableCollection<GrammarGuideSection> Parts { get; } = [];

    /// <summary>The record selected for reading.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedSection))]
    [NotifyPropertyChangedFor(nameof(SelectedTitle))]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private GdrSummary? _selectedRecord;

    /// <summary>The selected record and its discussion, including writing-system information.</summary>
    [ObservableProperty]
    private GdrShowResponse? _shownRecord;

    /// <summary>The outline entry of <see cref="SelectedRecord"/>, which the outline selects through.</summary>
    public GrammarGuideSection? SelectedSection
    {
        get => SelectedRecord is { } record ? Parts.SelectMany(part => part.Flatten()).FirstOrDefault(entry => entry.Id == record.Id) : null;
        set
        {
            if (value is null && SelectedRecord is not null && Parts.Count > 0) return;
            SelectedRecord = value?.Record;
        }
    }

    /// <summary>The selected section's number and title, or empty when none is selected.</summary>
    public string SelectedTitle => SelectedSection?.Label ?? SelectedRecord?.Title ?? "";

    /// <summary>Whether a section is selected.</summary>
    public bool HasSelection => SelectedRecord is not null;

    /// <summary>The headings in the selected section's stored text.</summary>
    public IReadOnlyList<GuideHeading> Headings => GrammarGuideSection.HeadingsOf(ShownRecord);

    /// <summary>Whether the selected section's text has headings to list.</summary>
    public bool HasHeadings => Headings.Count > 0;

    /// <summary>The grammar objects the selected section's stored text links to: names and links only.</summary>
    public IReadOnlyList<GuideGrammarName> GrammarNames => GuideGrammarName.From(ShownRecord);

    /// <summary>Whether the selected section's text links to any grammar object.</summary>
    public bool HasGrammarNames => GrammarNames.Count > 0;

    /// <summary>The generated examples block, hidden because no source supplies it yet.</summary>
    public GrammarGuideExamplesViewModel Examples { get; } = new();

    /// <summary>Whether the selected section was read from a Baseline the live project has been saved after.</summary>
    public bool IsShownStale => ShownRecord?.IsStale == true;

    /// <summary>Opens a grammar object's link in FieldWorks.</summary>
    public IAsyncRelayCommand<Uri> OpenInFieldWorksCommand { get; }

    /// <summary>Why something could not be read; empty when nothing is wrong.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string _notice = "";

    /// <summary>Whether <see cref="Notice"/> says anything.</summary>
    public bool HasNotice => Notice.Length > 0;

    /// <summary>Whether the list still has its old name, so the Guide is read-only.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(ListNotice))]
    [NotifyPropertyChangedFor(nameof(HasListNotice))]
    private bool _usesOldListName;

    /// <summary>Whether a section's text may be edited here; false while the list has its old name.</summary>
    public bool CanEdit => !UsesOldListName;

    /// <summary>The sentence saying the list needs renaming; empty when it does not.</summary>
    public string ListNotice => UsesOldListName ? MotifAppStrings.GuideListOldName : "";

    /// <summary>Whether <see cref="ListNotice"/> says anything.</summary>
    public bool HasListNotice => UsesOldListName;

    /// <summary>What the outline says when it is empty; empty when there are records.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEmptyText))]
    private string _emptyText = MotifAppStrings.GuideNoProject;

    /// <summary>Whether <see cref="EmptyText"/> says anything.</summary>
    public bool HasEmptyText => EmptyText.Length > 0;

    /// <summary>Raised after the record list was replaced from a newer Baseline, with the records now listed.</summary>
    public event EventHandler? RecordsReloaded;

    /// <summary>Raised when the project was closed or switched, so the editor drops everything it held.</summary>
    public event EventHandler? ProjectWasCleared;

    /// <summary>The FieldWorks project a new link names.</summary>
    public string? FieldWorksProjectName => Context.ProjectPath is { } path
        ? Path.GetFileNameWithoutExtension(path.Replace('\\', Path.DirectorySeparatorChar)) : null;

    /// <summary>The store the editor stages through, or null without an open project.</summary>
    public IGdrEditStore? EditStore
    {
        get
        {
            if (Context.ProjectPath is not { } project) return null;
            if (_editStore is null || !string.Equals(_editStoreProject, project, StringComparison.Ordinal))
            {
                _editStore = Context.Commands.OpenGrammarGuideEditStore(project);
                _editStoreProject = project;
            }
            return _editStore;
        }
    }

    /// <summary>A writing system's name by its tag, as the project names it; the tag itself when the project does not.</summary>
    public string WritingSystemName(string tag)
    {
        if (Context.ProjectPath is not { } project) return tag;
        try { _writingSystemName ??= WritingSystemNames.For(project); }
        catch (Exception exception) when (exception is not OperationCanceledException) { return tag; }
        return _writingSystemName(tag);
    }

    /// <summary>Reads one record as the Baseline holds it now; null when the project is closed or the read is refused.</summary>
    public async Task<GdrShowResponse?> ReadRecordAsync(string id)
    {
        if (Context.ProjectPath is not { } project) return null;
        var result = await Context.Commands.ShowGrammarGuideAsync(new GdrShowRequest(project, id), CancellationToken.None)
            .ConfigureAwait(true);
        return Context.ProjectPath == project && result.Succeeded ? result.Value : null;
    }

    /// <summary>Finds the object at a FieldWorks Markdown path, in <paramref name="baseline"/> when the section was read from it.</summary>
    public async Task<CommandOutcome<GdrPathLink>> LinkToPathAsync(string path, BaselineToken? baseline)
    {
        if (Context.ProjectPath is not { } project)
            return CommandOutcome<GdrPathLink>.Refused(new Refusal("guide.no-project", FailureReason.NotFound, MotifAppStrings.GuideNoProject));
        return await Context.Commands.LinkGrammarGuidePathAsync(new GdrLinkToPathRequest(project, path, baseline), CancellationToken.None)
            .ConfigureAwait(true);
    }

    /// <summary>Selects the record <paramref name="id"/>, as when the editor kept another one shown.</summary>
    public void Select(string? id) =>
        SelectedRecord = id is null ? null : Records.FirstOrDefault(record => record.Id == id) ?? SelectedRecord;

    partial void OnShownRecordChanged(GdrShowResponse? value)
    {
        OnPropertyChanged(nameof(Headings));
        OnPropertyChanged(nameof(HasHeadings));
        OnPropertyChanged(nameof(GrammarNames));
        OnPropertyChanged(nameof(HasGrammarNames));
        OnPropertyChanged(nameof(IsShownStale));
        // No reviewed query joins approved occurrences to this section yet, so there is nothing to supply.
        Examples.Clear();
    }

    partial void OnSelectedRecordChanged(GdrSummary? value)
    {
        if (_reloading) return;
        StartShow(value);
    }

    private void StartShow(GdrSummary? value)
    {
        _showCancellation?.Cancel();
        _showCancellation = null;
        ShownRecord = null;
        Notice = "";
        var generation = ++_generation;
        if (value is null || Context.ProjectPath is not { } project) return;
        var cancellation = new CancellationTokenSource();
        _showCancellation = cancellation;
        _showTasks.RemoveAll(task => task.IsCompletedSuccessfully);
        _showTasks.Add(ShowAsync(project, value.Id, generation, cancellation));
    }

    protected override void OnProjectCleared()
    {
        _generation++;
        _loadCancellation?.Cancel();
        _showCancellation?.Cancel();
        _editStore = null;
        _editStoreProject = null;
        _writingSystemName = null;
        SelectedRecord = null;
        Parts.Clear();
        Records.Clear();
        ShownRecord = null;
        Notice = "";
        UsesOldListName = false;
        EmptyText = MotifAppStrings.GuideNoProject;
        ProjectWasCleared?.Invoke(this, EventArgs.Empty);
    }

    protected override Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken) =>
        LoadAsync(projectPath, cancellationToken);

    protected override Task OnBaselineCapturedAsync(CancellationToken cancellationToken) =>
        Context.ProjectPath is { } project ? LoadAsync(project, cancellationToken) : Task.CompletedTask;

    protected override async Task OnStopWorkAsync()
    {
        _generation++;
        _loadCancellation?.Cancel();
        _showCancellation?.Cancel();
        await Task.WhenAll(_loadTasks.Concat(_showTasks)).ConfigureAwait(true);
        _loadTasks.Clear();
        _showTasks.Clear();
    }

    private Task LoadAsync(string project, CancellationToken cancellationToken)
    {
        _loadCancellation?.Cancel();
        _showCancellation?.Cancel();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loadCancellation = cancellation;
        var generation = ++_generation;
        _loadTasks.RemoveAll(task => task.IsCompletedSuccessfully);
        var read = ReadListAsync(project, generation, cancellation);
        _loadTasks.Add(read);
        return read;
    }

    private async Task ReadListAsync(string project, int generation, CancellationTokenSource cancellation)
    {
        try
        {
            var token = cancellation.Token;
            var result = await Context.Commands.ListGrammarGuideAsync(new GdrListRequest(project), token)
                .ConfigureAwait(true);
            if (generation != _generation || token.IsCancellationRequested || Context.ProjectPath != project) return;
            // The open section stays open while it is listed, so typing survives a newer Baseline.
            var keptId = SelectedRecord?.Id;
            _reloading = true;
            try
            {
                Records.Clear();
                Parts.Clear();
                SelectedRecord = null;
                if (!result.Succeeded || result.Value is not { } value)
                {
                    ShownRecord = null;
                    UsesOldListName = false;
                    Notice = result.Refusal is { Code: RefusalCodes.GdrAmbiguous } ? MotifAppStrings.GuideListAmbiguous
                        : result.Refusal is { } refusal
                        ? string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.GuideReadFailedFormat, refusal.Message)
                        : "";
                    // The ambiguity sentence says why there is no Guide; "no sections yet" would contradict it.
                    EmptyText = result.Refusal is { Code: RefusalCodes.GdrAmbiguous } ? "" : MotifAppStrings.GuideNoRecords;
                    return;
                }
                Notice = "";
                UsesOldListName = value.UsesOldListName;
                foreach (var record in value.Records) Records.Add(record);
                foreach (var part in GrammarGuideSection.Outline(value.Records)) Parts.Add(part);
                EmptyText = !value.HasBaseline ? MotifAppStrings.GuideNoBaseline :
                    value.Records.Count == 0 ? MotifAppStrings.GuideNoRecords : "";
                SelectedRecord = keptId is null ? null : Records.FirstOrDefault(record => record.Id == keptId);
            }
            finally { _reloading = false; }
            RecordsReloaded?.Invoke(this, EventArgs.Empty);
            if (SelectedRecord is { } kept) StartShow(kept);
            else ShownRecord = null;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_loadCancellation, cancellation)) _loadCancellation = null;
            cancellation.Dispose();
        }
    }

    private async Task ShowAsync(string project, string id, int generation, CancellationTokenSource cancellation)
    {
        try
        {
            var token = cancellation.Token;
            var result = await Context.Commands.ShowGrammarGuideAsync(new GdrShowRequest(project, id), token)
                .ConfigureAwait(true);
            if (generation != _generation || token.IsCancellationRequested || Context.ProjectPath != project) return;
            if (result.Succeeded) ShownRecord = result.Value;
            else Notice = string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.GuideReadFailedFormat,
                result.Refusal?.Message);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_showCancellation, cancellation)) _showCancellation = null;
            cancellation.Dispose();
        }
    }

    private async Task OpenInFieldWorksAsync(Uri? uri)
    {
        if (uri is null || Context.UriLauncher is not { } launcher) return;
        try
        {
            if (!await launcher.LaunchAsync(uri).ConfigureAwait(true)) Notice = MotifAppStrings.GuideOpenFailed;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Notice = MotifAppStrings.GuideOpenFailed;
        }
    }
}
