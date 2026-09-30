using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Tests.App;

/// <summary>
/// A synthetic grammar check in the shape PanGloss reports: real warning codes and group names, the parser's own
/// sentences, and named FieldWorks objects carrying their links. The screenshots and the Warnings page's word
/// tests share it, so the pictures show what the tests pin.
/// </summary>
internal static class SeededGrammarFindings
{
    private const string Link = "silfw://localhost/link?database=Sample&tool=";

    /// <summary>The objects each finding names that FieldWorks can open.</summary>
    public static int LinkedSubjectCount => All().Where(finding => finding.Subject.Any(part => part.FieldWorksLink is not null))
        .Select(finding => finding.Text).Distinct(StringComparer.Ordinal).Count();

    public static IReadOnlyList<GrammarWarning> All()
    {
        var findings = new List<GrammarWarning>();
        void Add(string code, string group, GrammarWarningPart? subject, string problem, int count,
            GrammarFindingOrigin origin = GrammarFindingOrigin.Import)
        {
            var text = $"warning: {code}: " + (subject is null ? problem : $"{subject.Text}: {problem}");
            for (var index = 0; index < count; index++)
            {
                findings.Add(new GrammarWarning(GrammarDiagnosticLevel.Warning, group,
                    subject is null ? [] : [subject], [new GrammarWarningPart(problem, GrammarWarningPartRole.Text)], text)
                {
                    Group = group, Code = code, Description = problem, Origin = origin,
                });
            }
        }

        Add("grammar.environment.invalid", "Invalid phonological environment", null,
            "environment representation failed validation", 7);
        Add("grammar.environment.invalid", "Invalid phonological environment",
            Named("e2 (/ _ [C])", "PhEnvironment", "EnvironmentEdit", "5f0a2e3c-1b1d-4c55-9c1e-6d2a3b4c5d01"),
            "unknown natural class \"C\"; treated as absent", 5);
        Add("conversion.unsegmentable-form", "Allomorph form cannot be segmented",
            Named("kat", "MoForm", "lexiconEdit", "5f0a2e3c-1b1d-4c55-9c1e-6d2a3b4c5d02"),
            "cannot segment \"kat\": no character definition matches at position 0; skipped", 3);
        Add("grammar.msa.no-rule-form-allomorphs", "Analysis has no usable affix form", null,
            "MSA has zero loadable allomorphs for this stratum bucket", 2);
        Add("grammar.phoneme.nfd-collision", "Phoneme representation collision",
            Named("ng'", "PhPhoneme", "phonemeEdit", "5f0a2e3c-1b1d-4c55-9c1e-6d2a3b4c5d03"),
            "representation collides with an earlier phoneme/boundary; skipped", 1);
        Add("migration.inferred-segment-with-feature-rule", "Character is not listed as a phoneme", null,
            "inferred segment \"ŋ\" carries no authored feature values, so it satisfies every feature-based natural class", 2);
        var entry = 10;
        foreach (var name in new[] { "mbo - ADD", "di - EVID", "phwet - entrar", "botari - boa tarde" })
        {
            Add("hc-partial-morpheme", "Partial morpheme analysis",
                Named(name, "LexEntry", "lexiconEdit", $"5f0a2e3c-1b1d-4c55-9c1e-6d2a3b4c5d{entry++}"),
                $"Lexical entry '{name}' is partially analyzed.", 1, GrammarFindingOrigin.Check);
        }
        return findings;
    }

    private static GrammarWarningPart Named(string title, string kind, string tool, string guid) =>
        new(title, GrammarWarningPartRole.Object, guid, kind, $"{Link}{tool}&guid={guid}")
        {
            Title = title, SubjectGuid = guid, FieldWorksGuid = guid, FieldWorksTool = tool,
            LinkStatus = FieldWorksLinkStatus.Available,
        };
}
