using System.ComponentModel;
using SIL.Motif.App.GuideQueries;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

public sealed partial class ProposalDocumentViewModel
{
    private string? _queriesKey;

    partial void OnRationaleEditorChanged(RationaleEditorModel? oldValue, RationaleEditorModel? newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= OnRationaleTextChanged;
        if (newValue is not null) newValue.PropertyChanged += OnRationaleTextChanged;
    }

    private void OnRationaleTextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RationaleEditorModel.Markdown) && Detail is { } detail) ShowQueries(detail);
    }

    // Run again only when the Rationale or the Proposal's state changed, so a refresh never reruns them.
    private void ShowQueries(ProposalDetailProjection detail)
    {
        var markdown = RationaleEditor?.Markdown ?? detail.Rationale?.Markdown ?? "";
        var key = QueriesKey(detail, markdown);
        if (key == _queriesKey) return;
        _queriesKey = key;
        Queries.Show(GuideQueryDocument.FromMarkdown(markdown));
    }

    // The Proposal side reads the latest Trial, so a Trial that finishes, or a newer one, is a new key.
    private string QueriesKey(ProposalDetailProjection detail, string markdown) =>
        _review?.Trial is { } trial ? trial.JobId + "/" + trial.Status + "\n" + detail.ProposalId + "\n" + markdown
            : "none\n" + detail.ProposalId + "\n" + markdown;
}
