using SIL.Motif.Contract;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Tests.App;

internal static class ProposalsCaptureFixture
{
    public const string SelectedProposalId = "proposal/nasal-assimilation";

    public static IReadOnlyList<ProposalCard> Cards { get; } =
    [
        Card("pending/one", "Your changes", "proposed", ProposalGroup.NotAppliedYet,
            [new("rule", 6, "6 changes")], TrialStanding.NotTried, isPending: true),
        Card(SelectedProposalId, "Nasal assimilation before /p/", "proposed", ProposalGroup.WaitingForYou,
            [new("rule", 1, "1 rule"), new("parse-approved", 2, "2 words")], TrialStanding.Current,
            Trial("nasal", 93, 95, 41, 43, 52, 52, 24, 22, 2400, 2300)),
        Card("proposal/reduplicated-forms", "Reduplicated forms keep Cont", "proposed", ProposalGroup.NeedsAnotherTry,
            [new("rule", 9, "9 changes")], TrialStanding.NeedsAnotherTry(TrialStaleReason.BaselineMoved),
            Trial("reduplicated", 93, 96, 41, 48, 52, 60, 24, 24, 2400, 2900,
                TrialFreshness.BaselineMoved)),
        Card("proposal/retire-environments", "Retire unused environments", "proposed", ProposalGroup.WaitingForYou,
            [new("rule", 4, "4 changes")], TrialStanding.Current,
            Trial("retire", 93, 93, 41, 41, 52, 52, 24, 20, 2400, 2400)),
        Card("proposal/unspecified-nasal", "Unspecified nasal default", "applied", ProposalGroup.SetAside,
            [new("rule", 2, "2 changes")], TrialStanding.Current,
            Trial("unspecified", 92, 93, 39, 41, 50, 50, 26, 24, 2500, 2400)),
        Card("proposal/kan-glosses", "Merge -kan glosses", "rejected", ProposalGroup.SetAside,
            [new("parse-approved", 5, "5 changes")], TrialStanding.Current,
            Trial("kan", 93, 93, 41, 40, 52, 52, 24, 25, 2400, 2400)),
        Card("proposal/old-nasal-rule", "Old nasal rule", "superseded", ProposalGroup.SetAside,
            [new("rule", 1, "1 change")], TrialStanding.Current,
            Trial("old-nasal", 91, 92, 38, 39, 48, 48, 26, 25, 2600, 2500)),
        Card("proposal/men-before-vowels", "Try meN- before vowels", "withdrawn", ProposalGroup.SetAside,
            [new("rule", 3, "3 changes")], TrialStanding.NotTried),
    ];

    public static ProposalStandings For(bool includeClosed) => new(
        includeClosed ? Cards : Cards.Where(card => card.Group != ProposalGroup.SetAside).ToArray(), 4);

    public static void Configure(FakeCommandClient fake) =>
        fake.OnProposalStandings((request, _) => Task.FromResult(
            CommandOutcome<ProposalStandings>.Success(For(request.IncludeSetAside))));

    private static ProposalCard Card(string id, string label, string status, ProposalGroup group,
        IReadOnlyList<ChangeKindCount> kinds, TrialStanding standing, TrialSummary? trial = null,
        bool isPending = false) => new(id, label, status, group, isPending, kinds, standing, trial, []);

    private static TrialSummary Trial(string id, double coverageBefore, double coverageAfter, int keptBefore,
        int keptAfter, int totalBefore, int totalAfter, int warningsBefore, int warningsAfter,
        double timeBeforeMs, double timeAfterMs,
        TrialFreshness freshness = TrialFreshness.Current)
    {
        var completed = new DateTimeOffset(2026, 10, 10, 14, 0, 0, TimeSpan.Zero);
        return new TrialSummary("job/" + id, completed, freshness, TrialWordScope.AllWords,
            new Paired<double?>(coverageBefore, coverageAfter, true, true, true, null),
            new Paired<int>(keptBefore, keptAfter, true, true, true, null),
            // H supplies kept counts; these fixture totals are synthetic and stay explicit.
            new Paired<int>(totalBefore, totalAfter, true, true, false, null),
            new Paired<double?>(timeBeforeMs, timeAfterMs, true, true, false, null),
            new Paired<int?>(warningsBefore, warningsAfter, true, true, false, null), 0, 0);
    }
}
