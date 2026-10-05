using SIL.Motif.Contract.Responses;
using System.Text.Json;

namespace SIL.Motif.Tests.App;

/// <summary>
/// A synthetic schema 4 grammar check with PanGloss text, structured FieldWorks places, and linked subjects.
/// The screenshots and Warnings page tests share it, so the pictures show what the tests pin.
/// </summary>
internal static class SeededGrammarFindings
{
    private const string Link = "silfw://localhost/link?database=Sample&tool=";
    private const string UnsegmentableFormGuid = "5f0a2e3c-1b1d-4c55-9c1e-6d2a3b4c5d02";

    /// <summary>The objects each finding names that FieldWorks can open.</summary>
    public static int LinkedSubjectCount => All().Where(finding => finding.Subject.Any(part => part.FieldWorksLink is not null))
        .Select(finding => finding.Text).Distinct(StringComparer.Ordinal).Count();

    public static IReadOnlyList<GrammarWarning> All()
    {
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "TestFixtures", "GrammarHealth", "catalog-advice-v0.6.2.json")));
        var findings = new List<GrammarWarning>();
        void Add(string code, string group, GrammarWarningPart? subject, string problem, int count,
            GrammarFindingOrigin origin = GrammarFindingOrigin.Import, string? explanation = null,
            string? guidance = null)
        {
            if (catalog.RootElement.TryGetProperty(code, out var advice))
            {
                explanation ??= advice.GetProperty("explanation").GetString();
                guidance ??= advice.GetProperty("guidance").GetString()!
                    .Replace("{subject}", subject?.Title ?? subject?.Text ?? "the item", StringComparison.Ordinal);
            }
            var text = $"warning: {code}: " + (subject is null ? problem : $"{subject.Text}: {problem}");
            var words = subject?.Reach is { IsRoute: true } reach
                ? new WarningWords(reach.Path == WarningWordsPath.Spelling ? WarningWordsMatch.Spelling
                    : reach.Path == WarningWordsPath.Membership ? WarningWordsMatch.Membership
                    : WarningWordsMatch.Identity, [], []) { Paths = [reach.Path] }
                : null;
            for (var index = 0; index < count; index++)
            {
                findings.Add(new GrammarWarning(GrammarDiagnosticLevel.Warning, group,
                    subject is null ? [] : [subject], [new GrammarWarningPart(problem, GrammarWarningPartRole.Text)], text)
                {
                    Group = group,
                    Code = code,
                    Title = code == "grammar.environment.invalid" ? "Environment could not be read" : group,
                    Description = problem,
                    Explanation = explanation,
                    Guidance = guidance,
                    FieldWorksPlaces = PlacesFor(code),
                    Origin = origin,
                    YourWords = words,
                });
            }
        }

        Add("grammar.environment.invalid", "Invalid phonological environment", null,
            "environment representation failed validation", 7);
        Add("grammar.environment.invalid", "Invalid phonological environment",
            Named("e2 (/ _ [C])", "PhEnvironment", "EnvironmentEdit", "5f0a2e3c-1b1d-4c55-9c1e-6d2a3b4c5d01",
                new WarningReach(WarningWordsPath.ThroughAllomorphs)),
            "unknown natural class \"C\"; treated as absent", 5);
        Add("conversion.unsegmentable-form", "Allomorph form cannot be segmented",
            Named("kat", "MoForm", "lexiconEdit", UnsegmentableFormGuid,
                new WarningReach(WarningWordsPath.ThroughAllomorphs)
                { AllomorphIds = [UnsegmentableFormGuid] }),
            "cannot segment \"kat\": no character definition matches at position 0; skipped", 3);
        Add("grammar.msa.no-rule-form-allomorphs", "Analysis has no usable affix form", null,
            "MSA has zero loadable allomorphs for this stratum bucket", 2);
        Add("grammar.phoneme.nfd-collision", "Phoneme representation collision",
            Named("ng'", "PhPhoneme", "phonemeEdit", "5f0a2e3c-1b1d-4c55-9c1e-6d2a3b4c5d03",
                new WarningReach(WarningWordsPath.Spelling) { Spellings = ["ng'"] }),
            "representation collides with an earlier phoneme/boundary; skipped", 1);
        Add("migration.inferred-segment-with-feature-rule", "Character is not listed as a phoneme", null,
            "inferred segment \"ŋ\" carries no authored feature values, so it satisfies every feature-based natural class", 2);
        var entry = 10;
        foreach (var name in new[] { "mbo - ADD", "di - EVID", "phwet - entrar", "botari - boa tarde" })
        {
            Add("hc-stem-no-grammatical-category", "Stem has no category",
                Named(name, "LexEntry", "lexiconEdit", $"5f0a2e3c-1b1d-4c55-9c1e-6d2a3b4c5d{entry++}",
                    new WarningReach(WarningWordsPath.ThroughAllomorphs)),
                $"Lexical entry '{name}' has no grammatical category.", 1, GrammarFindingOrigin.Check,
                explanation: "This stem is marked partial because its grammatical category is missing. " +
                    "Its category restrictions cannot be enforced as authored.",
                guidance: "In Lexicon > Lexicon Edit, open the named entry and set Grammatical Info. > Category " +
                    "for its stem analysis.");
        }
        return findings;
    }

    private static GrammarWarningPart Named(string title, string kind, string tool, string guid,
        WarningReach? reach = null) =>
        new(title, GrammarWarningPartRole.Object, guid, kind, $"{Link}{tool}&guid={guid}")
        {
            Title = title, Status = GrammarSubjectStatus.Object, SubjectGuid = guid, FieldWorksGuid = guid, FieldWorksTool = tool,
            LinkStatus = FieldWorksLinkStatus.Available, Reach = reach,
        };

    private static IReadOnlyList<GrammarFieldWorksPlace> PlacesFor(string code) => code switch
    {
        "grammar.environment.invalid" => [new("EnvironmentEdit", "Representation")],
        "conversion.unsegmentable-form" => [new("lexiconEdit", "Form")],
        "grammar.msa.no-rule-form-allomorphs" => [new("lexiconEdit", "Morphology")],
        "grammar.phoneme.nfd-collision" => [new("phonemeEdit", "Representation")],
        _ => [],
    };
}
