using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SIL.LCModel;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Projection;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Warns about each form that already ends with the literal material to the right of the slot in every environment it
/// names, or starts with the literal material to the left of it. An environment restricts the neighbour, so a form
/// such as Tagalog prefix `pang` before `ng` is correct; but the same shape is the usual mistake when a stem is
/// meant to stop before a linker (`batan-ung` where `batan-u` was meant), so the finding asks for a second look and
/// refuses nothing. Natural classes, boundaries and optional parts are ignored. Entries marked do-not-use-for-parsing
/// are skipped; pinned by `AFormThatRepeatsTheMaterialItsEnvironmentConditionsOnIsAWarningAndOneThatStopsBeforeItIsNot`.
/// </summary>
internal static class RepeatedEnvironmentMaterialWarnings
{
    internal static GrammarCheckResponse AddTo(GrammarCheckResponse check, LcmCache cache)
    {
        var warnings = new List<GrammarWarning>();
        foreach (var entry in cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances()
                     .Where(entry => !entry.DoNotUseForParsing))
        foreach (var form in Forms(entry))
        {
            var environments = Environments(form).Distinct().ToArray();
            if (environments.Length == 0) continue;
            var repeated = Repeats(Text(cache, form), environments);
            if (repeated is not null) warnings.Add(Warning(cache, entry, form, repeated));
        }
        if (warnings.Count == 0) return check;

        var ordered = warnings.OrderBy(warning => warning.Description, StringComparer.Ordinal).ToArray();
        var summary = check.Summary.ToList();
        summary.Add(new GrammarWarningSummary(GrammarFindingCodes.AllomorphRepeatsEnvironmentMaterial,
            "Allomorph repeats its environment's material", GrammarDiagnosticLevel.Warning, ordered.Length));
        return check with { Findings = check.Findings.Concat(ordered).ToArray(), Summary = summary };
    }

    /// <summary>The explanation of how the form repeats every environment, or null when any one environment is not repeated.</summary>
    internal static string? Repeats(string form, IReadOnlyList<IPhEnvironment> environments)
    {
        var reasons = new List<string>();
        foreach (var environment in environments)
        {
            var pattern = environment.StringRepresentation?.Text ?? string.Empty;
            var slash = pattern.IndexOf('/');
            var body = slash >= 0 ? pattern[(slash + 1)..] : pattern;
            var slot = body.IndexOf('_');
            if (slot < 0) return null;
            var left = Literal(body[..slot]);
            var right = Literal(body[(slot + 1)..]);
            if (right.Length > 0 && form.EndsWith(right, StringComparison.OrdinalIgnoreCase))
                reasons.Add($"'{pattern.Trim()}' (the form already ends with '{right}')");
            else if (left.Length > 0 && form.StartsWith(left, StringComparison.OrdinalIgnoreCase))
                reasons.Add($"'{pattern.Trim()}' (the form already starts with '{left}')");
            else return null;
        }
        return string.Join("; ", reasons);
    }

    private static string Literal(string side) => Regex.Replace(side, @"\[[^\]]*\]|\([^)]*\)|[#\s-]", "");

    private static IEnumerable<IMoForm> Forms(ILexEntry entry) =>
        (entry.LexemeFormOA is { } lexeme ? [lexeme] : Array.Empty<IMoForm>()).Concat(entry.AlternateFormsOS);

    private static IEnumerable<IPhEnvironment> Environments(IMoForm form) => form switch
    {
        IMoStemAllomorph stem => stem.PhoneEnvRC,
        IMoAffixAllomorph affix => affix.PhoneEnvRC,
        _ => [],
    };

    private static string Text(LcmCache cache, IMoForm form)
    {
        var text = WritingSystemTextReader.BestAnalysis(cache, form.Form).Text;
        return text.Length == 0 ? WritingSystemTextReader.First(cache, form.Form).Text : text;
    }

    private static GrammarWarning Warning(LcmCache cache, ILexEntry entry, IMoForm form, string reasons)
    {
        var entryName = Name(cache, entry.LexemeFormOA?.Form, "unnamed entry");
        var formText = Name(cache, form.Form, entryName);
        var subject = new GrammarWarningPart($"Form {formText} of {entryName}", GrammarWarningPartRole.Object,
            CanonicalId.FromGuid(form.Guid).Value, form.ClassName)
        {
            Title = formText,
            Subtitle = "Environment repeats the form",
            SubjectGuid = form.Guid.ToString("D"),
            Status = GrammarSubjectStatus.Object,
        };
        var owner = formText == entryName ? string.Empty : $" of '{entryName}'";
        var description = $"Form '{formText}'{owner} already contains the material its environment conditions on: {reasons}.";
        return new GrammarWarning(GrammarDiagnosticLevel.Warning, "Allomorph repeats its environment's material",
            [subject], [], description)
        {
            Code = GrammarFindingCodes.AllomorphRepeatsEnvironmentMaterial,
            Group = "Lexicon",
            Title = "Allomorph repeats its environment's material",
            Description = description,
            Explanation = "An environment restricts the neighbour the form meets, and the neighbour supplies that material. " +
                "A prefix such as Tagalog `pang` before `ng` is correct. But a stem written to stop before a linker " +
                "(`batan-u` before the linker `ng`) that is spelled with the linker (`batan-ung`) never matches.",
            Guidance = "If the form should stop before the neighbour's material, rewrite it without that material. If it " +
                "genuinely ends or starts with the same letters, as `pang` before `ng`, leave it: Motif keeps what LibLCM can hold.",
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
