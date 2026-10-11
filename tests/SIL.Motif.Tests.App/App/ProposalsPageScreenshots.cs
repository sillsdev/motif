using System.Globalization;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Saves the Proposals page with one Proposal in each group, as the proposal-ready project shows it.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProposalsPageScreenshots
{
    [ScreenshotFact]
    public void CaptureProposalsPage()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
            var fake = new FakeCommandClient();
            fake.ProposalStandingsCompleteWith(new ProposalStandings(
            [
                Card("proposal/sound", "Sound change", ProposalGroup.WaitingForYou, Summary(TrialFreshness.Current),
                    [new("rule", 2, "2 rules"), new("allomorph", 1, "1 allomorph")]),
                Card("proposal/gloss", "Gloss for sense", ProposalGroup.NeedsAnotherTry,
                    Summary(TrialFreshness.BaselineMoved), [new("gloss", 1, "1 gloss")]),
                Card("proposal/new", "Plural prefix", ProposalGroup.NotTried, null, [new("affix", 1, "1 affix")]),
                Card("proposal/mine", "Your changes", ProposalGroup.NotAppliedYet, null, [new("parse-approved", 3, "3 parses approved")],
                    pending: true),
            ], 2));
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                workspace.CurrentPage = WorkspacePage.Proposals;
                PageScreenshots.Settle(window);
                ReviewScreenshots.SaveAll(window, folder, "proposals-page");
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    private static ProposalCard Card(string id, string label, ProposalGroup group, TrialSummary? trial,
        IReadOnlyList<ChangeKindCount> kinds, bool pending = false) =>
        new(id, label, "proposed", group, pending, kinds,
            group switch
            {
                ProposalGroup.WaitingForYou => TrialStanding.Current,
                ProposalGroup.NeedsAnotherTry => TrialStanding.NeedsAnotherTry(TrialStaleReason.BaselineMoved),
                _ => TrialStanding.NotTried,
            }, trial, []);

    private static TrialSummary Summary(TrialFreshness freshness) => new("job/1",
        new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero), freshness,
        TrialWordScope.AllWords,
        new Paired<double?>(60, 64.5, true, true, true, null), new Paired<int>(10, 9, true, true, true, null),
        new Paired<int>(12, 12, true, true, false, null), new Paired<double?>(2500, 2600, true, true, false, null),
        new Paired<int?>(3, 3, true, true, false, null), 1, 0);
}
