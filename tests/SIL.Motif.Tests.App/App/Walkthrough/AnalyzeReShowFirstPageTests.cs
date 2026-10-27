using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App.Walkthrough;

/// <summary>
/// Pins the work a warm return to Texts &gt; Analyze does inside the showing frame by counting word strips and
/// visuals, never time: the frame may build only the first-page token budget, and the rest of the first screenful
/// arrives when the deferred Background work has run.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class AnalyzeReShowFirstPageTests(ITestOutputHelper output)
{
    // Budgeted tokens at about 47 visuals each, plus line chrome; the unbudgeted return created 769.
    private const int CreatedVisualBound = 400;

    [Fact]
    public void WarmReturnToAnalyzeBuildsOnlyTheFirstPageTokenBudgetInFrameThenFillsTheScreenful()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var project = new LargeProjectFixture(smallScale: true);
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
            using var window = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath, parserPath: parser);
            window.Show();
            window.OpenRecentProjectByClick(project.FwDataPath);
            await window.Workspace.Context.EvidencePublication;
            window.WaitUntilProjectIsQuiet(TimeSpan.FromSeconds(60), "large project did not finish opening");
            window.ShowPage(WorkspacePage.Texts);
            window.WaitUntil(() => window.Workspace.PageModel<TextsPageModel>().ResultsInText.Texts.Count == project.TextIds.Count,
                TimeSpan.FromSeconds(60), "the large Texts were not loaded");
            var texts = window.Workspace.PageModel<TextsPageModel>();

            // Visit Analyze once, fully settled, as a person who has read it would have.
            texts.Tab = TextsTab.AnalyzeTexts;
            window.WaitUntil(() => { Settle(window); return Strips(window).Count > 0; },
                TimeSpan.FromSeconds(60), "the first visit to Analyze showed no word strips");

            var rounds = new List<(int Tokens, int Created)>();
            for (var round = 0; round < 2; round++)
            {
                texts.Tab = TextsTab.Matrix;
                Settle(window);
                var before = Visuals(window);

                texts.Tab = TextsTab.AnalyzeTexts;
                for (var iteration = 0; iteration < 10; iteration++)
                {
                    Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
                }
                var inFrame = Strips(window).Count;
                var created = Visuals(window).Count(visual => !before.Contains(visual));
                output.WriteLine($"round {round}: in-frame word strips {inFrame}, created visuals {created}");
                rounds.Add((inFrame, created));
                Assert.True(inFrame <= ProgressiveItemsControl.FirstPageTokenBudget,
                    $"{inFrame} word strips were built in the showing frame; the budget is {ProgressiveItemsControl.FirstPageTokenBudget}.");
                Assert.True(created <= CreatedVisualBound,
                    $"{created} visuals were created in the showing frame; the bound is {CreatedVisualBound}.");

                Settle(window);
                var lines = window.Window.GetVisualDescendants().OfType<ProgressiveItemsControl>()
                    .Where(control => control.Name == "ResultsInTextPanelTokensItems").ToArray();
                Assert.NotEmpty(lines);
                foreach (var line in lines)
                {
                    await line.PageRefresh;
                    Settle(window);
                    var expected = Math.Min(20, ((SIL.Motif.App.Services.IProgressivePageSource)line.FullItemsSource!).Count);
                    Assert.Equal(expected, line.GetVisualDescendants().OfType<WordStripToken>().Count());
                }
            }
            // Created visuals vary with one-time template work; the bounds above are the contract.
            Assert.Equal(rounds[0].Tokens, rounds[1].Tokens);
            Assert.True(rounds[1].Created <= rounds[0].Created + CreatedVisualBound / 4,
                $"A second warm return created {rounds[1].Created} visuals after {rounds[0].Created} on the first.");
        }, TimeSpan.FromMinutes(10));
    }

    private static List<WordStripToken> Strips(WalkthroughWindow window) =>
        window.Window.GetVisualDescendants().OfType<WordStripToken>().ToList();

    private static HashSet<Visual> Visuals(WalkthroughWindow window) =>
        new(window.Window.GetVisualDescendants().OfType<Visual>(), ReferenceEqualityComparer.Instance as IEqualityComparer<Visual>);

    private static void Settle(WalkthroughWindow window)
    {
        for (var pass = 0; pass < 3; pass++) PageScreenshots.Settle(window.Window);
    }
}
