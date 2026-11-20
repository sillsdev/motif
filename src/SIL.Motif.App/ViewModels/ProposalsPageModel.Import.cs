namespace SIL.Motif.App.ViewModels;

public sealed partial class ProposalsPageModel
{
    /// <summary>Reads the list again and selects the Draft an import just staged; false when the list does not hold it.</summary>
    internal async Task<bool> SelectStagedAsync(string proposalId, CancellationToken cancellationToken)
    {
        if (Context.ProjectPath is not { } path) return false;
        await LoadAsync(path, cancellationToken).ConfigureAwait(true);
        var card = Groups.SelectMany(group => group.Cards).FirstOrDefault(row => row.ProposalId == proposalId);
        if (card is null) return false;
        SelectByUser(card);
        return true;
    }
}
