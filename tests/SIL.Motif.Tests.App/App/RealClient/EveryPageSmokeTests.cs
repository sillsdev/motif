using Avalonia.Controls;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Tests.App.Walkthrough;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

/// <summary>
/// The screenshot harness promoted to a test: open the real window over a disposable copy of the fixture project,
/// parse a small selection, and visit every registered page with Diagnostics on and off. A page may not draw a
/// control as its type name, stay blank under its title, keep a "Reading…" state after it settles, or clip text at
/// the smallest supported size. Failures are collected across every page so one run names them all.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "Integration")]
public sealed class EveryPageSmokeTests(PristineProjectFixture pristine, Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void EveryPageOfAParsedProjectReadsAsWordsWithDiagnosticsOnAndOff()
    {
        using var project = new WalkthroughProject(pristine);
        var parser = FakeParser.CopyRecordingInvocations(project.ManagedRoot);
        var deadline = WalkthroughSteps.DeadlineIn(240);
        var faults = new List<string>();

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var walkthrough = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath, parserPath: parser);
            walkthrough.Show();
            walkthrough.ChooseNewProject();
            walkthrough.WaitUntil(() => walkthrough.Workspace.Baseline.HasBaseline &&
                walkthrough.Workspace.Selection.Texts.Count == 1 && walkthrough.Workspace.Context.Setup?.IsOpen == true,
                WalkthroughSteps.Remaining(deadline), "the fixture project did not open");
            SetupWalkthroughActions.FinishFirstRun(walkthrough, SeededProject.TextTitle,
                ParserWorkLimit.DefaultWork.ToString(), WalkthroughSteps.Remaining(deadline));
            var reading = new
            {
                morphs = new[]
                {
                    new
                    {
                        form = pristine.Seed.FirstLexemeFormId.ToString("D"),
                        msa = project.FirstMsaId.ToString("D"),
                        inflType = (string?)null,
                        guessedString = (string?)null,
                    },
                },
            };
            walkthrough.SetFakeParserBehavior(new
            {
                words = new object[]
                {
                    new { word = SeededProject.AnalysedWordForm, outcome = "complete", signature = "smoke" },
                    new { word = SeededProject.UnanalysedWordForm, outcome = "complete", signature = "smoke-reading",
                        analyses = new[] { reading } },
                },
            });
            walkthrough.TypePastedWords(SeededProject.UnanalysedWordForm);
            walkthrough.Workspace.Assess.RunCommand.Execute(null);
            walkthrough.WaitUntil(() => walkthrough.Workspace.Assess.State == RunState.Completed &&
                walkthrough.Workspace.Context.EvidencePublication.IsCompleted,
                WalkthroughSteps.Remaining(deadline), "the selection did not finish parsing");

            var workspace = walkthrough.Workspace;
            foreach (var (width, height) in new[] { (1440, 900), (640, 600) })
            {
                walkthrough.Window.Width = width;
                walkthrough.Window.Height = height;
                foreach (var diagnostics in new[] { true, false })
                {
                    workspace.CurrentPage = WorkspacePage.Progress;
                    workspace.Context.Diagnostics = diagnostics;
                    foreach (var entry in PageRegistry.Entries)
                    {
                        var scene = $"{entry.Page}@{width}x{height} diagnostics={(diagnostics ? "on" : "off")}";
                        if (entry.IsDiagnostic && !diagnostics) continue;
                        workspace.CurrentPage = entry.Page;
                        if (entry.Page == WorkspacePage.Words) OpenAWordCard(walkthrough, workspace, deadline);
                        SettlePage(walkthrough, deadline);
                        var view = walkthrough.Window.PageView(entry.Page);
                        var pageTexts = RenderedPageText.Visible(view);
                        // The shared word panel and the status bar sit outside the page's own view, so look at the window.
                        var texts = RenderedPageText.Visible(walkthrough.Window);
                        output.WriteLine($"{scene}: {texts.Count} texts; body {RenderedPageText.Body(pageTexts, entry.Title, project.FwDataPath).Count}");
                        foreach (var name in RenderedPageText.TypeNameFaults(texts))
                            faults.Add($"{scene}: draws a type name '{name}'");
                        foreach (var stillReading in RenderedPageText.StillReadingFaults(texts))
                            faults.Add($"{scene}: still shows '{stillReading}' after settling");
                        if (RenderedPageText.Body(pageTexts, entry.Title, project.FwDataPath).Count == 0)
                            faults.Add($"{scene}: shows nothing but its title");
                        if (width == 640)
                            foreach (var clip in RenderedPageText.ClipFaults(texts))
                                faults.Add($"{scene}: {clip}");
                    }
                }
            }
            await Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));

        Assert.True(faults.Count == 0, string.Join(Environment.NewLine, faults.Distinct()));
    }

    /// <summary>Opens the first listed word so its shared word panel is on the Words page for the visits.</summary>
    private static void OpenAWordCard(WalkthroughWindow walkthrough, WorkspaceShellViewModel workspace, long deadline)
    {
        var words = workspace.PageModel<WordsPageModel>();
        words.Words.SearchText = SeededProject.UnanalysedWordForm;
        walkthrough.WaitUntil(() => !words.Words.IsLoading && words.Words.Rows.Any(row => row.Form == SeededProject.UnanalysedWordForm),
            WalkthroughSteps.Remaining(deadline), "the Words page did not list the unanalysed word");
        words.Words.Rows.First(row => row.Form == SeededProject.UnanalysedWordForm).Listed.IsOpen = true;
        walkthrough.WaitUntil(() => words.OccurrencePanel is not null, WalkthroughSteps.Remaining(deadline),
            "the word panel did not open");
        var panel = words.OccurrencePanel!;
        if (panel.ChosenReading is null && panel.Readings.Count > 0) panel.ChosenReading = panel.Readings[0];
        panel.CheckShownGaps();
    }

    /// <summary>Gives the page a bounded wait for its reading state to go, then settles layout.</summary>
    private static void SettlePage(WalkthroughWindow walkthrough, long deadline)
    {
        PageScreenshots.Settle(walkthrough.Window);
        try
        {
            walkthrough.WaitUntil(() =>
            {
                PageScreenshots.Settle(walkthrough.Window);
                var view = walkthrough.Window.PageView(walkthrough.Workspace.CurrentPage);
                return !RenderedPageText.StillReadingFaults(RenderedPageText.Visible(view)).Any();
            }, TimeSpan.FromSeconds(Math.Min(20, WalkthroughSteps.Remaining(deadline).TotalSeconds)), "page kept reading");
        }
        catch (Exception) { /* the test reports what still shows */ }
        PageScreenshots.Settle(walkthrough.Window);
    }
}
