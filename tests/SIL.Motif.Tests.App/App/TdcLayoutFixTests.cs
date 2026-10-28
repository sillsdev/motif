using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Avalonia.VisualTree;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Two layout defects the Windows review found: the Statistics Completion heading was cut off at 1040 px, and the
/// Parsimony staged-decision chip ran into the row's Keep, Defer and Ask buttons at 640x600 and 150% zoom.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class TdcLayoutFixTests
{
    [Theory]
    [InlineData(1040)]
    [InlineData(900)]
    [InlineData(800)]
    [InlineData(640)]
    public void TheStatisticsCompletionHeadingFits(int width)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(seedReaderToken: false);
            try
            {
                window.Width = width;
                window.Height = 1300;
                workspace.CurrentPage = WorkspacePage.Timing;
                LayoutInvariantEngineTests.Settle(window);
                var statistics = window.GetVisualDescendants().OfType<Expander>()
                    .First(expander => expander.Header as string == MotifAppStrings.TimingDetailedStatisticsHeading
                        && expander.IsEffectivelyVisible);
                statistics.IsExpanded = true;
                LayoutInvariantEngineTests.Settle(window);

                var heading = statistics.GetVisualDescendants().OfType<DataGridColumnHeader>()
                    .Single(header => header.IsEffectivelyVisible && header.Content as string == MotifAppStrings.StatisticsColumnCompletion);
                var text = heading.GetVisualDescendants().OfType<TextBlock>()
                    .Single(block => block.IsEffectivelyVisible && block.Text == MotifAppStrings.StatisticsColumnCompletion);
                var needed = text.TextLayout.WidthIncludingTrailingWhitespace;
                Assert.True(needed <= text.Bounds.Width + 0.5,
                    $"The Completion heading needs {needed:0.#} px but its column leaves {text.Bounds.Width:0.#} px.");
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StagedDecisionChipsNeverOverlapTheRowButtonsAt640By600And150Percent(bool darkTheme)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
            var previousTheme = Application.Current!.RequestedThemeVariant;
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false,
                configure: (fake, _) => ParsimonyStateScreenshots.Configure(fake, "dense-review"),
                advancedAiModeEnabled: true);
            var settings = SettingsTestAccess.Model(window);
            var previousZoom = settings.ZoomPercent;
            try
            {
                window.Width = 640;
                window.Height = 600;
                settings.ZoomPercent = 150;
                Application.Current!.RequestedThemeVariant =
                    darkTheme ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
                workspace.CurrentPage = WorkspacePage.Parsimony;
                PageScreenshots.Settle(window);
                await ParsimonyStateScreenshots.DriveDenseReviewAsync(workspace.PageModel<ParsimonyPageModel>());
                PageScreenshots.Settle(window);

                var chips = window.GetVisualDescendants().OfType<Control>().Where(control => control.IsEffectivelyVisible
                    && AutomationProperties.GetAutomationId(control) == AutomationIds.ParsimonyDecision).ToArray();
                Assert.True(chips.Length >= 3, $"Expected the three staged decisions, found {chips.Length}.");
                foreach (var chip in chips)
                {
                    var row = chip.GetVisualAncestors().OfType<Border>().First(border => border.Classes.Contains("denseTableRow"));
                    var chipBox = BoundsIn(chip, window);
                    var buttons = row.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible
                        && button.Bounds.Width > 0).ToArray();
                    Assert.NotEmpty(buttons);
                    foreach (var button in buttons)
                    {
                        var box = BoundsIn(button, window);
                        Assert.False(chipBox.Intersects(box),
                            $"The staged chip {chipBox} overlaps the '{button.Content}' button {box}.");
                    }
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = previousTheme;
                settings.ZoomPercent = previousZoom;
                window.Close();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Theory]
    [InlineData(640, 600)]
    [InlineData(1040, 780)]
    public void WhatChangedShowsAnEmptyStateInsteadOfABlankBodyWhenNothingCompares(int width, int height)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(seedReaderToken: false);
            try
            {
                window.Width = width;
                window.Height = height;
                workspace.PageModel<TextsPageModel>().Tab = TextsTab.WhatChanged;
                workspace.CurrentPage = WorkspacePage.Texts;
                PageScreenshots.Settle(window);

                var panel = window.GetVisualDescendants().OfType<DifferencePanel>().Single();
                Assert.False(panel.Difference.HasDifference, "The sample scene was expected to hold no moves.");
                var empty = window.GetVisualDescendants().OfType<TextBlock>()
                    .SingleOrDefault(text => text.Name == "DifferenceEmptyState" && text.IsEffectivelyVisible);
                Assert.NotNull(empty);
                Assert.Equal(MotifAppStrings.DifferenceEmptyState, empty.Text);
                var box = BoundsIn(empty, window);
                Assert.True(new Rect(default, window.ClientSize).Contains(box), $"The empty state {box} is outside the window.");
                Assert.True(empty.Bounds.Height >= empty.TextLayout.Height - 0.5, "The empty state text is clipped.");
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    // The control's box in window pixels; zoom scales sizes as well as positions.
    private static Rect BoundsIn(Control control, Visual window) =>
        new Rect(default, control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);
}

