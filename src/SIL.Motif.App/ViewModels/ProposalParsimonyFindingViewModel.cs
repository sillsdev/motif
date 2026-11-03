using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// One new or changed Finding of a Proposal's Parsimony group, in the Report's own words, with a button that reads
/// the kept views its evidence points to. Reading is on request, so a section with many Findings reads nothing it
/// is not asked about.
/// </summary>
public sealed partial class ProposalParsimonyFindingViewModel : ObservableObject
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<string>>> _readEvidence;
    private CancellationTokenSource? _cancellation;

    /// <summary>Creates the Finding.</summary>
    /// <param name="findingId">The Finding's stable identity in its Report.</param>
    /// <param name="kind">New or Evidence Changed, in the window's words.</param>
    /// <param name="line">The measure's name and the Finding's description as one line.</param>
    /// <param name="readEvidence">Reads one line for each evidence view the Finding points to.</param>
    public ProposalParsimonyFindingViewModel(string findingId, string kind, string line,
        Func<CancellationToken, Task<IReadOnlyList<string>>> readEvidence)
    {
        FindingId = findingId;
        Kind = kind;
        Line = line;
        _readEvidence = readEvidence;
        ShowEvidenceCommand = new AsyncRelayCommand(ShowEvidenceAsync, () => CanShowEvidence);
    }

    /// <summary>The Finding's stable identity in its Report.</summary>
    public string FindingId { get; }

    /// <summary>New or Evidence Changed.</summary>
    public string Kind { get; }

    /// <summary>The measure's name and the Finding's description.</summary>
    public string Line { get; }

    /// <summary>The Finding line's automation identifier.</summary>
    public string AutomationId => AutomationIds.ForProposalParsimonyFinding(FindingId);

    /// <summary>The Show Evidence button's automation identifier.</summary>
    public string EvidenceAutomationId => AutomationIds.ForProposalParsimonyEvidence(FindingId);

    /// <summary>The Show Evidence button's words.</summary>
    public string ShowEvidenceText => MotifAppStrings.ProposalParsimonyShowEvidence;

    /// <summary>The Show Evidence button's accessible name, which says whose evidence it shows.</summary>
    public string ShowEvidenceAccessibleName =>
        string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalParsimonyShowEvidenceAccessibleName, Line);

    /// <summary>The evidence text's automation identifier.</summary>
    public string EvidenceTextAutomationId => AutomationIds.ForProposalParsimonyEvidenceText(FindingId);

    /// <summary>Reads the evidence views once; asking again after they were read shows the same lines.</summary>
    public IAsyncRelayCommand ShowEvidenceCommand { get; }

    /// <summary>The evidence lines, or the words shown while they are read; empty until asked for.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEvidence), nameof(CanShowEvidence))]
    private string _evidenceText = string.Empty;

    /// <summary>Whether the evidence has been asked for.</summary>
    public bool HasEvidence => EvidenceText.Length > 0;

    /// <summary>Whether the button is offered: the evidence has not been asked for.</summary>
    public bool CanShowEvidence => !HasEvidence;

    partial void OnEvidenceTextChanged(string value) => ShowEvidenceCommand.NotifyCanExecuteChanged();

    /// <summary>Stops a read that is under way and shows the button again; a result that arrives later is dropped.</summary>
    internal void Cancel()
    {
        var cancellation = _cancellation;
        _cancellation = null;
        if (cancellation is null) return;
        cancellation.Cancel();
        cancellation.Dispose();
        EvidenceText = string.Empty;
    }

    private async Task ShowEvidenceAsync()
    {
        EvidenceText = MotifAppStrings.ProposalParsimonyEvidenceReading;
        var cancellation = _cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        try
        {
            var lines = await _readEvidence(token).ConfigureAwait(true);
            if (token.IsCancellationRequested) return;
            EvidenceText = lines.Count == 0 ? MotifAppStrings.ProposalParsimonyEvidenceNone : string.Join("\n", lines);
        }
        catch (OperationCanceledException)
        {
            // A cancel has already reset the text; any other cancellation leaves the button to ask again.
            if (!token.IsCancellationRequested) EvidenceText = string.Empty;
        }
        finally
        {
            if (ReferenceEquals(_cancellation, cancellation))
            {
                _cancellation = null;
                cancellation.Dispose();
            }
        }
    }
}
