using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Moves one sense, with its sub-senses and examples, out of its entry into a new entry of its own.</summary>
/// <param name="Sense">The top-level sense to move.</param>
/// <param name="Form">The new entry's lexeme form, without affix hyphens.</param>
/// <param name="FormWritingSystem">The vernacular writing system tag of <paramref name="Form"/>.</param>
/// <param name="MorphType">The existing morph type of the new entry's lexeme form.</param>
/// <param name="Msa">
/// The grammatical info of the new entry. Absent means the new entry gets a copy of the sense's own analysis,
/// which FieldWorks' "move sense to a copy of its entry" also does.
/// </param>
public sealed record MoveSenseToEntryIntent(
    CanonicalId Sense, string Form, string FormWritingSystem, CanonicalId MorphType, LexicalEntryMsa? Msa = null);

/// <summary>Parses the closed JSON intent for moving a sense to a new entry.</summary>
public static class MoveSenseToEntryIntentParser
{
    private const string ConstructName = "MoveSenseToEntry";

    /// <summary>Reads the sense, the new entry's form and morph type, and optionally its grammatical info.</summary>
    public static MoveSenseToEntryIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName, "sense", "form", "formWs", "morphType", "msa");
        return new MoveSenseToEntryIntent(
            SoundSystemIntentParsing.Id(authored, "sense"),
            SoundSystemIntentParsing.Text(authored, "form"),
            SoundSystemIntentParsing.Text(authored, "formWs"),
            SoundSystemIntentParsing.Id(authored, "morphType"),
            authored.TryGetProperty("msa", out _) ? AuthorLexicalEntryIntentParser.ParseMsa(authored, ConstructName) : null);
    }
}

/// <summary>
/// Lowers a sense move into the creates of a new entry (its lexeme form and analysis) and one
/// <see cref="LexSenseMoveOperationKinds.MoveToEntry"/>. The sense keeps its identity, glosses, definitions, examples
/// and sub-senses. As in FieldWorks, an entry's only sense cannot be moved, and the old entry's analysis is removed
/// when no sense uses it any more.
/// </summary>
public static class MoveSenseToEntryComposer
{
    private const string ConstructName = "MoveSenseToEntry";
    private const string Rationale = "Authored by the MoveSenseToEntry composer: the sense belongs to an entry of its own.";

    /// <summary>Composes the move without mutating the caller's project.</summary>
    /// <param name="cache">The project to resolve <paramref name="intent"/>'s references against.</param>
    /// <param name="intent">The authored move.</param>
    /// <param name="mintId">Overrides id minting; the Draft supplies one so ids stay unique within it.</param>
    public static IReadOnlyList<OperationEnvelope> Build(
        LcmCache cache, MoveSenseToEntryIntent intent, Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var sense = LexicalFormAuthoring.Resolve<ICmObject>(cache, intent.Sense, ConstructName) as ILexSense
            ?? throw new InvalidOperationException($"'{ConstructName}': the sense id must name a sense.");
        if (sense.Owner is not ILexEntry from)
            throw new InvalidOperationException($"'{ConstructName}': only a top-level sense of an entry can be moved.");
        if (from.SensesOS.Count < 2)
            throw new InvalidOperationException(
                $"'{ConstructName}': the entry has only this sense; FieldWorks does not offer the move then either, " +
                "because an entry must keep a sense. Edit the entry's lexeme form instead.");
        var moved = sense.AllSenses.ToArray();
        var oldMsa = LexSenseMoveHandler.RequireMovable(sense, moved, from);
        var msa = intent.Msa ?? CopyOf(cache, oldMsa);

        var source = mintId ?? (() => CanonicalId.Mint());
        var entry = new AuthorLexicalEntryIntent(intent.Form, intent.FormWritingSystem, intent.MorphType, "", "", msa);
        var operations = AuthorLexicalEntryComposer.BuildEntry(cache, entry, source, null, withSense: false).ToList();
        var createEntry = operations.Single(operation => operation.Kind == LexDbEntriesOperationKinds.Create);
        var createMsa = operations.Single(operation => operation.Kind == LexEntryMorphoSyntaxAnalysesOperationKinds.Create);
        var deleteMsa = oldMsa is not null && oldMsa.Owner?.Guid == from.Guid && LexSenseMoveHandler.Unused(from, moved, oldMsa)
            ? CanonicalId.FromGuid(oldMsa.Guid).Value : null;
        var after = JsonSerializer.SerializeToElement(new
        {
            entry = createEntry.EntityId!.Value.Value,
            msa = createMsa.EntityId!.Value.Value,
            fromEntry = CanonicalId.FromGuid(from.Guid).Value,
            deleteMsa,
        });
        operations.Add(new OperationEnvelope(LexicalFormAuthoring.Normalize(source()), LexSenseMoveOperationKinds.MoveToEntry,
            target: LexicalFormAuthoring.Normalize(intent.Sense), after: after, rationale: Rationale,
            dependsOn: [new OperationDependency(createEntry.OperationId), new OperationDependency(createMsa.OperationId)]));
        return operations;
    }

    /// <summary>The new entry's analysis when none is given: a copy of the sense's own.</summary>
    private static LexicalEntryMsa CopyOf(LcmCache cache, IMoMorphSynAnalysis? old)
    {
        if (old is null)
            throw new InvalidOperationException(
                $"'{ConstructName}': the sense has no analysis to copy; give 'msa' with a kind and category.");
        static CanonicalId Id(ICmObject? item) => item is null ? throw new InvalidOperationException(
            $"'{ConstructName}': the sense's analysis has no category to copy; give 'msa' with a kind and category.")
            : CanonicalId.FromGuid(item.Guid);
        (LexicalEntryMsa Msa, string[] Ignored) copy = old switch
        {
            IMoStemMsa stem => (new(LexicalEntryMsaKind.Stem, Id(stem.PartOfSpeechRA),
                InflectionClass: stem.InflectionClassRA is { } cls ? CanonicalId.FromGuid(cls.Guid) : null),
                ["PartOfSpeech", "InflectionClass"]),
            IMoInflAffMsa inflectional => (new(LexicalEntryMsaKind.Inflectional, Id(inflectional.PartOfSpeechRA),
                Slots: inflectional.SlotsRC.Select(slot => CanonicalId.FromGuid(slot.Guid)).ToArray()),
                ["PartOfSpeech", "Slots"]),
            IMoDerivAffMsa derivational => (new(LexicalEntryMsaKind.Derivational, Id(derivational.FromPartOfSpeechRA),
                Id(derivational.ToPartOfSpeechRA)), ["FromPartOfSpeech", "ToPartOfSpeech"]),
            IMoUnclassifiedAffixMsa unclassified => (new(LexicalEntryMsaKind.Unclassified, Id(unclassified.PartOfSpeechRA)),
                ["PartOfSpeech"]),
            _ => throw new InvalidOperationException(
                $"'{ConstructName}': an analysis of class {old.ClassName} cannot be copied; give 'msa' instead."),
        };
        if (LexSenseEmptiness.FirstPopulatedField(cache, old, copy.Ignored) is { } extra)
            throw new InvalidOperationException(
                $"'{ConstructName}': the sense's analysis has content in '{extra}' that a copy would drop; give 'msa' instead.");
        if (copy.Msa.Slots is { Count: 0 }) copy = (copy.Msa with { Slots = null }, copy.Ignored);
        return copy.Msa;
    }
}
