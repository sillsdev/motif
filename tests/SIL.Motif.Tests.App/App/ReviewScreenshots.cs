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
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using WordPresentationRow = SIL.Motif.App.Controls.WordPresentation.WordRow;

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

    [Fact]
    public void ReviewRowsStayBelowTheirHeaderWhenScrolledAndOpened()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.PendingChangesIs(Blocked());
            var (workspace, window) = await OpenAsync(fake);
            try
            {
                window.Height = 460;
                workspace.CurrentPage = WorkspacePage.Review;
                PageScreenshots.Settle(window);

                var panel = Assert.Single(window.GetVisualDescendants().OfType<ReviewPanel>());
                var slot = Assert.Single(panel.GetVisualDescendants().OfType<WordListSlot>());
                var list = Assert.Single(slot.GetVisualDescendants().OfType<ListBox>(),
                    control => control.Name == "ReviewItems" && control.ItemCount > 0);
                var entries = workspace.PageModel<ReviewPageModel>().ReviewEntries;
                var firstChangeIndex = entries.Select((entry, index) => (entry, index))
                    .First(pair => pair.entry.IsChangeRow).index;
                list.ScrollIntoView(firstChangeIndex);
                list.UpdateLayout();
                PageScreenshots.Settle(window);
                var row = list.GetVisualDescendants().OfType<WordPresentationRow>().First();
                WordListHeaderAssertions.FirstRowIsBelowHeader(slot, row);

                var scroll = Assert.Single(list.GetVisualDescendants().OfType<ScrollViewer>(),
                    viewer => viewer.Name == "PART_ScrollViewer");
                Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
                row.State = row.State! with { IsOpen = true };
                window.UpdateLayout();
                scroll.Offset = new Vector(0, scroll.Extent.Height - scroll.Viewport.Height);
                row.BringIntoView();
                PageScreenshots.Settle(window);
                WordListHeaderAssertions.FirstRowIsBelowHeader(slot, row);
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
                var row = Assert.Single(window.GetVisualDescendants().OfType<WordPresentationRow>(),
                    item => item.Data?.Key == change.Presentation.Key);

                row.State = row.State! with { IsOpen = true };
                PageScreenshots.Settle(window);
                var card = Assert.Single(row.GetVisualDescendants().OfType<WordCard>());
                var pending = Assert.IsType<WordCardPendingChange>(Assert.Single(card.Document!.Sections,
                    section => section is WordCardPendingChange));
                Assert.Equal(change.Reading, pending.Reading);
                Assert.Same(page.Context, pending.Context);

                var undo = Assert.Single(row.GetVisualDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == change.UndoAutomationName);
                undo.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                PageScreenshots.Settle(window);

                Assert.Single(fake.PendingRemoveRequests);

                var uncertain = page.ReviewGroups.SelectMany(group => group.Items)
                    .Single(item => item.Word == "watoto");
                var panel = Assert.Single(window.GetVisualDescendants().OfType<ReviewPanel>());
                var items = panel.FindControl<ListBox>("ReviewItems")!;
                var uncertainIndex = page.ReviewEntries.Select((entry, index) => (entry, index))
                    .Single(pair => ReferenceEquals(pair.entry.Change, uncertain)).index;
                items.ScrollIntoView(uncertainIndex);
                PageScreenshots.Settle(window);
                var uncertainRow = Assert.Single(items.ContainerFromIndex(uncertainIndex)!
                    .GetVisualDescendants().OfType<WordPresentationRow>());
                Assert.Contains(uncertainRow.GetVisualDescendants().OfType<CopyableTextBlock>(),
                    text => text.Text == "Now reads");
                var reconfirm = Assert.Single(uncertainRow.GetVisualDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == uncertain.CheckAgainAutomationName);
                reconfirm.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
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
                var kitabuRow = Assert.Single(window.GetVisualDescendants().OfType<WordPresentationRow>(),
                    row => row.Data?.Facts.Word == "kitabu");
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
                Assert.DoesNotContain("✓", visibleTexts);
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
    public void CaptureRetirementProposalReview()
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
                workspace.CurrentPage = WorkspacePage.Review;
                var review = workspace.PageModel<ReviewPageModel>();
                PageScreenshots.Settle(window);
                review.ShowRetirementReview(RetirementProjection(includeUnresolved: false));
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
                Assert.Contains("The sound rule was traced for this reading", visibleTexts);
                Assert.Contains("Resolved", visibleTexts);
                SaveAll(window, folder, "retirement-review-reading-evidence");

                review.ShowRetirementReview(RetirementProjection(includeUnresolved: true));
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

    private static void SaveAll(MainWindow window, string folder, string name)
    {
        foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
        {
            Application.Current!.RequestedThemeVariant = variant;
            foreach (var width in new[] { 1040, 1240 })
            {
                window.Width = width;
                window.Height = 780;
                Save(window, Path.Combine(folder, $"{name}-{width}-{theme}.png"));
            }
        }
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

    private static async Task<(WorkspaceShellViewModel Workspace, MainWindow Window)> OpenAsync(FakeCommandClient fake)
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

    private static PendingChangesSnapshot Blocked()
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

    private static PendingChange Change(string word, string kind, string? storedAnalysisId, ReviewAnalysis analysis) =>
        new(word, "wordform/" + word, word, kind, "assessment/one", word, ["operation/" + word])
        {
            Analyses = [analysis],
            OriginPage = WorkspacePage.Texts.ToString(),
            StoredAnalysisId = storedAnalysisId,
        };

    private static ReviewAnalysis Stored(params (string Form, string Gloss)[] morphs) =>
        new(Reading(morphs), ReadingGrade.Candidate, true, true);

    private static ParserReading Reading(params (string Form, string Gloss)[] morphs) =>
        new(morphs.Select(morph => new ParserReadingMorph(morph.Form, morph.Gloss, "n", null, false, null)).ToArray());

    private static BaselineToken Token() =>
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
