using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>One more grammatical info (morphosyntactic analysis) for an entry that already has one.</summary>
/// <param name="Entry">The existing entry that gains the analysis.</param>
/// <param name="Msa">The new grammatical info; a stem for a stem entry, an affix kind for an affix entry.</param>
/// <param name="Senses">Existing senses of the entry that should point at the new analysis.</param>
/// <param name="Gloss">When given, a new sense with this gloss is added and points at the new analysis.</param>
/// <param name="AnalysisWritingSystem">The writing system tag of the new sense's gloss and definition.</param>
/// <param name="Definition">The new sense's optional definition.</param>
/// <param name="FromInflectionClass">For a derivational analysis, the one inflection class stems must belong to.</param>
public sealed record AddMorphoSyntaxAnalysisIntent(
    CanonicalId Entry,
    LexicalEntryMsa Msa,
    IReadOnlyList<CanonicalId>? Senses = null,
    string? Gloss = null,
    string? AnalysisWritingSystem = null,
    string? Definition = null,
    CanonicalId? FromInflectionClass = null);

/// <summary>Parses the closed JSON intent for adding a grammatical info to an entry.</summary>
public static class AddMorphoSyntaxAnalysisIntentParser
{
    private const string ConstructName = "AddMorphoSyntaxAnalysis";

    /// <summary>Reads the entry, the new grammatical info and the senses that should use it.</summary>
    public static AddMorphoSyntaxAnalysisIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName, "entry", "msa", "senses", "gloss", "analysisWs",
            "definition", "fromInflectionClass");
        var gloss = authored.TryGetProperty("gloss", out _) ? SoundSystemIntentParsing.Text(authored, "gloss") : null;
        var analysisWs = authored.TryGetProperty("analysisWs", out _)
            ? SoundSystemIntentParsing.Text(authored, "analysisWs") : null;
        var definition = authored.TryGetProperty("definition", out _)
            ? SoundSystemIntentParsing.Text(authored, "definition") : null;
        if ((gloss is null) != (analysisWs is null))
            throw new ContractParseException($"'{ConstructName}': 'gloss' and 'analysisWs' go together.");
        if (definition is not null && gloss is null)
            throw new ContractParseException($"'{ConstructName}': 'definition' belongs to a new sense, so it needs 'gloss'.");
        var intent = new AddMorphoSyntaxAnalysisIntent(SoundSystemIntentParsing.Id(authored, "entry"),
            AuthorLexicalEntryIntentParser.ParseMsa(authored, ConstructName),
            AuthorLexicalEntryIntentParser.NonEmptyIds(authored, "senses"), gloss, analysisWs, definition,
            authored.TryGetProperty("fromInflectionClass", out _)
                ? SoundSystemIntentParsing.Id(authored, "fromInflectionClass") : null);
        if (intent.FromInflectionClass is not null && intent.Msa.Kind != LexicalEntryMsaKind.Derivational)
            throw new ContractParseException(
                $"'{ConstructName}': only a derivational msa takes 'fromInflectionClass'.");
        return intent;
    }
}

/// <summary>
/// Lowers one <see cref="AddMorphoSyntaxAnalysisIntent"/> into the operations that give an existing entry a second
/// grammatical info beside the ones it has, as LibLCM and FieldWorks hold it: an entry owns any number of analyses,
/// each sense points at one, and the parser loads every analysis of an entry as its own rule or stem. So one suffix
/// can serve two categories, and one derivational affix can carry one rule per host class.
/// </summary>
public static class AddMorphoSyntaxAnalysisComposer
{
    private const string ConstructName = "AddMorphoSyntaxAnalysis";
    private const string Rationale = "Authored by the AddMorphoSyntaxAnalysis composer.";

    /// <summary>Builds the operations without mutating the caller's project.</summary>
    /// <param name="cache">The project to resolve <paramref name="intent"/>'s references against.</param>
    /// <param name="intent">The authored analysis.</param>
    /// <param name="mintId">Overrides id minting; tests supply a deterministic source.</param>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, AddMorphoSyntaxAnalysisIntent intent,
        Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(intent.Msa);
        var entryId = LexicalFormAuthoring.Normalize(intent.Entry);
        var msa = intent.Msa with
        {
            Category = LexicalFormAuthoring.Normalize(intent.Msa.Category),
            ToCategory = intent.Msa.ToCategory is { } to ? LexicalFormAuthoring.Normalize(to) : null,
            InflectionClass = intent.Msa.InflectionClass is { } cls ? LexicalFormAuthoring.Normalize(cls) : null,
            Slots = LexicalFormAuthoring.Normalize(intent.Msa.Slots),
        };
        AuthorLexicalEntryComposer.RequireMsaShape(msa, ConstructName);
        var entry = LexicalFormAuthoring.Resolve<ILexEntry>(cache, entryId, ConstructName);
        var isStemForm = entry.LexemeFormOA switch
        {
            IMoStemAllomorph => true,
            IMoAffixAllomorph or IMoAffixProcess => false,
            _ => throw new InvalidOperationException(
                $"'{ConstructName}': entry '{entryId.Value}' has no lexeme form, so it is neither a stem nor an affix."),
        };
        if (isStemForm != (msa.Kind == LexicalEntryMsaKind.Stem))
            throw new InvalidOperationException(
                $"'{ConstructName}': entry '{entryId.Value}' has " +
                (isStemForm ? "a stem form, so its analysis must be a stem msa." : "an affix form, so its analysis must be an affix msa, not a stem msa."));
        var category = AuthorLexicalEntryComposer.RequireMsaReferences(cache, msa, ConstructName);
        CanonicalId? fromClass = null;
        if (intent.FromInflectionClass is { } classId)
        {
            if (msa.Kind != LexicalEntryMsaKind.Derivational)
                throw new InvalidOperationException($"'{ConstructName}': only a derivational msa takes 'fromInflectionClass'.");
            fromClass = LexicalFormAuthoring.RequireInflectionClasses(cache, [classId], [category], ConstructName)[0];
        }

        if ((intent.Senses is null || intent.Senses.Count == 0) && intent.Gloss is null)
            throw new InvalidOperationException(
                $"'{ConstructName}': give 'senses' or 'gloss', because an analysis no sense uses is removed by FieldWorks the next time the project is checked or opened.");
        RequireNotDuplicate(entry, msa, fromClass);

        var senseIds = (intent.Senses ?? []).Select(LexicalFormAuthoring.Normalize).ToArray();
        if (senseIds.Select(id => id.ToGuid()).Distinct().Count() != senseIds.Length)
            throw new InvalidOperationException($"'{ConstructName}': 'senses' must not repeat an identity.");
        var owned = entry.AllSenses.Select(sense => sense.Guid).ToHashSet();
        foreach (var senseId in senseIds)
        {
            LexicalFormAuthoring.Resolve<ILexSense>(cache, senseId, ConstructName);
            if (!owned.Contains(senseId.ToGuid()))
                throw new InvalidOperationException(
                    $"'{ConstructName}': sense '{senseId.Value}' does not belong to entry '{entryId.Value}'.");
        }
        var goneNote = RequireNoRemainingReferences(entry, senseIds);
        var gloss = intent.Gloss is null ? null : LexicalFormAuthoring.RequireText(intent.Gloss, "gloss", ConstructName);
        var definition = intent.Definition is null || gloss is null
            ? null : LexicalFormAuthoring.RequireText(intent.Definition, "definition", ConstructName);
        if (gloss is not null)
            LexicalFormAuthoring.RequireWritingSystem(cache, intent.AnalysisWritingSystem ?? "", vernacular: false, ConstructName);

        var source = mintId ?? (() => CanonicalId.Mint());
        CanonicalId Mint() => LexicalFormAuthoring.Normalize(source());
        var operations = new List<OperationEnvelope>();
        var msaId = Mint();
        var createMsa = Add(LexEntryMorphoSyntaxAnalysesOperationKinds.Create, entryId,
            new { @class = AuthorLexicalEntryComposer.MsaClass(msa.Kind) }, entityId: msaId);
        var afterMsa = new[] { new OperationDependency(createMsa) };
        foreach (var (kind, after) in AuthorLexicalEntryComposer.MsaFieldWrites(msa, msa.Slots ?? []))
            Add(kind, msaId, after, dependsOn: afterMsa);
        if (fromClass is { } wanted)
            Add(MoDerivAffMsaFromInflectionClassOperationKinds.SetFromInflectionClass, msaId,
                new { @ref = wanted.Value }, dependsOn: afterMsa);
        foreach (var senseId in senseIds)
            Add(LexSenseMorphoSyntaxAnalysisOperationKinds.SetMorphoSyntaxAnalysis, senseId, new { @ref = msaId.Value },
                dependsOn: afterMsa, rationale: goneNote.TryGetValue(senseId.ToGuid(), out var note) ? note : null);
        if (gloss is not null)
        {
            var newSense = Mint();
            var last = entry.SensesOS.LastOrDefault();
            var createSense = Add(LexEntrySensesOperationKinds.Create, entryId, new { }, entityId: newSense,
                placement: last is null ? null : new Placement(CanonicalId.FromGuid(last.Guid), null));
            var afterSense = new[] { new OperationDependency(createSense) };
            Add(LexicalSenseOperationKinds.SetGloss, newSense,
                new { ws = intent.AnalysisWritingSystem, text = gloss }, dependsOn: afterSense);
            if (definition is not null)
                Add(LexSenseDefinitionOperationKinds.SetDefinition, newSense,
                    new { ws = intent.AnalysisWritingSystem, text = definition }, dependsOn: afterSense);
            Add(LexSenseMorphoSyntaxAnalysisOperationKinds.SetMorphoSyntaxAnalysis, newSense, new { @ref = msaId.Value },
                dependsOn: [.. afterSense, .. afterMsa]);
        }
        return operations;

        CanonicalId Add(string kind, CanonicalId target, object after, CanonicalId? entityId = null,
            IReadOnlyList<OperationDependency>? dependsOn = null, Placement? placement = null, string? rationale = null)
        {
            var operationId = Mint();
            operations.Add(new OperationEnvelope(operationId, kind, entityId: entityId, target: target,
                placement: placement, after: JsonSerializer.SerializeToElement(after), dependsOn: dependsOn,
                rationale: rationale ?? Rationale));
            return operationId;
        }
    }

    private static void RequireNotDuplicate(ILexEntry entry, LexicalEntryMsa msa, CanonicalId? fromClass)
    {
        var slots = (msa.Slots ?? []).Select(id => id.ToGuid()).Order().ToArray();
        var found = entry.MorphoSyntaxAnalysesOC.Any(existing => existing switch
        {
            IMoStemMsa stem => msa.Kind == LexicalEntryMsaKind.Stem && stem.PartOfSpeechRA?.Guid == msa.Category.ToGuid()
                && stem.InflectionClassRA?.Guid == msa.InflectionClass?.ToGuid(),
            IMoInflAffMsa infl => msa.Kind == LexicalEntryMsaKind.Inflectional && infl.PartOfSpeechRA?.Guid == msa.Category.ToGuid()
                && infl.SlotsRC.Select(slot => slot.Guid).Order().SequenceEqual(slots),
            IMoDerivAffMsa deriv => msa.Kind == LexicalEntryMsaKind.Derivational
                && deriv.FromPartOfSpeechRA?.Guid == msa.Category.ToGuid()
                && deriv.ToPartOfSpeechRA?.Guid == msa.ToCategory?.ToGuid()
                && deriv.FromInflectionClassRA?.Guid == fromClass?.ToGuid(),
            IMoUnclassifiedAffixMsa plain => msa.Kind == LexicalEntryMsaKind.Unclassified
                && plain.PartOfSpeechRA?.Guid == msa.Category.ToGuid(),
            _ => false,
        });
        if (found)
            throw new InvalidOperationException(
                $"'{ConstructName}': entry '{CanonicalId.FromGuid(entry.Guid).Value}' already has an identical analysis (same category, classes and slots); point its senses at that one instead.");
    }

    private static Dictionary<Guid, string> RequireNoRemainingReferences(ILexEntry entry, CanonicalId[] senseIds)
    {
        var moved = senseIds.Select(id => id.ToGuid()).ToHashSet();
        var notes = new Dictionary<Guid, string>();
        var olds = entry.AllSenses.Where(sense => moved.Contains(sense.Guid) && sense.MorphoSyntaxAnalysisRA is not null)
            .GroupBy(sense => sense.MorphoSyntaxAnalysisRA);
        foreach (var group in olds)
        {
            var old = group.Key;
            if (entry.AllSenses.Any(sense => !moved.Contains(sense.Guid) && sense.MorphoSyntaxAnalysisRA?.Guid == old.Guid))
                continue;
            var oldId = CanonicalId.FromGuid(old.Guid).Value;
            var users = old.ReferringObjects.Where(other => other.Guid != old.Guid && other is not ILexSense)
                .Where(other => other is not IWfiMorphBundle bundle || bundle.SenseRA is null || !moved.Contains(bundle.SenseRA.Guid))
                .Select(other => other switch
                {
                    IMoAdhocProhib => $"an ad hoc prohibition ({other.Guid:D})",
                    IWfiMorphBundle => $"a morph bundle ({other.Guid:D})",
                    _ => null,
                }).OfType<string>()
                .ToArray();
            if (users.Length > 0)
                throw new InvalidOperationException(
                    $"'{ConstructName}': moving the senses would leave analysis '{oldId}' with no sense, but {string.Join(", ", users)} still refers to it. " +
                    "Either keep one sense on the old analysis by leaving it out of 'senses', or remove that reference first (for an ad hoc prohibition, remove the prohibition).");
            var slotNames = (old as IMoInflAffMsa)?.SlotsRC
                .Select(slot => $"'{slot.Name.BestAnalysisAlternative.Text}' ({slot.Guid:D})").ToArray() ?? [];
            var slotNote = slotNames.Length == 0 ? "" : $" The old analysis's membership of slot {string.Join(", ", slotNames)} goes with it.";
            foreach (var sense in group)
                notes[sense.Guid] = $"Authored by the {ConstructName} composer. No sense will use analysis '{oldId}' afterwards, so LibLCM removes that analysis.{slotNote}";
        }
        return notes;
    }
}
