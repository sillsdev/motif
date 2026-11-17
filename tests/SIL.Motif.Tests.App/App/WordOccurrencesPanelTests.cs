using Avalonia;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.Controls;
using SIL.Motif.App.Services;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views.Words;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.Texts;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class WordOccurrencesPanelTests
{
    private static OccurrenceAnchor Anchor(int index) => new(Guid.Parse("00000000-0000-0000-0000-000000000001"),
        Guid.Parse("00000000-0000-0000-0000-000000000002"), Guid.Parse("00000000-0000-0000-0000-000000000003"), index);

    private sealed class Source(int count, string? writingSystem = null) : IWordOccurrenceSource
    {
        public int Count { get; } = count;

        public Task<IReadOnlyList<OccurrenceChoiceViewModel>> ReadPageAsync(int offset, int size, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<OccurrenceChoiceViewModel>>(
                Enumerable.Range(offset, Math.Max(0, Math.Min(size, Count - offset)))
                    .Select(i => new OccurrenceChoiceViewModel(Anchor(i), "Text", i, "sentence " + i,
                        InterlinearAnalysisStatus.Unapproved, "analysis") { SentenceWritingSystem = writingSystem }).ToArray());
    }

    private sealed class Actions : IWordOccurrenceActions
    {
        public List<OccurrenceAnchor> Staged { get; } = [];

        public Task<IReadOnlyList<OccurrenceAnchor>> StageKeepAsync(WordReadingOption reading,
            IReadOnlyList<OccurrenceChoiceViewModel> places, CancellationToken token)
        {
            Staged.AddRange(places.Select(place => place.Anchor));
            return Task.FromResult<IReadOnlyList<OccurrenceAnchor>>(places.Select(place => place.Anchor).ToArray());
        }

        public Task<bool> StageNotAWordAsync(string form, CancellationToken token) => Task.FromResult(true);

    }

    private static (WordOccurrencesViewModel Model, Actions Actions) NewModel(int count)
    {
        var actions = new Actions();
        return (new WordOccurrencesViewModel("wa", [new WordReadingOption("a1", "one", true)], new Source(count), actions), actions);
    }

    private static void Run(int count, Action<Window, WordOccurrencesPanel, WordOccurrencesViewModel, Actions> check,
        double width = 420, double height = 700) =>
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (model, actions) = NewModel(count);
            await model.LoadPageAsync(0);
            var panel = new WordOccurrencesPanel(model);
            var window = new MotifTestWindow { Width = width, Height = height, Content = panel };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                check(window, panel, model, actions);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(45));

    private static IEnumerable<CheckBox> PlaceBoxes(Visual root) => root.GetVisualDescendants().OfType<CheckBox>()
        .Where(box => (box.Name ?? string.Empty) != "" || AutomationProperties_Id(box).StartsWith("motif-word-occurrence-place-"));

    private static string AutomationProperties_Id(Control control) =>
        Avalonia.Automation.AutomationProperties.GetAutomationId(control) ?? string.Empty;

    private sealed class RefusingActions : IWordOccurrenceActions
    {
        public Task<IReadOnlyList<OccurrenceAnchor>> StageKeepAsync(WordReadingOption reading,
            IReadOnlyList<OccurrenceChoiceViewModel> places, CancellationToken token) =>
            throw new InvalidOperationException("The reader refused.");

        public Task<bool> StageNotAWordAsync(string form, CancellationToken token) => Task.FromResult(true);

    }

    private sealed class RefusingSource(int count) : IWordOccurrenceSource
    {
        private readonly Source _inner = new(count);
        public bool Refuse { get; set; }
        public int Count => _inner.Count;

        public Task<IReadOnlyList<OccurrenceChoiceViewModel>> ReadPageAsync(int offset, int size, CancellationToken token) =>
            Refuse ? throw new InvalidOperationException("The reader refused.") : _inner.ReadPageAsync(offset, size, token);
    }

    [Fact]
    public void ARefusedKeepLeavesTheAppRunningAndShowsTheSentence()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var model = new WordOccurrencesViewModel("wa", [new WordReadingOption("a1", "one", true)], new Source(3),
                new RefusingActions());
            await model.LoadPageAsync(0);
            model.CheckShownGaps();

            // ExecuteAsync would surface any exception the command let out; none may.
            await ((AsyncRelayCommand)model.ApproveCommand).ExecuteAsync(null);

            Assert.Equal(MotifAppStrings.OccurrencesNotStaged, model.StatusSentence);
            Assert.False(model.IsBusy);
            Assert.Equal(3, model.SelectedCount);
        }, TimeSpan.FromSeconds(45));
    }

    [Fact]
    public void AReaderRefusalWhileReadingCheckedPlacesOrPagingShowsTheSentenceAndNeverEscapesTheCommand()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var source = new RefusingSource(30);
            var model = new WordOccurrencesViewModel("wa", [new WordReadingOption("a1", "one", true)], source, new Actions());
            await model.LoadPageAsync(0);
            model.SetChecked(Anchor(25), true);
            source.Refuse = true;

            await ((AsyncRelayCommand)model.ApproveCommand).ExecuteAsync(null);
            Assert.Equal(MotifAppStrings.OccurrencesNotStaged, model.StatusSentence);

            model.StatusSentence = null;
            await ((AsyncRelayCommand)model.NextPageCommand).ExecuteAsync(null);
            Assert.Equal("The reader refused.", model.StatusSentence);
            Assert.Equal(0, model.PageOffset);
        }, TimeSpan.FromSeconds(45));
    }

    private sealed class FailingActions : IWordOccurrenceActions
    {
        public Task<IReadOnlyList<OccurrenceAnchor>> StageKeepAsync(WordReadingOption reading,
            IReadOnlyList<OccurrenceChoiceViewModel> places, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<OccurrenceAnchor>>([]);

        public Task<bool> StageNotAWordAsync(string form, CancellationToken token) =>
            throw new IOException("The host failed.");

    }

    private sealed class FailingSource(int count) : IWordOccurrenceSource
    {
        private readonly Source _inner = new(count);
        public bool Fail { get; set; }
        public int Count => _inner.Count;

        public Task<IReadOnlyList<OccurrenceChoiceViewModel>> ReadPageAsync(int offset, int size, CancellationToken token) =>
            Fail ? throw new IOException("The host failed.") : _inner.ReadPageAsync(offset, size, token);
    }

    [Fact]
    public void AHostFailureInNotAWordIsASentenceAndNeverEscapesTheCommand()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var model = new WordOccurrencesViewModel("wa", [new WordReadingOption("a1", "one", true)], new Source(3),
                new FailingActions());
            await model.LoadPageAsync(0);

            // ExecuteAsync would surface any exception the command let out; none may.
            await ((AsyncRelayCommand)model.NotAWordCommand).ExecuteAsync(null);
            Assert.Equal(MotifAppStrings.OccurrencesNotStaged, model.StatusSentence);
            Assert.False(model.IsBusy);
        }, TimeSpan.FromSeconds(45));
    }

    [Fact]
    public void APagingFailureSaysThePlacesCouldNotBeReadNotThatAChangeWasNotStaged()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var source = new FailingSource(30);
            var model = new WordOccurrencesViewModel("wa", [new WordReadingOption("a1", "one", true)], source, new Actions());
            await model.LoadPageAsync(0);
            source.Fail = true;

            await ((AsyncRelayCommand)model.NextPageCommand).ExecuteAsync(null);

            Assert.Equal(MotifAppStrings.OccurrencesPageNotRead, model.StatusSentence);
            Assert.NotEqual(MotifAppStrings.OccurrencesNotStaged, model.StatusSentence);
            Assert.Equal(0, model.PageOffset);
        }, TimeSpan.FromSeconds(45));
    }

    [Fact]
    public void ClickingOneTickOffStagesTheOtherAnchorsThroughTheRenderedPanel()
    {
        Run(3, (window, panel, model, actions) =>
        {
            model.CheckShownGaps();
            var boxes = PlaceBoxes(panel).ToList();
            Assert.Equal(3, boxes.Count);
            HeadlessClick.Click(window, boxes[1], "Include This Place");
            Assert.Equal(2, model.SelectedCount);
            var approve = panel.GetVisualDescendants().OfType<Button>()
                .Single(button => AutomationProperties_Id(button) == AutomationIds.WordOccurrencesApprove);
            HeadlessClick.Click(window, approve, "Approve");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            PageScreenshots.Settle(window);
            Assert.Equal([Anchor(0), Anchor(2)], actions.Staged.OrderBy(a => a.Index));
            Assert.False(model.Page[1].IsStaged);
            Assert.True(model.Page[0].IsStaged);
        });
    }

    [Fact]
    public void ARowCountOverOnePageRealizesAtMostOnePageOfPlaces()
    {
        Run(500, (_, panel, model, _) =>
        {
            Assert.True(model.Page.Count <= 20);
            Assert.True(PlaceBoxes(panel).Count() <= 20);
            Assert.True(model.HasNextPage);
        });
    }

    [Fact]
    public void TheCheckBoxesAndActionsAreReachableByTabInReadingOrder()
    {
        Run(3, (window, panel, _, _) =>
        {
            var focusable = panel.GetVisualDescendants().OfType<Control>()
                .Where(c => c.Focusable && c.IsEffectivelyVisible && c.IsEnabled && c is Button or CheckBox or ComboBox)
                .ToList();
            Assert.Contains(focusable, c => AutomationProperties_Id(c) == AutomationIds.WordOccurrencesApprove);
            // One reading is named in the summary and already chosen; the chooser appears only with several.
            Assert.DoesNotContain(focusable, c => AutomationProperties_Id(c) == AutomationIds.WordOccurrencesReading);
            var first = PlaceBoxes(panel).First();
            first.Focus();
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Assert.True(first.IsKeyboardFocusWithin || first.IsFocused);
        });
    }

    [Fact]
    public void ARightToLeftSentenceFlipsItsPlaceRowSoTheCheckBoxSitsAtTheRight()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var styles = new TextStyles();
            styles.ReplaceContext([WritingSystemTestData.Arabic]);
            var model = new WordOccurrencesViewModel("wa", [new WordReadingOption("a1", "one", true)],
                new Source(2, SeededProject.RightToLeftTag), new Actions());
            await model.LoadPageAsync(0);
            var panel = new WordOccurrencesPanel(model);
            SIL.Motif.App.Controls.WritingSystemText.SetResolver(panel, styles);
            var window = new MotifTestWindow { Width = 420, Height = 700, Content = panel };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var box = PlaceBoxes(panel).First();
                var row = Assert.IsType<Grid>(box.GetVisualParent());
                Assert.Equal(FlowDirection.RightToLeft, row.FlowDirection);
                var seen = new Rect(box.Bounds.Size).TransformToAABB(box.TransformToVisual(window)!.Value);
                Assert.True(seen.Left > window.ClientSize.Width / 2,
                    $"The check box is not on the right of an RTL row: {seen} in a {window.ClientSize} window.");
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(45));
    }

    [Fact]
    public void ANarrowZoomedFrameKeepsEveryActionInsideThePanel()
    {
        Run(3, (window, panel, _, _) =>
        {
            window.Width = 640 / 1.5;
            PageScreenshots.Settle(window);
            foreach (var button in panel.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible))
            {
                var origin = button.TranslatePoint(default, panel)!.Value;
                Assert.True(origin.X >= -0.5 && origin.X + button.Bounds.Width <= panel.Bounds.Width + 0.5,
                    $"A button leaves the panel at {origin}, width {button.Bounds.Width}, panel {panel.Bounds}.");
            }
        }, width: 427);
    }
}
