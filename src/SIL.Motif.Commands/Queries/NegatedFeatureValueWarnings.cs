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
/// (and so FieldWorks parses nothing from the project), while PanGloss reads the rest of the project and drops the
/// value's constraint, so a stem carrying one matches more than its author meant.
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
                "cannot parse with this project. PanGloss skips the value, so what carries it is not held back by it.",
            Guidance = "Remove this value in FieldWorks, or replace it with a closed value for each feature value " +
                "that is allowed, then run the grammar check again.",
            Origin = GrammarFindingOrigin.Check,
            Scope = GrammarSubjectStatus.Object,
        };
    }

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
        }
        return $"a {value.Owner?.ClassName ?? "grammar object"}";
    }

    private static string Name(LcmCache cache, IMultiAccessorBase? name, string fallback)
    {
        if (name is null) return fallback;
        var text = WritingSystemTextReader.BestAnalysis(cache, name).Text;
        if (text.Length == 0) text = WritingSystemTextReader.First(cache, name).Text;
        return text.Length == 0 ? fallback : text;
    }
}
