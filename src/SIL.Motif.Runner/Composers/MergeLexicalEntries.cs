using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Merges one entry into another, as FieldWorks' Merge Entry does.</summary>
/// <param name="Entry">The entry that is merged and ceases to exist.</param>
/// <param name="Into">The entry that survives.</param>
public sealed record MergeLexicalEntriesIntent(CanonicalId Entry, CanonicalId Into);

/// <summary>Parses the closed JSON intent for merging two entries.</summary>
public static class MergeLexicalEntriesIntentParser
{
    /// <summary>Reads both entries; both are required.</summary>
    public static MergeLexicalEntriesIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "MergeLexicalEntries", "entry", "into");
        return new(LexicalFormAuthoring.Normalize(SoundSystemIntentParsing.Id(authored, "entry")),
            LexicalFormAuthoring.Normalize(SoundSystemIntentParsing.Id(authored, "into")));
    }
}

/// <summary>
/// Lowers an entry merge to one <see cref="LexEntryMergeOperationKinds.Merge"/>, refusing a self-merge or a non-entry.
/// </summary>
public static class MergeLexicalEntriesComposer
{
    private const string ConstructName = "MergeLexicalEntries";
    private const string Rationale = "Authored by the MergeLexicalEntries composer: the two entries are one lexical item.";

    /// <summary>Composes the merge without mutating the caller's project.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, MergeLexicalEntriesIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var merged = LexicalFormAuthoring.Resolve<ICmObject>(cache, intent.Entry, ConstructName) as ILexEntry
            ?? throw new InvalidOperationException($"'{ConstructName}': 'entry' must name an entry.");
        var survivor = LexicalFormAuthoring.Resolve<ICmObject>(cache, intent.Into, ConstructName) as ILexEntry
            ?? throw new InvalidOperationException($"'{ConstructName}': 'into' must name an entry.");
        LexEntryMerging.Inspect(merged, survivor);
        return [new(CanonicalId.Mint(), LexEntryMergeOperationKinds.Merge, target: intent.Entry,
            after: JsonSerializer.SerializeToElement(new
            {
                lexicon = CanonicalId.FromGuid(cache.LangProject.LexDbOA.Guid).Value,
                into = intent.Into.Value,
            }), rationale: Rationale)];
    }
}
