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

/// <summary>The kind of grammatical info a new entry's sense carries, which decides how the parser uses it.</summary>
public enum LexicalEntryMsaKind
{
    /// <summary>A stem or root of one category.</summary>
    Stem,
    /// <summary>An inflectional affix attached to words of one category, optionally in slots.</summary>
    Inflectional,
    /// <summary>A derivational affix turning words of one category into another.</summary>
    Derivational,
    /// <summary>An affix whose function is not yet analysed, attached to words of one category.</summary>
    Unclassified,
}

/// <summary>The grammatical info (FieldWorks' morphosyntactic analysis) of a new entry.</summary>
/// <param name="Kind">Stem, inflectional, derivational, or unclassified.</param>
/// <param name="Category">The part of speech; for a derivational affix, the category it attaches to.</param>
/// <param name="ToCategory">For a derivational affix only, the category of the word it makes.</param>
/// <param name="Slots">For an inflectional affix only, the existing slots it fills.</param>
/// <param name="InflectionClass">For a stem only, its inflection class, such as a noun class.</param>
public sealed record LexicalEntryMsa(
    LexicalEntryMsaKind Kind,
    CanonicalId Category,
    CanonicalId? ToCategory = null,
    IReadOnlyList<CanonicalId>? Slots = null,
    CanonicalId? InflectionClass = null);

/// <summary>One new dictionary entry with one sense and typed grammatical info.</summary>
/// <param name="Form">The lexeme form, without affix hyphens.</param>
/// <param name="FormWritingSystem">The vernacular writing system tag of <paramref name="Form"/>.</param>
/// <param name="MorphType">The existing morph type, such as stem, root, prefix or suffix.</param>
/// <param name="Gloss">The sense's gloss.</param>
/// <param name="AnalysisWritingSystem">The writing system tag of the gloss and definition.</param>
/// <param name="Msa">The sense's grammatical info.</param>
/// <param name="Definition">The sense's optional definition.</param>
/// <param name="InflectionClasses">
/// For an affix form only, the inflection classes of the stems it may attach to; absent means unrestricted.
/// </param>
/// <param name="Environments">The phonological environments the lexeme form occurs in; absent means any.</param>
public sealed record AuthorLexicalEntryIntent(
    string Form,
    string FormWritingSystem,
    CanonicalId MorphType,
    string Gloss,
    string AnalysisWritingSystem,
    LexicalEntryMsa Msa,
    string? Definition = null,
    IReadOnlyList<CanonicalId>? InflectionClasses = null,
    IReadOnlyList<CanonicalId>? Environments = null);

/// <summary>Parses the closed JSON intent for creating one dictionary entry.</summary>
public static class AuthorLexicalEntryIntentParser
{
    private const string ConstructName = "AuthorLexicalEntry";

    /// <summary>Reads the form, morph type, gloss, optional definition and grammatical info.</summary>
    public static AuthorLexicalEntryIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName,
            "form", "formWs", "morphType", "gloss", "analysisWs", "definition", "msa", "inflectionClasses",
            "environments");
        if (!authored.TryGetProperty("msa", out var msa))
            throw new ContractParseException($"'{ConstructName}': 'msa' is required.");
        SoundSystemIntentParsing.Object(msa, ConstructName + ".msa",
            "kind", "category", "toCategory", "slots", "inflectionClass");
        var kind = SoundSystemIntentParsing.Text(msa, "kind") switch
        {
            "stem" => LexicalEntryMsaKind.Stem,
            "inflectional" => LexicalEntryMsaKind.Inflectional,
            "derivational" => LexicalEntryMsaKind.Derivational,
            "unclassified" => LexicalEntryMsaKind.Unclassified,
            var other => throw new ContractParseException(
                $"'{ConstructName}': msa kind '{other}' must be stem, inflectional, derivational or unclassified."),
        };
        var toCategory = Optional(msa, "toCategory");
        var slots = msa.TryGetProperty("slots", out _) ? SoundSystemIntentParsing.Ids(msa, "slots", ordered: false) : null;
        var inflectionClass = Optional(msa, "inflectionClass");
        if (toCategory is not null && kind != LexicalEntryMsaKind.Derivational)
            throw new ContractParseException($"'{ConstructName}': only a derivational msa takes 'toCategory'.");
        if (kind == LexicalEntryMsaKind.Derivational && toCategory is null)
            throw new ContractParseException($"'{ConstructName}': a derivational msa needs 'toCategory'.");
        if (slots is not null && kind != LexicalEntryMsaKind.Inflectional)
            throw new ContractParseException($"'{ConstructName}': only an inflectional msa takes 'slots'.");
        if (inflectionClass is not null && kind != LexicalEntryMsaKind.Stem)
            throw new ContractParseException($"'{ConstructName}': only a stem msa takes 'inflectionClass'.");
        return new AuthorLexicalEntryIntent(
            SoundSystemIntentParsing.Text(authored, "form"),
            SoundSystemIntentParsing.Text(authored, "formWs"),
            SoundSystemIntentParsing.Id(authored, "morphType"),
            SoundSystemIntentParsing.Text(authored, "gloss"),
            SoundSystemIntentParsing.Text(authored, "analysisWs"),
            new LexicalEntryMsa(kind, SoundSystemIntentParsing.Id(msa, "category"), toCategory, slots, inflectionClass),
            authored.TryGetProperty("definition", out _) ? SoundSystemIntentParsing.Text(authored, "definition") : null,
            NonEmptyIds(authored, "inflectionClasses"), NonEmptyIds(authored, "environments"));
    }

    /// <summary>Reads an optional unordered identity list; present means nonempty and free of repeats.</summary>
    internal static IReadOnlyList<CanonicalId>? NonEmptyIds(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out _)) return null;
        var ids = SoundSystemIntentParsing.Ids(element, name, ordered: false);
        if (ids.Length == 0) throw new ContractParseException($"'{name}' must name at least one identity when present.");
        if (ids.Select(id => id.ToGuid()).Distinct().Count() != ids.Length)
            throw new ContractParseException($"'{name}' must not repeat an identity.");
        return ids;
    }

    private static CanonicalId? Optional(JsonElement element, string name) =>
        element.TryGetProperty(name, out _) ? SoundSystemIntentParsing.Id(element, name) : null;
}

/// <summary>
/// Lowers one <see cref="AuthorLexicalEntryIntent"/> into the Layer-0 operations that make a complete entry:
/// the entry, its lexeme form, its grammatical info with category and slots or class, and one sense with gloss,
/// optional definition, and a link to that grammatical info. Every write after a create declares its dependency.
/// </summary>
public static class AuthorLexicalEntryComposer
{
    private const string ConstructName = "AuthorLexicalEntry";
    private const string Rationale = "Authored by the AuthorLexicalEntry composer.";

    /// <summary>Authors one entry without mutating the caller's project.</summary>
    /// <param name="cache">The project to resolve <paramref name="intent"/>'s references against.</param>
    /// <param name="intent">The authored entry.</param>
    /// <param name="mintId">Overrides id minting; tests supply a deterministic source.</param>
    public static IReadOnlyList<OperationEnvelope> Build(
        LcmCache cache, AuthorLexicalEntryIntent intent, Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(intent.Msa);
        intent = intent with
        {
            MorphType = LexicalFormAuthoring.Normalize(intent.MorphType),
            InflectionClasses = LexicalFormAuthoring.Normalize(intent.InflectionClasses),
            Environments = LexicalFormAuthoring.Normalize(intent.Environments),
            Msa = intent.Msa with
            {
                Category = LexicalFormAuthoring.Normalize(intent.Msa.Category),
                ToCategory = intent.Msa.ToCategory is { } to ? LexicalFormAuthoring.Normalize(to) : null,
                InflectionClass = intent.Msa.InflectionClass is { } cls ? LexicalFormAuthoring.Normalize(cls) : null,
                Slots = LexicalFormAuthoring.Normalize(intent.Msa.Slots),
            },
        };
        RequireMsaShape(intent.Msa);
        var form = LexicalFormAuthoring.RequireText(intent.Form, "form", ConstructName);
        var gloss = LexicalFormAuthoring.RequireText(intent.Gloss, "gloss", ConstructName);
        var definition = intent.Definition is null
            ? null : LexicalFormAuthoring.RequireText(intent.Definition, "definition", ConstructName);
        LexicalFormAuthoring.RequireWritingSystem(cache, intent.FormWritingSystem, vernacular: true, ConstructName);
        LexicalFormAuthoring.RequireWritingSystem(cache, intent.AnalysisWritingSystem, vernacular: false, ConstructName);

        var morphType = LexicalFormAuthoring.Resolve<IMoMorphType>(cache, intent.MorphType, ConstructName);
        var formClass = MoFormConcreteClassSelection.ClassFor(morphType, ConstructName);
        var msa = intent.Msa;
        if ((formClass == MoFormConcreteClass.StemAllomorph) != (msa.Kind == LexicalEntryMsaKind.Stem))
            throw new InvalidOperationException(
                $"'{ConstructName}': morph type '{morphType.Name.BestAnalysisAlternative.Text}' needs " +
                (formClass == MoFormConcreteClass.StemAllomorph ? "a stem msa." : "an affix msa, not a stem msa."));
        if (intent.InflectionClasses is not null && formClass != MoFormConcreteClass.AffixAllomorph)
            throw new InvalidOperationException($"'{ConstructName}': only an affix form takes 'inflectionClasses'.");
        var category = Category(cache, msa.Category);
        if (msa.ToCategory is { } toCategory) Category(cache, toCategory);
        var slots = msa.Slots ?? [];
        if (slots.Select(id => id.ToGuid()).Distinct().Count() != slots.Count)
            throw new InvalidOperationException($"'{ConstructName}': slots must not repeat.");
        foreach (var slotId in slots)
        {
            var slot = LexicalFormAuthoring.Resolve<IMoInflAffixSlot>(cache, slotId, ConstructName);
            if (!LexicalFormAuthoring.InCategoryOrAncestor(slot.Owner as IPartOfSpeech, category))
                throw new InvalidOperationException(
                    $"'{ConstructName}': slot '{slotId.Value}' does not belong to the category or one of its ancestors.");
        }
        if (msa.InflectionClass is { } classId)
        {
            var inflectionClass = LexicalFormAuthoring.Resolve<IMoInflClass>(cache, classId, ConstructName);
            if (!LexicalFormAuthoring.InCategoryOrAncestor(LexicalFormAuthoring.OwningCategory(inflectionClass), category))
                throw new InvalidOperationException(
                    $"'{ConstructName}': inflection class '{classId.Value}' does not belong to the category.");
        }
        IReadOnlyList<CanonicalId> environments = intent.Environments is null ? []
            : LexicalFormAuthoring.RequireEnvironments(cache, intent.Environments, ConstructName);
        IReadOnlyList<CanonicalId> restrictedClasses = intent.InflectionClasses is null ? []
            : LexicalFormAuthoring.RequireInflectionClasses(cache, intent.InflectionClasses, [category], ConstructName);

        var source = mintId ?? (() => CanonicalId.Mint());
        CanonicalId Mint() => LexicalFormAuthoring.Normalize(source());
        var operations = new List<OperationEnvelope>();
        var entryId = Mint();
        var createEntry = Add(LexDbEntriesOperationKinds.Create, CanonicalId.FromGuid(cache.LangProject.LexDbOA.Guid),
            new { }, entityId: entryId);
        var afterEntry = new[] { new OperationDependency(createEntry) };
        var formId = Mint();
        var createForm = Add(LexEntryLexemeFormOperationKinds.CreateLexemeForm, entryId,
            new { morphType = intent.MorphType.Value, ws = intent.FormWritingSystem, text = form },
            entityId: formId, dependsOn: afterEntry);
        foreach (var (kind, member) in LexicalFormAuthoring.Links(formClass, environments, restrictedClasses))
            Add(kind, formId, new { member = member.Value }, dependsOn: [new OperationDependency(createForm)]);

        var msaId = Mint();
        var createMsa = Add(LexEntryMorphoSyntaxAnalysesOperationKinds.Create, entryId,
            new { @class = MsaClass(msa.Kind) }, entityId: msaId, dependsOn: afterEntry);
        var afterMsa = new[] { new OperationDependency(createMsa) };
        switch (msa.Kind)
        {
            case LexicalEntryMsaKind.Stem:
                Add(MoStemMsaPartOfSpeechOperationKinds.SetPartOfSpeech, msaId, new { @ref = msa.Category.Value },
                    dependsOn: afterMsa);
                if (msa.InflectionClass is { } inflectionClass)
                    Add(MoStemMsaInflectionClassOperationKinds.SetInflectionClass, msaId,
                        new { @ref = inflectionClass.Value }, dependsOn: afterMsa);
                break;
            case LexicalEntryMsaKind.Inflectional:
                Add(MoInflAffMsaPartOfSpeechOperationKinds.SetPartOfSpeech, msaId, new { @ref = msa.Category.Value },
                    dependsOn: afterMsa);
                foreach (var slot in slots.OrderBy(id => id.Value, StringComparer.Ordinal))
                    Add(MoInflAffMsaSlotsOperationKinds.AddRefSlots, msaId, new { member = slot.Value },
                        dependsOn: afterMsa);
                break;
            case LexicalEntryMsaKind.Derivational:
                Add(MoDerivAffMsaFromPartOfSpeechOperationKinds.SetFromPartOfSpeech, msaId,
                    new { @ref = msa.Category.Value }, dependsOn: afterMsa);
                Add(MoDerivAffMsaToPartOfSpeechOperationKinds.SetToPartOfSpeech, msaId,
                    new { @ref = msa.ToCategory!.Value.Value }, dependsOn: afterMsa);
                break;
            case LexicalEntryMsaKind.Unclassified:
                Add(MoUnclassifiedAffixMsaPartOfSpeechOperationKinds.SetPartOfSpeech, msaId,
                    new { @ref = msa.Category.Value }, dependsOn: afterMsa);
                break;
        }

        var senseId = Mint();
        var createSense = Add(LexEntrySensesOperationKinds.Create, entryId, new { }, entityId: senseId,
            dependsOn: afterEntry);
        var afterSense = new[] { new OperationDependency(createSense) };
        Add(LexicalSenseOperationKinds.SetGloss, senseId, new { ws = intent.AnalysisWritingSystem, text = gloss },
            dependsOn: afterSense);
        if (definition is not null)
            Add(LexSenseDefinitionOperationKinds.SetDefinition, senseId,
                new { ws = intent.AnalysisWritingSystem, text = definition }, dependsOn: afterSense);
        Add(LexSenseMorphoSyntaxAnalysisOperationKinds.SetMorphoSyntaxAnalysis, senseId, new { @ref = msaId.Value },
            dependsOn: [new OperationDependency(createSense), new OperationDependency(createMsa)]);
        return operations;

        CanonicalId Add(string kind, CanonicalId target, object after, CanonicalId? entityId = null,
            IReadOnlyList<OperationDependency>? dependsOn = null)
        {
            var operationId = Mint();
            operations.Add(new OperationEnvelope(operationId, kind, entityId: entityId, target: target,
                after: JsonSerializer.SerializeToElement(after), dependsOn: dependsOn, rationale: Rationale));
            return operationId;
        }
    }

    private static void RequireMsaShape(LexicalEntryMsa msa)
    {
        if (!Enum.IsDefined(msa.Kind))
            throw new InvalidOperationException($"'{ConstructName}': unknown msa kind '{msa.Kind}'.");
        if (msa.ToCategory is not null && msa.Kind != LexicalEntryMsaKind.Derivational)
            throw new InvalidOperationException($"'{ConstructName}': only a derivational msa takes 'toCategory'.");
        if (msa.Kind == LexicalEntryMsaKind.Derivational && msa.ToCategory is null)
            throw new InvalidOperationException($"'{ConstructName}': a derivational msa needs 'toCategory'.");
        if (msa.Slots is not null && msa.Kind != LexicalEntryMsaKind.Inflectional)
            throw new InvalidOperationException($"'{ConstructName}': only an inflectional msa takes 'slots'.");
        if (msa.InflectionClass is not null && msa.Kind != LexicalEntryMsaKind.Stem)
            throw new InvalidOperationException($"'{ConstructName}': only a stem msa takes 'inflectionClass'.");
    }

    private static string MsaClass(LexicalEntryMsaKind kind) => kind switch
    {
        LexicalEntryMsaKind.Stem => "MoStemMsa",
        LexicalEntryMsaKind.Inflectional => "MoInflAffMsa",
        LexicalEntryMsaKind.Derivational => "MoDerivAffMsa",
        LexicalEntryMsaKind.Unclassified => "MoUnclassifiedAffixMsa",
        _ => throw new InvalidOperationException($"'{ConstructName}': unknown msa kind '{kind}'."),
    };

    private static IPartOfSpeech Category(LcmCache cache, CanonicalId id) =>
        LexicalFormAuthoring.Resolve<IPartOfSpeech>(cache, id, ConstructName);
}
