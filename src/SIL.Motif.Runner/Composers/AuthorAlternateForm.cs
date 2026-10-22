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

/// <summary>One further form of an existing entry, with the conditions under which it occurs.</summary>
/// <param name="Entry">The existing, or earlier-staged, dictionary entry that gains the form.</param>
/// <param name="Form">The written form, without affix hyphens.</param>
/// <param name="FormWritingSystem">The vernacular writing system tag of <paramref name="Form"/>.</param>
/// <param name="MorphType">The existing morph type; it must be the same kind (stem or affix) as the entry.</param>
/// <param name="Environments">The phonological environments the form occurs in; absent means any.</param>
/// <param name="InflectionClasses">
/// The classes this form attaches to for an inflectional MSA; a derivational MSA uses its MSA restriction.
/// </param>
/// <param name="Placement">
/// The neighbouring alternate forms that define the insertion gap; required when the entry already has any.
/// </param>
public sealed record AuthorAlternateFormIntent(
    CanonicalId Entry,
    string Form,
    string FormWritingSystem,
    CanonicalId MorphType,
    IReadOnlyList<CanonicalId>? Environments = null,
    IReadOnlyList<CanonicalId>? InflectionClasses = null,
    Placement? Placement = null);

/// <summary>Parses the closed JSON intent for adding one alternate form to an entry.</summary>
public static class AuthorAlternateFormIntentParser
{
    private const string ConstructName = "AuthorAlternateForm";

    /// <summary>Reads the entry, form, writing system, morph type, conditions and placement.</summary>
    public static AuthorAlternateFormIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName,
            "entry", "form", "formWs", "morphType", "environments", "inflectionClasses", "placement");
        return new AuthorAlternateFormIntent(
            SoundSystemIntentParsing.Id(authored, "entry"),
            SoundSystemIntentParsing.Text(authored, "form"),
            SoundSystemIntentParsing.Text(authored, "formWs"),
            SoundSystemIntentParsing.Id(authored, "morphType"),
            AuthorLexicalEntryIntentParser.NonEmptyIds(authored, "environments"),
            AuthorLexicalEntryIntentParser.NonEmptyIds(authored, "inflectionClasses"),
            ParsePlacement(authored));
    }

    internal static Placement? ParsePlacement(JsonElement authored, string constructName = ConstructName)
    {
        if (!authored.TryGetProperty("placement", out var value)) return null;
        SoundSystemIntentParsing.Object(value, constructName + ".placement", "after", "before");
        CanonicalId? after = value.TryGetProperty("after", out _) ? SoundSystemIntentParsing.Id(value, "after") : null;
        CanonicalId? before = value.TryGetProperty("before", out _) ? SoundSystemIntentParsing.Id(value, "before") : null;
        if (after is null && before is null)
            throw new ContractParseException($"'{constructName}': placement must name a neighbouring alternate form.");
        return new Placement(after, before);
    }
}

/// <summary>
/// Lowers one <see cref="AuthorAlternateFormIntent"/> into the operations that add an ordinary stem or affix
/// allomorph to an existing entry's ordered alternate forms and link its conditions. It adds no entry, sense
/// or grammatical info, never changes the incumbent lexeme form, and copies no restriction from it: each
/// form states its own environments and inflection classes.
/// </summary>
public static class AuthorAlternateFormComposer
{
    private const string ConstructName = "AuthorAlternateForm";
    private const string Rationale = "Authored by the AuthorAlternateForm composer.";

    /// <summary>Authors one alternate form without mutating the caller's project.</summary>
    /// <param name="cache">The project to resolve <paramref name="intent"/>'s references against.</param>
    /// <param name="intent">The authored form.</param>
    /// <param name="mintId">Overrides id minting; tests supply a deterministic source.</param>
    public static IReadOnlyList<OperationEnvelope> Build(
        LcmCache cache, AuthorAlternateFormIntent intent, Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        intent = intent with
        {
            Entry = LexicalFormAuthoring.Normalize(intent.Entry),
            MorphType = LexicalFormAuthoring.Normalize(intent.MorphType),
            InflectionClasses = LexicalFormAuthoring.Normalize(intent.InflectionClasses),
            Environments = LexicalFormAuthoring.Normalize(intent.Environments),
            Placement = intent.Placement is { } gap ? new Placement(
                gap.After is { } left ? LexicalFormAuthoring.Normalize(left) : null,
                gap.Before is { } right ? LexicalFormAuthoring.Normalize(right) : null) : null,
        };
        var text = LexicalFormAuthoring.RequireText(intent.Form, "form", ConstructName);
        LexicalFormAuthoring.RequireWritingSystem(cache, intent.FormWritingSystem, vernacular: true, ConstructName);

        var entry = LexicalFormAuthoring.Resolve<ILexEntry>(cache, intent.Entry, ConstructName);
        var morphType = LexicalFormAuthoring.Resolve<IMoMorphType>(cache, intent.MorphType, ConstructName);
        var formClass = MoFormConcreteClassSelection.ClassFor(morphType, ConstructName);
        LexicalFormAuthoring.RequireMatchingKind(entry, formClass, ConstructName);

        IReadOnlyList<CanonicalId> environments = intent.Environments is null ? []
            : LexicalFormAuthoring.RequireEnvironments(cache, intent.Environments, ConstructName);
        IReadOnlyList<CanonicalId> classes = [];
        if (intent.InflectionClasses is { } requested)
        {
            if (formClass != MoFormConcreteClass.AffixAllomorph)
                throw new InvalidOperationException($"'{ConstructName}': only an affix form takes 'inflectionClasses'.");
            var inflectionalCategories = LexicalFormAuthoring.InflectionalAffixCategories(entry, ConstructName);
            classes = LexicalFormAuthoring.RequireInflectionClasses(cache, requested, inflectionalCategories, ConstructName);
        }

        RequirePlacement(entry, intent.Placement);
        RequireOrderThatCanBeSelected(entry, intent.Placement, environments.Count > 0);

        var source = mintId ?? (() => CanonicalId.Mint());
        CanonicalId Mint() => LexicalFormAuthoring.Normalize(source());
        var operations = new List<OperationEnvelope>();
        var formId = Mint();
        var createId = Mint();
        operations.Add(new OperationEnvelope(createId, LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms,
            entityId: formId, target: intent.Entry, placement: intent.Placement,
            after: JsonSerializer.SerializeToElement(new
                { morphType = intent.MorphType.Value, ws = intent.FormWritingSystem, text }),
            rationale: Rationale));
        foreach (var (kind, member) in LexicalFormAuthoring.Links(formClass, environments, classes))
            operations.Add(new OperationEnvelope(Mint(), kind, target: formId,
                after: JsonSerializer.SerializeToElement(new { member = member.Value }),
                dependsOn: [new OperationDependency(createId)], rationale: Rationale));
        return operations;
    }

    internal static void RequirePlacement(ILexEntry entry, Placement? placement, string constructName = ConstructName)
    {
        var named = $" Entry '{CanonicalId.FromGuid(entry.Guid).Value}'.";
        var forms = entry.AlternateFormsOS.Select(form => form.Guid).ToArray();
        if (forms.Length == 0)
        {
            if (placement is not null)
                throw new InvalidOperationException($"'{constructName}': the first alternate form cannot name a neighbour." + named);
            return;
        }
        if (placement is null)
            throw new InvalidOperationException(
                $"'{constructName}': an entry with alternate forms requires placement beside an existing one." + named);
        var left = placement.After is { } after ? Array.IndexOf(forms, after.ToGuid()) : -1;
        var right = placement.Before is { } before ? Array.IndexOf(forms, before.ToGuid()) : forms.Length;
        if ((placement.After is not null && left < 0) || (placement.Before is not null && right < 0) || right != left + 1)
            throw new InvalidOperationException(
                $"'{constructName}': placement neighbours are missing or not adjacent in the current order." + named);
    }

    // An unrestricted form ahead of a restricted one always wins, and the lexeme form comes last.
    private static void RequireOrderThatCanBeSelected(ILexEntry entry, Placement? placement, bool restricted)
    {
        var forms = entry.AlternateFormsOS.ToList();
        var gap = placement is null ? 0
            : placement.After is { } after ? forms.FindIndex(form => form.Guid == after.ToGuid()) + 1
            : forms.FindIndex(form => form.Guid == placement.Before!.Value.ToGuid());
        var ahead = forms.Take(gap).ToList();
        var behind = forms.Skip(gap).ToList();
        if (entry.LexemeFormOA is { } lexeme) behind.Add(lexeme);
        if (restricted)
        {
            if (ahead.FirstOrDefault(form => !HasEnvironments(form)) is { } blocker)
                throw new InvalidOperationException(
                    $"'{ConstructName}': a restricted form placed after the unrestricted form '{FormText(blocker)}' would " +
                    "never be selected, because an unrestricted form earlier in the order always wins. Place it " +
                    $"before '{FormText(blocker)}'.");
        }
        else if (behind.FirstOrDefault(HasEnvironments) is { } shadowed)
        {
            throw new InvalidOperationException(
                $"'{ConstructName}': an unrestricted form placed before the restricted form '{FormText(shadowed)}' " +
                $"would stop it from ever being selected. Place it after '{FormText(shadowed)}', or give it environments.");
        }
    }

    private static bool HasEnvironments(IMoForm form) => form switch
    {
        IMoAffixAllomorph affix => affix.PhoneEnvRC.Count > 0,
        IMoStemAllomorph stem => stem.PhoneEnvRC.Count > 0,
        _ => false,
    };

    private static string FormText(IMoForm form) => form.Form.BestVernacularAlternative.Text ?? "?";

    internal static IReadOnlyList<IPartOfSpeech> AffixCategories(ILexEntry entry)
    {
        var categories = new List<IPartOfSpeech>();
        foreach (var msa in entry.MorphoSyntaxAnalysesOC)
        {
            var category = msa switch
            {
                IMoInflAffMsa inflectional => inflectional.PartOfSpeechRA,
                IMoDerivAffMsa derivational => derivational.FromPartOfSpeechRA,
                IMoUnclassifiedAffixMsa unclassified => unclassified.PartOfSpeechRA,
                _ => null,
            };
            if (category is not null) categories.Add(category);
        }
        return categories;
    }
}
