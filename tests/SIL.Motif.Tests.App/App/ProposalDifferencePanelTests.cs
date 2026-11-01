using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProposalDifferencePanelTests
{
    private static ProposalCard Card() =>
        new("p/one", "Open", "proposed", ProposalGroup.WaitingForYou, false, [new("rule", 1, "1 rule")],
            TrialStanding.Current, null, []);

    // Opens the Proposals page over a fake whose saved Difference is answered by the fixture, or by no Difference.
    private static async Task<(ProposalsPageModel Model, ProposalDifferencePanel Panel, MotifTestWindow Window)> OpenAsync(
        ProposalDifferenceFixture? fixture, double width = 1600)
    {
        var fake = new FakeCommandClient();
        fake.ProposalStandingsCompleteWith(new ProposalStandings([Card()], 0));
        if (fixture is not null) fake.OnProposalDifference(fixture.Read);
        var context = WorkspaceContextTests.NewContext(fake);
        var model = new ProposalsPageModel(context);
        await context.OpenProjectAsync(@"C:\projects\one.fwdata");
        await model.DifferenceReading;
        var page = new ProposalsPage(model);
        var host = WorkspaceLayoutHost.ForPage(page);
        var window = new MotifTestWindow { Width = width, Height = 1000, Content = host };
        window.Show();
        PageScreenshots.Settle(window);
        return (model, Assert.Single(host.GetVisualDescendants().OfType<ProposalDifferencePanel>()), window);
    }

    private static IEnumerable<FilterChip> Chips(Control root) => root.GetVisualDescendants().OfType<FilterChip>();

    private static void Click(Control control, MotifTestWindow window)
    {
        var button = Assert.IsAssignableFrom<Button>(control);
        Assert.True(button.IsEffectivelyEnabled, $"{button.Content} is disabled");
        Assert.True(button.Command!.CanExecute(button.CommandParameter));
        button.Command.Execute(button.CommandParameter);
        PageScreenshots.Settle(window);
    }

    [Fact]
    public void TheSectionStaysOpenToOfferMakingOneWhileTheProposalHasNoSavedDifference()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (model, panel, window) = await OpenAsync(fixture: null);
            try
            {
                Assert.Equal(ProposalDifferenceState.Absent, model.Difference.State);
                Assert.True(panel.IsEffectivelyVisible);
                Assert.False(panel.FindControl<Control>("DifferenceHeadline")!.IsEffectivelyVisible);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void TheSavedDifferenceShowsItsCountsChipsAndFirstPageOfWords()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (_, panel, window) = await OpenAsync(new ProposalDifferenceFixture());
            try
            {
                Assert.True(panel.IsEffectivelyVisible);
                Assert.Equal("15 words to check, 4 words improved",
                    panel.FindControl<CopyableTextBlock>("DifferenceHeadline")!.Text);
                Assert.Equal("Compared 30 words (60 occurrences) in Genesis 1.",
                    panel.FindControl<CopyableTextBlock>("DifferenceCompared")!.Text);
                Assert.Contains(Chips(panel), chip => chip.Label == "Lost an Approved Analysis" && chip.Count == 10);
                Assert.Contains(Chips(panel), chip => chip.Label == "Now One Approved Match" && chip.Count == 4);
                Assert.Contains(Chips(panel), chip => chip.Label == "Stopped at the Work Limit" && chip.Count == 2);
                Assert.Equal(ProposalDifferenceViewModel.PageSize,
                    panel.FindControl<ItemsControl>("DifferenceWordChips")!.GetVisualDescendants().OfType<FilterChip>().Count());
                Assert.True(panel.FindControl<Button>("DifferenceShowMoreWords")!.IsEffectivelyVisible);
                Assert.False(panel.FindControl<Button>("DifferenceClearFilters")!.IsEffectivelyVisible);
                Assert.False(panel.FindControl<Control>("DifferenceDetailRemoved")!.IsEffectivelyVisible);
                Assert.True(panel.FindControl<ItemsControl>("DifferenceNotes")!.IsEffectivelyVisible);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ChoosingChipsFiltersAndOpensTheWordsPlacesAndClearingRestoresThePage()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fixture = new ProposalDifferenceFixture();
            var (_, panel, window) = await OpenAsync(fixture);
            try
            {
                Click(Chips(panel).First(chip => chip.Label == "Lost an Approved Analysis"), window);

                Assert.Equal("delta.regression", fixture.Requests[^1].Bucket);
                Assert.True(Chips(panel).First(chip => chip.Label == "Lost an Approved Analysis").IsActive);
                Assert.Equal("Showing 10 of 10.", panel.FindControl<CopyableTextBlock>("DifferenceWordsStatus")!.Text);
                Assert.False(panel.FindControl<Button>("DifferenceShowMoreWords")!.IsEffectivelyVisible);
                Assert.True(panel.FindControl<Button>("DifferenceClearFilters")!.IsEffectivelyVisible);

                Click(panel.FindControl<ItemsControl>("DifferenceWordChips")!.GetVisualDescendants().OfType<FilterChip>()
                    .First(chip => chip.Label == "word04"), window);

                Assert.True(panel.FindControl<Control>("DifferenceSelectedWord")!.IsEffectivelyVisible);
                Assert.Equal("word04", fixture.Requests[^1].Word);
                Assert.Equal(["Line 1, word 1", "Line 2, word 2", "Line 3, word 3"],
                    panel.FindControl<ItemsControl>("DifferencePlaces")!.GetVisualDescendants().OfType<CopyableTextBlock>()
                        .Select(text => text.Text));

                Click(panel.FindControl<Button>("DifferenceClearFilters")!, window);

                Assert.Null(fixture.Requests[^1].Bucket);
                Assert.False(panel.FindControl<Control>("DifferenceSelectedWord")!.IsEffectivelyVisible);
                Assert.Equal("Showing 25 of 30.", panel.FindControl<CopyableTextBlock>("DifferenceWordsStatus")!.Text);

                Click(panel.FindControl<Button>("DifferenceShowMoreWords")!, window);
                Assert.Equal(30, panel.FindControl<ItemsControl>("DifferenceWordChips")!.GetVisualDescendants()
                    .OfType<FilterChip>().Count());
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ADecidedProposalReadsWellWithOnlyItsSummaryLeft()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (_, panel, window) = await OpenAsync(new ProposalDifferenceFixture(wordDetailKept: false));
            try
            {
                Assert.True(panel.IsEffectivelyVisible);
                Assert.Equal("15 words to check, 4 words improved",
                    panel.FindControl<CopyableTextBlock>("DifferenceHeadline")!.Text);
                Assert.True(panel.FindControl<Control>("DifferenceDetailRemoved")!.IsEffectivelyVisible);
                Assert.False(panel.FindControl<ItemsControl>("DifferenceWordChips")!.IsEffectivelyVisible);
                Assert.False(panel.FindControl<Button>("DifferenceShowMoreWords")!.IsEffectivelyVisible);
                var summaryChips = Chips(panel).Where(chip => chip.IsEffectivelyVisible).ToArray();
                Assert.NotEmpty(summaryChips);
                Assert.All(summaryChips, chip => Assert.False(chip.IsEnabled));
                Assert.Contains(summaryChips, chip => chip.Label == "Lost an Approved Analysis" && chip.Count == 10);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Theory]
    [InlineData(1600)]
    [InlineData(1280)]
    public void EveryChipStaysInsideThePanelAtTheInspectorWidth(double width)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (_, panel, window) = await OpenAsync(new ProposalDifferenceFixture(), width);
            try
            {
                var panelBounds = new Rect(panel.TranslatePoint(default, window) ?? default, panel.Bounds.Size);
                var visible = Chips(panel).Where(chip => chip.IsEffectivelyVisible).ToArray();
                Assert.NotEmpty(visible);
                foreach (var chip in visible)
                {
                    var origin = chip.TranslatePoint(default, window) ?? default;
                    Assert.True(origin.X >= panelBounds.X - 0.5 && origin.X + chip.Bounds.Width <= panelBounds.Right + 0.5,
                        $"{chip.Label} runs outside the Difference section ({origin.X}+{chip.Bounds.Width} in {panelBounds})");
                }
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void EverythingTheSectionShowsUsesWindowWords()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fixture = new ProposalDifferenceFixture();
            var (_, panel, window) = await OpenAsync(fixture);
            try
            {
                Click(Chips(panel).First(chip => chip.Label == "Lost an Approved Analysis"), window);
                Click(panel.FindControl<ItemsControl>("DifferenceWordChips")!.GetVisualDescendants().OfType<FilterChip>()
                    .First(), window);
                var shown = WindowWordsTests.Rendered(panel).ToArray();
                Assert.Contains("Difference", shown);
                Assert.DoesNotContain(shown, text => text.Contains("delta.", StringComparison.Ordinal));
                Assert.DoesNotContain(shown, text => text.Contains(" ms", StringComparison.Ordinal));
                WindowWordsTests.AssertWindowWords(shown);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }
    [Fact]
    public void AProposalDecidedFromOutsideWhileTheSectionIsOpenTurnsIntoTheSummaryWithTheRemovalNote()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fixture = new ProposalDifferenceFixture();
            var (_, panel, window) = await OpenAsync(fixture);
            try
            {
                Assert.True(panel.FindControl<ItemsControl>("DifferenceWordChips")!.IsEffectivelyVisible);
                fixture.WordDetailKept = false;

                Click(Chips(panel).First(chip => chip.Label == "Lost an Approved Analysis"), window);

                Assert.True(panel.FindControl<Control>("DifferenceDetailRemoved")!.IsEffectivelyVisible);
                Assert.False(panel.FindControl<ItemsControl>("DifferenceWordChips")!.IsEffectivelyVisible);
                Assert.DoesNotContain(panel.GetVisualDescendants().OfType<CopyableTextBlock>(),
                    text => text.IsEffectivelyVisible && text.Text == MotifAppStrings.ProposalDifferenceNoMatching);
                Assert.True(panel.FindControl<ItemsControl>("DifferenceNotes")!.IsEffectivelyVisible);
                var summaryChips = Chips(panel).Where(chip => chip.IsEffectivelyVisible).ToArray();
                Assert.NotEmpty(summaryChips);
                Assert.All(summaryChips, chip => Assert.False(chip.IsEnabled));
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ChipsNameWhatTheyShowAndTellAssistiveToolsWhetherTheyAreOn()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (_, panel, window) = await OpenAsync(new ProposalDifferenceFixture());
            try
            {
                var chip = Chips(panel).First(item => item.Label == "Lost an Approved Analysis");
                var toggle = Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(chip));
                Assert.Equal("Show Lost an Approved Analysis", AutomationProperties.GetName(chip));
                Assert.Equal(ToggleState.Off, toggle.ToggleState);

                Click(chip, window);

                Assert.Equal(ToggleState.On, toggle.ToggleState);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    // Low resolution, as the responsive suite uses it: 640 wide, 600 high.
    [Fact]
    public void AtLowResolutionALongWordWrapsInsideTheSectionAndItsFullLabelIsTheTooltip()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var longWord = new string('x', 120);
            var fixture = new ProposalDifferenceFixture(longestWord: longWord);
            var model = new ProposalDifferenceViewModel(fixture.Read, _ => { });
            await model.LoadAsync(@"C:\projects\one.fwdata", "proposal/one");
            var panel = new ProposalDifferencePanel { DataContext = model };
            var window = new MotifTestWindow
            {
                Width = 640, Height = 600, Content = new ScrollViewer { Content = panel },
            };
            window.Show();
            PageScreenshots.Settle(window);
            try
            {
                var panelBounds = new Rect(panel.TranslatePoint(default, window) ?? default, panel.Bounds.Size);
                foreach (var chip in Chips(panel).Where(item => item.IsEffectivelyVisible))
                {
                    var origin = chip.TranslatePoint(default, window) ?? default;
                    Assert.True(origin.X >= panelBounds.X - 0.5 && origin.X + chip.Bounds.Width <= panelBounds.Right + 0.5,
                        $"{chip.Label} runs outside the Difference section at 640 wide");
                }
                var longChip = Chips(panel).Single(item => item.Label == longWord);
                Assert.Equal(longWord, ToolTip.GetTip(longChip));
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }
}
