using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Deletes one sense, top-level or sub-sense, that carries content, with its sub-senses and examples.</summary>
/// <param name="Target">The sense to delete.</param>
/// <param name="Owner">The entry (for a top-level sense) or parent sense (for a sub-sense) that owns it, named so a wrong id is refused rather than deleted.</param>
public sealed record DeleteSenseIntent(CanonicalId Target, CanonicalId Owner);

/// <summary>Parses the closed JSON intent for deleting a sense.</summary>
public static class DeleteSenseIntentParser
{
    /// <summary>Reads the sense and its owner; both are required.</summary>
    public static DeleteSenseIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "DeleteSense", "target", "owner");
        return new(LexicalFormAuthoring.Normalize(SoundSystemIntentParsing.Id(authored, "target")),
            LexicalFormAuthoring.Normalize(SoundSystemIntentParsing.Id(authored, "owner")));
    }
}

/// <summary>
/// Lowers a sense deletion to one remove operation. LibLCM deletes a sense's owned
/// sub-senses and examples and silently drops references to it, so the composer declines a sense that anything refers
/// to, and an entry's only sense. The sense's analysis is removed too, but only when no remaining sense and no other
/// object uses it.
/// </summary>
public static class DeleteSenseComposer
{
    private const string ConstructName = "DeleteSense";
    private const string Rationale = "Authored by the DeleteSense composer: the sense duplicates content kept elsewhere.";

    /// <summary>Composes the deletion without mutating the caller's project.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, DeleteSenseIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var sense = LexicalFormAuthoring.Resolve<ICmObject>(cache, intent.Target, ConstructName) as ILexSense
            ?? throw new InvalidOperationException($"'{ConstructName}': the target must be a sense.");
        if (sense.Owner.Guid != intent.Owner.ToGuid())
            throw new InvalidOperationException(
                $"'{ConstructName}': the sense belongs to a different owner than the one named.");
        var plan = SenseRemoval.Inspect(sense);
        var after = JsonSerializer.SerializeToElement(new
        {
            owner = CanonicalId.FromGuid(plan.Owner.Guid).Value,
            topLevel = plan.TopLevel,
            entry = CanonicalId.FromGuid(plan.Entry.Guid).Value,
            msas = plan.OrphanedAnalyses.Select(msa => CanonicalId.FromGuid(msa.Guid).Value).Order(StringComparer.Ordinal).ToArray(),
        });
        return [new(CanonicalId.Mint(), LexSenseRemoveOperationKinds.Remove,
            target: LexicalFormAuthoring.Normalize(intent.Target), after: after, rationale: Rationale)];
    }
}
