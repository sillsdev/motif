using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Review;

/// <summary>Reads, once at capture, the lexeme forms the Proposal's variant links name, so the Trial never reopens the project.</summary>
/// <remarks>O(variant links) object lookups by id; nothing is parsed and no entry is scanned.</remarks>
internal static class VariantFormCapture
{
    private const string CreateVariantRef = "lexical/lexEntry/createVariantRef";

    internal static IReadOnlyDictionary<string, string>? Read(LcmCache cache, ProposalRecord? record)
    {
        if (record is null) return null;
        var proposal = ProposalJsonParser.Parse(JobCommands.CanonicalProposalJson(record));
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        var forms = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var operation in proposal.Operations.Where(item => item.Kind == CreateVariantRef))
        {
            if (operation.Target is { } target) Add(repository, forms, target.ToGuid());
            if (operation.After is { ValueKind: System.Text.Json.JsonValueKind.Object } after &&
                after.TryGetProperty("component", out var component) && CanonicalId.TryParse(component.GetString(), out var id))
                Add(repository, forms, id.ToGuid());
        }
        return forms.Count == 0 ? null : forms;
    }

    private static void Add(ICmObjectRepository repository, Dictionary<string, string> forms, Guid guid)
    {
        if (!repository.TryGetObject(guid, out var item)) return;
        var entry = item as ILexEntry ?? (item as ILexSense)?.Entry;
        var text = entry?.LexemeFormOA?.Form.BestVernacularAlternative?.Text;
        if (!string.IsNullOrEmpty(text)) forms[CanonicalId.FromGuid(guid).CanonicalSuffix] = text;
    }
}
