using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Commands.Store;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Commands;

public static partial class ProposalCommands
{
    // The verbs that edit one existing allomorph, with the intent property that names it.
    private static readonly Dictionary<string, string> AllomorphEditTargets = new(StringComparer.Ordinal)
    {
        ["compose-edit-allomorph-condition"] = "target",
        ["compose-edit-allomorph-form"] = "target",
        ["compose-edit-stem-allomorph-stem-name"] = "target",
        ["compose-author-allomorph-required-features"] = "allomorph",
    };

    // The verbs that add an alternate to an entry, with the intent property that names the entry.
    private static readonly Dictionary<string, string> AllomorphAddOwners = new(StringComparer.Ordinal)
    {
        ["compose-author-alternate-form"] = "entry",
        ["compose-author-affix-process-allomorph"] = "entry",
    };

    // The shadowed report stored with the Baseline check; an unreadable one is named in an advisory.
    private static ShadowedAlternatesReport? StoredShadowedReport(
        MotifDatabase database, ProjectLocator project, out string? unreadable)
    {
        unreadable = null;
        try
        {
            var baseline = new BaselineRepository(database).GetCurrentPublished(ProjectWorkspaceKey.Compute(project));
            if (baseline is null) return null;
            var token = JsonSerializer.Serialize(baseline.Token, MotifJson.CreateOptions());
            return new GrammarCheckRepository(database).GetLatest(token)?.ShadowedAlternates;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            unreadable = exception.Message;
            return null;
        }
    }

    // Edits of a shadowed allomorph, or adds beside a definite one, name the shadowing alternate.
    private static List<ComposeAdvisory> ShadowedAlternateAdvisories(
        ShadowedAlternatesReport? report, string? unreadable, IReadOnlyList<ComposeStep> steps,
        IReadOnlyList<(JsonElement Input, OperationEnvelope[] Added)> results)
    {
        var advisories = new List<ComposeAdvisory>();
        if (unreadable is not null)
            advisories.Add(new(ComposeAdvisoryCodes.ShadowedAlternate,
                $"The stored shadowed-alternates report could not be read ({unreadable}), so edits were not checked against it. " +
                "Run grammar check to store a fresh one.", []));
        if (report is not { Alternates.Count: > 0 }) return advisories;
        for (var index = 0; index < steps.Count && index < results.Count; index++)
        {
            if (steps[index].Verb is not { } verb || results[index].Input is not { ValueKind: JsonValueKind.Object } input)
                continue;
            if (AllomorphEditTargets.TryGetValue(verb, out var property) && GuidOf(input, property) is { } target)
            {
                foreach (var row in report.Alternates.Where(row => row.SourceAllomorphGuids.Any(guid => SameGuid(guid, target))))
                    advisories.Add(new(ComposeAdvisoryCodes.ShadowedAlternate, row.Tier == ShadowedAlternateTiers.Definite
                        ? $"{verb} edits {row.Label}, which PanGloss reports as shadowed by {Earlier(row)} ({Reasons(row)}): " +
                          "no word can select it, so the edit changes nothing a parse shows. Remove it, or place it before " +
                          "the alternate that shadows it and give it a narrower environment."
                        : $"{verb} edits {row.Label}, which PanGloss could not prove is selectable: it may be shadowed by " +
                          $"{Earlier(row)} ({Reasons(row)}). Check a parse trace before relying on the edit.", [index]));
            }
            else if (AllomorphAddOwners.TryGetValue(verb, out var owner) && GuidOf(input, owner) is { } entry)
            {
                foreach (var row in report.Definite.Where(row => row.EntryGuid is { } guid && SameGuid(guid, entry)))
                    advisories.Add(new(ComposeAdvisoryCodes.ShadowedAlternate,
                        $"{verb} adds an alternate to an entry that already has a shadowed one: PanGloss reports {row.Label} " +
                        $"as shadowed by {Earlier(row)} ({Reasons(row)}). A new alternate placed after {Earlier(row)} with " +
                        "the same conditions would be shadowed too; place it before, or give it its own environment.", [index]));
            }
        }
        return advisories;
    }

    private static string Earlier(ShadowedAlternate row) => row.ShadowedBy.Count == 0
        ? "an earlier alternate" : string.Join(", ", row.ShadowedBy.Select(form => $"'{form}'"));

    private static string Reasons(ShadowedAlternate row) =>
        row.Reasons.Count == 0 ? "no reason given" : string.Join(", ", row.Reasons);

    private static Guid? GuidOf(JsonElement input, string property) =>
        input.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String &&
        CanonicalId.TryParse(value.GetString(), out var id) ? id.ToGuid() : null;

    private static bool SameGuid(string text, Guid guid) => Guid.TryParse(text, out var parsed) && parsed == guid;
}
