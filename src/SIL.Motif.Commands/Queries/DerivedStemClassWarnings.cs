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
/// Warns about each derivational affix that restricts its stems to a From Inflection Class but names no To
/// Inflection Class. The derived stem then keeps the From class, so an affix that only fits other classes finds no
/// stem to attach to, and nothing in the project says so.
/// </summary>
internal static class DerivedStemClassWarnings
{
    internal static GrammarCheckResponse AddTo(GrammarCheckResponse check, LcmCache cache)
    {
        var warnings = cache.ServiceLocator.GetInstance<IMoDerivAffMsaRepository>().AllInstances()
            .Where(msa => msa.FromInflectionClassRA is not null && msa.ToInflectionClassRA is null &&
                msa.Owner is ILexEntry)
            .Select(msa => Warning(cache, msa))
            .OrderBy(warning => warning.Description, StringComparer.Ordinal)
            .ToArray();
        if (warnings.Length == 0) return check;

        var summary = check.Summary.ToList();
        summary.Add(new GrammarWarningSummary(GrammarFindingCodes.DerivedStemKeepsFromClass,
            "Derived stem keeps the From Inflection Class", GrammarDiagnosticLevel.Warning, warnings.Length));
        return check with { Findings = check.Findings.Concat(warnings).ToArray(), Summary = summary };
    }

    private static GrammarWarning Warning(LcmCache cache, IMoDerivAffMsa msa)
    {
        var entry = (ILexEntry)msa.Owner;
        var entryName = Name(cache, entry.LexemeFormOA?.Form, "unnamed entry");
        var fromClass = Name(cache, msa.FromInflectionClassRA!.Name, "unnamed inflection class");
        var id = CanonicalId.FromGuid(msa.Guid).Value;
        var subject = new GrammarWarningPart($"Derivational info of {entryName}", GrammarWarningPartRole.Object, id,
            msa.ClassName)
        {
            Title = entryName,
            Subtitle = $"From Inflection Class {fromClass}",
            SubjectGuid = msa.Guid.ToString("D"),
            Status = GrammarSubjectStatus.Object,
        };
        var description = $"Derivational affix '{entryName}' takes From Inflection Class '{fromClass}' and names no " +
            $"To Inflection Class, so the stems it derives keep '{fromClass}'.";
        return new GrammarWarning(GrammarDiagnosticLevel.Warning, "Derived stem keeps the From Inflection Class",
            [subject], [], description)
        {
            Code = GrammarFindingCodes.DerivedStemKeepsFromClass,
            Group = "Derivational affixes",
            Title = "Derived stem keeps the From Inflection Class",
            Description = description,
            Explanation = "When a derivational affix has no To Inflection Class, the derived stem is left in the class " +
                "it came from. An inflectional affix that attaches only to stems of other classes then finds no stem " +
                "to attach to, and the parser gives no reading without saying why.",
            Guidance = "If the derived stems belong to another inflection class, set it with " +
                "compose-edit-derivational-affix-restriction (toInflectionClass). If they keep the class, no change is needed.",
            Origin = GrammarFindingOrigin.Check,
            Scope = GrammarSubjectStatus.Object,
        };
    }

    private static string Name(LcmCache cache, IMultiAccessorBase? name, string fallback)
    {
        if (name is null) return fallback;
        var text = WritingSystemTextReader.BestAnalysis(cache, name).Text;
        if (text.Length == 0) text = WritingSystemTextReader.First(cache, name).Text;
        return text.Length == 0 ? fallback : text;
    }
}
