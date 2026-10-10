using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Styling;
using SIL.Motif.App.Walkthroughs;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class ExplainedWordCardCaptureTests
{
    [Fact]
    public async Task TheStagedCardIsNotQuietUntilApprovingTheWordHasMarkedItRead()
    {
        var root = WalkthroughReplayTestSupport.FindRepositoryRoot();
        var script = WalkthroughScriptLoader.Load(
            Path.Combine(root, "walkthroughs", "explained-word-card.walkthrough.json"));
        var steps = script.Steps.ToList();
        var stage = steps.FindIndex(step => step.Id == "stage-evler-approval");
        var capture = steps.FindIndex(step => step.Id == "unknown-staged");
        var help = WalkthroughHelpContent.Load(root, script.Id, "en");
        var clock = new FixedClock(WalkthroughReplayTestSupport.CaptureTime, TimeZoneInfo.Utc);
        using var project = await ExplainedWordCardWalkthroughProject.CreateAsync(
            WalkthroughTestFiles.EngineRoot(script.Id + "-held-read-state"));
        var context = new WalkthroughProjectContext(
            project.ManagedRoot, project.FwDataPath, project.TextId, project.ParserPath);
        var preparation = await WalkthroughFixtureSeeder.SeedAsync(script.Fixture, context, clock, project.ParserPath);
        var budget = WalkthroughReplay.DeadlineBudget(script);
        var deadline = Stopwatch.GetTimestamp() + (long)(budget.TotalSeconds * Stopwatch.Frequency);
        var gate = new HoldingStartGate();

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var previousCulture = CultureInfo.CurrentCulture;
            var previousTheme = Application.Current!.RequestedThemeVariant;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                Application.Current.RequestedThemeVariant = ThemeVariant.Light;
                using var walkthrough = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath,
                    startGate: gate, timeProvider: clock, parserPath: project.ParserPath);
                walkthrough.Window.Width = WalkthroughArtifacts.Width;
                walkthrough.Window.Height = 800;
                walkthrough.Show();
                WalkthroughReplay.Run(walkthrough, script with { Steps = steps[..stage] }, help, clock,
                    [], [], deadline, root, preparation);
                gate.HoldWordReadState();
                try
                {
                    WalkthroughReplay.Run(walkthrough, script with { Steps = steps[stage..capture] }, help, clock,
                        [], [], deadline, root, preparation);
                    var evler = AnalyzeTextsLayoutTests.RealizedLines(
                        walkthrough.Workspace.PageModel<TextsPageModel>().ResultsInText).SelectMany(line => line.Tokens)
                        .Single(token => token.Form.Normalize(NormalizationForm.FormC) == "evler");
                    Assert.True(evler.HasStagedChanges);
                    Assert.True(evler.ShowUnread, "The held read-state write should leave evler Unread.");

                    Assert.False(WalkthroughReplay.CardIsQuiet(walkthrough),
                        "The card was quiet while approving evler had not yet marked it Read.");
                }
                finally
                {
                    gate.ReleaseWordReadState();
                }

                walkthrough.WaitUntil(() => WalkthroughReplay.CardIsQuiet(walkthrough), TimeSpan.FromSeconds(30),
                    "approving evler did not finish once its read-state write was released");
                Assert.False(AnalyzeTextsLayoutTests.RealizedLines(
                    walkthrough.Workspace.PageModel<TextsPageModel>().ResultsInText).SelectMany(line => line.Tokens)
                    .Single(token => token.Form.Normalize(NormalizationForm.FormC) == "evler").ShowUnread);
                return Task.CompletedTask;
            }
            finally
            {
                Application.Current.RequestedThemeVariant = previousTheme;
                CultureInfo.CurrentCulture = previousCulture;
            }
        }, WalkthroughSteps.Remaining(deadline));
    }
}
