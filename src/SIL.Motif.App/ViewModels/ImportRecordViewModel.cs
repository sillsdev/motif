using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>Where an import of an edited record file stands.</summary>
public enum ImportRecordState
{
    /// <summary>No file has been read, or the file named changed since the last preview.</summary>
    Idle,

    /// <summary>The file was read and its change set is shown; it can be staged.</summary>
    Loaded,

    /// <summary>The import declined the file, or declined to stage it; <see cref="ImportRecordViewModel.Refusal"/> says why.</summary>
    Refused,

    /// <summary>The changes were staged in a new Draft, named by <see cref="ImportRecordViewModel.StagedProposalId"/>.</summary>
    Staged,

    /// <summary>The import failed for a reason that is not a refusal.</summary>
    Error,
}

/// <summary>
/// Opens an edited record file, previews what it would change and what it would refuse, then stages the changes in
/// a new Draft. It has no way to apply: the Draft is reviewed and applied elsewhere.
/// </summary>
public sealed partial class ImportRecordViewModel : ObservableObject, IDisposable
{
    private readonly ICommandClient _commands;
    private readonly string _project;
    private CancellationTokenSource? _running;
    private int _generation;
    private bool _staging;

    public ImportRecordViewModel(ICommandClient commands, string project)
    {
        _commands = commands;
        _project = project;
        PreviewCommand = new AsyncRelayCommand(() => PreviewAsync(), () => CanPreview);
        PreviousPageCommand = new AsyncRelayCommand(() => PreviewAsync(Page - 1), () => CanPreview && Page > 1);
        NextPageCommand = new AsyncRelayCommand(() => PreviewAsync(Page + 1), () => CanPreview && Page < PageCount);
        StageCommand = new AsyncRelayCommand(() => StageAsync(), () => CanStage);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy && !_staging);
    }

    public IAsyncRelayCommand PreviewCommand { get; }
    public IAsyncRelayCommand PreviousPageCommand { get; }
    public IAsyncRelayCommand NextPageCommand { get; }
    public IAsyncRelayCommand StageCommand { get; }
    public IRelayCommand CancelCommand { get; }

    /// <summary>The operations on the page shown, in the order they would apply.</summary>
    public ObservableCollection<RecordImportOperation> Operations { get; } = [];

    /// <summary>The refusals on the page shown.</summary>
    public ObservableCollection<RecordImportRefusal> Refusals { get; } = [];

    [ObservableProperty] private ImportRecordState _state;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _filePath = string.Empty;
    [ObservableProperty] private string _draftName = string.Empty;
    [ObservableProperty] private RecordImportResponse? _preview;
    [ObservableProperty] private Refusal? _refusal;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private int _page = 1;
    [ObservableProperty] private int _pageCount = 1;

    /// <summary>The id of the staged Draft, for the review page to open; null until <see cref="State"/> is Staged.</summary>
    [ObservableProperty] private string? _stagedProposalId;

    /// <summary>The name the Draft was staged under.</summary>
    [ObservableProperty] private string? _stagedDraftName;

    public int ChangeCount => Preview?.ChangeCount ?? 0;
    public int OperationCount => Preview?.OperationCount ?? 0;
    public int RefusalCount => Preview?.RefusalCount ?? 0;

    public bool CanPreview => !IsBusy && !string.IsNullOrWhiteSpace(FilePath);

    /// <summary>A previewed file with operations to stage, a Draft name, and nothing already staged or running.</summary>
    public bool CanStage => !IsBusy && State is ImportRecordState.Loaded or ImportRecordState.Refused
        && Preview is { OperationCount: > 0 } && !string.IsNullOrWhiteSpace(DraftName);

    partial void OnFilePathChanged(string value)
    {
        if (_staging) { NotifyCommands(); return; }
        _generation++;
        _running?.Cancel();
        IsBusy = false;
        Reset();
        // Reset changes nothing when the state was already Idle, yet a typed path alone makes Preview available.
        NotifyCommands();
    }

    partial void OnDraftNameChanged(string value) => NotifyCommands();
    partial void OnIsBusyChanged(bool value) => NotifyCommands();
    partial void OnStateChanged(ImportRecordState value) => NotifyCommands();

    partial void OnPreviewChanged(RecordImportResponse? value)
    {
        OnPropertyChanged(nameof(ChangeCount));
        OnPropertyChanged(nameof(OperationCount));
        OnPropertyChanged(nameof(RefusalCount));
        NotifyCommands();
    }

    partial void OnPageChanged(int value) => NotifyCommands();
    partial void OnPageCountChanged(int value) => NotifyCommands();

    /// <summary>Reads the file again and shows the given page of its change set; a changed file gives a new preview.</summary>
    public Task PreviewAsync(int page = 1) =>
        RunAsync(new RecordImportRequest(_project, FilePath, null, page), staging: false);

    /// <summary>Stages the previewed changes in a new Draft named <see cref="DraftName"/>.</summary>
    public Task StageAsync() =>
        RunAsync(new RecordImportRequest(_project, FilePath, DraftName.Trim()), staging: true);

    /// <summary>Stops a preview in flight; a stage cannot be stopped once started, so this does nothing then.</summary>
    public void Cancel()
    {
        if (!_staging) _running?.Cancel();
    }

    /// <summary>Cancels a preview in flight; a stage already started is left to finish, as <see cref="Cancel"/> does.</summary>
    public void Dispose()
    {
        _generation++;
        if (!_staging) _running?.Cancel();
    }

    private async Task RunAsync(RecordImportRequest request, bool staging)
    {
        if (IsBusy || (staging ? !CanStage : !CanPreview)) return;
        var generation = ++_generation;
        using var cancellation = new CancellationTokenSource();
        _running = cancellation;
        _staging = staging;
        IsBusy = true;
        try
        {
            var outcome = await _commands.StageRecordImportAsync(request, cancellation.Token);
            // A started stage always reports its result, because the Draft exists whether or not it is wanted.
            if (!staging && (cancellation.IsCancellationRequested || generation != _generation)) return;
            if (!outcome.Succeeded || outcome.Value is null)
            {
                Refuse(outcome.Refusal, keepPreview: staging || (request.Page > 1 && Preview is not null));
                return;
            }
            Accept(outcome.Value, staging);
        }
        catch (OperationCanceledException)
        {
            // A cancelled run leaves the page as it was.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (!staging && generation != _generation) return;
            ErrorMessage = exception.Message;
            State = ImportRecordState.Error;
        }
        finally
        {
            if (ReferenceEquals(_running, cancellation)) _running = null;
            if (staging) _staging = false;
            if (staging || generation == _generation) IsBusy = false;
        }
    }

    private void Accept(RecordImportResponse response, bool staging)
    {
        Refusal = null;
        ErrorMessage = null;
        if (staging)
        {
            StagedProposalId = response.ProposalId;
            StagedDraftName = response.DraftName;
            State = ImportRecordState.Staged;
            return;
        }
        Show(response);
        State = ImportRecordState.Loaded;
    }

    private void Show(RecordImportResponse response)
    {
        Preview = response;
        Page = response.Page;
        PageCount = Math.Max(1, response.PageCount);
        Operations.Clear();
        foreach (var operation in response.Operations) Operations.Add(operation);
        Refusals.Clear();
        foreach (var refusal in response.Refusals) Refusals.Add(refusal);
    }

    private void Refuse(Refusal? refusal, bool keepPreview)
    {
        if (!keepPreview) ClearPreview();
        Refusal = refusal;
        ErrorMessage = null;
        State = ImportRecordState.Refused;
    }

    private void Reset()
    {
        ClearPreview();
        Refusal = null;
        ErrorMessage = null;
        StagedProposalId = null;
        StagedDraftName = null;
        State = ImportRecordState.Idle;
    }

    private void ClearPreview()
    {
        Preview = null;
        Page = 1;
        PageCount = 1;
        Operations.Clear();
        Refusals.Clear();
    }

    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanPreview));
        OnPropertyChanged(nameof(CanStage));
        PreviewCommand.NotifyCanExecuteChanged();
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        StageCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }
}
