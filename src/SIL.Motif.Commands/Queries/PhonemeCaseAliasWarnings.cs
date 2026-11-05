using System.Text;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands.Queries;

/// <summary>Reports missing titlecase and uppercase representations of featured lowercase phonemes.</summary>
internal static class PhonemeCaseAliasWarnings
{
    internal static GrammarCheckResponse AddTo(GrammarCheckResponse check, LcmCache cache)
    {
        var casing = new CaseFunctions(cache.ServiceLocator.WritingSystems.DefaultVernacularWritingSystem);
        var warnings = new List<GrammarWarning>();
        foreach (var set in cache.LangProject.PhonologicalDataOA.PhonemeSetsOS)
        foreach (var phoneme in set.PhonemesOC.Where(phone => phone.FeaturesOA?.FeatureSpecsOC.Count > 0))
        {
            var codes = phoneme.CodesOS.Select(code => code.Representation.get_String(cache.DefaultVernWs)?.Text)
                .OfType<string>().Select(Normalize).Where(text => text.Length > 0).ToHashSet(StringComparer.Ordinal);
            foreach (var lower in codes.Where(text => Normalize(casing.ToLower(text)) == text).Order(StringComparer.Ordinal))
            {
                var missing = new[] { casing.ToTitle(lower), casing.ToUpper(lower) }.Select(Normalize)
                    .Where(text => text != lower && !codes.Contains(text)).Distinct(StringComparer.Ordinal).ToArray();
                if (missing.Length == 0) continue;
                var aliases = string.Join(", ", missing.Select(text => $"'{text}'"));
                var subject = new GrammarWarningPart($"Phoneme '{lower}'", GrammarWarningPartRole.Object,
                    CanonicalId.FromGuid(phoneme.Guid).Value, phoneme.ClassName)
                {
                    Title = lower,
                    SubjectGuid = phoneme.Guid.ToString("D"),
                    Status = GrammarSubjectStatus.Object,
                };
                var description = $"Featured phoneme '{lower}' has no explicit case representation {aliases} on the same phoneme.";
                warnings.Add(new GrammarWarning(GrammarDiagnosticLevel.Warning, "Phoneme case alias missing",
                    [subject], [], description)
                {
                    Code = GrammarFindingCodes.PhonemeCaseAliasMissing,
                    Group = "Sound inventory",
                    Title = "Phoneme case alias missing",
                    Description = description,
                    Explanation = "Implicit writing-system letters carry no phonological features. A capital letter " +
                        "can therefore unify with unrelated featured sounds and admit unintended analyses. " +
                        "A separate capital phoneme does not share this phoneme's identity.",
                    Guidance = $"If {aliases} spell this sound, add them to this phoneme with compose-edit-phoneme. " +
                        "Keep distinct accented sounds on their own phonemes with distinct feature bundles. " +
                        "If a spelling already belongs to another phoneme, review its identity and class membership before merging.",
                    Origin = GrammarFindingOrigin.Check,
                    Scope = GrammarSubjectStatus.Object,
                });
            }
        }
        if (warnings.Count == 0) return check;
        return check with
        {
            Findings = check.Findings.Concat(warnings).ToArray(),
            Summary = check.Summary.Append(new GrammarWarningSummary(GrammarFindingCodes.PhonemeCaseAliasMissing,
                "Phoneme case alias missing", GrammarDiagnosticLevel.Warning, warnings.Count)).ToArray(),
        };
    }

    private static string Normalize(string text) => text.Replace("\u25CC", string.Empty).Normalize(NormalizationForm.FormD);
}
