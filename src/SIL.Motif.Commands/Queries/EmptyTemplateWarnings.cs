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
/// Warns about each affix template that has no slot. LibLCM can hold one, so Motif does not refuse to author it,
/// but it holds no affix position, so it adds nothing the parser can use and nothing else in the project says so.
/// </summary>
internal static class EmptyTemplateWarnings
{
    internal static GrammarCheckResponse AddTo(GrammarCheckResponse check, LcmCache cache)
    {
        var warnings = Templates(cache.LangProject.PartsOfSpeechOA?.PossibilitiesOS ??
                Enumerable.Empty<ICmPossibility>())
            .SelectMany(category => category.AffixTemplatesOS
                .Where(template => !template.PrefixSlotsRS.Concat(template.SuffixSlotsRS).Concat(template.SlotsRS)
                    .Concat(template.ProcliticSlotsRS).Concat(template.EncliticSlotsRS).Any())
                .Select(template => Warning(cache, category, template)))
            .OrderBy(warning => warning.Description, StringComparer.Ordinal)
            .ToArray();
        if (warnings.Length == 0) return check;

        var summary = check.Summary.ToList();
        summary.Add(new GrammarWarningSummary(GrammarFindingCodes.TemplateHasNoSlots,
            "Affix template has no slots", GrammarDiagnosticLevel.Warning, warnings.Length));
        return check with { Findings = check.Findings.Concat(warnings).ToArray(), Summary = summary };
    }

    private static IEnumerable<IPartOfSpeech> Templates(IEnumerable<ICmPossibility> categories)
    {
        foreach (var category in categories.OfType<IPartOfSpeech>())
        {
            yield return category;
            foreach (var descendant in Templates(category.SubPossibilitiesOS)) yield return descendant;
        }
    }

    private static GrammarWarning Warning(LcmCache cache, IPartOfSpeech category, IMoInflAffixTemplate template)
    {
        var categoryName = Name(cache, category.Name, "unnamed part of speech");
        var templateName = Name(cache, template.Name, "unnamed template");
        var id = CanonicalId.FromGuid(template.Guid).Value;
        var subject = new GrammarWarningPart($"Template {templateName}", GrammarWarningPartRole.Object, id,
            template.ClassName)
        {
            Title = templateName,
            Subtitle = categoryName,
            SubjectGuid = template.Guid.ToString("D"),
            Status = GrammarSubjectStatus.Object,
        };
        var description = $"Template '{templateName}' for part of speech '{categoryName}' has no slots.";
        return new GrammarWarning(GrammarDiagnosticLevel.Warning, "Affix template has no slots", [subject], [], description)
        {
            Code = GrammarFindingCodes.TemplateHasNoSlots,
            Group = "Affix templates",
            Title = "Affix template has no slots",
            Description = description,
            Explanation = "A template with no slot holds no affix position, so it adds nothing the parser can use; " +
                "a trial of a final template without slots made no difference to the parse.",
            Guidance = "Add a slot with compose-author-affix-slot and put the template's affixes in it. A slot that " +
                "may be empty is an optional slot. If the template is not needed, leave it unused.",
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
