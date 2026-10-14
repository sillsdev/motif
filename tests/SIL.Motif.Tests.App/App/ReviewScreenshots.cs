using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.Services;
using SIL.Motif.App.Controls;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Tests.App.Walkthrough;
using CommunityToolkit.Mvvm.Input;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Saves Review changes in the states a person meets there, which the every-page capture cannot reach from its
/// empty list: changes blocked by one that no longer fits and one that is Uncertain, the page after an Apply whose
/// result could not be confirmed, and the empty page.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ReviewScreenshots
{
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ProjectPath = @"C:\Users\linguist\FieldWorks\Projects\Sample\Sample.fwdata";
    private static readonly Guid Story = Guid.Parse("11111111-0000-0000-0000-000000000001");

    [Fact]
    public void ReviewLayoutKeepsActionsAndCardBodiesVisibleWithoutScreenshotCapture()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.PendingChangesIs(Blocked());
            var (workspace, window) = await OpenAsync(fake);
            try
            {
                workspace.CurrentPage = WorkspacePage.Review;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                AssertSidePanelCardsHaveVisibleBody(window);
                var action = Assert.Single(window.GetVisualDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetAutomationId(button) == "motif-measure-changes");
                Assert.True(action.IsEffectivelyVisible);
                Assert.True(action.Bounds.Width > 0 && action.Bounds.Height > 0);
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Theory]
    [InlineData(1040)]
    [InlineData(1240)]
    public void ReviewOpinionCellStaysSeparateFromMorphologyOrMovesToTheInspector(int width)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.PendingChangesIs(Blocked());
            var (workspace, window) = await OpenAsync(fake);
            try
            {
                window.Width = width;
                window.Height = 780;
                workspace.CurrentPage = WorkspacePage.Review;
                PageScreenshots.Settle(window);

                var page = workspace.PageModel<ReviewPageModel>();
                var change = page.ReviewGroups.SelectMany(group => group.Items)
                    .Single(change => change.Word == "kitabu");
                var row = RowFor(window, change);
                var panel = Assert.Single(window.GetVisualDescendants().OfType<ReviewPanel>());
                var tracks = row.GetVisualDescendants().OfType<Grid>()
                    .Single(grid => DenseTable.GetColumns(grid) is not null);
                Assert.Contains(panel.FindControl<Grid>("ReviewTableHeader")!.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == "Opinion change");
                if (tracks.ColumnDefinitions[1].Width.Value == 0)
                {
                    Assert.Equal(0, tracks.ColumnDefinitions[2].Width.Value);
                    Assert.All(tracks.Children.OfType<Control>().Where(DenseTable.GetSecondary),
                        secondary => Assert.False(secondary.IsVisible));
                    page.SelectEntry(Assert.Single(page.Entries, entry => ReferenceEquals(entry.Change, change)));
                    SIL.Motif.App.Views.Shell.WorkspaceLayoutHost.RequestPageInspector(panel);
                    PageScreenshots.Settle(window);
                    var card = Assert.Single(panel.InspectorContent.GetVisualDescendants().OfType<WordCard>());
                    Assert.Equal(change.Reading, Assert.Single(card.Document!.Sections.OfType<WordCardPendingChange>()).Reading);
                    Assert.Empty(fake.ReviewMarkRequests);
                    return;
                }
                var opinion = Assert.Single(row.GetVisualDescendants().OfType<OpinionMark>(),
                    mark => mark.Kind == OpinionMarkKind.Unknown);
                Assert.True(opinion.IsEffectivelyVisible);
                Assert.Equal("Unknown", AutomationProperties.GetName(opinion));
                var firstMorpheme = Assert.Single(row.GetVisualDescendants().OfType<CopyableTextBlock>(),
                    text => text.Text == "ki-");
                Assert.True(firstMorpheme.IsEffectivelyVisible);
                var opinionBounds = new Rect(opinion.TranslatePoint(default, window)!.Value, opinion.Bounds.Size);
                var morphemeBounds = new Rect(firstMorpheme.TranslatePoint(default, window)!.Value,
                    firstMorpheme.Bounds.Size);

                Assert.True(opinionBounds.Right < morphemeBounds.Left,
                    $"At {width}px, the opinion ends at {opinionBounds.Right:0.#} but the first morpheme " +
                    $"starts at {morphemeBounds.Left:0.#}.");
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ReviewRowsStayBelowTheirHeaderWhenScrolledAndSelected()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            var snapshot = Blocked();
            fake.PendingChangesIs(snapshot with
            {
                Changes = Enumerable.Range(0, 12).SelectMany(index => snapshot.Changes.Select(change =>
                    change with { ChangeId = $"{change.ChangeId}/{index}" })).ToArray(),
                FitSummary = Enumerable.Range(0, 12).SelectMany(index => snapshot.FitSummary.Select(fit =>
                    fit with { ChangeId = $"{fit.ChangeId}/{index}" })).ToArray(),
            });
            var (workspace, window) = await OpenAsync(fake);
            try
            {
                window.Height = 460;
                workspace.CurrentPage = WorkspacePage.Review;
                PageScreenshots.Settle(window);

                var panel = Assert.Single(window.GetVisualDescendants().OfType<ReviewPanel>());
                var list = panel.FindControl<ListBox>("ReviewItems")!;
                var entries = workspace.PageModel<ReviewPageModel>().TableEntries
                    .OfType<ReviewListEntryViewModel>().ToArray();
                var header = panel.FindControl<Grid>("ReviewTableHeader")!;
                list.ScrollIntoView(entries[0]);
                PageScreenshots.Settle(window);
                AssertBelowHeader(list.ContainerFromItem(entries[0])!, header, window);

                var scroll = Assert.Single(list.GetVisualDescendants().OfType<ScrollViewer>(),
                    viewer => viewer.Name == "PART_ScrollViewer");
                Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
                list.SelectedItem = entries[^1];
                list.ScrollIntoView(entries[^1]);
                PageScreenshots.Settle(window);
                Assert.Same(entries[^1].Review, workspace.PageModel<ReviewPageModel>().Current);
                AssertBelowHeader(list.ContainerFromItem(entries[^1])!, header, window);
                Assert.True(scroll.Offset.Y > 0);
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ReviewWordPresentationReadsItsContextAndRoutesUndoWithoutApplying()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.PendingChangesIs(Blocked());
            var (workspace, window) = await OpenAsync(fake);
            try
            {
                workspace.CurrentPage = WorkspacePage.Review;
                PageScreenshots.Settle(window);
                var page = workspace.PageModel<ReviewPageModel>();
                var change = page.ReviewGroups.SelectMany(group => group.Items)
                    .First(item => item.Word == "kitabu");
                var row = RowFor(window, change);
                var panel = Assert.Single(window.GetVisualDescendants().OfType<ReviewPanel>());
                var inspector = panel.InspectorContent;
                page.SelectEntry(page.Entries.Single(entry => ReferenceEquals(entry.Change, change)));
                PageScreenshots.Settle(window);
                var card = Assert.Single(inspector.GetVisualDescendants().OfType<WordCard>());
                var pending = Assert.IsType<WordCardPendingChange>(Assert.Single(card.Document!.Sections,
                    section => section is WordCardPendingChange));
                Assert.Equal(change.Reading, pending.Reading);
                Assert.Same(page.Context, pending.Context);

                var undo = Assert.Single(row.GetVisualDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == change.UndoAutomationName);
                var undoCommand = Assert.IsAssignableFrom<IAsyncRelayCommand>(undo.Command);
                HeadlessClick.Click(window, undo, change.UndoAutomationName);
                var undoTask = undoCommand.ExecutionTask;
                Assert.NotNull(undoTask);
                await undoTask;
                PageScreenshots.Settle(window);

                Assert.Single(fake.PendingRemoveRequests);

                var uncertain = page.ReviewGroups.SelectMany(group => group.Items)
                    .Single(item => item.Word == "watoto");
                page.SelectEntry(page.Entries.Single(entry => ReferenceEquals(entry.Change, uncertain)));
                PageScreenshots.Settle(window);
                Assert.Contains(inspector.GetVisualDescendants().OfType<CopyableTextBlock>(),
                    text => text.IsEffectivelyVisible && text.Text == "Now reads");
                var reconfirm = Assert.Single(inspector.GetVisualDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == uncertain.CheckAgainAutomationName);
                var reconfirmCommand = Assert.IsAssignableFrom<IAsyncRelayCommand>(reconfirm.Command);
                HeadlessClick.Click(window, reconfirm, uncertain.CheckAgainAutomationName);
                var reconfirmTask = reconfirmCommand.ExecutionTask;
                Assert.NotNull(reconfirmTask);
                await reconfirmTask;
                PageScreenshots.Settle(window);

                Assert.Contains(fake.PendingReconfirmRequests, request => request.ChangeId == uncertain.ChangeId);
                Assert.Empty(fake.ApplyPendingRequests);
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [ScreenshotFact]
    public void CaptureReviewStates()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
            var fake = new FakeCommandClient();
            fake.PendingChangesIs(Blocked());
            var (workspace, window) = await OpenAsync(fake);
            try
            {
                window.Width = 1920;
                var review = workspace.PageModel<ReviewPageModel>();
                workspace.CurrentPage = WorkspacePage.Review;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.Equal(3, review.ApplyBlockers.Count);
                Assert.Equal("Apply is blocked by 2 changes", review.ApplyBlockedTitle);
                Assert.Equal("Needs another look", Assert.Single(review.ReviewGroups,
                    group => group.Title == "Needs another look").Title);
                var unknownToApproved = review.ReviewGroups.SelectMany(group => group.Items)
                    .Single(change => change.Word == "kitabu" && change.TransitionText == "Unknown → Approved");
                Assert.Equal("Unknown", unknownToApproved.Listed!.Row.OpinionLabel);
                Assert.Equal(new[] { "ki-", "tabu" }, unknownToApproved.Listed.Row.FieldWorksMorphemes
                    .Select(morph => morph.Form));
                PageScreenshots.Settle(window);
                var kitabuRow = RowFor(window, unknownToApproved);
                var fieldWorksPanel = kitabuRow.GetVisualDescendants().OfType<MorphemePanel>()
                    .First(panel => panel.Morphs?.Select(morph => morph.Form).SequenceEqual(["ki-", "tabu"]) == true);
                Assert.True(fieldWorksPanel.IsEffectivelyVisible);
                var visibleTexts = window.GetVisualDescendants().OfType<CopyableTextBlock>()
                    .Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToArray();
                Assert.Contains("ki-", visibleTexts);
                Assert.Contains("tabu", visibleTexts);
                Assert.Contains("7", visibleTexts);
                Assert.Contains("book", visibleTexts);
                Assert.Contains("Still fits", visibleTexts);
                Assert.Contains("✓", visibleTexts);
                Assert.Contains(review.ApplyBlockedTitle, visibleTexts);
                foreach (var blocker in review.ApplyBlockers)
                    Assert.DoesNotContain(blocker.Sentence, visibleTexts);
                AssertSidePanelCardsHaveVisibleBody(window);
                var visibleButtons = window.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.IsEffectivelyVisible).ToArray();
                var measureButton = Assert.Single(visibleButtons,
                    button => button.Content?.ToString() == "Check these changes");
                Assert.Equal("Check what applying does to the numbers", AutomationProperties.GetName(measureButton));
                Assert.Equal("motif-measure-changes", AutomationProperties.GetAutomationId(measureButton));
                var measurementActions = window.GetVisualDescendants().OfType<Button>()
                    .Where(button => AutomationProperties.GetAutomationId(button) == "motif-measure-changes")
                    .ToArray();
                Assert.Single(measurementActions);
                Assert.True(measurementActions[0].IsEffectivelyVisible);
                Assert.Single(visibleButtons,
                    button => AutomationProperties.GetAutomationId(button) == "motif-refresh-project");
                SaveAll(window, folder, "review-blocked");

                review.ShowReconciliationNeeded();
                window.UpdateLayout();
                var reconciliationButtons = window.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.IsEffectivelyVisible).ToArray();
                Assert.Single(reconciliationButtons,
                    button => AutomationProperties.GetAutomationId(button) == "motif-refresh-project");
                Assert.DoesNotContain(reconciliationButtons,
                    button => AutomationProperties.GetName(button) == "Choose what to parse");
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<CopyableTextBlock>(),
                    text => text.IsEffectivelyVisible && text.Text == review.ReconciliationNotice);
                AssertSidePanelCardsHaveVisibleBody(window);
                SaveAll(window, folder, "review-unconfirmed-apply");
                review.ClearReconciliationNeeded();

                fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/empty", [], []));
                await workspace.Context.Changes.ReloadAsync();
                SaveAll(window, folder, "review-empty");
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [ScreenshotFact]
    public void CaptureReviewWithStagedParsimonyJudgment()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
            var operation = "operation/parsimony-judgment";
            var title = "Record a decision on repeated ad hoc restrictions";
            var fake = new FakeCommandClient
            {
                Review = new ProposalReviewResponse(string.Empty, string.Empty, "revision/one",
                [new GrammarChangeEntry(operation, 0, title, [operation], [], "sha256:decision", false,
                    "RecordParsimonyDisposition", "1 decision", [], [], [])], null, null, [], 0)
                {
                    IsPendingChanges = true,
                    IsDraft = true,
                    DraftName = "pending",
                },
            };
            fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one", [], []));
            var (workspace, window) = await OpenAsync(fake);
            try
            {
                var page = workspace.PageModel<ReviewPageModel>();
                workspace.CurrentPage = WorkspacePage.Review;
                await page.LoadReviewAsync();
                PageScreenshots.Settle(window);

                Assert.Equal("1", page.Badge);
                Assert.Contains(page.Entries, entry => entry.Title == title && entry.IsGrammar);
                Assert.Contains(window.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == title);
                SaveAll(window, folder, "review-parsimony-judgment");
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [ScreenshotFact]
    public void CaptureRetirementProposalReview()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);

        var stored = StoredRetirementReview.Load().Review!;
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
            var fake = new FakeCommandClient();
            fake.PendingChangesIs(Blocked() with { DraftId = stored.ProposalId });
            var (workspace, window) = await OpenAsync(fake);
            try
            {
                workspace.CurrentPage = WorkspacePage.Review;
                var review = workspace.PageModel<ReviewPageModel>();
                PageScreenshots.Settle(window);
                review.ShowRetirementReview(stored);
                PageScreenshots.Settle(window);
                var panel = Assert.Single(window.GetVisualDescendants().OfType<RetirementProposalReviewPanel>());
                Assert.Same(review.RetirementReview, panel.DataContext);
                var parts = Assert.Single(panel.GetVisualDescendants().OfType<ItemsControl>(),
                    control => control.Name == "RetirementReviewParts");
                Assert.Same(panel.DataContext, parts.DataContext);
                Assert.Equal(4, ((RetirementProposalReviewViewModel)panel.DataContext!).Parts.Count);
                var partItems = parts.ItemsSource;
                Assert.NotNull(partItems);
                Assert.Equal(4, partItems.Cast<object>().Count());
                var scroll = panel.FindAncestorOfType<ScrollViewer>()!;
                ScrollTo(scroll, panel);
                PageScreenshots.Settle(window);
                var visibleTexts = window.GetVisualDescendants().OfType<CopyableTextBlock>()
                    .Where(control => control.IsEffectivelyVisible).Select(control => control.Text).ToArray();
                Assert.DoesNotContain("Destination needed", visibleTexts);
                Assert.Contains("1 word affected", visibleTexts);
                Assert.Contains("Sound changes", visibleTexts);
                Assert.Contains("Analysis of ata now uses form ra instead of ta", visibleTexts);

                window.Width = 1240;
                window.Height = 1800;
                Save(window, Path.Combine(folder, "retirement-review-connected-four-parts.png"));

                var readingTitle = Assert.Single(panel.GetVisualDescendants().OfType<CopyableTextBlock>(),
                    control => control.Text == "Tested readings");
                ScrollTo(scroll, readingTitle);
                PageScreenshots.Settle(window);
                visibleTexts = window.GetVisualDescendants().OfType<CopyableTextBlock>()
                    .Where(control => control.IsEffectivelyVisible).Select(control => control.Text).ToArray();
                Assert.Contains("The sound rule could not be traced for this reading", visibleTexts);
                Assert.Contains("Resolved", visibleTexts);
                SaveAll(window, folder, "retirement-review-reading-evidence");

                // A refusal, not the stored path, so it stays a hand-built state.
                review.ShowRetirementReview(RetirementProjection(includeUnresolved: true) with { ProposalId = stored.ProposalId });
                PageScreenshots.Settle(window);
                panel = Assert.Single(window.GetVisualDescendants().OfType<RetirementProposalReviewPanel>());
                scroll = panel.FindAncestorOfType<ScrollViewer>()!;
                var destinationTitle = Assert.Single(panel.GetVisualDescendants().OfType<CopyableTextBlock>(),
                    control => control.Text == "Destination needed");
                ScrollTo(scroll, destinationTitle);
                PageScreenshots.Settle(window);
                visibleTexts = window.GetVisualDescendants().OfType<CopyableTextBlock>()
                    .Where(control => control.IsEffectivelyVisible).Select(control => control.Text).ToArray();
                Assert.Contains(visibleTexts, text => text?.Contains(
                    "1 Approved analysis", StringComparison.Ordinal) == true);
                Assert.Contains(visibleTexts, text => text?.Contains(
                    "1 restriction", StringComparison.Ordinal) == true);
                SaveAll(window, folder, "retirement-review-unresolved-destination");
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    private static void ScrollTo(ScrollViewer scroll, Control target)
    {
        var location = target.TranslatePoint(new Point(0, 0), scroll);
        Assert.NotNull(location);
        scroll.Offset = new Vector(scroll.Offset.X, Math.Max(0, scroll.Offset.Y + location!.Value.Y));
        Dispatcher.UIThread.RunJobs();
        scroll.UpdateLayout();
    }

    internal static RetirementProposalReviewProjection RetirementProjection(bool includeUnresolved)
    {
        var dryRun = new DryRunProjection("draft/one", "intent/one", "Baseline",
            [new("bundle/one", "analysis/wfiMorphBundle/morph", [new("qaa", "old", "new")], null)
            {
                OperationIds = ["operation/bundle"],
            }, new("bundle/one", "analysis/wfiMorphBundle/form", [new("qaa", "ta", "ra")], null)
            {
                OperationIds = ["operation/bundle"],
            }], "effects/one", "footprint/one")
        {
            Operations =
            [
                new("operation/class", "grammar/phNaturalClass/setName", null, null, [], "{\"ws\":\"qaa\",\"text\":\"Vowels\"}"),
                new("operation/rule", "grammar/phSegmentRule/setDisabled", null, null,
                    ["operation/class"], "{\"value\":false}"),
                new("operation/bundle", "analysis/wfiMorphBundle/setMorph", null, null,
                    ["operation/rule"], null),
                new("operation/reference", "grammar/moAlloAdhocProhib/retargetReferences", null, null,
                    ["operation/rule"], null),
                new("operation/delete", "lexical/lexEntry/deleteAlternateForm", null, null,
                    ["operation/bundle", "operation/reference"], null),
            ],
        };
        var statistics = new RetirementReviewStatistics(
            new(1, 0, 0, 0), new(1, 0, 0, 0), new(1, 0, 0, 0), 1,
            [new(true, true, 1, 1), new(true, false, 0, 0),
                new(false, true, 0, 0), new(false, false, 0, 0)],
            0, 1, 1, 0, new("finding/one", 1, 1, 0, 1, Digest, Digest, true), Digest);
        var details = new RetirementReviewDetail("bundle/one", "bundle-morph",
            "WfiMorphBundle.Morph", "wordform/one", "analysis/one", "bundle/one", null,
            "approved", "Morph", 0, "qaa: ta", "qaa: ra");
        return new RetirementProposalReviewProjection("draft/one", "intent/one", dryRun, statistics,
            [new("rule", "The new rule and sound class", ["operation/class", "operation/rule"], [])
                { Details = [new("rule/one", "rule-component", "New sound rule: r becomes t before a vowel", null, null, null,
                    "rule/one", null, null, null, null, null)] },
             new("bundle", "Analyses and bundle text", ["operation/bundle"], [.. dryRun.Effects])
                { Details = [details] },
             new("other-references", "Other references", ["operation/reference"], [])
                { Details = [new("source/one", "adhoc-single", "Restriction moved to the remaining form", null,
                    null, null, null, null, null, null, null, null)] },
             new("retired-form", "Deleted forms", ["operation/delete"], [])
                { Details = [new("form/retired", "retired-form", "Deleted form: -ta (alternate of -ra)", null, null,
                    null, null, null, null, null, null, null)] }],
            [new("case/one", "reading/one", "wordform/one", "ata", "qaa", "ata", "ata",
                "approved", "A", "preserved", true, true, true, "traced", "rule/one", Digest, null)],
            new("finding/one", 1, 1, 0, 1, Digest, Digest, true, false),
            includeUnresolved
                ? new(1, 1, 1, 0, [new("source/one", "other-native", "WfiAnalysis.PartOfSpeech",
                    "wordform/one", "analysis/one", null, null, "approved", "PartOfSpeech", null, null, null)])
                    { Message = "Declare every replacement destination." }
                : new(0, 0, 0, 0, [])
                { Message = string.Empty },
            []);
    }

    private static Control RowFor(Window window, ChangeViewModel change)
    {
        var panel = Assert.Single(window.GetVisualDescendants().OfType<ReviewPanel>());
        var list = panel.FindControl<ListBox>("ReviewItems")!;
        var entry = panel.Page.TableEntries.OfType<ReviewListEntryViewModel>()
            .Single(row => ReferenceEquals(row.Change, change));
        list.ScrollIntoView(entry);
        PageScreenshots.Settle(window);
        return list.ContainerFromItem(entry)!;
    }

    private static void AssertBelowHeader(Control row, Control header, Window window)
    {
        var rowBounds = new Rect(row.TranslatePoint(default, window)!.Value, row.Bounds.Size);
        var headerBounds = new Rect(header.TranslatePoint(default, window)!.Value, header.Bounds.Size);
        Assert.True(rowBounds.Top >= headerBounds.Bottom,
            $"The row starts at {rowBounds.Top:0.#}, above the column header's end {headerBounds.Bottom:0.#}.");
    }

    private static void AssertSidePanelCardsHaveVisibleBody(MainWindow window)
    {
        var sidePanel = Assert.Single(window.GetVisualDescendants().OfType<StackPanel>(),
            panel => panel.Classes.Contains("reviewRight") && panel.IsEffectivelyVisible);
        var cards = sidePanel.Children.OfType<Border>()
            .Where(card => card.Classes.Contains("card") && card.IsEffectivelyVisible).ToArray();
        Assert.NotEmpty(cards);
        Assert.All(cards, card =>
        {
            var hasBody = card.GetVisualDescendants().Any(control => control.IsEffectivelyVisible &&
                (control is Button || control is ItemsControl ||
                 control is CopyableTextBlock text && !text.Classes.Contains("section-title") &&
                 !string.IsNullOrWhiteSpace(text.Text)));
            Assert.True(hasBody, "A visible Review side-panel card has only its title.");
        });
    }

    internal static void SaveAll(MainWindow window, string folder, string name)
    {
        window.Height = 780;
        PageScreenshots.SaveAll(window, folder, name);
    }

    private static void Save(MainWindow window, string path)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        LayoutAssertions.BeforeCapture(window);
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException($"No frame rendered for {path}.");
        frame.Save(path, PngBitmapEncoderOptions.Default);
    }

    internal static async Task<(WorkspaceShellViewModel Workspace, MainWindow Window)> OpenAsync(FakeCommandClient fake)
    {
        var clock = PageScreenshots.NewCaptureClock();
        fake.KnownProjectsListIs([new KnownProjectSummary(ProjectPath, PageScreenshots.CaptureStartAt)]);
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(Token(), PageScreenshots.BaselineSaveAt, false));
        fake.ListTextsCompletesWith(new TextInventoryResponse(
            [new TextChoiceSummary(Story, "Hadithi ya sungura")], HasBaseline: true));
        var selection = new SelectionViewModel(fake);
        var workspace = new WorkspaceShellViewModel(
            new ProjectViewModel(fake, new Picker()), new BaselineViewModel(fake),
            selection, new AssessViewModel(fake, selection), new Folder(), new Drag(), fake,
            clock, techDemoNotice: PageScreenshots.FirstRunNotice());
        var window = new MainWindow();
        window.Compose(workspace);
        window.Show();
        await workspace.SetProjectAsync(ProjectPath);
        workspace.Context.Setup?.SkipCommand.Execute(null);
        workspace.Context.RegisterOccurrenceLocationProvider(occurrence =>
            new TextOccurrenceLocation(0, occurrence.Index, occurrence.Index,
                $"Hadithi ya sungura, line {occurrence.Index + 1}"));
        await workspace.Context.Changes.ReloadAsync();
        return (workspace, window);
    }

    internal static PendingChangesSnapshot Blocked()
    {
        var kitabu = Change("kitabu", ChangeKinds.Approve, "analysis/kitabu", Stored(("ki-", "7"), ("tabu", "book")));
        var anapenda = Change("anapenda", ChangeKinds.Approve, "analysis/anapenda",
            Stored(("a-", "3SG"), ("na-", "PRS"), ("pend", "love"), ("-a", "FV"))) with
        {
            Occurrence = new OccurrenceAnchor(Story, Story, Story, 2),
        };
        var chakula = Change("chakula", ChangeKinds.Approve, null,
            new ReviewAnalysis(Reading(("ch-", "7"), ("akula", "food")), ReadingGrade.NoOpinion, true, false));
        var watoto = Change("watoto", ChangeKinds.Candidate, "analysis/watoto",
            new ReviewAnalysis(Reading(("wa-", "2"), ("toto", "child")), ReadingGrade.Approved, true, true));
        var uncertain = new ChangeFit("watoto", ChangeFitStatus.Uncertain, ["The words in the source sentence have changed."])
        {
            Uncertainty = new ChangeUncertainty("The words in the source sentence have changed.",
                [Token(0, "watoto"), Token(1, "hawajafika"), Token(2, "mwalimu")],
                [Token(0, "watoto"), Token(1, "hawajaja"), Token(2, "mwalimu")]),
        };
        return new PendingChangesSnapshot("draft/one", "revision/one", [kitabu, anapenda, chakula, watoto],
        [
            new ChangeFit("kitabu", false, ["Analysis analysis/kitabu was deleted or moved from wordform wordform/kitabu."]),
            new ChangeFit("anapenda", true, []),
            new ChangeFit("chakula", true, []),
            uncertain,
        ]);
    }

    private static OccurrenceWordToken Token(int index, string form) => new(index, "wordform/" + form, form);

    internal static PendingChange Change(string word, string kind, string? storedAnalysisId, ReviewAnalysis analysis) =>
        new(word, "wordform/" + word, word, kind, "assessment/one", word, ["operation/" + word])
        {
            Analyses = [analysis],
            OriginPage = WorkspacePage.Texts.ToString(),
            StoredAnalysisId = storedAnalysisId,
        };

    internal static ReviewAnalysis Stored(params (string Form, string Gloss)[] morphs) =>
        new(Reading(morphs), ReadingGrade.Candidate, true, true);

    internal static ParserReading Reading(params (string Form, string Gloss)[] morphs) =>
        new(morphs.Select(morph => new ParserReadingMorph(morph.Form, morph.Gloss, "n", null, false, null)).ToArray());

    internal static BaselineToken Token() =>
        new("project-1", "sha256:" + new string('a', 64), "1", "2026-09-22T10:00:00Z", "sha256:" + new string('b', 64));

    private sealed class Picker : IProjectPicker
    {
        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(ProjectPath);
    }

    private sealed class Folder : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class Drag : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(
            PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) =>
            Task.FromResult(allowedEffects);
    }
}
