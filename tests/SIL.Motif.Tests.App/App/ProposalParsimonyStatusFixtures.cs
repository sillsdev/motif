using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsimony;

namespace SIL.Motif.Tests.App;

/// <summary>The seven Parsimony states of one Proposal, built the way the status command shapes them.</summary>
internal static class ProposalParsimonyStatusFixtures
{
    public const string Computed = "P-adhoc-duplicate";
    public const string Second = "R-env-broad";
    public const string Unavailable = "P-statement-unused";

    private const string Queued = "2026-11-01T12:00:00.0000000Z";

    public static ProposalParsimonyStatus NotSetUp(string id) => Make(id, ProposalParsimonyState.NotSetUp,
        "status-parser-missing", job: null, groups: [Unrun(), TextGroup()]);

    public static ProposalParsimonyStatus NotYetRun(string id, string code = "status-no-trial") =>
        Make(id, ProposalParsimonyState.NotYetRun, code, job: null, groups: [Unrun(), TextGroup()]);

    public static ProposalParsimonyStatus Waiting(string id, string? behind = null) => Make(id,
        ProposalParsimonyState.Queued, "status-queued", Job("queued", null, behind), groups: [Unrun(), TextGroup()]);

    public static ProposalParsimonyStatus Running(string id, string stage = "measure", int step = 3, int steps = 6) =>
        Make(id, ProposalParsimonyState.Running, "status-running",
            Job("running", new JobStageProgress(stage, step, steps), null), groups: [Unrun(), TextGroup()]);

    public static ProposalParsimonyStatus Ready(string id) => Make(id, ProposalParsimonyState.Ready,
        "status-freshness-partial", Job("completed", null, null, finished: true), groups: [Measured(), TextGroup()]);

    public static ProposalParsimonyStatus Stale(string id, params ProposalParsimonyStaleReason[] reasons) => Make(id,
        ProposalParsimonyState.Stale, reasons.Length == 0 ? "status-freshness-unknown" : "status-stale",
        Job("completed", null, null, finished: true), groups: [Measured(), TextGroup()],
        stale: new ProposalParsimonyStale(reasons, "k1", "k2"));

    public static ProposalParsimonyStatus Failed(string id, ProposalParsimonyFailureCategory category,
        string text = "Harness text.", string code = "status-failed") => Make(id, ProposalParsimonyState.Failed, code,
        Job("failed", null, null, finished: true), groups: [Unrun(), TextGroup()],
        failure: new ProposalParsimonyFailure(category, text, false, Queued));

    public static ProposalParsimonyGroup Unrun() => new(ParsimonyMeasureGroup.Grammar,
        ProposalParsimonyGroupState.NotYetRun, null, null, [], []);

    public static ProposalParsimonyGroup TextGroup(ProposalParsimonyGroupState state = ProposalParsimonyGroupState.NotSetUp) =>
        new(ParsimonyMeasureGroup.Text, state, "Harness note about the Text group.", null, [], []);

    /// <summary>A Grammar group with two counted measures and one the evidence did not reach.</summary>
    public static ProposalParsimonyGroup Measured() => new(ParsimonyMeasureGroup.Grammar,
        ProposalParsimonyGroupState.Ready, null, new ProposalParsimonyCandidate("b1", "b2", "id"),
        [
            new ParsimonyMeasureRun(Computed, ParsimonyMeasureStatus.Computed, 10, 2, "allomorph", null, null, null),
            new ParsimonyMeasureRun(Second, ParsimonyMeasureStatus.Computed, 8, 0, "environment", null, null, null),
            new ParsimonyMeasureRun(Unavailable, ParsimonyMeasureStatus.NotAvailable, null, null, "statement", null,
                null, "Missing table."),
        ],
        [
            new ProposalParsimonyMeasureChange(Computed, 2, 1, 5, 3, "r1", "r2"),
            new ProposalParsimonyMeasureChange(Second, 0, 0, 8, 0, "r3", "r4"),
        ]);

    public static ProposalParsimonyStatus With(this ProposalParsimonyStatus status, ProposalParsimonyGroup grammar) =>
        status with { Groups = [grammar, .. status.Groups.Skip(1)] };

    private static ProposalParsimonyJob Job(string status, JobStageProgress? stage, string? behind,
        bool finished = false) => new("job/parsimony", status, stage, Queued, finished ? Queued : null, behind);

    private static ProposalParsimonyStatus Make(string id, ProposalParsimonyState state, string code,
        ProposalParsimonyJob? job, IReadOnlyList<ProposalParsimonyGroup> groups, ProposalParsimonyStale? stale = null,
        ProposalParsimonyFailure? failure = null) =>
        new(ProposalParsimonyStatus.CurrentSchemaVersion, id, "sha256:intent", "job/trial", state,
            new ProposalParsimonyReason(code, "Harness sentence."), job, stale, failure, groups);
}
