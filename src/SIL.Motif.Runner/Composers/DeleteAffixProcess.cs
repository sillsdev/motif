using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Deletes a whole affix-process graph, optionally keeping a plain allomorph as its replacement.</summary>
public sealed record DeleteAffixProcessIntent(CanonicalId Entry, CanonicalId Form, CanonicalId? Replacement = null);

public static class DeleteAffixProcessIntentParser
{
    public static DeleteAffixProcessIntent Parse(JsonElement value)
    {
        SoundSystemIntentParsing.Object(value, "DeleteAffixProcess", "entry", "form", "replacement");
        return new(SoundSystemIntentParsing.Id(value, "entry"), SoundSystemIntentParsing.Id(value, "form"),
            value.TryGetProperty("replacement", out _) ? SoundSystemIntentParsing.Id(value, "replacement") : null);
    }
}

public static class DeleteAffixProcessComposer
{
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, DeleteAffixProcessIntent intent)
    {
        var entry = ReferenceFieldLowering.Resolve<ILexEntry>(cache, intent.Entry, "DeleteAffixProcess");
        var process = ReferenceFieldLowering.Resolve<IMoAffixProcess>(cache, intent.Form, "DeleteAffixProcess");
        AffixProcessDeletion.Validate(cache, entry, process, intent.Replacement);
        return [new(CanonicalId.Mint(), AffixProcessDeleteOperationKinds.Delete, target: intent.Form,
            after: JsonSerializer.SerializeToElement(new
            {
                entry = CanonicalId.FromGuid(entry.Guid).Value,
                lexeme = entry.LexemeFormOA?.Guid == process.Guid,
                replacement = intent.Replacement?.Value,
                graphDigest = OwnedGraphGuard.Digest(cache, process),
                members = OwnedGraphGuard.Members(process).Select(item => CanonicalId.FromGuid(item.Guid).Value).ToArray(),
            }), rationale: "Delete the complete process graph after its references have been explicitly moved or removed.")];
    }
}
