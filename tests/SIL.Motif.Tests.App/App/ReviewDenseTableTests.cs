using SIL.Motif.App;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.Tests.App.Walkthrough;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ReviewDenseTableTests
{
    [Fact]
    public void SharedTableKeepsGrammarAndWordRowsAlignedWithVisibleActionsAndStackedMorphology()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = MixedChanges();
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                window.Width = 1920;
                window.Height = 1080;
                workspace.CurrentPage = WorkspacePage.Review;
                await workspace.PageModel<ReviewPageModel>().LoadReviewAsync();
                PageScreenshots.Settle(window);
                var panel = Assert.Single(window.GetVisualDescendants().OfType<ReviewPanel>());
                var list = panel.FindControl<ListBox>("ReviewItems")!;

                Assert.Contains("denseTable", list.Classes);
                var entries = list.ItemsSource!.Cast<object>().ToArray();
                Assert.Equal(["Grammar changes", "Unknown → Approved"],
                    entries.Select(entry => entry switch
                    {
                        ReviewGrammarGroupViewModel grammar => grammar.Title,
                        ReviewChangeGroupViewModel group => group.Title,
                        _ => null,
                    }).Where(title => title is not null));
                var rows = entries.OfType<ReviewListEntryViewModel>().Where(row => row.Review is not null).ToArray();
                Assert.Equal(2, rows.Length);
                var header = panel.FindControl<Grid>("ReviewTableHeader")!;
                Assert.NotNull(DenseTable.GetColumns(header));
                foreach (var row in rows)
                {
                    var container = list.ContainerFromItem(row)!;
                    var border = Assert.Single(container.GetVisualDescendants().OfType<Border>(),
                        control => control.Classes.Contains("denseTableRow"));
                    var tracks = Assert.Single(border.GetVisualDescendants().OfType<Grid>(),
                        grid => DenseTable.GetColumns(grid) is not null);
                    Assert.Equal(DenseTable.GetColumns(header), DenseTable.GetColumns(tracks));
                    Assert.Equal(header.ColumnDefinitions.Count, tracks.ColumnDefinitions.Count);
                    var keep = Assert.Single(border.GetVisualDescendants().OfType<Button>(), button => button.Content is "Keep");
                    var undo = Assert.Single(border.GetVisualDescendants().OfType<Button>(), button => button.Content is "Undo");
                    var more = Assert.Single(border.GetVisualDescendants().OfType<Button>(),
                        button => button.Classes.Contains("denseTableMenu"));
                    foreach (var action in new[] { keep, undo, more })
                    {
                        Assert.True(action.IsEffectivelyVisible);
                        Assert.True(action.IsTabStop);
                        Assert.True(action.Bounds.Width >= 24 && action.Bounds.Height >= 24);
                        var bounds = new Rect(action.TranslatePoint(default, border)!.Value, action.Bounds.Size);
                        Assert.True(bounds.Left >= 0 && bounds.Right <= border.Bounds.Width,
                            $"{row.Review!.Key} {action.Content}: {bounds}, row width {border.Bounds.Width}; " +
                            $"Keep {keep.Bounds.Width}, Undo {undo.Bounds.Width}, menu {more.Bounds.Width}.");
                    }
                    Assert.Equal("⋯", more.Content);
                    Assert.Same(row.Review!.KeepCommand, keep.Command);
                    Assert.Same(row.Review.UndoCommand, undo.Command);
                }
                var word = Assert.Single(rows, row => row.Change is not null);
                var wordContainer = list.ContainerFromItem(word)!;
                Assert.Contains(wordContainer.GetVisualDescendants().OfType<CopyableTextBlock>(), text => text.Text == "Still fits");
                var morphology = Assert.Single(wordContainer.GetVisualDescendants().OfType<MorphemePanel>());
                Assert.Equal(["ki-", "tabu"], morphology.Morphs!.Select(morph => morph.Form));
                var form = Assert.Single(morphology.GetVisualDescendants().OfType<CopyableTextBlock>(), text => text.Text == "tabu");
                var gloss = Assert.Single(morphology.GetVisualDescendants().OfType<CopyableTextBlock>(), text => text.Text == "book");
                Assert.True(form.TranslatePoint(default, morphology)!.Value.Y + form.Bounds.Height <=
                    gloss.TranslatePoint(default, morphology)!.Value.Y);
                Assert.Empty(fake.ReviewMarkRequests);
                Assert.Empty(fake.ApplyPendingRequests);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Theory]
    [InlineData(Key.Apps, RawInputModifiers.None)]
    [InlineData(Key.Down, RawInputModifiers.Alt)]
    public void RowMenuGestureAndPointerDoNotMarkOrMoveTheChange(Key key, RawInputModifiers modifiers)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = MixedChanges();
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                window.Width = 1920;
                workspace.CurrentPage = WorkspacePage.Review;
                var page = workspace.PageModel<ReviewPageModel>();
                await page.LoadReviewAsync();
                PageScreenshots.Settle(window);
                var panel = Assert.Single(window.GetVisualDescendants().OfType<ReviewPanel>());
                var list = panel.FindControl<ListBox>("ReviewItems")!;
                var entry = page.TableEntries.OfType<ReviewListEntryViewModel>().First();
                list.SelectedItem = entry;
                var row = list.ContainerFromItem(entry)!;
                Assert.True(row.Focus());
                PageScreenshots.Settle(window);
                var menu = Assert.Single(row.GetVisualDescendants().OfType<Button>(),
                    button => button.Classes.Contains("denseTableMenu"));
                var flyout = Assert.IsType<Flyout>(menu.Flyout);
                var current = page.Current;
                window.KeyPress(key, modifiers, PhysicalKey.None, null);
                window.KeyRelease(key, modifiers, PhysicalKey.None, null);
                PageScreenshots.Settle(window);
                Assert.True(flyout.IsOpen);
                Assert.Same(current, page.Current);
                Assert.Empty(fake.ReviewMarkRequests);
                flyout.Hide();
                PageScreenshots.Settle(window);
                HeadlessClick.Click(window, menu, "Row actions");
                Assert.True(flyout.IsOpen);
                Assert.Same(current, page.Current);
                Assert.Empty(fake.ReviewMarkRequests);
                Assert.Empty(fake.ApplyPendingRequests);
                flyout.Hide();
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void RowKeepMarksAttentionWithoutRemovingOrApplyingTheChange()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = MixedChanges();
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                window.Width = 1920;
                workspace.CurrentPage = WorkspacePage.Review;
                var page = workspace.PageModel<ReviewPageModel>();
                await page.LoadReviewAsync();
                PageScreenshots.Settle(window);
                var panel = Assert.Single(window.GetVisualDescendants().OfType<ReviewPanel>());
                var list = panel.FindControl<ListBox>("ReviewItems")!;
                var word = page.TableEntries.OfType<ReviewListEntryViewModel>().Single(entry => entry.Change is not null);
                var row = list.ContainerFromItem(word)!;
                var keep = Assert.Single(row.GetVisualDescendants().OfType<Button>(), button => button.Content is "Keep");
                var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(keep.Command);
                var revision = fake.Review!.Revision;
                HeadlessClick.Click(window, keep, "Keep kitabu");
                if (command.ExecutionTask is { } execution) await execution;
                PageScreenshots.Settle(window);
                Assert.True(word.Review!.IsLookedAt);
                var mark = Assert.Single(fake.ReviewMarkRequests);
                Assert.Equal(word.Review.Key, mark.EntryKey);
                Assert.True(mark.LookedAt);
                Assert.Equal(revision, fake.Review.Revision);
                Assert.Empty(fake.PendingRemoveRequests);
                Assert.Empty(fake.ApplyPendingRequests);
                Assert.Contains(page.Entries, entry => entry.Key == word.Review.Key);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void GroupUndoUsesTheTypedCommandWithoutSelectingOrMarkingItsHeading()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = MixedChanges();
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                window.Width = 1920;
                workspace.CurrentPage = WorkspacePage.Review;
                var page = workspace.PageModel<ReviewPageModel>();
                await page.LoadReviewAsync();
                PageScreenshots.Settle(window);
                var panel = Assert.Single(window.GetVisualDescendants().OfType<ReviewPanel>());
                var list = panel.FindControl<ListBox>("ReviewItems")!;
                var group = Assert.Single(page.TableEntries.OfType<ReviewChangeGroupViewModel>());
                var heading = list.ContainerFromItem(group)!;
                var current = page.Current;
                HeadlessClick.Click(window, heading, "Group heading");
                Assert.Same(current, page.Current);
                Assert.False(list.SelectedItem is IDenseTableGroupHeading);
                var undo = Assert.Single(heading.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, MotifAppStrings.UndoAll));
                Assert.Same(group.UndoAllCommand, undo.Command);
                var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(undo.Command);
                HeadlessClick.Click(window, undo, "Undo all in group");
                if (command.ExecutionTask is { } execution) await execution;
                Assert.Single(fake.PendingRemoveRequests);
                Assert.Empty(fake.ReviewMarkRequests);
                Assert.Empty(fake.ApplyPendingRequests);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectingACompactRowKeepsTheInspectorClosedUntilDetailsAreRequested(bool grammar)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = MixedChanges();
            var (workspace, sample) = await ReviewScreenshots.OpenAsync(fake);
            var page = workspace.PageModel<ReviewPageModel>();
            await page.LoadReviewAsync();
            var panel = new ReviewPanel(page);
            var host = WorkspaceLayoutHost.ForPage(panel);
            var window = new MotifTestWindow { Width = 640, Height = 780, Content = host };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var list = panel.FindControl<ListBox>("ReviewItems")!;
                var selected = page.TableEntries.OfType<ReviewListEntryViewModel>()
                    .Single(entry => entry.Review?.IsGrammar == grammar);
                HeadlessClick.Click(window, list.ContainerFromItem(selected)!, grammar ? "grammar change" : "kitabu change");
                PageScreenshots.Settle(window);
                Assert.Same(selected.Review, page.Current);
                Assert.False(host.FindControl<Border>("InspectorPane")!.IsVisible);
                var row = list.ContainerFromItem(selected)!;
                var menu = Assert.Single(row.GetVisualDescendants().OfType<Button>(),
                    button => button.Classes.Contains("denseTableMenu"));
                HeadlessClick.Click(window, menu, "Row actions");
                var flyout = Assert.IsType<Flyout>(menu.Flyout);
                var body = Assert.IsAssignableFrom<Control>(flyout.Content);
                var details = Assert.Single(body.GetVisualDescendants().OfType<Button>(), button => button.Content is "Details");
                HeadlessClick.Click(TopLevel.GetTopLevel(details) ?? window, details, "Details for this change");
                PageScreenshots.Settle(window);
                Assert.True(host.FindControl<Border>("InspectorPane")!.IsVisible);
                Assert.False(flyout.IsOpen);
                Assert.Same(selected.Review, page.Current);
                if (selected.Change is { } change)
                {
                    var card = Assert.Single(panel.InspectorContent.GetVisualDescendants().OfType<WordCard>());
                    Assert.Equal(change.Presentation.Key, card.Document!.Key);
                }
                Assert.Empty(fake.ReviewMarkRequests);
                Assert.Empty(fake.ApplyPendingRequests);
            }
            finally { window.Close(); sample.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void CompactRowsKeepIdentityFitAndActionsAcrossSizesThemesAndZoom() =>
        CheckScenes(capture: false);

    [ScreenshotFact]
    public void CaptureDenseReviewAcrossSizesThemesAndZoom() => CheckScenes(capture: true);

    private static void CheckScenes(bool capture)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = MixedChanges();
            fake.Review = fake.Review! with
            {
                LookedAtCount = 1,
                Entries = fake.Review.Entries.Select(entry => entry is WordChangeEntry word
                    ? word with { LookedAt = true } : entry).ToArray(),
            };
            var (workspace, sample) = await ReviewScreenshots.OpenAsync(fake);
            SettingsTestAccess.Model(sample).ZoomPercent = 100;
            workspace.CurrentPage = WorkspacePage.Review;
            var page = workspace.PageModel<ReviewPageModel>();
            await page.LoadReviewAsync();
            var panel = new ReviewPanel(page);
            var host = WorkspaceLayoutHost.ForPage(panel);
            var transform = new LayoutTransformControl { Child = host };
            var window = new MotifTestWindow { Width = 640, Height = 480, Content = transform };
            var prior = Application.Current!.RequestedThemeVariant;
            try
            {
                window.Show();
                var list = panel.FindControl<ListBox>("ReviewItems")!;
                var word = page.TableEntries.OfType<ReviewListEntryViewModel>().Single(entry => entry.Change is not null);
                list.SelectedItem = word;
                foreach (var (width, height) in new[] { (640, 480), (1040, 780), (1240, 780), (1920, 1080) })
                foreach (var zoom in new[] { 100, 150 })
                foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
                {
                    window.Width = width;
                    window.Height = height;
                    transform.LayoutTransform = new ScaleTransform(zoom / 100d, zoom / 100d);
                    Application.Current.RequestedThemeVariant = variant;
                    PageScreenshots.Settle(window);
                    var header = panel.FindControl<Grid>("ReviewTableHeader")!;
                    var compact = header.ColumnDefinitions[1].Width.Value == 0;
                    word = list.ItemsSource!.Cast<object>().OfType<ReviewListEntryViewModel>()
                        .Single(entry => entry.Review?.Key == word.Review!.Key);
                    list.SelectedItem = word;
                    list.ScrollIntoView(word);
                    PageScreenshots.Settle(window);
                    Assert.True(list.ContainerFromItem(word) is ListBoxItem,
                        $"{width}/{height}/{zoom}/{theme}: row missing; list {list.Bounds}, panel {panel.Bounds}, host {host.Bounds}, " +
                        $"header {header.Bounds}, item count {list.ItemCount}.");
                    var row = Assert.IsType<ListBoxItem>(list.ContainerFromItem(word));
                    var frame = Assert.Single(row.GetVisualDescendants().OfType<Border>(),
                        border => border.Classes.Contains("denseTableRow"));
                    var grid = Assert.Single(frame.GetVisualDescendants().OfType<Grid>(),
                        candidate => DenseTable.GetColumns(candidate) is not null);
                    var identity = Assert.Single(frame.GetVisualDescendants().OfType<CopyableTextBlock>(),
                        text => text.Text == "kitabu" && text.IsEffectivelyVisible);
                    Assert.True(identity.IsEffectivelyVisible);
                    Assert.True(identity.Bounds.Width >= (compact ? 110 : 32),
                        $"{width}/{height}/{zoom}/{theme}: primary word width {identity.Bounds.Width}, row {frame.Bounds.Width}");
                    Assert.True(identity.Bounds.Height > 0);
                    Assert.Equal(header.ColumnDefinitions.Select(column => column.Width),
                        grid.ColumnDefinitions.Select(column => column.Width));
                    foreach (var secondary in grid.Children.OfType<Control>().Where(DenseTable.GetSecondary))
                        Assert.Equal(!compact, secondary.IsVisible);
                    Assert.Contains(frame.GetVisualDescendants().OfType<CopyableTextBlock>(),
                        text => text.Text == "Still fits" && text.IsEffectivelyVisible);
                    foreach (var action in frame.GetVisualDescendants().OfType<Button>())
                    {
                        Assert.True(action.IsEffectivelyVisible);
                        var bounds = new Rect(action.TranslatePoint(default, frame)!.Value, action.Bounds.Size);
                        Assert.True(bounds.Left >= 0 && bounds.Right <= frame.Bounds.Width,
                            $"{width}/{height}/{zoom}/{theme}: {action.Content} {bounds} exceeds row {frame.Bounds.Width}");
                        Assert.True(bounds.Width >= 24 && bounds.Height >= 24);
                    }
                    if (!capture) continue;
                    LayoutAssertions.AssertCurrent(window);
                    var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
                    Directory.CreateDirectory(folder);
                    using var image = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Review frame missing.");
                    image.Save(Path.Combine(folder, $"review-dense-{width}-{height}-{zoom}-{theme}.png"),
                        PngBitmapEncoderOptions.Default);
                }
                Assert.Empty(fake.ReviewMarkRequests);
                Assert.Empty(fake.ApplyPendingRequests);
            }
            finally
            {
                Application.Current.RequestedThemeVariant = prior;
                window.Close(); sample.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    internal static FakeCommandClient MixedChanges()
    {
        var reading = ReviewScreenshots.Reading(("ki-", "7"), ("tabu", "book"));
        var change = ReviewScreenshots.Change("kitabu", "approve", "analysis/one",
            new ReviewAnalysis(reading, ReadingGrade.Candidate, true, true));
        var fake = new FakeCommandClient
        {
            Review = new ProposalReviewResponse("", "pending", "revision/one",
                [new GrammarChangeEntry("rule", 0, "Nasal assimilation", ["op/rule"], [], "digest/rule", false,
                    "AuthorPhonologicalRule", "1 rule", [], [], []),
                 new WordChangeEntry(change.ChangeId, 1, change.Word, change.OperationIds, [], "digest/word", false,
                    change, new ChangeFit(change.ChangeId, true, []),
                    new Paired<ParserReading?>(reading, reading, true, true, false, null))], null, null, [], 0)
            { IsPendingChanges = true, IsDraft = true, DraftName = "pending" },
        };
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one", [change],
            [new ChangeFit(change.ChangeId, true, [])]));
        return fake;
    }
}
