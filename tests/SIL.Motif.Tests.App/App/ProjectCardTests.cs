using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the top bar's project chip and the project card it opens: what the chip says in words, how the menus fold
/// behind the ≡ button, and that the card holds the story of the project and the commands that were word buttons.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProjectCardTests
{
    private static readonly string[] MenuNames = ["FileMenu", "EditMenu", "ViewMenu", "ToolsMenu", "HelpMenu"];

    [Fact]
    public void TheChipCarriesTheStatusInWordsAndTheWideChipShowsItBesideTheName()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false);
            try
            {
                window.Width = 1240;
                PageScreenshots.Settle(window);
                var chip = ProjectCardAccess.Chip(window);
                Assert.Equal(workspace.ProjectChipAccessibleName, AutomationProperties.GetName(chip));
                Assert.Contains(workspace.ProjectName, AutomationProperties.GetName(chip), StringComparison.Ordinal);
                Assert.Contains(workspace.FreshnessLabel, AutomationProperties.GetName(chip), StringComparison.Ordinal);
                Assert.Equal(workspace.ProjectChipAccessibleName, ToolTip.GetTip(chip));
                var detail = window.FindControl<TextBlock>("ProjectChipDetail")!;
                Assert.True(detail.IsEffectivelyVisible);
                Assert.StartsWith("· " + workspace.FreshnessLabel, detail.Text, StringComparison.Ordinal);
                Assert.True(window.FindControl<Border>("TopBarSeparator")!.IsEffectivelyVisible);
                Assert.Equal(MenuNames, window.FindControl<Menu>("MainMenu")!.Items.OfType<MenuItem>().Select(item => item.Name));

                window.Width = 700;
                PageScreenshots.Settle(window);
                Assert.False(detail.IsEffectivelyVisible, "Narrow, the chip keeps its dot and name alone.");
                Assert.Contains(workspace.FreshnessLabel, AutomationProperties.GetName(chip), StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData(1240, 100)]
    [InlineData(700, 100)]
    [InlineData(640, 150)]
    public void RefreshStaysBesideTheChipWhenTheSavedSelectionNeedsParsingAfterRefresh(int width, int zoom)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var app = Application.Current!;
            var previousZoom = (int)Math.Round(WindowZoomPolicy.Transform.ScaleX * 100);
            var captureMayFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            FakeCommandClient client = null!;
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: true,
                activateTextsPage: false, configure: (fake, _) =>
                {
                    client = fake;
                    fake.OnCaptureBaseline(async (request, _) =>
                    {
                        await captureMayFinish.Task;
                        return CommandOutcome<BaselineCaptureResponse>.Success(new BaselineCaptureResponse(
                            PageScreenshots.Token(), request.ProjectPath, PageScreenshots.BaselineSaveAt,
                            FieldWorksHeldProject: false, ReusedExistingBytes: true));
                    });
                });
            try
            {
                var setup = workspace.Context.Setup;
                Assert.NotNull(setup);
                workspace.CurrentPage = WorkspacePage.Overview;
                LayoutInvariantFixtures.Apply(window, new LayoutCell(width, 780, zoom));
                PageScreenshots.Settle(window);
                Assert.True(workspace.Baseline.HasBaseline);
                Assert.True(setup.CanRunDefaultSelection);
                Assert.False(workspace.Context.NeedsAssessment);
                Assert.True(workspace.FreshnessIsCurrent);
                Assert.False(workspace.FreshnessIsStale);
                Assert.Empty(client.CaptureBaselineRequests);
                var refresh = window.FindControl<Button>("RefreshIconButton")!;
                Assert.True(refresh.IsEffectivelyVisible && refresh.IsEffectivelyEnabled);
                Assert.Same(workspace.RefreshCommand, refresh.Command);
                HeadlessClick.Click(window, refresh, MotifAppStrings.RefreshAccessibleName);
                Assert.Single(client.CaptureBaselineRequests);
                Assert.True(workspace.FreshnessIsBusy);
                var running = workspace.RefreshCommand.ExecutionTask;
                Assert.NotNull(running);
                captureMayFinish.SetResult();
                await running;
                await workspace.Context.EvidencePublication;
                PageScreenshots.Settle(window);

                Assert.Equal(WorkspacePage.Overview, workspace.CurrentPage);
                Assert.True(workspace.Baseline.HasBaseline);
                Assert.True(setup.CanRunDefaultSelection);
                Assert.False(setup.IsOpen);
                Assert.True(workspace.Context.NeedsAssessment);
                Assert.True(workspace.FreshnessIsCurrent);
                Assert.False(workspace.FreshnessIsStale);
                Assert.False(workspace.FreshnessIsBusy);
                Assert.False(workspace.Assess.IsActive);
                Assert.True(refresh.IsEffectivelyVisible,
                    "The adjacent Refresh icon must remain visible while the saved Selection needs parsing.");
                Assert.True(refresh.IsEffectivelyEnabled);
                Assert.Same(workspace.RefreshCommand, refresh.Command);
                var chip = ProjectCardAccess.Chip(window);
                var chipBounds = new Rect(chip.Bounds.Size).TransformToAABB(chip.TransformToVisual(window)!.Value);
                var refreshBounds = new Rect(refresh.Bounds.Size).TransformToAABB(refresh.TransformToVisual(window)!.Value);
                Assert.True(refreshBounds.Width > 0 && refreshBounds.Height > 0);
                Assert.InRange(refreshBounds.Left - chipBounds.Right, -0.5, refreshBounds.Height);
                Assert.True(chipBounds.Top < refreshBounds.Bottom && refreshBounds.Top < chipBounds.Bottom);
                HeadlessClick.Click(window, refresh, MotifAppStrings.RefreshAccessibleName);
                await workspace.RefreshCommand.ExecutionTask!;
                Assert.Equal(2, client.CaptureBaselineRequests.Count);
            }
            finally
            {
                captureMayFinish.TrySetResult();
                if (workspace.RefreshCommand.ExecutionTask is { } running) await running;
                window.Close();
                await workspace.DisposeAsync();
                WindowZoomPolicy.Apply(app, previousZoom);
            }
        }, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void NarrowTheMenusSitBehindOneMenuButtonAndWideTheyAreTheBarsOwn()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false);
            try
            {
                var menu = window.FindControl<Menu>("MainMenu")!;
                window.Width = 700;
                PageScreenshots.Settle(window);
                var button = Assert.Single(menu.Items.OfType<MenuItem>());
                Assert.Equal(AutomationIds.MenuButton, AutomationProperties.GetAutomationId(button));
                Assert.Equal(MotifAppStrings.MainMenuButtonAccessibleName, AutomationProperties.GetName(button));
                Assert.Equal(MenuNames, button.Items.OfType<MenuItem>().Select(item => item.Name));
                Assert.Same(button, window.FindControl<MenuItem>("FileMenu")!.Parent);
                Assert.Equal("Undo", Plain(window.FindControl<MenuItem>("UndoMenuItem")!));
                Assert.Same(window.FindControl<MenuItem>("EditMenu"), window.FindControl<MenuItem>("UndoMenuItem")!.Parent);

                window.Width = 1240;
                PageScreenshots.Settle(window);
                Assert.Equal(MenuNames, menu.Items.OfType<MenuItem>().Select(item => item.Name));
                Assert.Same(menu, window.FindControl<MenuItem>("FileMenu")!.Parent);
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void NarrowAltEOpensTheMenuButtonThenEdit()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false);
            try
            {
                window.Width = 700;
                PageScreenshots.Settle(window);
                window.KeyPress(Key.E, RawInputModifiers.Alt, PhysicalKey.E, null);
                PageScreenshots.Settle(window);
                var button = window.FindControl<Menu>("MainMenu")!.Items.OfType<MenuItem>().Single();
                Assert.True(button.IsSubMenuOpen, "Alt+E did not open the menu button.");
                Assert.True(window.FindControl<MenuItem>("EditMenu")!.IsSubMenuOpen, "Alt+E did not open Edit.");
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void TheCardTellsTheProjectsStoryAndHoldsTheCommandsThatWereWordButtons()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false);
            try
            {
                window.Width = 1240;
                PageScreenshots.Settle(window);
                var card = ProjectCardAccess.Open(window);
                var texts = card.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible)
                    .Select(text => text.Text).ToArray();
                Assert.Contains(workspace.ProjectName, texts);
                Assert.Contains(workspace.ProjectCardSubtitle, texts);
                Assert.StartsWith("FieldWorks project · opened ", workspace.ProjectCardSubtitle, StringComparison.Ordinal);
                Assert.Contains(workspace.FreshnessLabel, texts);
                Assert.Contains(workspace.FreshnessDetail, texts);
                Assert.Contains(workspace.ProjectCardParsedText, texts);
                Assert.Contains(workspace.ProjectCardTextsSummary, texts);
                Assert.Contains("Texts to Parse", texts);

                var status = card.FindControl<Border>("CardStatusBlock")!;
                Assert.Equal(workspace.FreshnessIsCurrent, status.Classes.Contains("current"));
                Assert.Equal(workspace.FreshnessIsStale, status.Classes.Contains("stale"));

                var buttons = card.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).ToArray();
                Button Named(string name) => Assert.Single(buttons, button => button.Name == name);
                Assert.Same(workspace.RefreshCommand, Named("CardRefreshButton").Command);
                Assert.Same(workspace.ConfigureCommand, Named("CardChooseButton").Command);
                Assert.Same(workspace.ConfigureCommand, Named("CardSettingsButton").Command);
                Assert.Same(workspace.SelectNewProjectCommand, Named("CardOpenAnotherButton").Command);
                Assert.Equal("Choose…", Plain(Named("CardChooseButton")));
                Assert.Equal("Project Settings…", Plain(Named("CardSettingsButton")));
                Assert.Equal("Open Another Project…", Plain(Named("CardOpenAnotherButton")));
                Assert.All(buttons, button =>
                {
                    Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)), $"{button.Name} has no accessible name.");
                    Assert.Equal(1, ((string)button.Content!).Count(c => c == '_'));
                });
                Assert.Equal(buttons.Length, buttons.Select(button => ((string)button.Content!)[((string)button.Content!).IndexOf('_') + 1]
                    .ToString().ToUpperInvariant()).Distinct().Count());
                Assert.Equal(AutomationIds.ProjectCard, AutomationProperties.GetAutomationId(card.FindControl<Border>("ProjectCard")!));
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void EscapeClosesTheCardAndFocusReturnsToTheChip()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false);
            try
            {
                var chip = ProjectCardAccess.Chip(window);
                var card = ProjectCardAccess.Open(window);
                var flyout = Assert.IsType<Flyout>(chip.Flyout);
                Assert.True(flyout.IsOpen);
                Assert.NotNull(card);
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                PageScreenshots.Settle(window);
                Assert.False(flyout.IsOpen, "Esc left the project card open.");
                Assert.True(chip.IsFocused, "Focus did not return to the chip.");
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void ChoosingACommandInTheCardRunsItAndClosesTheCard()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false);
            try
            {
                var flyout = Assert.IsType<Flyout>(ProjectCardAccess.Chip(window).Flyout);
                var card = ProjectCardAccess.Open(window);
                var choose = card.FindControl<Button>("CardChooseButton")!;
                var command = Assert.IsAssignableFrom<CommunityToolkit.Mvvm.Input.IAsyncRelayCommand>(choose.Command);
                HeadlessClick.Click(window, choose, "Choose");
                Assert.NotNull(command.ExecutionTask);
                await command.ExecutionTask;
                PageScreenshots.Settle(window);
                Assert.True(workspace.Context.Setup!.IsOpen, "Choose… did not open the choice of texts.");
                Assert.False(flyout.IsOpen, "Choose… left the card over the dialog it opened.");
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(1));
    }

    private static string Plain(MenuItem item) => ((string)item.Header!).Replace("_", "", StringComparison.Ordinal);

    private static string Plain(Button button) => ((string)button.Content!).Replace("_", "", StringComparison.Ordinal);
}
