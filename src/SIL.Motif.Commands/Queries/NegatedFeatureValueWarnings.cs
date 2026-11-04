using System;
using System.Collections.Generic;
using System.Linq;
using SIL.LCModel;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Projection;
using SIL.Motif.Projection.Grammar;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Warns about each stored negated feature value. LibLCM can hold one, but FieldWorks' parser loader throws on it
/// (and so FieldWorks parses nothing from the project), and PanGloss declines the project too, so a Trial
/// declines until the value is removed.
/// </summary>
internal static class NegatedFeatureValueWarnings
{
    internal static GrammarCheckResponse AddTo(GrammarCheckResponse check, LcmCache cache)
    {
        var warnings = cache.ServiceLocator.GetInstance<IFsNegatedValueRepository>().AllInstances()
            .Select(value => Warning(cache, value))
            .OrderBy(warning => warning.Description, StringComparer.Ordinal)
            .ToArray();
        if (warnings.Length == 0) return check;

        var summary = check.Summary.ToList();
        summary.Add(new GrammarWarningSummary(GrammarFindingCodes.NegatedFeatureValue,
            "Negated feature value FieldWorks cannot read", GrammarDiagnosticLevel.Warning, warnings.Length));
        return check with { Findings = check.Findings.Concat(warnings).ToArray(), Summary = summary };
    }

    private static GrammarWarning Warning(LcmCache cache, IFsNegatedValue value)
    {
        var feature = Name(cache, value.FeatureRA?.Name, "unnamed feature");
        var negated = Name(cache, value.ValueRA?.Name, "unnamed value");
        var owner = Owner(cache, value);
        var id = CanonicalId.FromGuid(value.Guid).Value;
        var subject = new GrammarWarningPart($"Not {negated}", GrammarWarningPartRole.Object, id, value.ClassName)
        {
            Title = $"{feature} is not {negated}",
            Subtitle = owner,
            SubjectGuid = value.Guid.ToString("D"),
            Status = GrammarSubjectStatus.Object,
        };
        var description = $"Feature '{feature}' is set to 'not {negated}' on {owner}.";
        return new GrammarWarning(GrammarDiagnosticLevel.Warning, "Negated feature value", [subject], [], description)
        {
            Code = GrammarFindingCodes.NegatedFeatureValue,
            Group = "Feature values",
            Title = "Negated feature value FieldWorks cannot read",
            Description = description,
            Explanation = "FieldWorks' parser loader throws when it reads a negated feature value, so FieldWorks " +
                "cannot parse with this project. PanGloss declines the project too, so a Trial declines until the value is removed.",
            Guidance = Guidance(value, owner),
            Origin = GrammarFindingOrigin.Check,
            Scope = GrammarSubjectStatus.Object,
        };
    }

    /// <summary>Stems, inflectional and derivational affixes have a Motif route, pinned by composer tests.</summary>
    private static string Guidance(IFsNegatedValue value, string owner) =>
        value.Owner?.Owner is IMoStemMsa or IMoInflAffMsa or IMoDerivAffMsa
            ? "Remove it with compose-remove-feature-value (motif_remove_feature_value), giving this feature " +
                "structure and the feature, then author a closed value if the stem or affix should carry one. " +
                "A person reviews and applies the Proposal."
            : $"Motif has no command that removes a negated value from {owner}, and whether FieldWorks can clear " +
                "it was not checked. Until it is gone FieldWorks cannot parse with this project and a Trial declines.";

    private static string Owner(LcmCache cache, ICmObject value)
    {
        for (var owner = value.Owner; owner is not null; owner = owner.Owner)
        {
            if (owner is ILexEntry entry)
                return $"entry '{Name(cache, entry.LexemeFormOA?.Form, "unnamed entry")}'";
            if (owner is IPartOfSpeech category)
                return $"part of speech '{Name(cache, category.Name, "unnamed part of speech")}'";
            if (owner is IPhPhoneme phoneme)
                return $"phoneme '{Name(cache, phoneme.Name, "unnamed phoneme")}'";
            if (owner is IPhNCFeatures naturalClass)
                return $"natural class '{Name(cache, naturalClass.Name, "unnamed natural class")}'";
            if (owner is IMoInflClass inflectionClass)
                return $"inflection class '{Name(cache, inflectionClass.Name, "unnamed inflection class")}'";
        }
        for (var owner = value.Owner; owner is not null; owner = owner.Owner)
            if (owner.ClassName is not ("FsFeatStruc" or "FsComplexValue"))
                return $"a {owner.ClassName}";
        return "a grammar object";
    }

    private static string Name(LcmCache cache, IMultiAccessorBase? name, string fallback)
    {
        if (name is null) return fallback;
        var text = WritingSystemTextReader.BestAnalysis(cache, name).Text;
        if (text.Length == 0) text = WritingSystemTextReader.First(cache, name).Text;
        return text.Length == 0 ? fallback : text;
    }
}
