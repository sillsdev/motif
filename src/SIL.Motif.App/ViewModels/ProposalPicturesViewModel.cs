using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;

namespace SIL.Motif.App.ViewModels;

/// <summary>One picture on the selected Proposal: its bytes for the thumbnail and a sentence that says what it is.</summary>
public sealed partial class ProposalPictureItem : ObservableObject
{
    /// <summary>Creates the item.</summary>
    public ProposalPictureItem(PictureView view, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(view);
        PictureId = view.PictureId;
        Bytes = bytes;
        Caption = view.Caption ?? "";
        Description = string.Format(CultureInfo.CurrentCulture, MotifAppStrings.PictureDescription,
            view.Width, view.Height, Math.Max(1, (view.ByteCount + 512) / 1024));
    }

    /// <summary>The picture's number in the store.</summary>
    public long PictureId { get; }

    /// <summary>The stored WebP, PNG or JPEG bytes, which the thumbnail decodes.</summary>
    public byte[] Bytes { get; }

    /// <summary>The person's one-line caption; empty when it has none.</summary>
    public string Caption { get; }

    /// <summary>Whether the picture has a caption to show.</summary>
    public bool HasCaption => Caption.Length > 0;

    /// <summary>Its size in pixels and in kilobytes, in words.</summary>
    public string Description { get; }

    /// <summary>Whether removal of this picture is awaiting confirmation.</summary>
    [ObservableProperty]
    private bool _isConfirmingRemoval;
}

/// <summary>
/// The pictures on the selected Proposal. A person adds one through the file picker and removes any; the command prepares
/// the stored picture at at most 1000 pixels and about 1 MB. A decided Proposal shows its pictures without changing them.
/// </summary>
public sealed partial class ProposalPicturesViewModel : ObservableObject
{
    private readonly Func<ShowPicturesRequest, CancellationToken, Task<CommandOutcome<ProposalPicturesResponse>>> _show;
    private readonly Func<AddPictureRequest, CancellationToken, Task<CommandOutcome<ProposalPictureChangedResponse>>> _add;
    private readonly Func<RemovePictureRequest, CancellationToken, Task<CommandOutcome<ProposalPictureChangedResponse>>> _remove;
    private readonly IPictureIntake? _intake;
    private string? _projectPath;
    private string? _proposalId;
    private int _generation;
    private ProposalPictureItem? _pendingRemoval;

    /// <summary>Creates the model.</summary>
    public ProposalPicturesViewModel(
        Func<ShowPicturesRequest, CancellationToken, Task<CommandOutcome<ProposalPicturesResponse>>> show,
        Func<AddPictureRequest, CancellationToken, Task<CommandOutcome<ProposalPictureChangedResponse>>> add,
        Func<RemovePictureRequest, CancellationToken, Task<CommandOutcome<ProposalPictureChangedResponse>>> remove,
        IPictureIntake? intake)
    {
        _show = show ?? throw new ArgumentNullException(nameof(show));
        _add = add ?? throw new ArgumentNullException(nameof(add));
        _remove = remove ?? throw new ArgumentNullException(nameof(remove));
        _intake = intake;
        AddCommand = new AsyncRelayCommand(AddAsync, () => CanAdd);
        RemoveCommand = new AsyncRelayCommand<ProposalPictureItem>(AskToRemoveAsync);
        ConfirmRemoveCommand = new AsyncRelayCommand<ProposalPictureItem>(RemoveAsync);
        CancelRemoveCommand = new RelayCommand(CancelRemove);
    }

    /// <summary>The pictures, oldest first.</summary>
    public ObservableCollection<ProposalPictureItem> Items { get; } = [];

    /// <summary>Chooses a picture and adds it.</summary>
    public IAsyncRelayCommand AddCommand { get; }

    /// <summary>Asks before removing the picture it is given.</summary>
    public IAsyncRelayCommand<ProposalPictureItem> RemoveCommand { get; }

    /// <summary>Removes the picture whose removal was confirmed.</summary>
    public IAsyncRelayCommand<ProposalPictureItem> ConfirmRemoveCommand { get; }

    /// <summary>Keeps the picture whose removal was requested.</summary>
    public IRelayCommand CancelRemoveCommand { get; }

    /// <summary>Whether a Proposal is shown, so the section appears.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isVisible;

    /// <summary>Whether the Proposal is open, so pictures can be added and removed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdd))]
    [NotifyPropertyChangedFor(nameof(CanRemove))]
    [NotifyPropertyChangedFor(nameof(ShowsLimit))]
    private bool _canEdit;

    /// <summary>The words for a picture that could not be added; empty when none failed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    private string _problem = "";

    /// <summary>A refusal the store gave the last add or remove; null when none did.</summary>
    [ObservableProperty]
    private WindowRefusal? _shownRefusal;

    /// <summary>The caption for the next picture added; cleared once it is stored.</summary>
    [ObservableProperty]
    private string _caption = "";

    /// <summary>How many pictures the Proposal holds out of how many it may, as "3 of 10".</summary>
    public string CountText => string.Format(CultureInfo.CurrentCulture, MotifAppStrings.PictureCountFormat,
        Items.Count, ProposalPictures.MaxPictures);

    /// <summary>Whether the Proposal holds as many pictures as it may.</summary>
    public bool IsFull => Items.Count >= ProposalPictures.MaxPictures;

    /// <summary>Whether to say that one picture must go before another comes.</summary>
    public bool ShowsLimit => CanEdit && IsFull;

    /// <summary>Whether the Add Picture button and the caption box are offered.</summary>
    public bool CanAdd => CanEdit && _intake is not null && !IsFull;

    /// <summary>Whether each picture offers Remove.</summary>
    public bool CanRemove => CanEdit;

    /// <summary>Whether there is a problem to show.</summary>
    public bool HasProblem => Problem.Length > 0;

    /// <summary>Whether any picture is shown.</summary>
    public bool HasItems => Items.Count > 0;

    /// <summary>Whether the section shows its none-yet sentence.</summary>
    public bool IsEmpty => IsVisible && Items.Count == 0;

    partial void OnCanEditChanged(bool value) => AddCommand.NotifyCanExecuteChanged();

    private void CountChanged()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(IsFull));
        OnPropertyChanged(nameof(ShowsLimit));
        OnPropertyChanged(nameof(CanAdd));
        AddCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Reads a Proposal's pictures; <paramref name="canEdit"/> is false once it is decided.</summary>
    public async Task LoadAsync(string projectPath, string proposalId, bool canEdit, CancellationToken cancellationToken = default)
    {
        CancelRemove();
        CanEdit = false;
        var generation = ++_generation;
        _projectPath = projectPath;
        _proposalId = proposalId;
        Problem = "";
        ShownRefusal = null;
        var shown = await ReadAsync(projectPath, proposalId, cancellationToken).ConfigureAwait(true);
        if (generation != _generation) return;
        if (shown is null) return;
        Fill(shown);
        CanEdit = canEdit;
        IsVisible = true;
    }

    /// <summary>Forgets the Proposal, as when none is selected.</summary>
    public void Clear()
    {
        CancelRemove();
        _generation++;
        _projectPath = _proposalId = null;
        Items.Clear();
        CountChanged();
        IsVisible = false;
        CanEdit = false;
        Problem = "";
        ShownRefusal = null;
    }

    private async Task<IReadOnlyList<ProposalPictureItem>?> ReadAsync(string path, string proposalId, CancellationToken cancellationToken)
    {
        var list = await _show(new ShowPicturesRequest(path, MotifProductVersion.CurrentText, proposalId), cancellationToken)
            .ConfigureAwait(true);
        if (!list.Succeeded)
        {
            ShownRefusal = WindowRefusal.From(list.Refusal!);
            return null;
        }
        var items = new List<ProposalPictureItem>();
        foreach (var view in list.Value!.Pictures)
        {
            var one = await _show(new ShowPicturesRequest(path, MotifProductVersion.CurrentText, proposalId, view.PictureId),
                cancellationToken).ConfigureAwait(true);
            if (!one.Succeeded)
            {
                ShownRefusal = WindowRefusal.From(one.Refusal!);
                return null;
            }
            items.Add(new ProposalPictureItem(view, Convert.FromBase64String(one.Value!.DataBase64!)));
        }
        return items;
    }

    private void Fill(IReadOnlyList<ProposalPictureItem> items)
    {
        Items.Clear();
        foreach (var item in items) Items.Add(item);
        CountChanged();
    }

    private async Task AddAsync()
    {
        if (_intake is null || _projectPath is not { } path || _proposalId is not { } proposalId) return;
        Problem = "";
        ShownRefusal = null;
        var chosen = await _intake.ChooseAsync().ConfigureAwait(true);
        if (path != _projectPath || proposalId != _proposalId) return;
        switch (chosen.Status)
        {
            case PictureIntakeStatus.Cancelled:
                return;
            case PictureIntakeStatus.Unsupported:
                Problem = MotifAppStrings.PictureNotSupported;
                return;
            case PictureIntakeStatus.TooLarge:
                Problem = MotifAppStrings.PictureTooBig;
                return;
            case PictureIntakeStatus.Ready when chosen.Bytes is { } bytes:
                var added = await _add(new AddPictureRequest(path, MotifProductVersion.CurrentText, proposalId, bytes,
                        Caption: Caption.Trim() is { Length: > 0 } caption ? caption : null),
                    CancellationToken.None).ConfigureAwait(true);
                if (!added.Succeeded) ShownRefusal = WindowRefusal.From(added.Refusal!);
                else Caption = "";
                break;
            default:
                Problem = MotifAppStrings.PictureNotRead;
                return;
        }
        await ReloadAsync(path, proposalId).ConfigureAwait(true);
    }

    private Task AskToRemoveAsync(ProposalPictureItem? item)
    {
        CancelRemove();
        if (item is not null && CanEdit && Items.Contains(item))
        {
            _pendingRemoval = item;
            item.IsConfirmingRemoval = true;
        }
        return Task.CompletedTask;
    }

    private void CancelRemove()
    {
        if (_pendingRemoval is { } item) item.IsConfirmingRemoval = false;
        _pendingRemoval = null;
    }

    private async Task RemoveAsync(ProposalPictureItem? item)
    {
        if (item is null || !ReferenceEquals(item, _pendingRemoval) || !CanEdit ||
            _projectPath is not { } path || _proposalId is not { } proposalId) return;
        CancelRemove();
        Problem = "";
        ShownRefusal = null;
        var removed = await _remove(new RemovePictureRequest(path, MotifProductVersion.CurrentText, proposalId, item.PictureId),
            CancellationToken.None).ConfigureAwait(true);
        if (path != _projectPath || proposalId != _proposalId) return;
        if (!removed.Succeeded) ShownRefusal = WindowRefusal.From(removed.Refusal!);
        await ReloadAsync(path, proposalId).ConfigureAwait(true);
    }

    // Reloads even after a refused write, so the list is what the store holds.
    private async Task ReloadAsync(string path, string proposalId)
    {
        var generation = ++_generation;
        var refusal = ShownRefusal;
        var shown = await ReadAsync(path, proposalId, CancellationToken.None).ConfigureAwait(true);
        if (generation != _generation || shown is null) return;
        Fill(shown);
        ShownRefusal ??= refusal;
        if (refusal?.Code == RefusalCodes.ProposalClosed) CanEdit = false;
    }
}
