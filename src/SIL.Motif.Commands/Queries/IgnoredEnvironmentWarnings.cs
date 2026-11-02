using System;
using System.Collections.Generic;
using System.Linq;
using SIL.LCModel;
using SIL.LCModel.Core.Phonology;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Projection;
using SIL.Motif.Projection.Grammar;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Warns about each form that names an environment the parser cannot read, as one written with a morpheme boundary
/// '+'. FieldWorks' parser loader drops such an environment, so the form is not restricted by it, and PanGloss does
/// the same. The check uses the same recognizer as that loader, over the same phonemes and natural classes.
/// </summary>
internal static class IgnoredEnvironmentWarnings
{
    private const string DottedCircle = "◌";

    internal static GrammarCheckResponse AddTo(GrammarCheckResponse check, LcmCache cache)
    {
        var phonology = cache.LangProject.PhonologicalDataOA;
        if (phonology is null) return check;
        var recognizer = new PhonEnvRecognizer(
            phonology.AllPhonemes().Select(phoneme => phoneme.Replace(DottedCircle, string.Empty)).ToArray(),
            phonology.AllNaturalClassAbbrs().ToArray());
        var verdicts = new Dictionary<Guid, bool>();
        var warnings = new List<GrammarWarning>();
        foreach (var entry in cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances())
        foreach (var form in Forms(entry))
        foreach (var environment in Environments(form).Distinct())
        {
            if (!verdicts.TryGetValue(environment.Guid, out var readable))
                verdicts[environment.Guid] = readable = recognizer.Recognize(environment.StringRepresentation?.Text ?? string.Empty);
            if (!readable) warnings.Add(Warning(cache, entry, form, environment));
        }
        if (warnings.Count == 0) return check;

        var ordered = warnings.OrderBy(warning => warning.Description, StringComparer.Ordinal).ToArray();
        var summary = check.Summary.ToList();
        summary.Add(new GrammarWarningSummary(GrammarFindingCodes.AllomorphEnvironmentIgnoredByParser,
            "Allomorph environment the parser ignores", GrammarDiagnosticLevel.Warning, ordered.Length));
        return check with { Findings = check.Findings.Concat(ordered).ToArray(), Summary = summary };
    }

    private static IEnumerable<IMoForm> Forms(ILexEntry entry) =>
        (entry.LexemeFormOA is { } lexeme ? [lexeme] : Array.Empty<IMoForm>()).Concat(entry.AlternateFormsOS);

    private static IEnumerable<IPhEnvironment> Environments(IMoForm form) => form switch
    {
        IMoStemAllomorph stem => stem.PhoneEnvRC,
        IMoAffixAllomorph affix => affix.PhoneEnvRC.Concat(affix.PositionRS),
        _ => [],
    };

    private static GrammarWarning Warning(LcmCache cache, ILexEntry entry, IMoForm form, IPhEnvironment environment)
    {
        var entryName = Name(cache, entry.LexemeFormOA?.Form, "unnamed entry");
        var formText = Name(cache, form.Form, entryName);
        var pattern = environment.StringRepresentation?.Text ?? string.Empty;
        var subject = new GrammarWarningPart($"Form {formText} of {entryName}", GrammarWarningPartRole.Object,
            CanonicalId.FromGuid(form.Guid).Value, form.ClassName)
        {
            Title = formText,
            Subtitle = $"Environment {pattern}",
            SubjectGuid = form.Guid.ToString("D"),
            Status = GrammarSubjectStatus.Object,
        };
        var owner = formText == entryName ? string.Empty : $" of '{entryName}'";
        var description = $"Form '{formText}'{owner} names the environment '{pattern}', which the parser cannot read, " +
            "so it ignores it and the form is not restricted by it.";
        return new GrammarWarning(GrammarDiagnosticLevel.Warning, "Allomorph environment the parser ignores",
            [subject], [], description)
        {
            Code = GrammarFindingCodes.AllomorphEnvironmentIgnoredByParser,
            Group = "Lexicon",
            Title = "Allomorph environment the parser ignores",
            Description = description,
            Explanation = "The parser reads an environment only when every segment and natural class in it is a phoneme or " +
                "natural class of the project; a boundary marker such as '+' is not one. It drops an environment it cannot " +
                "read, so the form it was written to restrict can appear anywhere, and the parser gives more readings " +
                "than the environment was meant to allow.",
            Guidance = "Rewrite the environment with phonemes and natural classes only. Where the restriction needs a " +
                "morpheme boundary, state it as a phonological rule with a boundary context " +
                "(compose-author-phonological-rule) and limit it with compose-edit-phonological-rule-features, " +
                "and check the result in a Trial. Or correct the environment in FieldWorks: Grammar > Environments.",
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
