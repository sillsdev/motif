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
/// <param name="ExceptionFeatures">
/// For a stem only, the existing exception features the analysis carries; two stem analyses that differ only by these
/// are distinct, as LibLCM holds them, so each can be the one a rule requires or excludes.
/// </param>
public sealed record LexicalEntryMsa(
    LexicalEntryMsaKind Kind,
    CanonicalId Category,
    CanonicalId? ToCategory = null,
    IReadOnlyList<CanonicalId>? Slots = null,
    CanonicalId? InflectionClass = null,
    IReadOnlyList<CanonicalId>? ExceptionFeatures = null);

/// <summary>One new dictionary entry with one sense and typed grammatical info.</summary>
/// <param name="Form">The lexeme form, without affix hyphens.</param>
/// <param name="FormWritingSystem">The vernacular writing system tag of <paramref name="Form"/>.</param>
/// <param name="MorphType">The existing morph type, such as stem, root, prefix or suffix.</param>
/// <param name="Gloss">The sense's gloss.</param>
/// <param name="AnalysisWritingSystem">The writing system tag of the gloss and definition.</param>
/// <param name="Msa">The sense's grammatical info.</param>
/// <param name="Definition">The sense's optional definition.</param>
/// <param name="InflectionClasses">
/// For an inflectional affix form only, the classes it may attach to; a derivational affix uses its MSA restriction.
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
        var parsedMsa = ParseMsa(authored, ConstructName, allowExceptionFeatures: true);
        return new AuthorLexicalEntryIntent(
            SoundSystemIntentParsing.Text(authored, "form"),
            SoundSystemIntentParsing.Text(authored, "formWs"),
            SoundSystemIntentParsing.Id(authored, "morphType"),
            SoundSystemIntentParsing.Text(authored, "gloss"),
            SoundSystemIntentParsing.Text(authored, "analysisWs"),
            parsedMsa,
            authored.TryGetProperty("definition", out _) ? SoundSystemIntentParsing.Text(authored, "definition") : null,
            NonEmptyIds(authored, "inflectionClasses"), NonEmptyIds(authored, "environments"));
    }

    internal static LexicalEntryMsa ParseMsa(JsonElement authored, string constructName,
        bool allowExceptionFeatures = false)
    {
        if (!authored.TryGetProperty("msa", out var msa))
            throw new ContractParseException($"'{constructName}': 'msa' is required.");
        if (allowExceptionFeatures)
            SoundSystemIntentParsing.Object(msa, constructName + ".msa",
                "kind", "category", "toCategory", "slots", "inflectionClass", "exceptionFeatures");
        else
            SoundSystemIntentParsing.Object(msa, constructName + ".msa",
                "kind", "category", "toCategory", "slots", "inflectionClass");
        var kind = SoundSystemIntentParsing.Text(msa, "kind") switch
        {
            "stem" => LexicalEntryMsaKind.Stem,
            "inflectional" => LexicalEntryMsaKind.Inflectional,
            "derivational" => LexicalEntryMsaKind.Derivational,
            "unclassified" => LexicalEntryMsaKind.Unclassified,
            var other => throw new ContractParseException(
                $"'{constructName}': msa kind '{other}' must be stem, inflectional, derivational or unclassified."),
        };
        var toCategory = Optional(msa, "toCategory");
        var slots = msa.TryGetProperty("slots", out _) ? SoundSystemIntentParsing.Ids(msa, "slots", ordered: false) : null;
        var inflectionClass = Optional(msa, "inflectionClass");
        var exceptionFeatures = msa.TryGetProperty("exceptionFeatures", out _)
            ? NonEmptyIds(msa, "exceptionFeatures") : null;
        if (exceptionFeatures is not null && kind != LexicalEntryMsaKind.Stem)
            throw new ContractParseException($"'{constructName}': only a stem msa takes 'exceptionFeatures'.");
        if (toCategory is not null && kind != LexicalEntryMsaKind.Derivational)
            throw new ContractParseException($"'{constructName}': only a derivational msa takes 'toCategory'.");
        if (kind == LexicalEntryMsaKind.Derivational && toCategory is null)
            throw new ContractParseException($"'{constructName}': a derivational msa needs 'toCategory'.");
        if (slots is not null && kind != LexicalEntryMsaKind.Inflectional)
            throw new ContractParseException($"'{constructName}': only an inflectional msa takes 'slots'.");
        if (inflectionClass is not null && kind != LexicalEntryMsaKind.Stem)
            throw new ContractParseException($"'{constructName}': only a stem msa takes 'inflectionClass'.");
        return new LexicalEntryMsa(kind, SoundSystemIntentParsing.Id(msa, "category"), toCategory, slots, inflectionClass,
            exceptionFeatures);
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
        LcmCache cache, AuthorLexicalEntryIntent intent, Func<CanonicalId>? mintId = null) =>
        BuildEntry(cache, intent, mintId, null);

    /// <summary>
    /// Builds an entry whose lexeme form is created from <paramref name="formPayload"/> when it is given. With
    /// <paramref name="withSense"/> false the entry gets no sense, gloss or definition, because a sense that already
    /// exists is moved into it afterwards.
    /// </summary>
    internal static IReadOnlyList<OperationEnvelope> BuildEntry(
        LcmCache cache, AuthorLexicalEntryIntent intent, Func<CanonicalId>? mintId, object? formPayload,
        bool withSense = true)
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
        RequireMsaShape(intent.Msa, ConstructName);
        var form = formPayload is null ? LexicalFormAuthoring.RequireText(intent.Form, "form", ConstructName) : "";
        var gloss = withSense ? LexicalFormAuthoring.RequireText(intent.Gloss, "gloss", ConstructName) : "";
        var definition = intent.Definition is null || !withSense
            ? null : LexicalFormAuthoring.RequireText(intent.Definition, "definition", ConstructName);
        if (formPayload is null)
            LexicalFormAuthoring.RequireWritingSystem(cache, intent.FormWritingSystem, vernacular: true, ConstructName);
        if (withSense)
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
        if (intent.InflectionClasses is not null && msa.Kind == LexicalEntryMsaKind.Derivational)
            throw new InvalidOperationException(LexicalFormAuthoring.DerivationalClassesRefusal(ConstructName));
        var category = RequireMsaReferences(cache, msa, ConstructName);
        var slots = msa.Slots ?? [];
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
            formPayload ?? new { morphType = intent.MorphType.Value, ws = intent.FormWritingSystem, text = form },
            entityId: formId, dependsOn: afterEntry);
        foreach (var (kind, member) in LexicalFormAuthoring.Links(formClass, environments, restrictedClasses))
            Add(kind, formId, new { member = member.Value }, dependsOn: [new OperationDependency(createForm)]);

        var msaId = Mint();
        var createMsa = Add(LexEntryMorphoSyntaxAnalysesOperationKinds.Create, entryId,
            new { @class = MsaClass(msa.Kind) }, entityId: msaId, dependsOn: afterEntry);
        var afterMsa = new[] { new OperationDependency(createMsa) };
        foreach (var (kind, after) in MsaFieldWrites(msa, slots))
            Add(kind, msaId, after, dependsOn: afterMsa);

        if (!withSense) return operations;
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


    /// <summary>
    /// Checks the categories, slots and stem class a grammatical info names exist and fit each other, and returns the
    /// category it is about.
    /// </summary>
    internal static IPartOfSpeech RequireMsaReferences(LcmCache cache, LexicalEntryMsa msa, string construct)
    {
        var category = LexicalFormAuthoring.Resolve<IPartOfSpeech>(cache, msa.Category, construct);
        if (msa.ToCategory is { } toCategory) LexicalFormAuthoring.Resolve<IPartOfSpeech>(cache, toCategory, construct);
        var slots = msa.Slots ?? [];
        if (slots.Select(id => id.ToGuid()).Distinct().Count() != slots.Count)
            throw new InvalidOperationException($"'{construct}': slots must not repeat.");
        foreach (var slotId in slots)
        {
            var slot = LexicalFormAuthoring.Resolve<IMoInflAffixSlot>(cache, slotId, construct);
            if (!LexicalFormAuthoring.SlotServesCategory(slot.Owner as IPartOfSpeech, category))
                throw new InvalidOperationException(
                    $"'{construct}': slot '{slotId.Value}' does not belong to the category or one of its ancestors or subcategories." +
                    LexicalFormAuthoring.UnrelatedSlotAdvice);
        }
        if (msa.InflectionClass is { } classId)
        {
            var inflectionClass = LexicalFormAuthoring.Resolve<IMoInflClass>(cache, classId, construct);
            if (!LexicalFormAuthoring.InCategoryOrAncestor(LexicalFormAuthoring.OwningCategory(inflectionClass), category))
                throw new InvalidOperationException(
                    $"'{construct}': inflection class '{classId.Value}' does not belong to the category.");
        }
        if (msa.ExceptionFeatures is { } features)
        {
            if (features.Select(id => id.ToGuid()).Distinct().Count() != features.Count)
                throw new InvalidOperationException($"'{construct}': 'exceptionFeatures' must not repeat an identity.");
            var allowed = cache.LangProject.MorphologicalDataOA?.ProdRestrictOA?.ReallyReallyAllPossibilities
                .Select(item => item.Guid).ToHashSet() ?? [];
            foreach (var featureId in features.Where(id => !allowed.Contains(id.ToGuid())))
                throw new InvalidOperationException(
                    $"'{construct}': '{featureId.Value}' is not one of the project's exception features.");
        }
        return category;
    }

    /// <summary>The field writes, in order, that fill a freshly created grammatical info of <paramref name="msa"/>'s kind.</summary>
    internal static IEnumerable<(string Kind, object After)> MsaFieldWrites(LexicalEntryMsa msa,
        IReadOnlyList<CanonicalId> slots)
    {
        switch (msa.Kind)
        {
            case LexicalEntryMsaKind.Stem:
                yield return (MoStemMsaPartOfSpeechOperationKinds.SetPartOfSpeech, new { @ref = msa.Category.Value });
                if (msa.InflectionClass is { } inflectionClass)
                    yield return (MoStemMsaInflectionClassOperationKinds.SetInflectionClass,
                        new { @ref = inflectionClass.Value });
                foreach (var feature in (msa.ExceptionFeatures ?? []).OrderBy(id => id.Value, StringComparer.Ordinal))
                    yield return (MoStemMsaProdRestrictOperationKinds.AddRefProdRestrict, new { member = feature.Value });
                break;
            case LexicalEntryMsaKind.Inflectional:
                yield return (MoInflAffMsaPartOfSpeechOperationKinds.SetPartOfSpeech, new { @ref = msa.Category.Value });
                foreach (var slot in slots.OrderBy(id => id.Value, StringComparer.Ordinal))
                    yield return (MoInflAffMsaSlotsOperationKinds.AddRefSlots, new { member = slot.Value });
                break;
            case LexicalEntryMsaKind.Derivational:
                yield return (MoDerivAffMsaFromPartOfSpeechOperationKinds.SetFromPartOfSpeech,
                    new { @ref = msa.Category.Value });
                yield return (MoDerivAffMsaToPartOfSpeechOperationKinds.SetToPartOfSpeech,
                    new { @ref = msa.ToCategory!.Value.Value });
                break;
            case LexicalEntryMsaKind.Unclassified:
                yield return (MoUnclassifiedAffixMsaPartOfSpeechOperationKinds.SetPartOfSpeech,
                    new { @ref = msa.Category.Value });
                break;
        }
    }

    internal static void RequireMsaShape(LexicalEntryMsa msa, string construct)
    {
        if (!Enum.IsDefined(msa.Kind))
            throw new InvalidOperationException($"'{construct}': unknown msa kind '{msa.Kind}'.");
        if (msa.ToCategory is not null && msa.Kind != LexicalEntryMsaKind.Derivational)
            throw new InvalidOperationException($"'{construct}': only a derivational msa takes 'toCategory'.");
        if (msa.Kind == LexicalEntryMsaKind.Derivational && msa.ToCategory is null)
            throw new InvalidOperationException($"'{construct}': a derivational msa needs 'toCategory'.");
        if (msa.Slots is not null && msa.Kind != LexicalEntryMsaKind.Inflectional)
            throw new InvalidOperationException($"'{construct}': only an inflectional msa takes 'slots'.");
        if (msa.InflectionClass is not null && msa.Kind != LexicalEntryMsaKind.Stem)
            throw new InvalidOperationException($"'{construct}': only a stem msa takes 'inflectionClass'.");
        if (msa.ExceptionFeatures is not null && msa.Kind != LexicalEntryMsaKind.Stem)
            throw new InvalidOperationException($"'{construct}': only a stem msa takes 'exceptionFeatures'.");
    }

    internal static string MsaClass(LexicalEntryMsaKind kind) => kind switch
    {
        LexicalEntryMsaKind.Stem => "MoStemMsa",
        LexicalEntryMsaKind.Inflectional => "MoInflAffMsa",
        LexicalEntryMsaKind.Derivational => "MoDerivAffMsa",
        LexicalEntryMsaKind.Unclassified => "MoUnclassifiedAffixMsa",
        _ => throw new InvalidOperationException($"unknown msa kind '{kind}'."),
    };
}
