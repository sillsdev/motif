using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using Xunit;
using static SIL.Motif.Tests.App.ProposalCompareViewModelTests;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProposalCompareLayoutTests
{
    private static Button[] ButtonsWithId(Control root, string id) => root.GetVisualDescendants().OfType<Button>()
        .Where(button => AutomationProperties.GetAutomationId(button) == id).ToArray();

    private static async Task ReachableAsync(Control control, Window window, int width)
    {
        control.BringIntoView();
        PageScreenshots.Settle(window);
        Assert.True(control.IsEffectivelyVisible);
        var bounds = LayoutAssertions.BoundsIn(control, window);
        Assert.True(bounds.Width > 0 && bounds.Height > 0);
        Assert.InRange(bounds.Left, -LayoutAssertions.Tolerance, width + LayoutAssertions.Tolerance);
        Assert.InRange(bounds.Right, -LayoutAssertions.Tolerance, width + LayoutAssertions.Tolerance);
        Assert.InRange(bounds.Top, -LayoutAssertions.Tolerance, window.ClientSize.Height + LayoutAssertions.Tolerance);
        Assert.InRange(bounds.Bottom, -LayoutAssertions.Tolerance, window.ClientSize.Height + LayoutAssertions.Tolerance);
        await Task.CompletedTask;
    }

    private static MotifTestWindow Host(Control panel, int width, int zoom) => new()
    {
        Width = width, Height = 600,
        Content = new ScrollViewer
        {
            Content = new LayoutTransformControl
            {
                Child = panel,
                LayoutTransform = new Avalonia.Media.ScaleTransform(zoom / 100.0, zoom / 100.0),
            },
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        },
    };

    [Theory]
    [InlineData(640, 150)]
    [InlineData(1280, 100)]
    public void BackBothApplyButtonsAndTheDifferingWordsStayReachableInRtlAtNarrowWidthAndZoom(int width, int zoom)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            await using var arranged = await ArrangeAsync(scenario => scenario
                .Add("one", [new(string.Concat(Enumerable.Repeat("كلمة", 40)), 0, 1)]));
            using var compare = arranged.Compare();
            var panel = new ProposalComparePanel(compare) { FlowDirection = Avalonia.Media.FlowDirection.RightToLeft };
            var window = Host(panel, width, zoom);
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var back = Assert.Single(ButtonsWithId(panel, AutomationIds.ProposalCompareBack));
                await ReachableAsync(back, window, width);
                var applies = ButtonsWithId(panel, AutomationIds.ProposalPrepareApply);
                Assert.Equal(2, applies.Length);
                foreach (var apply in applies) await ReachableAsync(apply, window, width);
                var differences = panel.FindControl<ProgressiveItemsControl>("ProposalCompareDifferences")!;
                differences.BringIntoView();
                PageScreenshots.Settle(window);
                Assert.True(differences.IsEffectivelyVisible);
                Assert.Equal(2, panel.GetVisualDescendants().OfType<ProposalCompareSide>().Count());
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void AnUnavailableComparisonShowsTheReasonAndRemeasureAndNoEffects()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            await using var arranged = await ArrangeAsync(scenario => scenario
                .Replace("two", summary => summary with { TextId = "text/two" }));
            using var compare = arranged.Compare();
            var panel = new ProposalComparePanel(compare);
            var window = Host(panel, 640, 100);
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var remeasure = Assert.Single(ButtonsWithId(panel, AutomationIds.ProposalCompareRemeasure));
                Assert.True(remeasure.IsEffectivelyVisible);
                await ReachableAsync(remeasure, window, 640);
                var texts = panel.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible)
                    .Select(text => text.Text).ToArray();
                Assert.Contains(MotifAppStrings.ProposalCompareReasonText, texts);
                Assert.DoesNotContain(texts, text => text is not null && text.StartsWith("Both Measured", StringComparison.Ordinal));
                Assert.DoesNotContain(MotifAppStrings.BoardWillFillIn, texts);
                Assert.False(panel.FindControl<ProgressiveItemsControl>("ProposalCompareDifferences")!.IsEffectivelyVisible);
                Assert.All(panel.GetVisualDescendants().OfType<ProposalCompareSide>().Select(side =>
                    side.FindControl<ProgressiveItemsControl>("ProposalCompareFillInWords")!), words =>
                    Assert.False(words.IsEffectivelyVisible));
                Assert.Equal(2, ButtonsWithId(panel, AutomationIds.ProposalPrepareApply).Length);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void EachSidesDiagnosticsSlotIsInTheTreeOnlyWhileDiagnosticsIsOn()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            await using var arranged = await ArrangeAsync();
            using var compare = arranged.Compare();
            arranged.Context.Diagnostics = true;
            var first = new TextBlock { Text = "First diagnostic" };
            var second = new TextBlock { Text = "Second diagnostic" };
            arranged.First.DiagnosticsContent = first;
            arranged.Second.DiagnosticsContent = second;
            var panel = new ProposalComparePanel(compare);
            var window = Host(panel, 1000, 100);
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                Assert.Contains(first, panel.GetVisualDescendants());
                Assert.Contains(second, panel.GetVisualDescendants());
                arranged.Context.Diagnostics = false;
                PageScreenshots.Settle(window);
                Assert.Null(arranged.First.DiagnosticsContent);
                Assert.DoesNotContain(first, panel.GetVisualDescendants());
                Assert.DoesNotContain(second, panel.GetVisualDescendants());
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void TheHostFillsTheSlotWithExactlyTheCheckedPairAndBackReturnsToTheOpenDocuments()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var scenario = new ProposalComparisonScenario()
                .Add("one", [new("fills", 0, 1)]).Add("two", [new("other", 0, 1)]).Add("three", [new("third", 0, 1)]);
            var fake = new FakeCommandClient();
            scenario.InstallOn(fake);
            fake.ProposalStandingsCompleteWith(new ProposalStandings(
                [Card("one"), Card("two"), Card("three")], 0));
            var context = WorkspaceContextTests.NewContext(fake);
            var page = ProposalCompareHost.Attach(new ProposalsPageModel(context));
            await context.OpenProjectAsync("project.fwdata");
            context.Baseline = Baselined();
            try
            {
                var cards = page.Groups.SelectMany(group => group.Cards).ToDictionary(card => card.ProposalId);
                Assert.True(page.HasComparisonHost);
                Assert.False(page.CompareCommand.CanExecute(null));
                cards["one"].IsCheckedForComparison = true;
                Assert.False(page.CompareCommand.CanExecute(null));
                cards["two"].IsCheckedForComparison = true;
                cards["three"].IsCheckedForComparison = true;
                Assert.False(cards["three"].IsCheckedForComparison);
                await Task.WhenAll(page.Documents.Items.Select(document => document.EnsureLoadedAsync()));
                page.Documents.Items.Single(document => document.ProposalId == "one").AddCommentCommand.Execute(null);
                var editor = Assert.Single(page.Documents.Items.Single(document => document.ProposalId == "one").Comments);
                editor.Document.Paragraphs[0].Runs[0].Text = "Typing kept";

                Assert.True(page.CompareCommand.CanExecute(null));
                page.CompareCommand.Execute(null);

                var panel = Assert.IsType<ProposalComparePanel>(page.ComparisonContent);
                var compare = Assert.IsType<ProposalCompareViewModel>(panel.DataContext);
                Assert.Equal(["one", "two"], new[] { compare.First.Document.ProposalId, compare.Second.Document.ProposalId });
                Assert.True(page.IsComparing);

                compare.BackCommand.Execute(null);

                Assert.Null(page.ComparisonContent);
                Assert.False(page.IsComparing);
                Assert.True(cards["one"].IsCheckedForComparison);
                Assert.True(cards["two"].IsCheckedForComparison);
                Assert.Same(editor, Assert.Single(page.Documents.Items.Single(document => document.ProposalId == "one").Comments));
                Assert.Equal("Typing kept", editor.Text());
                Assert.Equal(["one", "two"], page.Documents.ComparisonPair.Select(document => document.ProposalId).Order());

                page.CompareCommand.Execute(null);
                Assert.NotNull(page.ComparisonContent);
                cards["two"].IsCheckedForComparison = false;
                Assert.Null(page.ComparisonContent);
            }
            finally
            {
                foreach (var document in page.Documents.Items)
                    foreach (var comment in document.Comments.ToArray()) comment.CancelCommand.Execute(null);
                await page.StopWorkAsync();
                await context.StopProjectWorkAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    private static ProposalCard Card(string id) => new(id, id, "proposed", ProposalGroup.NotTried, false, [],
        TrialStanding.NotTried, null, []);
}
