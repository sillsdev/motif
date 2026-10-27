using SIL.Motif.App;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

/// <summary>Switches every page and Texts tab over the 3000-word large fixture with the trace on (opt-in, MOTIF_PERF=1).</summary>
[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "System")]
[Trait("Category", "Perf")]
public sealed class LargeProjectViewSwitchPerfTests
{
    [PerfFact]
    public void EveryPageAndViewSwitchesOverTheLargeProject()
    {
        var path = Environment.GetEnvironmentVariable("MOTIF_PERF_TRACE")
            ?? Path.Combine(Path.GetTempPath(), "motif-perf-large-trace.jsonl");
        File.Delete(path);
        var warmPasses = int.TryParse(Environment.GetEnvironmentVariable("MOTIF_PERF_N"), out var n) && n > 0 ? n : 3;
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var project = new LargeProjectFixture();
            var parser = FakeParser.CopyRecordingInvocations(project.ManagedRoot);
            FakeParser.BehaveBesideExecutable(parser, new
            {
                words = project.Words.Select((word, index) => new
                {
                    word = word.Form,
                    outcome = index % 5 == 0 ? "capped" : index % 5 == 1 ? "no-analysis" : "complete",
                    elapsedMs = 1 + index % 20,
                    analyses = index % 5 < 2 ? Array.Empty<object>() : new object[]
                    {
                        new { morphs = new[] { new
                        {
                            form = word.AllomorphId.ToString("D"), msa = word.MsaId.ToString("D"),
                            inflType = (string?)null, guessedString = (string?)null,
                        } } },
                    },
                }),
            });
            var client = RealCommandClient.Create(project.ManagedRoot, parser);
            Assert.True((await client.CaptureBaselineAsync(new BaselineCaptureRequest(project.FwDataPath), CancellationToken.None)).Succeeded);
            Assert.True((await client.SetDefaultSelectionAsync(new SetDefaultSelectionRequest(
                project.FwDataPath, "Large Selection", project.TextIds, []), CancellationToken.None)).Succeeded);
            Assert.True((await client.SkipSetupAsync(new SkipSetupRequest(project.FwDataPath), CancellationToken.None)).Succeeded);
            Assert.True((await client.AssessAsync(new AssessRequest(project.FwDataPath),
                new Progress<AssessmentProgress>(), CancellationToken.None)).Succeeded);
            var pending = await client.LoadPendingChangesAsync(
                new PendingChangesRequest(project.FwDataPath, MotifProductVersion.CurrentText), CancellationToken.None);
            var revision = pending.Value!.Revision;
            foreach (var word in project.Words.Take(200))
            {
                var put = await client.PutPendingChangeAsync(new PutPendingChangeRequest(
                    project.FwDataPath, MotifProductVersion.CurrentText, revision,
                    new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                        CanonicalId.FromGuid(word.WordformId).Value, word.Form)), CancellationToken.None);
                revision = put.Value!.Revision;
            }
            using var window = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath, parserPath: parser);
            window.Show();
            window.OpenRecentProjectByClick(project.FwDataPath);
            await window.Workspace.Context.EvidencePublication;
            window.WaitUntilProjectIsQuiet(TimeSpan.FromSeconds(60), "large project did not finish opening");
            window.ShowPage(WorkspacePage.Overview);
            PageScreenshots.Settle(window.Window);
            // Prime the Texts reader, as a user who has opened Texts once would have.
            window.ShowPage(WorkspacePage.Texts);
            window.WaitUntil(() => window.Workspace.PageModel<TextsPageModel>().ResultsInText.Texts.Count == LargeProjectFixture.TextCount,
                TimeSpan.FromSeconds(60), "the large Texts were not loaded");
            window.ShowPage(WorkspacePage.Overview);
            PageScreenshots.Settle(window.Window);

            using var sink = MotifTrace.StartListening(path);
            for (var pass = 0; pass <= warmPasses; pass++)
            {
                MotifTrace.RunLabel = $"{(pass == 0 ? "cold" : "warm")} large";
                foreach (var (name, page, tab) in PageScreenshots.Views())
                    await ViewSwitchPerfTests.Switch(window.Workspace, window.Window, name, page, tab);
            }
            MotifTrace.RunLabel = null;
        }, TimeSpan.FromMinutes(20));
    }
}
