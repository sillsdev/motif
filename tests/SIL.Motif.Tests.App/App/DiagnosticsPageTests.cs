using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;
using PresentationWordRow = SIL.Motif.App.Controls.WordPresentation.WordRow;
using WordCard = SIL.Motif.App.Controls.WordPresentation.WordCard;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class DiagnosticsPageTests
{
    [Theory]
    [InlineData(640)]
    [InlineData(1280)]
    public void GroupsOnlyRecordedCodesAndSelectsReadableEvidence(int width)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            var finding = new GrammarWarning(GrammarDiagnosticLevel.Warning, "Recorded", [], [], "Actual finding")
                { Code = "recorded-code" };
            fake.StoredGrammarCheckIs(new GrammarCheckResponse([finding, finding], HasBaseline: true));
            var context = WorkspaceContextTests.NewContext(fake);
            context.Diagnostics = true;
            var model = new DiagnosticsPageModel(context);
            await context.OpenProjectAsync("project.fwdata");
            var page = new DiagnosticsPage(model);
            var window = new MotifTestWindow { Width = width, Height = 600, Content = page };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var row = Assert.Single(model.Warnings.Rows);
                Assert.Equal(2, row.RepeatCount);
                Assert.Contains("recorded-code", row.DiagnosticGroupText);
                var list = Assert.Single(page.GetVisualDescendants().OfType<ListBox>());
                list.SelectedIndex = 0;
                PageScreenshots.Settle(window);
                Assert.Same(row, Assert.Single(model.Warnings.Grammar.Warnings.SelectedDetails));
                Assert.DoesNotContain(page.GetVisualDescendants().OfType<Button>(),
                    button => (button.Content as string ?? string.Empty).Contains("Claude", StringComparison.Ordinal));
                Assert.DoesNotContain(page.GetVisualDescendants().OfType<Button>(), button => button.Content as string == "Hide This Warning");
                var body = Assert.Single(page.GetVisualDescendants().OfType<Grid>(), grid => grid.Name == "WarningBody");
                Assert.Equal(width >= 1280 ? 2 : 1, body.ColumnDefinitions.Count);
                LayoutAssertions.AssertCurrent(window);
                Assert.Empty(fake.CheckGrammarRequests);
                Assert.Empty(fake.AssessRequests);
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void KeyboardSelectionShowsTheMatchingWarningEvidence()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.StoredGrammarCheckIs(new GrammarCheckResponse([
                new(GrammarDiagnosticLevel.Warning, "First", [], [], "First evidence") { Code = "first" },
                new(GrammarDiagnosticLevel.Warning, "Second", [], [], "Second evidence") { Code = "second" },
            ], HasBaseline: true));
            var context = WorkspaceContextTests.NewContext(fake);
            context.Diagnostics = true;
            var model = new DiagnosticsPageModel(context);
            await context.OpenProjectAsync("project.fwdata");
            var page = new DiagnosticsPage(model);
            var window = new MotifTestWindow { Width = 640, Height = 600, Content = page };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var list = Assert.Single(page.GetVisualDescendants().OfType<ListBox>());
                list.SelectedIndex = 0;
                Assert.True(Assert.IsType<ListBoxItem>(list.ContainerFromIndex(0)).Focus());
                window.KeyPress(Avalonia.Input.Key.Down,
                    Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.ArrowDown, null);
                window.KeyRelease(Avalonia.Input.Key.Down,
                    Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.ArrowDown, null);
                PageScreenshots.Settle(window);
                Assert.Equal(1, list.SelectedIndex);
                Assert.Same(model.Warnings.Rows[1], Assert.Single(model.Warnings.Grammar.Warnings.SelectedDetails));
                Assert.Contains(page.GetVisualDescendants().OfType<CopyableTextBlock>(),
                    text => text.IsEffectivelyVisible && text.Text == "Second evidence");
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void RenderedWarningWordActionsOpenTheirExactWordInTexts()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            var words = new[] { "exact-form", "member-form", "spelling-form" };
            var subject = new GrammarWarningPart("Named form", GrammarWarningPartRole.Object,
                "66666666-6666-6666-6666-666666666666", "MoForm")
            {
                Reach = new WarningReach(WarningWordsPath.Uses),
            };
            ObjectUseWord Word(string form) => new(new WordRow(form, WordRowOutcome.NoParse, "Lost", WordRowTone.Problem));
            var finding = new GrammarWarning(GrammarDiagnosticLevel.Warning, "Recorded", [subject], [], "Actual finding")
            {
                Code = "recorded-code",
                YourWords = new WarningWords(WarningWordsMatch.Identity, [Word(words[0])], [])
                {
                    MembershipCandidates = [Word(words[1])],
                    SpellingCandidates = [Word(words[2])],
                    Paths = [WarningWordsPath.Uses, WarningWordsPath.Membership, WarningWordsPath.Spelling],
                },
            };
            fake.StoredGrammarCheckIs(new GrammarCheckResponse([finding], HasBaseline: true));
            var context = WorkspaceContextTests.NewContext(fake);
            context.Diagnostics = true;
            var model = new DiagnosticsPageModel(context);
            var texts = new TextsPageModel(context);
            var assessment = WorkspaceContextTests.Assessment() with
            {
                Words = words.Select(word => new AssessmentWordResult(word, "no-analysis", false, "Complete", 3, null)).ToArray(),
            };
            await using var selection = await WorkspaceContextTests.OpenCapturedSelectionAsync(fake, context,
                new TextWordsResponse([], [], true), assessment, words);
            await texts.ResultsInText.SelectionRefresh;
            var page = new DiagnosticsPage(model);
            var window = new MotifTestWindow { Width = 1280, Height = 1000, Content = page };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                Assert.Single(page.GetVisualDescendants().OfType<ListBox>()).SelectedIndex = 0;
                PageScreenshots.Settle(window);
                var lists = new[] { "WarningsInspectorWordRows", "WarningsInspectorMembershipRows", "WarningsInspectorSpellingRows" };
                for (var index = 0; index < lists.Length; index++)
                {
                    context.OpenDiagnostics(DiagnosticsTab.Warnings);
                    var list = Assert.Single(page.GetVisualDescendants().OfType<ItemsControl>(), control => control.Name == lists[index]);
                    list.BringIntoView();
                    PageScreenshots.Settle(window);
                    var row = Assert.Single(list.GetVisualDescendants().OfType<PresentationWordRow>());
                    row.State = row.State! with { IsOpen = true };
                    PageScreenshots.Settle(window);
                    Assert.Single(row.GetVisualDescendants().OfType<WordCard>());
                    var open = Assert.Single(row.GetVisualDescendants().OfType<Button>(),
                        button => AutomationProperties.GetAutomationId(button) == AutomationIds.WordOpenInText);
                    Assert.True(open.IsEffectivelyVisible);
                    Assert.True(open.IsEnabled);
                    open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await texts.ResultsInText.SelectionRefresh;
                    PageScreenshots.Settle(window);
                    Assert.Equal(WorkspacePage.Texts, context.CurrentPage);
                    Assert.Equal(words[index], Assert.IsType<ResultsTokenViewModel>(texts.ResultsInText.SelectedToken).Form);
                }
                Assert.Empty(fake.CheckGrammarRequests);
                Assert.Empty(fake.AssessRequests);
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(30));
    }

    [UiThreadFact]
    public async Task AnUnavailableCheckIsDistinctFromAMeasuredEmptyCheck()
    {
        var fake = new FakeCommandClient();
        var context = WorkspaceContextTests.NewContext(fake);
        context.Diagnostics = true;
        var model = new DiagnosticsPageModel(context);
        await context.OpenProjectAsync("project.fwdata");
        Assert.Equal(MotifAppStrings.DiagnosticsUnavailable, model.Warnings.StatusText);
        fake.StoredGrammarCheckIs(new GrammarCheckResponse([], HasBaseline: true));
        await model.Warnings.ReadAsync("project.fwdata", CancellationToken.None);
        Assert.Equal(MotifAppStrings.DiagnosticsNoWarnings, model.Warnings.StatusText);
        await context.StopProjectWorkAsync();
    }
}
