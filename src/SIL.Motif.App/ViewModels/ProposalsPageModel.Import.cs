namespace SIL.Motif.App.ViewModels;

public sealed partial class ProposalsPageModel
{
    /// <summary>Reads the list again and selects the Draft an import just staged, if the list now holds it.</summary>
    internal async Task SelectStagedAsync(string proposalId, CancellationToken cancellationToken)
    {
        if (Context.ProjectPath is not { } path) return;
        await LoadAsync(path, cancellationToken).ConfigureAwait(true);
        var card = Groups.SelectMany(group => group.Cards).FirstOrDefault(row => row.ProposalId == proposalId);
        if (card is not null) SelectByUser(card);
    }
}
