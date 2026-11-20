using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Contract.Responses;
using Codes = SIL.Motif.Contract.Commands.RefusalCodes;

namespace SIL.Motif.App.ViewModels;

/// <summary>One change the file asks for that the import will not make, in words a person can read.</summary>
public sealed record ImportRefusalRow(string Code, string Class, string Field, string Reason)
{
    /// <summary>The object class and field the change was about, or an empty string for the object itself.</summary>
    public string Where => string.Join(' ', new[] { Class, Field }.Where(part => part.Length != 0));

    public bool HasWhere => Where.Length != 0;
}

/// <summary>One operation the file would make: what kind, on which object.</summary>
public sealed record ImportOperationRow(string Kind, string Target);

/// <summary>
/// What the Open Record File window binds to: the import view model, the file chooser, and the step that opens
/// the staged Draft's review page. Every control's enablement comes from a property here, so none can look live
/// and do nothing.
/// </summary>
public sealed class ImportRecordDialogModel : ObservableObject, IDisposable
{
    private readonly IRecordFilePicker _picker;
    private readonly Func<string, CancellationToken, Task> _openDraft;
    private bool _isPicking;
    private bool _isOpening;
    private bool _disposed;
    private string? _openFailure;

    public ImportRecordDialogModel(ImportRecordViewModel import, IRecordFilePicker picker,
        Func<string, CancellationToken, Task> openDraft)
    {
        ArgumentNullException.ThrowIfNull(import);
        ArgumentNullException.ThrowIfNull(picker);
        ArgumentNullException.ThrowIfNull(openDraft);
        Import = import;
        _picker = picker;
        _openDraft = openDraft;
        BrowseCommand = new AsyncRelayCommand(BrowseAsync, () => CanBrowse);
        OpenDraftCommand = new AsyncRelayCommand(OpenDraftAsync, () => CanOpenDraft);
        Import.PropertyChanged += OnImportChanged;
        Import.Operations.CollectionChanged += (_, _) => RebuildRows();
        Import.Refusals.CollectionChanged += (_, _) => RebuildRows();
    }

    /// <summary>The import's own state and commands: preview, paging, stage and cancel.</summary>
    public ImportRecordViewModel Import { get; }

    public IAsyncRelayCommand BrowseCommand { get; }
    public IAsyncRelayCommand OpenDraftCommand { get; }

    /// <summary>The operations on this page, as rows.</summary>
    public ObservableCollection<ImportOperationRow> OperationRows { get; } = [];

    /// <summary>The refusals on this page, as rows.</summary>
    public ObservableCollection<ImportRefusalRow> RefusalRows { get; } = [];

    /// <summary>The import's whole-file refusal in the window's words, or null when there is none.</summary>
    public WindowRefusal? ShownRefusal => Import.Refusal is { } refusal
        ? WindowRefusal.From(refusal, SentenceFor(refusal.Code)) : null;

    public bool HasShownRefusal => Import.Refusal is not null;

    public bool CanBrowse => !Import.IsBusy && !_isPicking && !_isOpening;

    /// <summary>Whether the staged Draft can be opened for review.</summary>
    public bool CanOpenDraft => Import.State == ImportRecordState.Staged && Import.StagedProposalId is not null && !_isOpening;

    /// <summary>The automatic open of the staged Draft, or a completed task when none has started.</summary>
    public Task OpenTask { get; private set; } = Task.CompletedTask;

    /// <summary>Raised once the staged Draft's review page is open, when the window has nothing left to do.</summary>
    public event EventHandler? DraftOpened;

    public bool HasPreview => Import.Preview is not null;
    public bool HasOperations => OperationRows.Count > 0;
    public bool HasRefusals => RefusalRows.Count > 0;
    public bool IsStaged => Import.State == ImportRecordState.Staged;
    public bool HasPaging => Import.PageCount > 1;

    /// <summary>The text of the Cancel button: Close once a Draft exists, since there is nothing left to cancel.</summary>
    public string CancelText => IsStaged ? MotifAppStrings.Close : MotifAppStrings.Cancel;

    public string CancelAccessibleName => IsStaged
        ? MotifAppStrings.ImportRecordCloseAccessibleName : MotifAppStrings.ImportRecordCancelAccessibleName;

    public string PageText => string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ImportRecordPageFormat,
        Import.Page, Import.PageCount);

    /// <summary>One sentence about where the import stands.</summary>
    public string StatusText
    {
        get
        {
            if (_openFailure is not null) return _openFailure;
            if (Import.IsBusy)
                return Import.CancelCommand.CanExecute(null)
                    ? MotifAppStrings.ImportRecordReading : MotifAppStrings.ImportRecordStaging;
            return Import.State switch
            {
                ImportRecordState.Loaded or ImportRecordState.Refused when Import.Preview is not null =>
                    string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ImportRecordSummaryFormat,
                        Import.ChangeCount, Import.OperationCount, Import.RefusalCount),
                ImportRecordState.Staged => string.Format(CultureInfo.CurrentCulture,
                    MotifAppStrings.ImportRecordStagedFormat, Import.StagedDraftName),
                ImportRecordState.Error => Import.ErrorMessage ?? string.Empty,
                ImportRecordState.Refused => string.Empty,
                _ => MotifAppStrings.ImportRecordIdle,
            };
        }
    }

    /// <summary>Stops a preview in flight; the window then closes, and a stage already started finishes by itself.</summary>
    public void Cancel() => Import.Cancel();

    /// <summary>
    /// Closes the model. A preview in flight is cancelled; a stage already started finishes by itself and its Draft is
    /// still opened, so closing the window never loses it.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        Import.Dispose();
        if (!Import.IsBusy) Import.PropertyChanged -= OnImportChanged;
    }

    private async Task BrowseAsync()
    {
        _isPicking = true;
        NotifyAll();
        try
        {
            var path = await _picker.PickRecordFileAsync().ConfigureAwait(true);
            if (path is null) return;
            Import.FilePath = path;
            await Import.PreviewAsync().ConfigureAwait(true);
        }
        finally
        {
            _isPicking = false;
            NotifyAll();
        }
    }

    private async Task OpenDraftAsync()
    {
        if (Import.StagedProposalId is not { } id) return;
        _isOpening = true;
        _openFailure = null;
        var opened = false;
        NotifyAll();
        try
        {
            await _openDraft(id, CancellationToken.None).ConfigureAwait(true);
            opened = true;
        }
        catch (InvalidOperationException exception)
        {
            _openFailure = exception.Message;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _openFailure = MotifAppStrings.ImportRecordOpenFailed;
        }
        finally
        {
            _isOpening = false;
            NotifyAll();
        }
        if (opened) DraftOpened?.Invoke(this, EventArgs.Empty);
    }

    private void OnImportChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ImportRecordViewModel.FilePath))
        {
            _openFailure = null;
        }
        NotifyAll();
        if (_disposed && !Import.IsBusy) Import.PropertyChanged -= OnImportChanged;
        // A staged Draft opens by itself; the Open Draft button stays for when that fails.
        if (e.PropertyName is nameof(ImportRecordViewModel.State) && Import.State == ImportRecordState.Staged &&
            OpenDraftCommand.CanExecute(null))
            OpenTask = OpenDraftCommand.ExecuteAsync(null);
    }

    private void RebuildRows()
    {
        OperationRows.Clear();
        foreach (var operation in Import.Operations)
            OperationRows.Add(new ImportOperationRow(operation.Kind, operation.Target ?? string.Empty));
        RefusalRows.Clear();
        foreach (var refusal in Import.Refusals)
            RefusalRows.Add(new ImportRefusalRow(refusal.Code, refusal.Class ?? string.Empty, refusal.Field, refusal.Reason));
        OnPropertyChanged(nameof(HasOperations));
        OnPropertyChanged(nameof(HasRefusals));
    }

    private void NotifyAll()
    {
        foreach (var name in new[]
        {
            nameof(ShownRefusal), nameof(HasShownRefusal), nameof(CanBrowse), nameof(CanOpenDraft), nameof(HasPreview),
            nameof(IsStaged), nameof(HasPaging), nameof(CancelText), nameof(CancelAccessibleName), nameof(PageText),
            nameof(StatusText),
        }) OnPropertyChanged(name);
        BrowseCommand.NotifyCanExecuteChanged();
        OpenDraftCommand.NotifyCanExecuteChanged();
    }

    private static string SentenceFor(string code) => code switch
    {
        Codes.RecordImportPlanRefused => MotifAppStrings.ImportRecordRefusalPlanRefused,
        Codes.RecordImportFileUnreadable => MotifAppStrings.ImportRecordRefusalUnreadable,
        Codes.RecordImportNothingToStage => MotifAppStrings.ImportRecordRefusalNothing,
        Codes.RecordImportInvalidPage => MotifAppStrings.ImportRecordRefusalPage,
        Codes.DraftNameCollision => MotifAppStrings.ImportRecordRefusalNameTaken,
        _ => WindowRefusal.GenericSentence,
    };
}
