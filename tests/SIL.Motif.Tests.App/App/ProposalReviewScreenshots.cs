using System.Globalization;
using Avalonia;
using Avalonia.Styling;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Saves Review changes with a Proposal open: grammar and word entries, the looked-at tally, the Apply card, and the
/// prompt that names what else Undo removes.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProposalReviewScreenshots
{
    private const string ProposalId = "proposal/sound";

    [ScreenshotFact]
    public void CaptureReviewOfAProposal()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
            var fake = new FakeCommandClient();
            var kitabu = WordChange("kitabu");
            var watoto = WordChange("watoto");
            fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one", [kitabu, watoto],
                [new ChangeFit("kitabu", true, []), new ChangeFit("watoto", true, [])]));
            fake.ReadTrialCompletesWith(new TrialRecord("job/trial", ProposalId, "sha256:intent",
                PageScreenshots.Token(), null,
                new Dictionary<string, string> { [AssessmentKinds.ParseTime] = "assessment/trial" }, ["kitabu"], true,
                null, JobStatus.Completed, new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero),
                TrialFreshness.Current));
            fake.Review = new ProposalReviewResponse(ProposalId, "sha256:intent", "sha256:revision",
            [
                Rule("rule", 0, null, "Voicing rule before vowels"),
                Rule("retarget", 1, "rule", "Retarget the restriction to the new rule"),
                Word(2, kitabu),
                Word(3, watoto),
            ], null, null, [], 0) { EvidenceDigest = "sha256:evidence", Label = "Sound change" };
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                await workspace.Context.OpenProposalAsync(ProposalId, CancellationToken.None);
                var page = workspace.PageModel<ReviewPageModel>();
                workspace.CurrentPage = WorkspacePage.Review;
                await page.LoadReviewAsync();
                while (page.Current?.Key != "rule" && page.PreviousCommand.CanExecute(null))
                    page.PreviousCommand.Execute(null);
                Assert.Equal("rule", page.Current!.Key);
                PageScreenshots.Settle(window);
                await page.KeepCommand.ExecuteAsync(null);
                await page.KeepCommand.ExecuteAsync(null);
                PageScreenshots.Settle(window);
                Assert.Equal("2 of 4 looked at", page.LookedAtText);
                ReviewScreenshots.SaveAll(window, folder, "proposal-review-list");

                page.PreviousCommand.Execute(null);
                page.PreviousCommand.Execute(null);
                Assert.Equal("rule", page.Current!.Key);
                Assert.Equal(["rule"], page.Entries[1].Entry!.DependsOnEntries);
                await page.UndoCommand.ExecuteAsync(null);
                PageScreenshots.Settle(window);
                Assert.NotNull(page.UndoPrompt);
                ReviewScreenshots.SaveAll(window, folder, "proposal-review-undo-dependents");
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    private static GrammarChangeEntry Rule(string key, int ordinal, string? dependsOn, string title) =>
        new(key, ordinal, title, ["op-" + key], dependsOn is null ? [] : [dependsOn], "digest-" + key, false,
            "AuthorPhonologicalRule", "1 rule", [], [], []);

    private static PendingChange WordChange(string word) => ReviewScreenshots.Change(word, ChangeKinds.Approve,
        "analysis/" + word, ReviewScreenshots.Stored(("ki-", "7"), ("tabu", "book")));

    private static WordChangeEntry Word(int ordinal, PendingChange change) =>
        new(change.Word, ordinal, "Approve " + change.Word, change.OperationIds, [], "digest-" + change.Word, false,
            change, null, new Paired<ParserReading?>(ReviewScreenshots.Reading(("ki-", "7"), ("tabu", "book")), null,
                true, false, false, null));
}
