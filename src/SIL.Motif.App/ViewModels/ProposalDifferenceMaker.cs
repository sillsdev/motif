using System.ComponentModel;
using System.Globalization;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>What the Proposals inspector offers for the selected Proposal's Difference.</summary>
public enum DifferenceOffer
{
    /// <summary>A Difference can be made now.</summary>
    Available,

    /// <summary>The Proposal was never tried, so there is nothing to compare yet.</summary>
    NotTried,

    /// <summary>The Proposal is decided, so a new Difference would not be kept.</summary>
    Decided,

    /// <summary>The Proposal is deferred: its words were dropped, and making the Difference again brings them back.</summary>
    Deferred,
}

/// <summary>One of the selected Proposal's saved Differences, named for the list that switches between them.</summary>
/// <param name="DifferenceId">The id that reads it.</param>
/// <param name="TextId">The Text's GUID.</param>
/// <param name="Reference">Whose decisions it was read against, <c>human</c> or <c>source-validated</c>.</param>
/// <param name="Label">The Text and the decisions in the window's words.</param>
public sealed record DifferenceSavedChoice(string DifferenceId, string TextId, string Reference, string Label);

/// <summary>One Text a Difference can compare.</summary>
/// <param name="Id">The Text's own GUID, the only thing the request carries.</param>
/// <param name="Title">The Text's title, for display.</param>
public sealed record DifferenceTextChoice(Guid Id, string Title);

/// <summary>
/// One run of making the selected Proposal's Difference for a chosen Text: the shared run lifecycle (progress,
/// cancel, typed outcome) around the same <c>difference produce</c> command the CLI and MCP call.
/// </summary>
/// <remarks>
/// The run applies nothing. A cancelled run saves nothing, because the command publishes its result in one
/// transaction after its last cancellation check.
/// </remarks>
public sealed class ProposalDifferenceMaker : CommandRunViewModel<ProposalDifferenceResponse>
{
    private readonly Func<ProposalDifferenceProduceRequest, IProgress<AssessmentProgress>, CancellationToken,
        Task<CommandOutcome<ProposalDifferenceResponse>>> _produce;
    private readonly Func<ProposalDifferenceProduceRequest?> _describe;
    private readonly Action<ProposalDifferenceResponse> _made;

    /// <summary>Creates the run.</summary>
    /// <param name="produce">Runs the command; throwing is shown as a refusal.</param>
    /// <param name="describe">The request for the Proposal and Text now chosen, or <see langword="null"/> when none.</param>
    /// <param name="made">Told the response of a run that completed, still on the owner's thread.</param>
    public ProposalDifferenceMaker(
        Func<ProposalDifferenceProduceRequest, IProgress<AssessmentProgress>, CancellationToken,
            Task<CommandOutcome<ProposalDifferenceResponse>>> produce,
        Func<ProposalDifferenceProduceRequest?> describe, Action<ProposalDifferenceResponse> made)
    {
        ArgumentNullException.ThrowIfNull(produce);
        ArgumentNullException.ThrowIfNull(describe);
        ArgumentNullException.ThrowIfNull(made);
        _produce = produce;
        _describe = describe;
        _made = made;
        PropertyChanged += OnOwnPropertyChanged;
    }

    /// <summary>Whether the run is cancelling and nothing more can be asked of it.</summary>
    public bool IsCancelling => State == RunState.Cancelling;

    /// <summary>Whether the last run was cancelled, which is not a failure.</summary>
    public bool WasCancelled => State == RunState.Cancelled;

    /// <summary>The last run's refusal in the window's words; a cancellation is not one.</summary>
    public WindowRefusal? FailureShown => State == RunState.Refused && Refusal is { } refusal
        ? WindowRefusal.From(refusal, MotifAppStrings.ProposalDifferenceMakeFailed) : null;

    /// <summary>The cancelled run's sentence.</summary>
    public string CancelledText => MotifAppStrings.RefusalDifferenceCancelled;

    /// <summary>What the run is doing, in words and never in time.</summary>
    public string ProgressText => State == RunState.Cancelling ? MotifAppStrings.ProposalDifferenceCancelling
        : Progress switch
        {
            { Stage: AssessmentStage.Finalizing, Total: { } total } progress => string.Format(
                CultureInfo.CurrentCulture, MotifAppStrings.ProposalDifferenceMakingFormat,
                progress.Completed.ToString("N0", CultureInfo.CurrentCulture),
                total.ToString("N0", CultureInfo.CurrentCulture)),
            { Stage: AssessmentStage.SelectingWords } => MotifAppStrings.ProposalDifferenceMakingReadingText,
            _ => MotifAppStrings.ProposalDifferenceMaking,
        };

    /// <summary>Asks whether the command can start again, as when the chosen Text or Proposal changed.</summary>
    public void Refresh() => RunCommand.NotifyCanExecuteChanged();

    protected override bool CanStartCore() => _describe() is not null;

    protected override async Task<CommandOutcome<ProposalDifferenceResponse>> ExecuteCoreAsync(
        CancellationToken cancellationToken)
    {
        var request = _describe() ?? throw new InvalidOperationException("No Proposal and Text were chosen.");
        try
        {
            return await _produce(request, this, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return CommandOutcome<ProposalDifferenceResponse>.Refused(new Refusal(
                RefusalCodes.DifferenceCancelled, FailureReason.Cancelled, "The comparison was cancelled."));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return CommandOutcome<ProposalDifferenceResponse>.Refused(new Refusal(
                RefusalCodes.WindowDifferenceMakeFailed, FailureReason.Refused, exception.Message));
        }
    }

    protected override void OnRunSucceeded(ProposalDifferenceResponse response) => _made(response);

    private void OnOwnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Progress):
                OnPropertyChanged(nameof(ProgressText));
                break;
            case nameof(State):
                OnPropertyChanged(nameof(ProgressText));
                OnPropertyChanged(nameof(IsCancelling));
                OnPropertyChanged(nameof(WasCancelled));
                OnPropertyChanged(nameof(FailureShown));
                break;
            case nameof(Refusal):
                OnPropertyChanged(nameof(FailureShown));
                break;
        }
    }
}
