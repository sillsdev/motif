using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>Separates stored Source Context, machine-local navigation and FieldWorks Text selection.</summary>
public sealed partial class SourcesViewModel : ObservableObject
{
    private readonly ICommandClient _commands;
    private readonly Func<CancellationToken, Task>? _reloadChanges;
    private readonly Func<bool>? _canStage;
    private readonly List<Task> _tasks = [];
    private CancellationTokenSource? _readCancellation;
    private CancellationTokenSource? _detailCancellation;
    private int _generation;
    private string? _project;
    private bool _listStale;
    private BaselineToken? _baseline;
    private bool _unavailablePassage;
    private PassageChoiceViewModel _navigationChoice = new(null);
    private readonly List<CancellationTokenSource> _cancellations = [];

    public SourcesViewModel(ICommandClient commands, SelectionViewModel selection,
        Func<CancellationToken, Task>? reloadChanges = null, Func<bool>? canStage = null,
        Func<CancellationToken, Task>? usePending = null)
    {
        _commands = commands;
        Selection = selection;
        _reloadChanges = reloadChanges;
        _canStage = canStage;
        UsePendingProposalCommand = new AsyncRelayCommand(() => Track(UsePendingAsync(usePending!)),
            () => _project is not null && !IsBusy && !CanStageSource && usePending is not null);
        ShowFieldWorksCommand = new RelayCommand(() => Navigate(new(null)));
        ReloadCommand = new AsyncRelayCommand(() => LoadAsync(_project!), () => _project is not null && !IsBusy);
        SetFolderCommand = new AsyncRelayCommand(() => WriteAsync(token => _commands.SetSourceFolderAsync(
            new(_project!, SelectedSource!.Id, FolderText), token), false), CanSetFolder);
        SetCurrentCommand = new AsyncRelayCommand(() => WriteAsync(token => _commands.SetSourceCurrentAsync(
            new(_project!, SelectedSource!.Id, CurrentText), token), false), CanUseSource);
        ClearCurrentCommand = new AsyncRelayCommand(() => WriteAsync(token => _commands.ClearSourceCurrentAsync(
            new(_project!, SelectedSource!.Id), token), false), CanUseSource);
        StageContextCommand = new AsyncRelayCommand(() => WriteAsync(token => _commands.StageSourceContextAsync(
            new(_project!, SelectedSource!.Id, ContextText), token), true), () => CanUseSource() && CanStageSource);
        AddParatextCommand = new AsyncRelayCommand(() => WriteAsync(token => _commands.StageSourceAddAsync(
            new(_project!, NewId, NewName, NewVersification, NewContext, Folder: string.IsNullOrWhiteSpace(NewFolder) ? null : NewFolder),
            token), true), () => _project is not null && !IsBusy && !_listStale && CanStageSource);
    }

    public SelectionViewModel Selection { get; }
    public ObservableCollection<SourceRowViewModel> Sources { get; } = [];
    public Task Loading { get; private set; } = Task.CompletedTask;
    public IRelayCommand ShowFieldWorksCommand { get; }
    public IAsyncRelayCommand ReloadCommand { get; }
    public IAsyncRelayCommand UsePendingProposalCommand { get; }
    public bool CanStageSource => _canStage?.Invoke() ?? true;

    public void RefreshStagingTarget()
    {
        OnPropertyChanged(nameof(CanStageSource));
        NotifyCommands();
    }
    public IAsyncRelayCommand SetFolderCommand { get; }
    public IAsyncRelayCommand SetCurrentCommand { get; }
    public IAsyncRelayCommand ClearCurrentCommand { get; }
    public IAsyncRelayCommand StageContextCommand { get; }
    public IAsyncRelayCommand AddParatextCommand { get; }

    [ObservableProperty] private SourceRowViewModel? _selectedSource;
    [ObservableProperty] private SourceShowResponse? _detail;
    [ObservableProperty] private string _folderText = string.Empty;
    [ObservableProperty] private string _contextText = string.Empty;
    [ObservableProperty] private string _currentText = string.Empty;
    [ObservableProperty] private string? _notice;
    [ObservableProperty] private WindowRefusal? _shownRefusal;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _newId = string.Empty;
    [ObservableProperty] private string _newName = string.Empty;
    [ObservableProperty] private string _newVersification = string.Empty;
    [ObservableProperty] private string _newContext = string.Empty;
    [ObservableProperty] private string _newFolder = string.Empty;

    public bool IsFieldWorks => SelectedSource is null && !_unavailablePassage;
    [ObservableProperty] private string? _passageReference;

    public bool HasSource => SelectedSource is not null;
    public bool HasDetail => Detail is not null;
    public bool CanReadPassage => IsFieldWorks;
    public string PassageTitle => SelectedSource?.Name ?? (_unavailablePassage ? MotifAppStrings.SourcesUnavailable : MotifAppStrings.SourcesFieldWorksTexts);
    public string PassageMessage => IsFieldWorks ? string.Empty : _navigationChoice.SourceId is null
        ? MotifAppStrings.SourcesPassageUnavailable : MotifAppStrings.SourcesExternalPassageUnavailable;
    public string ContextSummary => Detail?.Source.Context ?? string.Empty;
    public string DetailProblem => Detail?.Source.Error ?? (Detail?.Source.Status == "Removed" ? MotifAppStrings.SourcesRemoved : string.Empty);

    partial void OnSelectedSourceChanged(SourceRowViewModel? value)
    {
        InvalidateDetail();
        _unavailablePassage = false;
        PassageReference = null;
        _navigationChoice = new(value?.Id);
        Detail = null;
        ShownRefusal = null;
        Notice = _listStale ? MotifAppStrings.SourcesOutdated : null;
        FolderText = ContextText = CurrentText = string.Empty;
        NotifyNavigation();
        if (value is null || _project is null) return;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_readCancellation?.Token ?? CancellationToken.None);
        _cancellations.Add(cancellation);
        _detailCancellation = cancellation;
        Loading = Track(ReadDetailAsync(_project, value.Id, _generation, cancellation));
    }

    partial void OnDetailChanged(SourceShowResponse? value)
    {
        OnPropertyChanged(nameof(HasDetail));
        OnPropertyChanged(nameof(ContextSummary));
        OnPropertyChanged(nameof(DetailProblem));
        NotifyCommands();
    }
    partial void OnIsBusyChanged(bool value) => NotifyCommands();
    partial void OnFolderTextChanged(string value) => NotifyCommands();

    /// <summary>Selects an exact Source identity; an unknown Source cannot fall back to unrelated Texts.</summary>
    public void Navigate(PassageChoiceViewModel choice)
    {
        var selected = choice.SourceId is { } id
            ? Sources.FirstOrDefault(row => row.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) : null;
        if (selected is not null && ReferenceEquals(selected, SelectedSource)) OnSelectedSourceChanged(selected);
        else SelectedSource = selected;
        _unavailablePassage = choice.SourceId is not null && SelectedSource is null;
        _navigationChoice = choice;
        PassageReference = choice.Passage;
        if (_unavailablePassage) Notice = MotifAppStrings.SourcesNoLongerAvailable;
        else if (choice.SourceId is null) Notice = null;
        NotifyNavigation();
        if (choice.Passage is { } passage && SelectedSource is { } source && _detailCancellation is { } cancellation &&
            _project is { } project)
            Loading = Track(ValidatePassageAsync(Loading, project, source.Id, passage, _generation, cancellation));
    }

    internal void RejectFieldWorksPassage()
    {
        _unavailablePassage = true;
        Notice = MotifAppStrings.SourcesPassageUnavailable;
        NotifyNavigation();
    }

    private async Task ValidatePassageAsync(Task detail, string project, string source, string passage, int generation,
        CancellationTokenSource cancellation)
    {
        try
        {
            await detail.ConfigureAwait(true);
            if (!IsCurrent(project, generation, cancellation) || Detail is null) return;
            var result = await _commands.WhereSourceAsync(new(project, passage, source, _baseline), cancellation.Token)
                .ConfigureAwait(true);
            if (!IsCurrent(project, generation, cancellation)) return;
            if (!result.Succeeded)
            {
                PassageReference = null;
                if (result.Refusal!.Code == RefusalCodes.SourceRefInvalid)
                    ShownRefusal = SourceRefusal(result.Refusal);
                else FailMetadataRead(result.Refusal);
                return;
            }
            if (result.Value!.IsStale)
            {
                _listStale = true;
                Notice = MotifAppStrings.SourcesOutdated;
                NotifyCommands();
                return;
            }
            var place = result.Value.Places.SingleOrDefault(row => row.Source.Equals(source, StringComparison.OrdinalIgnoreCase));
            if (place is not { Place: not null, Error: null })
            {
                PassageReference = null;
                Notice = MotifAppStrings.SourcesChapterUnavailable;
                return;
            }
            PassageReference = result.Value.Ref;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }

    public Task LoadAsync(string project, CancellationToken cancellationToken = default)
    {
        var previous = _project == project ? _navigationChoice : null;
        Clear();
        _project = project;
        _listStale = true;
        if (previous?.SourceId is not null)
        {
            _navigationChoice = previous;
            _unavailablePassage = true;
            PassageReference = previous.Passage;
        }
        NotifyNavigation();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellations.Add(cancellation);
        _readCancellation = cancellation;
        return Track(ReadListAsync(project, _generation, cancellation));
    }

    public void Clear()
    {
        _readCancellation?.Cancel();
        _readCancellation = null;
        InvalidateDetail();
        _project = null;
        SelectedSource = null;
        Detail = null;
        Sources.Clear();
        _listStale = false;
        _baseline = null;
        _unavailablePassage = false;
        _navigationChoice = new(null);
        PassageReference = null;
        IsBusy = false;
        Notice = null;
        ShownRefusal = null;
        NewId = NewName = NewVersification = NewContext = NewFolder = string.Empty;
        NotifyNavigation();
    }

    public async Task StopAsync()
    {
        _readCancellation?.Cancel();
        InvalidateDetail();
        await Task.WhenAll(_tasks.ToArray()).ConfigureAwait(true);
        _tasks.Clear();
        foreach (var cancellation in _cancellations) cancellation.Dispose();
        _cancellations.Clear();
        _readCancellation = _detailCancellation = null;
    }

    private async Task ReadListAsync(string project, int generation, CancellationTokenSource cancellation)
    {
        try
        {
            var result = await _commands.ListSourcesAsync(new(project, All: true), cancellation.Token).ConfigureAwait(true);
            if (!IsCurrent(project, generation, cancellation)) return;
            if (!result.Succeeded)
            {
                FailMetadataRead(result.Refusal!);
                return;
            }
            _baseline = result.Value!.Baseline;
            _listStale = result.Value.IsStale;
            Notice = _listStale ? MotifAppStrings.SourcesOutdated : result.Value.Sources.Count == 0 ? MotifAppStrings.SourcesNone : null;
            foreach (var source in result.Value.Sources) Sources.Add(new(source));
            NotifyCommands();
            var choice = _navigationChoice;
            if (choice.SourceId is not null)
            {
                Navigate(choice);
                if (SelectedSource is not null) await Loading.ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }

    private async Task ReadDetailAsync(string project, string id, int generation, CancellationTokenSource cancellation)
    {
        try
        {
            var shown = await _commands.ShowSourceAsync(new(project, id, _baseline), cancellation.Token).ConfigureAwait(true);
            if (!IsCurrent(project, generation, cancellation)) return;
            if (!shown.Succeeded)
            {
                FailMetadataRead(shown.Refusal!);
                return;
            }
            var current = await _commands.GetSourceCurrentAsync(new(project, id, shown.Value!.Baseline), cancellation.Token)
                .ConfigureAwait(true);
            if (!IsCurrent(project, generation, cancellation)) return;
            Detail = shown.Value;
            FolderText = Detail!.Source.Folder ?? string.Empty;
            ContextText = Detail.Source.Context;
            if (current.Succeeded)
                CurrentText = current.Value!.Current.SingleOrDefault(row => row.Source.Equals(id, StringComparison.OrdinalIgnoreCase))?.Chapter ?? string.Empty;
            else
            {
                FailMetadataRead(current.Refusal!);
            }
            if (shown.Value.IsStale || current.Value?.IsStale == true)
            {
                _listStale = true;
                Notice = MotifAppStrings.SourcesOutdated;
            }
            NotifyCommands();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }

    private async Task UsePendingAsync(Func<CancellationToken, Task> usePending)
    {
        var token = _readCancellation?.Token ?? CancellationToken.None;
        IsBusy = true;
        var generation = _generation;
        try { await usePending(token).ConfigureAwait(true); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { if (generation == _generation) IsBusy = false; }
    }

    private Task WriteAsync<T>(Func<CancellationToken, Task<CommandOutcome<T>>> write, bool staged) where T : class =>
        Track(WriteCoreAsync(write, staged));

    private async Task WriteCoreAsync<T>(Func<CancellationToken, Task<CommandOutcome<T>>> write, bool staged) where T : class
    {
        if (_project is not { } project) return;
        var generation = _generation;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _detailCancellation?.Token ?? _readCancellation?.Token ?? CancellationToken.None);
        IsBusy = true;
        ShownRefusal = null;
        Notice = null;
        try
        {
            var result = await write(cancellation.Token).ConfigureAwait(true);
            if (!IsCurrent(project, generation, cancellation)) return;
            if (!result.Succeeded) { ShownRefusal = SourceRefusal(result.Refusal!); return; }
            if (staged)
            {
                if (_reloadChanges is not null) await _reloadChanges(cancellation.Token).ConfigureAwait(true);
                if (!IsCurrent(project, generation, cancellation)) return;
                Notice = MotifAppStrings.SourcesStaged;
            }
            else if (SelectedSource is { } selected)
            {
                await ReadDetailAsync(project, selected.Id, generation, cancellation).ConfigureAwait(true);
                if (IsCurrent(project, generation, cancellation)) Notice = MotifAppStrings.SourcesLocalSaved;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally { if (_generation == generation) IsBusy = false; }
    }

    private void FailMetadataRead(Refusal refusal)
    {
        _listStale = true;
        ShownRefusal = SourceRefusal(refusal);
        Notice = refusal.Code == RefusalCodes.GdrBaselineChanged
            ? MotifAppStrings.SourcesOutdated : MotifAppStrings.SourcesUnavailable;
        NotifyCommands();
    }

    private static WindowRefusal SourceRefusal(Refusal refusal) => WindowRefusal.From(refusal,
        refusal.Code switch
        {
            RefusalCodes.SourceRefInvalid => MotifAppStrings.SourcesInvalidChapter,
            RefusalCodes.SourceContextInvalid => MotifAppStrings.SourcesInvalidContext,
            RefusalCodes.SourceFolderMissing => MotifAppStrings.SourcesFolderUnavailable,
            RefusalCodes.SourceChapterNotInSource => MotifAppStrings.SourcesChapterUnavailable,
            _ => MotifAppStrings.SourcesActionUnavailable,
        });

    private bool IsCurrent(string project, int generation, CancellationTokenSource cancellation) =>
        _project == project && generation == _generation && !cancellation.IsCancellationRequested;
    private bool CanUseSource() => _project is not null && !IsBusy && !_listStale &&
        Detail is { IsStale: false, Source: { Kind: "Paratext", Status: "Active", Error: null } };
    private bool CanSetFolder() => _project is not null && !IsBusy && !_listStale &&
        Detail?.Source is { Kind: "Paratext", Status: "Active" } && !string.IsNullOrWhiteSpace(FolderText);
    private void InvalidateDetail()
    {
        _generation++;
        _detailCancellation?.Cancel();
        _detailCancellation = null;
        IsBusy = false;
    }
    private Task Track(Task task)
    {
        _tasks.RemoveAll(completed => completed.IsCompletedSuccessfully);
        _tasks.Add(task);
        return task;
    }
    private void NotifyNavigation()
    {
        foreach (var property in new[] { nameof(IsFieldWorks), nameof(HasSource), nameof(CanReadPassage), nameof(PassageTitle), nameof(PassageMessage) })
            OnPropertyChanged(property);
        NotifyCommands();
    }
    private void NotifyCommands()
    {
        UsePendingProposalCommand?.NotifyCanExecuteChanged();
        ReloadCommand?.NotifyCanExecuteChanged();
        SetFolderCommand?.NotifyCanExecuteChanged();
        SetCurrentCommand?.NotifyCanExecuteChanged();
        ClearCurrentCommand?.NotifyCanExecuteChanged();
        StageContextCommand?.NotifyCanExecuteChanged();
        AddParatextCommand?.NotifyCanExecuteChanged();
    }
}
