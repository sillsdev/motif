using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Commands.Queries;

namespace SIL.Motif.App.ViewModels;

public sealed partial class ProposalDocumentViewModel
{
    private ProposalEntryNames _entryNames = ProposalEntryNames.None;
    private string? _entryNamesKey;

    /// <summary>Why the entries and senses the Proposal touches could not be read from the Baseline; null when they were.</summary>
    [ObservableProperty] private WindowRefusal? _entryRefusal;

    // The entries and senses the changes name, read from the Baseline so a change can say "palit sense 2".
    private async Task ReadEntryNamesAsync(ProposalDetailProjection detail, CancellationToken cancellationToken)
    {
        var ids = ProposalEntryNames.Askable(detail.Operations
            .Where(operation => operation.Kind.StartsWith("lexical/", StringComparison.Ordinal))
            .Select(operation => operation.Target)
            .Concat(ShownEffects(detail)
                .Where(effect => effect.Field.StartsWith("lexical/", StringComparison.Ordinal))
                .Select(effect => effect.CanonicalId)));
        var key = string.Join('|', ids) + "@" + _context.Baseline?.Token?.SemanticIdentity;
        if (key == _entryNamesKey) return;
        if (ids.Count == 0)
        {
            (_entryNames, _entryNamesKey, EntryRefusal) = (ProposalEntryNames.None, key, null);
            return;
        }
        var names = new ProposalEntryNames();
        foreach (var chunk in ids.Chunk(LexicalEntryQuery.MaxIds))
        {
            var read = await _context.Commands.ReadLexicalEntriesAsync(new LexicalEntryRequest(ProjectPath, chunk),
                cancellationToken).ConfigureAwait(true);
            if (!read.Succeeded)
            {
                // The page keeps showing ids; the entry read failing never hides the Proposal.
                (_entryNames, _entryNamesKey, EntryRefusal) = (ProposalEntryNames.None, null, WindowRefusal.From(read.Refusal!));
                return;
            }
            names.Add(read.Value!.Lookups);
        }
        (_entryNames, _entryNamesKey, EntryRefusal) = (names, key, null);
    }
}
