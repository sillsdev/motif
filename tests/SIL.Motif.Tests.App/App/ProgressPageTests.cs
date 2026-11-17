using SIL.Motif.App;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.SelectionReading;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProgressPageTests
{
    [Theory]
    [InlineData(640, 400, 1.5, true)]
    [InlineData(1280, 800, 1.0, false)]
    public void PlainNextRowsWrapAndActivateByKeyboardInBothThemes(int width, int height, double scale, bool rtl)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.ProposalStandingsCompleteWith(new ProposalStandings([
                new ProposalCard("proposal/1", "First", "proposed", ProposalGroup.WaitingForYou, false, [],
                    TrialStanding.Current, null, [])], 0));
            var context = WorkspaceContextTests.NewContext(fake);
            var model = new ProgressPageModel(context);
            await context.OpenProjectAsync("project.fwdata");
            var page = new ProgressPage(model);
            var window = new MotifTestWindow
            {
                Width = width, Height = height,
                FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                RequestedThemeVariant = rtl ? ThemeVariant.Dark : ThemeVariant.Light,
                Content = new LayoutTransformControl { LayoutTransform = new ScaleTransform(scale, scale), Child = page },
            };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var buttons = page.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.Classes.Contains("progressNextRow")).ToArray();
                Assert.Single(buttons);
                var identities = buttons.Select(AutomationProperties.GetAutomationId).ToArray();
                foreach (var identity in identities)
                {
                    context.OpenPage(WorkspacePage.Progress);
                    PageScreenshots.Settle(window);
                    var button = Assert.Single(page.GetVisualDescendants().OfType<Button>(), item =>
                        AutomationProperties.GetAutomationId(item) == identity);
                    button.BringIntoView();
                    button.Focus();
                    PageScreenshots.Settle(window);
                    Assert.True(button.IsFocused);
                    var bounds = new Rect(button.Bounds.Size).TransformToAABB(button.TransformToVisual(window)!.Value);
                    Assert.InRange(bounds.Left, -1, window.ClientSize.Width);
                    Assert.InRange(bounds.Right, 0, window.ClientSize.Width + 1);
                    Assert.InRange(bounds.Top, -1, window.ClientSize.Height);
                    Assert.InRange(bounds.Bottom, 0, window.ClientSize.Height + 1);
                    Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)));
                    Assert.All(button.GetVisualDescendants().OfType<TextBlock>(),
                        text => Assert.Equal(TextWrapping.Wrap, text.TextWrapping));
                    window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                    Assert.Equal(WorkspacePage.Proposals, context.CurrentPage);
                }
                Assert.Empty(page.GetVisualDescendants().OfType<HealthStrip>());
                Assert.Empty(fake.AssessRequests);
            }
            finally
            {
                window.Close();
                await context.StopProjectWorkAsync();
            }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ProgressSaysNothingIsParsedBeforeAParseAndShowsTheStoredCountsAfter()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            var unparsed = SampleOverview.Populated() with { AssessmentId = null, AssessedUtc = null };
            fake.OverviewCompletesWith(unparsed);
            var context = WorkspaceContextTests.NewContext(fake);
            var model = new ProgressPageModel(context);
            await context.OpenProjectAsync("project.fwdata");
            var page = new ProgressPage(model);
            var window = new MotifTestWindow { Width = 1280, Height = 800, Content = page };
            string Seen() => string.Join("\n", page.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.IsEffectivelyVisible).Select(text => text.Text));
            Button Visible(string id) => Assert.Single(page.GetVisualDescendants().OfType<Button>(),
                button => button.IsEffectivelyVisible && AutomationProperties.GetAutomationId(button) == id);
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                Assert.Contains("No Texts Parsed Yet", Seen());
                Assert.Contains("Parse All Words", Seen());
                Assert.DoesNotContain("Parsed So Far", Seen());
                Visible(AutomationIds.ProgressChooseTexts).Command!.Execute(null);
                Assert.Equal(WorkspacePage.Texts, context.CurrentPage);

                var parsed = SampleOverview.Populated();
                fake.OverviewCompletesWith(parsed);
                await model.TryAgainCommand.ExecuteAsync(null);
                PageScreenshots.Settle(window);
                var seen = Seen();
                Assert.DoesNotContain("No Texts Parsed Yet", seen);
                Assert.Contains("Parsed So Far", seen);
                var coverage = parsed.TextCoverage;
                Assert.Contains($"{coverage.ParsedOccurrences:N0} of {coverage.TotalOccurrences:N0} places", seen);
                Assert.Contains($"{coverage.ParsedWords:N0} parsed", seen);
                Assert.Contains("not your approval", seen);
                Visible(AutomationIds.ProgressReviewWords).Command!.Execute(null);
                Assert.Equal(WorkspacePage.Words, context.CurrentPage);
            }
            finally
            {
                window.Close();
                await context.StopProjectWorkAsync();
            }
        }, TimeSpan.FromSeconds(30));
    }

    // fix1-progress: a parse finished here shows its card in place, never blank.
    [Fact]
    public void ProgressShowsAReadingStateThenTheParsedCardAfterAParseWithoutLeavingThePage()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.OverviewCompletesWith(SampleOverview.Populated() with { AssessmentId = null, AssessedUtc = null });
            var readerGate = new TaskCompletionSource();
            fake.SelectionReaderHandler = async (_, _) =>
            {
                await readerGate.Task;
                return CommandOutcome<SelectionReader>.Refused(new Refusal("selection-reader.no-baseline",
                    FailureReason.Refused, "Not available in this test."));
            };
            var context = WorkspaceContextTests.NewContext(fake);
            var model = new ProgressPageModel(context);
            await context.OpenProjectAsync("project.fwdata");
            var page = new ProgressPage(model);
            var window = new MotifTestWindow { Width = 1280, Height = 800, Content = page };
            string Seen() => string.Join("\n", page.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.IsEffectivelyVisible).Select(text => text.Text));
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                Assert.Contains("No Texts Parsed Yet", Seen());

                // The stored Overview is slow to read after the parse, as on a real project.
                var overviewGate = new TaskCompletionSource();
                var parsed = SampleOverview.Populated();
                fake.OnOverview(async (_, _) =>
                {
                    await overviewGate.Task;
                    return CommandOutcome<OverviewResponse>.Success(parsed);
                });
                context.PublishEvidence(new WorkspaceEvidence(WorkspaceContextTests.Assessment(), DateTimeOffset.UtcNow, false));
                PageScreenshots.Settle(window);
                var meanwhile = Seen();
                Assert.Contains(MotifAppStrings.ProgressReadingParse, meanwhile);
                Assert.DoesNotContain("No Texts Parsed Yet", meanwhile);
                Assert.DoesNotContain("Parsed So Far", meanwhile);

                // The page is not waiting for the Selection reader, which is still blocked here.
                overviewGate.SetResult();
                for (var i = 0; i < 200 && !Seen().Contains("Parsed So Far"); i++)
                {
                    await Task.Delay(25);
                    PageScreenshots.Settle(window);
                }
                Assert.Contains("Parsed So Far", Seen());
                Assert.DoesNotContain(MotifAppStrings.ProgressReadingParse, Seen());
            }
            finally
            {
                readerGate.TrySetResult();
                window.Close();
                await context.StopProjectWorkAsync();
            }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ProgressStaysSilentAboutParsingWhenTheStoredOverviewCannotBeRead()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var context = WorkspaceContextTests.NewContext(new FakeCommandClient());
            var model = new ProgressPageModel(context);
            await context.OpenProjectAsync("project.fwdata");
            var window = new MotifTestWindow { Width = 1280, Height = 800, Content = new ProgressPage(model) };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var seen = string.Join("\n", window.GetVisualDescendants().OfType<TextBlock>()
                    .Where(text => text.IsEffectivelyVisible).Select(text => text.Text));
                Assert.Contains("Parse results could not be read", seen);
                Assert.DoesNotContain("No Texts Parsed Yet", seen);
                Assert.DoesNotContain("Parsed So Far", seen);
            }
            finally
            {
                window.Close();
                await context.StopProjectWorkAsync();
            }
        }, TimeSpan.FromSeconds(30));
    }
}
