using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace SIL.Motif.App.ViewModels;

public sealed partial class ProposalDocumentViewModel
{
    private ICommand? _readAgain;

    /// <summary>How long a read may run before the page stops saying "Reading" and offers Read Again.</summary>
    internal TimeSpan StuckReadAfter { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Whether the read has run past its bound; the page then shows a reason and Read Again.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsReadingLine), nameof(ShowsReadStuck))]
    private bool _readTimedOut;

    internal TimeSpan SupersededGrace { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Whether the plain "Reading Proposal" line shows: a read is running and still within its bound.</summary>
    public bool ShowsReadingLine => IsLoading && !ReadTimedOut;

    /// <summary>Whether the page shows that the read ran long, with the Read Again button.</summary>
    public bool ShowsReadStuck => IsLoading && ReadTimedOut;

    /// <summary>Starts a fresh read of this Proposal, replacing one that never finished.</summary>
    public ICommand ReadAgainCommand => _readAgain ??= new RelayCommand(() => _ = RefreshAsync());

    partial void OnIsLoadingChanged(bool value)
    {
        if (!value) ReadTimedOut = false;
        OnPropertyChanged(nameof(ShowsReadingLine));
        OnPropertyChanged(nameof(ShowsReadStuck));
        RaiseApply();
    }

    // The read's task ends with the read or when a newer read supersedes it, so a hung call holds no caller.
    private async Task ReadBoundedAsync(int generation, CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        var superseded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = token.Register(() => superseded.TrySetResult());
        using var watch = CancellationTokenSource.CreateLinkedTokenSource(token);
        var watching = WatchReadAsync(generation, watch.Token);
        try
        {
            var read = ReadAsync(generation, cancellation);
            // A superseded read gets a moment to unwind before its callers stop waiting for it.
            if (await Task.WhenAny(read, superseded.Task).ConfigureAwait(true) != read)
                await Task.WhenAny(read, Task.Delay(SupersededGrace)).ConfigureAwait(true);
        }
        finally
        {
            watch.Cancel();
        }
        await watching.ConfigureAwait(true);
    }

    // Never leaves the document on "Reading" for good: past the bound the read gets a reason and a way out.
    private async Task WatchReadAsync(int generation, CancellationToken token)
    {
        try
        {
            await Task.Delay(StuckReadAfter, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (Current(generation) && IsLoading) ReadTimedOut = true;
    }
}
