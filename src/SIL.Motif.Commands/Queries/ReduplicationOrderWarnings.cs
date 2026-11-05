using SIL.LCModel;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Projection;

namespace SIL.Motif.Commands.Queries;

/// <summary>Reports competing solid and hyphenated full-copy forms in their declared allomorph order.</summary>
internal static class ReduplicationOrderWarnings
{
    private const string Label = "Solid form can block hyphen form";

    internal static GrammarCheckResponse AddTo(GrammarCheckResponse check, LcmCache cache)
    {
        var warnings = new List<GrammarWarning>();
        foreach (var entry in cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances()
                     .Where(entry => !entry.DoNotUseForParsing))
        {
            var forms = entry.AlternateFormsOS.Concat(entry.LexemeFormOA is { } lexeme ? [lexeme] : [])
                .OfType<IMoAffixProcess>().Where(form => !form.IsAbstract).ToArray();
            for (var earlier = 0; earlier < forms.Length; earlier++)
            {
                if (!IsRestrictedSolidCopy(forms[earlier])) continue;
                for (var later = earlier + 1; later < forms.Length; later++)
                    if (IsWholeStemHyphenCopy(cache, forms[later]))
                        warnings.Add(Warning(cache, entry, forms[earlier], forms[later]));
            }
        }
        if (warnings.Count == 0) return check;
        var ordered = warnings.OrderBy(warning => warning.Description, StringComparer.Ordinal)
            .ThenBy(warning => warning.Subject[1].ObjectId, StringComparer.Ordinal).ToArray();
        var summary = check.Summary.ToList();
        summary.Add(new(GrammarFindingCodes.RestrictedSolidBlocksHyphen, Label, GrammarDiagnosticLevel.Warning, ordered.Length));
        return check with { Findings = check.Findings.Concat(ordered).ToArray(), Summary = summary };
    }

    private static bool IsRestrictedSolidCopy(IMoAffixProcess form)
    {
        var input = form.InputOS.ToArray();
        return input.Length > 0 && input.All(part => part is IPhSimpleContextSeg or IPhSimpleContextNC) &&
            form.OutputOS.Count == input.Length * 2 && form.OutputOS.Select((step, index) =>
                step is IMoCopyFromInput copy && copy.ContentRA?.Guid == input[index % input.Length].Guid).All(matches => matches);
    }

    private static bool IsWholeStemHyphenCopy(LcmCache cache, IMoAffixProcess form)
    {
        if (form.InputOS.Count != 1 || form.InputOS[0] is not IPhVariable variable || form.OutputOS.Count != 3)
            return false;
        return form.OutputOS[0] is IMoCopyFromInput first && first.ContentRA?.Guid == variable.Guid &&
            form.OutputOS[2] is IMoCopyFromInput last && last.ContentRA?.Guid == variable.Guid &&
            form.OutputOS[1] is IMoInsertPhones insert && insert.ContentRS.Count == 1 &&
            insert.ContentRS[0] is IPhPhoneme hyphen && hyphen.CodesOS.Count > 0 &&
            hyphen.CodesOS[0].Representation.get_String(cache.DefaultVernWs)?.Text?.Trim() == "-";
    }

    private static GrammarWarning Warning(LcmCache cache, ILexEntry entry, IMoAffixProcess solid, IMoAffixProcess hyphen)
    {
        var entryName = Name(cache, entry.LexemeFormOA?.Form,
            entry.SensesOS.Select(sense => Name(cache, sense.Gloss, "")).FirstOrDefault(name => name.Length > 0) ?? "unnamed entry");
        var solidName = Name(cache, solid.Form, "solid copy") + " (" +
            string.Join("", solid.InputOS.Select(part => ContextName(cache, part))) + " copied twice)";
        var hyphenName = Name(cache, hyphen.Form, "hyphenated copy") + " (whole stem + '-' + whole stem)";
        var description = $"Entry '{entryName}' puts restricted solid form '{solidName}' ahead of unrestricted hyphen form '{hyphenName}'. " +
            "The solid form can block the hyphen spelling for stems its shape matches.";
        return new GrammarWarning(GrammarDiagnosticLevel.Warning, Label,
            [Subject(entry, entryName), Subject(solid, solidName), Subject(hyphen, hyphenName)], [], description)
        {
            Code = GrammarFindingCodes.RestrictedSolidBlocksHyphen,
            Group = "Allomorph order",
            Title = Label,
            Description = description,
            Explanation = "An earlier matching solid form can block a later hyphenated whole-stem copy in FieldWorks' HermitCrab and PanGloss.",
            Guidance = "If both spellings should parse, put the restricted solid form in its own entry.",
            Origin = GrammarFindingOrigin.Check,
            Scope = GrammarSubjectStatus.Object,
        };
    }

    private static GrammarWarningPart Subject(ICmObject item, string title) =>
        new(title, GrammarWarningPartRole.Object, CanonicalId.FromGuid(item.Guid).Value, item.ClassName)
        {
            Title = title,
            SubjectGuid = item.Guid.ToString("D"),
            Status = GrammarSubjectStatus.Object,
        };

    private static string ContextName(LcmCache cache, ICmObject part) => part switch
    {
        IPhSimpleContextSeg segment => Name(cache, segment.FeatureStructureRA?.CodesOS.FirstOrDefault()?.Representation, "segment"),
        IPhSimpleContextNC naturalClass => "[" + Name(cache, naturalClass.FeatureStructureRA?.Abbreviation, "class") + "]",
        _ => "shape",
    };

    private static string Name(LcmCache cache, IMultiAccessorBase? name, string fallback)
    {
        if (name is null) return fallback;
        var text = WritingSystemTextReader.BestAnalysis(cache, name).Text;
        if (text.Length == 0) text = WritingSystemTextReader.First(cache, name).Text;
        return text.Length == 0 ? fallback : text;
    }
}
