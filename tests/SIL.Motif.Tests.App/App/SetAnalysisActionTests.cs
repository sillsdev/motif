using SIL.Motif.App;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;
using static SIL.Motif.Tests.App.AnalyzeTextsLayoutTests;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class SetAnalysisActionTests
{
    [Theory]
    [InlineData("strip", 640)]
    [InlineData("card", 1240)]
    public void ARealWordMenuNamesSetAnalysisAndLeavesTheCurrentOpinionAlone(string host, int width)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenAnalyzeTexts(width);
            try
            {
                var fake = Assert.IsType<FakeCommandClient>(workspace.Context.Commands);
                var surface = await SurfaceAsync(workspace, window, host, "Sungura");
                var mark = PartButton(surface, "fix");
                var menu = OpenByPointer(window, mark);
                Assert.Equal("Set analysis", AutomationProperties.GetName(mark));
                Assert.StartsWith("Set analysis", Assert.IsType<string>(ToolTip.GetTip(mark)), StringComparison.Ordinal);
                var first = Assert.IsType<ResultsTokenViewModel>(menu.DataContext).Disposition.StoredRows.First();
                var choices = menu.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.DataContext is WordDispositionTile tile && first.Tiles.Contains(tile)).ToArray();
                Assert.Equal(new[] { "Set analysis: Approved", "Set analysis: Unknown",
                    "Set analysis: Disapproved", "Set analysis: Remove" }, choices.Select(AutomationProperties.GetName));
                Assert.All(choices, choice => Assert.Contains(AutomationProperties.GetName(choice)!,
                    VisibleText(choice), StringComparison.Ordinal));
                var current = Assert.Single(choices, button => ((WordDispositionTile)button.DataContext!).IsCurrent);
                Assert.NotNull(current.Command);
                Assert.False(current.Command!.CanExecute(current.CommandParameter));
                var popup = TopLevel.GetTopLevel(current)!;
                var point = current.TranslatePoint(new Point(current.Bounds.Width / 2, current.Bounds.Height / 2), popup)
                    ?? throw new InvalidOperationException("The current Opinion is not attached to its popup.");
                popup.MouseDown(point, MouseButton.Left);
                popup.MouseUp(point, MouseButton.Left);
                PageScreenshots.Settle(window);
                Assert.Empty(fake.PendingPutRequests);
                Assert.Empty(fake.ApplyPendingRequests);
                Assert.Empty(fake.ApplyProposalRequests);
                mark.Flyout!.Hide();
            }
            finally { window.Close(); await workspace.DisposeAsync(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void WordCardStagesTheCapturedOccurrenceAndEvidence()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenAnalyzeTexts();
            try
            {
                var card = await SurfaceAsync(workspace, window, "card", "Sungura");
                var fake = Assert.IsType<FakeCommandClient>(workspace.Context.Commands);
                var menu = OpenByPointer(window, PartButton(card, "fix"));
                var choice = Assert.Single(menu.GetVisualDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Set analysis: Unknown");
                var captured = Assert.IsType<WordMarkingChoice>(choice.CommandParameter);
                Assert.NotNull(captured.Target.ExpectedContext);
                Assert.NotNull(captured.Choice.StoredAnalysisId);
                var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(choice.Command);
                HeadlessClick.Click(TopLevel.GetTopLevel(choice)!, choice, "Set analysis: Unknown");
                Assert.NotNull(command.ExecutionTask);
                await command.ExecutionTask!;
                var put = Assert.Single(fake.PendingPutRequests);
                Assert.Equal(captured.Target.Form, put.Change.Word);
                Assert.Equal(captured.Target.WordformId,
                    SIL.Motif.Contract.Ids.CanonicalId.Parse(put.Change.WordformId).ToGuid());
                Assert.Equal(captured.Target.Occurrence, put.Change.Occurrence);
                Assert.Equal(captured.Choice.StoredAnalysisId, put.Change.StoredAnalysisId);
                Assert.Equal(captured.Target.ExpectedContext, put.ExpectedContext);
                Assert.Empty(fake.ApplyPendingRequests);
                Assert.Empty(fake.ApplyProposalRequests);
            }
            finally { window.Close(); await workspace.DisposeAsync(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemoveFromTheRealSetMenuTargetsOneAnalysisAndTheWindowUndoRestoresIt(bool card)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenAnalyzeTexts();
            try
            {
                var fake = Assert.IsType<FakeCommandClient>(workspace.Context.Commands);
                var surface = await SurfaceAsync(workspace, window, card ? "card" : "strip", "Sungura");
                var mark = PartButton(surface, "fix");
                var wireId = AutomationProperties.GetAutomationId(mark);
                var menu = OpenByPointer(window, mark);
                var remove = Assert.Single(menu.GetVisualDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Set analysis: Remove");
                var captured = Assert.IsType<WordMarkingChoice>(remove.CommandParameter);
                Assert.NotNull(captured.Target.WordformId);
                Assert.NotNull(captured.Choice.StoredAnalysisId);
                var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(remove.Command);
                HeadlessClick.Click(TopLevel.GetTopLevel(remove)!, remove, "Set analysis: Remove");
                Assert.NotNull(command.ExecutionTask);
                await command.ExecutionTask!;
                PageScreenshots.Settle(window);
                var put = Assert.Single(fake.PendingPutRequests);
                Assert.Equal(ChangeKinds.RemoveAnalysis, put.Change.Kind);
                Assert.Equal(captured.Choice.StoredAnalysisId, put.Change.StoredAnalysisId);
                Assert.Equal(captured.Target.Form, put.Change.Word);
                Assert.Equal(captured.Target.ExpectedContext, put.ExpectedContext);
                Assert.Equal(captured.Target.WordformId,
                    SIL.Motif.Contract.Ids.CanonicalId.Parse(put.Change.WordformId).ToGuid());
                Assert.Null(put.ProposalId);
                Assert.Single(workspace.Context.Changes.Items);
                mark.Flyout!.Hide();
                var undone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                System.Collections.Specialized.NotifyCollectionChangedEventHandler changed = (_, _) =>
                {
                    if (workspace.Context.Changes.Items.Count == 0) undone.TrySetResult();
                };
                EventHandler closed = (_, _) => undone.TrySetException(
                    new InvalidOperationException("The window closed before Undo completed."));
                workspace.Context.Changes.Items.CollectionChanged += changed;
                window.Closed += closed;
                try
                {
                    Assert.True(StripOf(Panel(window), "Sungura").Focus());
                    window.KeyPress(Key.Z, OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control,
                        PhysicalKey.None, null);
                    await undone.Task;
                }
                finally
                {
                    workspace.Context.Changes.Items.CollectionChanged -= changed;
                    window.Closed -= closed;
                }
                PageScreenshots.Settle(window);
                Assert.Empty(workspace.Context.Changes.Items);
                Assert.EndsWith("-fix", wireId, StringComparison.Ordinal);
                Assert.Empty(fake.ApplyPendingRequests);
            }
            finally { window.Close(); await workspace.DisposeAsync(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Theory]
    [InlineData("strip")]
    [InlineData("card")]
    public void TheCompactAddCountOpensAddAnalysisFromPanGloss(string host)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenAnalyzeTexts();
            try
            {
                var surface = await SurfaceAsync(workspace, window, host, "chakula");
                var add = PartButton(surface, "add");
                Assert.StartsWith("+", VisibleText(add), StringComparison.Ordinal);
                var menu = OpenByPointer(window, add);
                Assert.Equal("Add analysis from PanGloss", AutomationProperties.GetName(add));
                Assert.Contains("Add analysis from PanGloss", VisibleText(menu), StringComparison.Ordinal);
                Assert.Contains(menu.GetVisualDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Add as Approved" && button.Command!.CanExecute(button.CommandParameter));
                Assert.Empty(Assert.IsType<FakeCommandClient>(workspace.Context.Commands).PendingPutRequests);
                add.Flyout!.Hide();
            }
            finally { window.Close(); await workspace.DisposeAsync(); }
        }, TimeSpan.FromMinutes(3));
    }

    private static async Task<Control> SurfaceAsync(
        WorkspaceShellViewModel workspace, Window window, string host, string form)
    {
        var reader = workspace.PageModel<TextsPageModel>().ResultsInText;
        reader.CloseTokenCard();
        PageScreenshots.Settle(window);
        var strip = StripOf(Panel(window), form);
        if (host == "strip")
        {
            Assert.IsType<WordStripToken>(strip).FocusWord();
            PageScreenshots.Settle(window);
            return strip;
        }
        var token = Assert.IsType<ResultsTokenViewModel>(ResultsInTextPanel.TokenOf(strip));
        if (host == "card")
        {
            await reader.OpenTokenCardAsync(token);
            PageScreenshots.Settle(window);
            return OpenCard(window);
        }
        throw new ArgumentOutOfRangeException(nameof(host), host, null);
    }

    private static Button PartButton(Control surface, string part) =>
        Assert.Single(surface.GetVisualDescendants().OfType<Button>(), button => button.IsEffectivelyVisible &&
            (AutomationProperties.GetAutomationId(button) ?? string.Empty).EndsWith("-" + part, StringComparison.Ordinal));

    private static Control OpenByPointer(Window window, Button button)
    {
        button.BringIntoView();
        PageScreenshots.Settle(window);
        HeadlessClick.Click(window, button, AutomationProperties.GetName(button)!);
        var flyout = Assert.IsType<Flyout>(button.Flyout);
        Assert.True(flyout.IsOpen);
        PageScreenshots.Settle(window);
        return FlyoutContent.Of(flyout);
    }
}
