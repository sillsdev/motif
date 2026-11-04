using System;
using System.Collections.Generic;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Deletes one whole dictionary entry that nothing outside it refers to.</summary>
/// <param name="Entry">The entry to delete.</param>
public sealed record DeleteLexicalEntryIntent(CanonicalId Entry);

/// <summary>Parses the closed JSON intent for deleting an entry.</summary>
public static class DeleteLexicalEntryIntentParser
{
    /// <summary>Reads the entry; it is required.</summary>
    public static DeleteLexicalEntryIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "DeleteLexicalEntry", "entry");
        return new(LexicalFormAuthoring.Normalize(SoundSystemIntentParsing.Id(authored, "entry")));
    }
}

/// <summary>
/// Lowers an entry deletion to one <see cref="LexEntryRemoveOperationKinds.Remove"/>. An entry that a variant,
/// complex form, lexical relation, parser analysis or ad hoc prohibition still refers to is refused naming the
/// referrer, so a reference is never silently dropped.
/// </summary>
public static class DeleteLexicalEntryComposer
{
    private const string ConstructName = "DeleteLexicalEntry";
    private const string Rationale = "Authored by the DeleteLexicalEntry composer: the entry is redundant and unreferenced.";

    /// <summary>Composes the deletion without mutating the caller's project.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, DeleteLexicalEntryIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var entry = LexicalFormAuthoring.Resolve<ICmObject>(cache, intent.Entry, ConstructName) as ILexEntry
            ?? throw new InvalidOperationException($"'{ConstructName}': the entry id must name an entry.");
        LexEntryRemoval.Inspect(entry);
        return [new(CanonicalId.Mint(), LexEntryRemoveOperationKinds.Remove, target: LexicalFormAuthoring.Normalize(intent.Entry),
            after: JsonSerializer.SerializeToElement(new { lexicon = CanonicalId.FromGuid(cache.LangProject.LexDbOA.Guid).Value }), rationale: Rationale)];
    }
}
