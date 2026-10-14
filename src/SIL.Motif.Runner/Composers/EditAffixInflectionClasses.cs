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

/// <summary>Replaces an ordinary inflectional affix form's exact unordered class restriction; empty means unrestricted.</summary>
public sealed record EditAffixInflectionClassesIntent(CanonicalId Target,
    IReadOnlyList<CanonicalId> ExpectedInflectionClasses, IReadOnlyList<CanonicalId> InflectionClasses);

/// <summary>Parses required exact identity sets, including empty sets, comparing identities by decoded GUID.</summary>
public static class EditAffixInflectionClassesIntentParser
{
    public static EditAffixInflectionClassesIntent Parse(JsonElement input)
    {
        SoundSystemIntentParsing.Object(input, "EditAffixInflectionClasses",
            "target", "expectedInflectionClasses", "inflectionClasses");
        return new(LexicalFormAuthoring.Normalize(SoundSystemIntentParsing.Id(input, "target")),
            NormalizeSet(SoundSystemIntentParsing.Ids(input, "expectedInflectionClasses", false)),
            NormalizeSet(SoundSystemIntentParsing.Ids(input, "inflectionClasses", false)));
    }

    internal static CanonicalId[] NormalizeSet(IReadOnlyList<CanonicalId> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var normalized = ids.Select(LexicalFormAuthoring.Normalize).ToArray();
        if (normalized.Distinct().Count() != normalized.Length)
            throw new ContractParseException("An inflection class set must not repeat a decoded identity.");
        return normalized.OrderBy(id => id.Value, StringComparer.Ordinal).ToArray();
    }
}

/// <summary>Edits class memberships on an ordinary affix in an entry with only usable inflectional grammatical info.</summary>
public static class EditAffixInflectionClassesComposer
{
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, EditAffixInflectionClassesIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var expected = EditAffixInflectionClassesIntentParser.NormalizeSet(intent.ExpectedInflectionClasses);
        var desired = EditAffixInflectionClassesIntentParser.NormalizeSet(intent.InflectionClasses);
        var form = LexicalFormAuthoring.Resolve<IMoAffixAllomorph>(cache, intent.Target, "EditAffixInflectionClasses");
        if (form.Owner is not ILexEntry entry)
            throw new InvalidOperationException("The ordinary affix form must belong to a lexical entry.");
        LexicalFormAuthoring.RequireMatchingKind(entry, MoFormConcreteClass.AffixAllomorph, "EditAffixInflectionClasses");
        if (entry.LexemeFormOA.Guid != form.Guid && !entry.AlternateFormsOS.Any(alternate => alternate.Guid == form.Guid))
            throw new InvalidOperationException("The target must be the entry's lexeme form or an alternate form.");
        if (entry.LexemeFormOA.MorphTypeRA?.Guid == MoMorphTypeTags.kguidMorphCircumfix)
            throw new InvalidOperationException("Circumfix entries are outside ordinary inflection class editing.");
        var morphType = form.MorphTypeRA?.Guid;
        var text = form.Form.get_String(cache.DefaultVernWs)?.Text;
        if (form.IsAbstract || string.IsNullOrWhiteSpace(text) || text.Contains('[') ||
            (morphType != MoMorphTypeTags.kguidMorphPrefix && morphType != MoMorphTypeTags.kguidMorphSuffix &&
             morphType != MoMorphTypeTags.kguidMorphPrefixingInterfix && morphType != MoMorphTypeTags.kguidMorphSuffixingInterfix))
            throw new InvalidOperationException("The target must be a nonabstract ordinary prefix or suffix with a vernacular form.");
        var msas = entry.MorphoSyntaxAnalysesOC.ToArray();
        if (msas.Length == 0 || msas.Any(msa => msa is not IMoInflAffMsa { PartOfSpeechRA: not null }))
            throw new InvalidOperationException("The affix entry must have only inflectional grammatical info with usable categories.");
        var categories = msas.Cast<IMoInflAffMsa>().Select(msa =>
            InflectionAuthoring.Category(cache, CanonicalId.FromGuid(msa.PartOfSpeechRA.Guid))).ToArray();
        var current = EditAffixInflectionClassesIntentParser.NormalizeSet(form.InflectionClassesRC.Select(cls =>
            CanonicalId.FromGuid(cls.Guid)).ToArray());
        if (!current.SequenceEqual(expected))
            throw new InvalidOperationException("The affix's inflection classes differ from the expected set.");
        if (desired.Length != 0)
            LexicalFormAuthoring.RequireInflectionClasses(cache, desired, categories, "EditAffixInflectionClasses");
        var target = LexicalFormAuthoring.Normalize(intent.Target);
        return current.Except(desired).Select(id => Operation(MoAffixFormInflectionClassesOperationKinds.RemoveRefInflectionClasses, id))
            .Concat(desired.Except(current).Select(id => Operation(MoAffixFormInflectionClassesOperationKinds.AddRefInflectionClasses, id)))
            .ToArray();

        OperationEnvelope Operation(string kind, CanonicalId member) => new(CanonicalId.Mint(), kind, target: target,
            after: JsonSerializer.SerializeToElement(new { member = member.Value }),
            rationale: "Authored by the EditAffixInflectionClasses composer.");
    }
}
