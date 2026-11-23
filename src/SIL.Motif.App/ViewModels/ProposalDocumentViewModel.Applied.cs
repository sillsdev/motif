using CommunityToolkit.Mvvm.ComponentModel;

namespace SIL.Motif.App.ViewModels;

public sealed partial class ProposalDocumentViewModel
{
    /// <summary>When and by whom an Applied Proposal was applied, from the record Apply froze.</summary>
    [ObservableProperty] private string _appliedHeading = string.Empty;

    [ObservableProperty] private IReadOnlyList<string> _appliedEffects = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAppliedEvidence))]
    private IReadOnlyList<string> _appliedEvidence = [];

    private bool _hasAppliedRecord;

    /// <summary>Whether the Proposal is Applied and Apply kept a record of it.</summary>
    public bool HasAppliedRecord => _hasAppliedRecord;

    /// <summary>Whether any Difference was saved before the Apply.</summary>
    public bool HasAppliedEvidence => AppliedEvidence.Count > 0;

    private void RefreshAppliedRecord(SIL.Motif.Contract.Responses.ProposalDetailProjection? detail)
    {
        var record = detail is { Status: "applied" } ? detail.AppliedRecord : null;
        _hasAppliedRecord = record is not null;
        AppliedHeading = record is null ? string.Empty : ProposalAppliedText.Heading(record);
        AppliedEffects = record is null ? [] : ProposalAppliedText.Effects(record, _entryNames);
        AppliedEvidence = record is null ? [] : ProposalAppliedText.Evidence(record);
        OnPropertyChanged(nameof(HasAppliedRecord));
    }
}
