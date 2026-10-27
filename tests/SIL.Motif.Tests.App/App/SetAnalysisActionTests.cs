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
    [InlineData("review", 1240)]
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
                Settle(window);
                Assert.Equal(host == "review" ? 1 : 0, fake.PendingPutRequests.Count);
                Assert.Empty(fake.ApplyPendingRequests);
                Assert.Empty(fake.ApplyProposalRequests);
                if (host == "review")
                    Assert.Equal("1 change not applied yet", workspace.PageModel<ReviewPageModel>().CountText);
                mark.Flyout!.Hide();
            }
            finally { window.Close(); await workspace.DisposeAsync(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ReviewDetailsOwnAnActionableCardForTheCapturedWordAndEvidence()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenAnalyzeTexts();
            try
            {
                var card = await SurfaceAsync(workspace, window, "review", "Sungura");
                Assert.NotNull(PartButton(card, "fix"));
                var fake = Assert.IsType<FakeCommandClient>(workspace.Context.Commands);
                Assert.Single(fake.PendingPutRequests);
                var menu = OpenByPointer(window, PartButton(card, "fix"));
                var approved = Assert.Single(menu.GetVisualDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Set analysis: Approved");
                Assert.True(Assert.IsType<WordDispositionTile>(approved.DataContext).IsCurrent);
                Assert.Null(approved.CommandParameter);
                var unknown = Assert.Single(menu.GetVisualDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Set analysis: Unknown");
                Assert.False(Assert.IsType<WordDispositionTile>(unknown.DataContext).IsCurrent);
                var captured = Assert.IsType<WordMarkingChoice>(unknown.CommandParameter);
                Assert.NotNull(captured.Target.ExpectedContext);
                Assert.NotNull(captured.Choice.StoredAnalysisId);
                Assert.True(unknown.Command!.CanExecute(unknown.CommandParameter));
                var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(unknown.Command);
                HeadlessClick.Click(TopLevel.GetTopLevel(unknown)!, unknown, "Set analysis: Unknown in Review");
                Assert.NotNull(command.ExecutionTask);
                await command.ExecutionTask!;
                Assert.Equal(2, fake.PendingPutRequests.Count);
                var put = fake.PendingPutRequests.Last();
                Assert.Equal(captured.Target.Form, put.Change.Word);
                Assert.Equal(captured.Target.WordformId,
                    SIL.Motif.Contract.Ids.CanonicalId.Parse(put.Change.WordformId).ToGuid());
                Assert.Equal(captured.Target.Occurrence, put.Change.Occurrence);
                Assert.Equal(captured.Choice.StoredAnalysisId, put.Change.StoredAnalysisId);
                Assert.Equal(captured.Target.ExpectedContext, put.ExpectedContext);
                Assert.Null(put.ProposalId);
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
                Settle(window);
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
                Settle(window);
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
        Settle(window);
        var strip = StripOf(Panel(window), form);
        if (host == "strip")
        {
            Assert.IsType<WordStripToken>(strip).FocusWord();
            Settle(window);
            return strip;
        }
        var token = Assert.IsType<ResultsTokenViewModel>(ResultsInTextPanel.TokenOf(strip));
        if (host == "card")
        {
            await reader.OpenTokenCardAsync(token);
            Settle(window);
            return OpenCard(window);
        }
        Assert.Equal("review", host);
        var assessment = Assert.IsType<SIL.Motif.Contract.Responses.AssessCommandResponse>(workspace.Assess.Result);
        var assessed = Assert.Single(assessment.Words, word => word.Word == form);
        Assert.NotNull(workspace.Assess.Words.Find(form));
        var captured = token.CaptureActionTarget(workspace.Context.Changes.AssessmentId);
        Assert.NotNull(captured.WordformId);
        Assert.NotNull(captured.Occurrence);
        Assert.NotNull(captured.ExpectedContext);
        Assert.Equal(assessed.Origin?.AssessmentId, captured.AssessmentId);
        Assert.True(strip.Focus());
        window.KeyPress(Key.D, RawInputModifiers.None, PhysicalKey.None, null);
        Assert.NotNull(reader.StageMarkingChoiceForTargetCommand.ExecutionTask);
        await reader.StageMarkingChoiceForTargetCommand.ExecutionTask!;
        var staged = Assert.Single(workspace.Context.Changes.Items);
        Assert.Equal(captured.WordformId,
            SIL.Motif.Contract.Ids.CanonicalId.Parse(staged.WordformId!).ToGuid());
        Assert.Equal(captured.Occurrence, staged.Occurrence);
        workspace.CurrentPage = WorkspacePage.Review;
        var review = workspace.PageModel<ReviewPageModel>();
        await review.LoadReviewAsync();
        var entry = Assert.Single(review.Entries, entry => entry.Change?.Word == form);
        Assert.Equal(staged.ChangeId, entry.Change!.ChangeId);
        review.SelectEntry(entry);
        Settle(window);
        var panel = Assert.Single(window.GetVisualDescendants().OfType<ReviewPanel>());
        var list = panel.FindControl<ListBox>("ReviewItems")!;
        Assert.NotNull(review.SelectedTableEntry);
        var row = list.ContainerFromItem(review.SelectedTableEntry)!;
        Assert.NotNull(row);
        var rowMenu = Assert.Single(row.GetVisualDescendants().OfType<Button>(),
            button => button.Classes.Contains("denseTableMenu"));
        HeadlessClick.Click(window, rowMenu, "Review row actions");
        var rowFlyout = Assert.IsType<Flyout>(rowMenu.Flyout);
        Assert.True(rowFlyout.IsOpen);
        var body = FlyoutContent.Of(rowFlyout);
        var details = Assert.Single(body.GetVisualDescendants().OfType<Button>(), button => button.Content is "Details");
        HeadlessClick.Click(TopLevel.GetTopLevel(details)!, details, "Details for the captured change");
        Settle(window);
        if (review.ShowContextCommand.ExecutionTask is { } contextRead) await contextRead;
        Settle(window);
        Assert.False(rowFlyout.IsOpen);
        Assert.Same(entry, review.Current);
        var card = Assert.Single(panel.InspectorContent.GetVisualDescendants().OfType<WordCard>());
        await CardReadFinishedAsync(window, card);
        Settle(window);
        Assert.NotNull(card.Document);
        Assert.Null(card.Failure);
        Assert.False(card.CanRetry);
        Assert.Equal(entry.Change.Presentation.Key, card.Document.Key);
        Assert.Contains(card.Document!.Sections, section => section is WordCardAnalysis { Token: not null });
        var analysis = Assert.Single(card.Document.Sections.OfType<WordCardAnalysis>());
        var reviewed = Assert.IsType<ResultsTokenViewModel>(analysis.Token);
        var reviewedTarget = reviewed.CaptureActionTarget(workspace.Context.Changes.AssessmentId);
        Assert.Equal(captured.Form, reviewedTarget.Form);
        Assert.Equal(captured.WordformId, reviewedTarget.WordformId);
        Assert.Equal(captured.Occurrence, reviewedTarget.Occurrence);
        Assert.Equal(captured.AssessmentId, reviewedTarget.AssessmentId);
        Assert.Equal(workspace.Context.SelectionReads.Reader!.Context.ExpectedWriteContext(),
            reviewedTarget.ExpectedContext);
        Assert.NotNull(reviewed.Actions);
        Assert.Contains(reviewed.Disposition.StoredRows.SelectMany(stored => stored.Tiles), tile =>
            !tile.IsCurrent && tile.Action?.IsAvailable == true &&
            reviewed.StageMarkingChoiceForTokenCommand?.CanExecute(tile.Action) == true);
        return card;
    }

    private static async Task CardReadFinishedAsync(Window window, WordCard card)
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Check()
        {
            if (card.Document is not null || card.CanRetry ||
                card.Failure is { } failure && failure != "Loading word details…")
                finished.TrySetResult();
        }
        void Changed(object? sender, AvaloniaPropertyChangedEventArgs change) => Check();
        void Closed(object? sender, EventArgs args) => finished.TrySetException(
            new InvalidOperationException("The window closed before Review's card read finished."));
        card.PropertyChanged += Changed;
        window.Closed += Closed;
        try
        {
            Check();
            await finished.Task;
        }
        finally
        {
            card.PropertyChanged -= Changed;
            window.Closed -= Closed;
        }
    }

    private static Button PartButton(Control surface, string part) =>
        Assert.Single(surface.GetVisualDescendants().OfType<Button>(), button => button.IsEffectivelyVisible &&
            (AutomationProperties.GetAutomationId(button) ?? string.Empty).EndsWith("-" + part, StringComparison.Ordinal));

    private static Control OpenByPointer(Window window, Button button)
    {
        button.BringIntoView();
        Settle(window);
        HeadlessClick.Click(window, button, AutomationProperties.GetName(button)!);
        var flyout = Assert.IsType<Flyout>(button.Flyout);
        Assert.True(flyout.IsOpen);
        Settle(window);
        return FlyoutContent.Of(flyout);
    }
}
