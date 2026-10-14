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

internal static class RequiredSlotWarnings
{
    internal static GrammarCheckResponse AddTo(GrammarCheckResponse check, LcmCache cache)
    {
        var assignedSlots = cache.ServiceLocator.GetInstance<IMoInflAffMsaRepository>().AllInstances()
            .SelectMany(msa => msa.SlotsRC).ToHashSet();
        var warnings = Templates(cache.LangProject.PartsOfSpeechOA?.PossibilitiesOS ??
                Enumerable.Empty<ICmPossibility>())
            .SelectMany(category => category.AffixTemplatesOS.SelectMany(template =>
                Slots(template).Distinct().Where(slot => !slot.Optional && !assignedSlots.Contains(slot))
                    .Select(slot => Warning(cache, category, template, slot))))
            .ToArray();
        if (warnings.Length == 0) return check;

        var findings = check.Findings.Concat(warnings).ToArray();
        var summary = check.Summary.ToList();
        var existing = summary.FindIndex(item => item.Code == GrammarFindingCodes.RequiredSlotEmpty);
        if (existing >= 0)
        {
            var item = summary[existing];
            summary[existing] = item with { Count = item.Count + warnings.Length };
        }
        else
        {
            summary.Add(new GrammarWarningSummary(GrammarFindingCodes.RequiredSlotEmpty,
                "Required slot has no affixes", GrammarDiagnosticLevel.Warning, warnings.Length));
        }
        return check with { Findings = findings, Summary = summary };
    }

    private static IEnumerable<IPartOfSpeech> Templates(IEnumerable<ICmPossibility> categories)
    {
        foreach (var category in categories.OfType<IPartOfSpeech>())
        {
            yield return category;
            foreach (var descendant in Templates(category.SubPossibilitiesOS)) yield return descendant;
        }
    }

    private static IEnumerable<IMoInflAffixSlot> Slots(IMoInflAffixTemplate template) =>
        template.PrefixSlotsRS.Concat(template.SuffixSlotsRS).Concat(template.SlotsRS)
            .Concat(template.ProcliticSlotsRS).Concat(template.EncliticSlotsRS);

    private static GrammarWarning Warning(LcmCache cache, IPartOfSpeech category,
        IMoInflAffixTemplate template, IMoInflAffixSlot slot)
    {
        var categoryName = Name(cache, category.Name, "unnamed part of speech");
        var templateName = Name(cache, template.Name, "unnamed template");
        var slotName = Name(cache, slot.Name, "unnamed slot");
        var id = CanonicalId.FromGuid(slot.Guid).Value;
        var subject = new GrammarWarningPart($"Slot {slotName}", GrammarWarningPartRole.Object, id, slot.ClassName)
        {
            Title = slotName,
            Subtitle = $"{templateName} · {categoryName}",
            SubjectGuid = slot.Guid.ToString("D"),
            Status = GrammarSubjectStatus.Object,
        };
        var description = $"Required slot '{slotName}' in template '{templateName}' for part of speech " +
            $"'{categoryName}' has no inflectional affixes.";
        return new GrammarWarning(GrammarDiagnosticLevel.Warning, "Required slot has no affixes",
            [subject], [], description)
        {
            Code = GrammarFindingCodes.RequiredSlotEmpty,
            Group = "Required inflectional slots",
            Title = "Required slot has no affixes",
            Description = description,
            Explanation = "This slot is marked required but has no affixes. FieldWorks and PanGloss currently treat an empty required slot as optional.",
            Guidance = "Add an inflectional affix to this slot, or mark the slot optional if it may be empty.",
            Origin = GrammarFindingOrigin.Check,
            Scope = GrammarSubjectStatus.Object,
        };
    }

    private static string Name(LcmCache cache, IMultiAccessorBase name, string fallback)
    {
        var text = WritingSystemTextReader.BestAnalysis(cache, name).Text;
        if (text.Length == 0) text = WritingSystemTextReader.First(cache, name).Text;
        return text.Length == 0 ? fallback : text;
    }
}
