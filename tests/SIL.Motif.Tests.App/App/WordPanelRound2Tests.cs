using Avalonia;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.Texts;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views.Words;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Round 2: one place to choose a reading, an Approve that says what to do first, Not a Word apart from it, one body size.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "Unit")]
public sealed class WordPanelRound2Tests
{
    private static OccurrenceAnchor Anchor(int index) => new(Guid.Parse("00000000-0000-0000-0000-000000000001"),
        Guid.Parse("00000000-0000-0000-0000-000000000002"), Guid.Parse("00000000-0000-0000-0000-000000000003"), index);

    private sealed class Source(int count) : IWordOccurrenceSource
    {
        public int Count { get; } = count;

        public Task<IReadOnlyList<OccurrenceChoiceViewModel>> ReadPageAsync(int offset, int size, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<OccurrenceChoiceViewModel>>(
                Enumerable.Range(offset, Math.Max(0, Math.Min(size, Count - offset)))
                    .Select(i => new OccurrenceChoiceViewModel(Anchor(i), "Text", i, "sentence " + i,
                        InterlinearAnalysisStatus.Unapproved, "analysis")).ToArray());
    }

    private sealed class Actions : IWordOccurrenceActions
    {
        public Task<IReadOnlyList<OccurrenceAnchor>> StageKeepAsync(WordReadingOption reading,
            IReadOnlyList<OccurrenceChoiceViewModel> places, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<OccurrenceAnchor>>(places.Select(place => place.Anchor).ToArray());

        public Task<bool> StageNotAWordAsync(string form, CancellationToken token) => Task.FromResult(true);

        public Task<string> AskAsync(string form, CancellationToken token) => Task.FromResult("asked");
    }

    private static void Show(int places, int readings, Action<Window, WordOccurrencesPanel, WordOccurrencesViewModel> check) =>
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var options = Enumerable.Range(1, readings).Select(i => new WordReadingOption("a" + i, "reading " + i, true)).ToArray();
            var model = new WordOccurrencesViewModel("wa", options, new Source(places), new Actions());
            await model.LoadPageAsync(0);
            var panel = new WordOccurrencesPanel(model);
            var window = new MotifTestWindow { Width = 420, Height = 800, Content = panel };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                check(window, panel, model);
            }
            finally { window.Close(); }
        }, TimeSpan.FromSeconds(45));

    private static IEnumerable<string> Texts(Visual root) => root.GetVisualDescendants().OfType<TextBlock>()
        .Where(block => block.IsEffectivelyVisible && !string.IsNullOrEmpty(block.Text)).Select(block => block.Text!);

    private static Button ById(Visual root, string id) => Assert.Single(root.GetVisualDescendants().OfType<Button>(),
        button => Avalonia.Automation.AutomationProperties.GetAutomationId(button) == id);

    [Fact]
    public void ApproveSaysToChooseAReadingNeverAZeroCount()
    {
        Show(3, 3, (_, panel, model) =>
        {
            var approve = ById(panel, AutomationIds.WordOccurrencesApprove);
            Assert.False(approve.IsEffectivelyEnabled);
            Assert.Equal(MotifAppStrings.OccurrencesApproveChooseReading, model.ApproveLabel);
            Assert.Contains(MotifAppStrings.OccurrencesApproveChooseReading,
                approve.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text));
            Assert.DoesNotContain(Texts(panel), text => text.Contains(" 0 Places", StringComparison.Ordinal) && text.StartsWith("Approve", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void ApproveAsksForAPlaceOnceAReadingIsChosenAndNoneIsChecked()
    {
        Show(3, 3, (window, panel, model) =>
        {
            model.ChosenReading = model.Readings[0];
            PageScreenshots.Settle(window);
            var approve = ById(panel, AutomationIds.WordOccurrencesApprove);
            Assert.False(approve.IsEffectivelyEnabled);
            Assert.Contains(MotifAppStrings.OccurrencesApproveCheckPlace,
                approve.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text));
            model.CheckShownGaps();
            PageScreenshots.Settle(window);
            Assert.True(approve.IsEffectivelyEnabled);
            Assert.Contains("Approve 3 Places", approve.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text));
        });
    }

    [Fact]
    public void ThereIsOneControlForChoosingAReading()
    {
        Show(2, 3, (_, panel, _) =>
        {
            var chooser = Assert.Single(panel.GetVisualDescendants().OfType<ComboBox>());
            Assert.Equal(AutomationIds.WordOccurrencesReading, Avalonia.Automation.AutomationProperties.GetAutomationId(chooser));
            Assert.Empty(panel.GetVisualDescendants().OfType<RadioButton>());
            Assert.Empty(panel.GetVisualDescendants().OfType<ListBox>());
        });
    }

    [Fact]
    public void NotAWordIsNotBesideApprove()
    {
        Show(3, 3, (window, panel, _) =>
        {
            var approve = ById(panel, AutomationIds.WordOccurrencesApprove);
            var notAWord = ById(panel, AutomationIds.WordOccurrencesNotAWord);
            Assert.True(notAWord.IsEffectivelyVisible);
            Assert.NotSame(approve.GetVisualParent(), notAWord.GetVisualParent());
            var approveTop = approve.TranslatePoint(default, panel)!.Value.Y;
            var notTop = notAWord.TranslatePoint(default, panel)!.Value.Y;
            Assert.True(Math.Abs(approveTop - notTop) > approve.Bounds.Height,
                $"Not a Word ({notTop}) shares a row with Approve ({approveTop}).");
        });
    }

    [Fact]
    public void ApproveCarriesThePrimaryStyleClass()
    {
        Show(2, 1, (_, panel, _) => Assert.Contains("primary", ById(panel, AutomationIds.WordOccurrencesApprove).Classes));
    }

    [Fact]
    public void AWordWithNoReadingStillShowsNoZeroCountApprove()
    {
        Show(2, 0, (_, panel, model) =>
        {
            Assert.Equal(MotifAppStrings.OccurrencesApproveNothing, model.ApproveLabel);
            Assert.False(ById(panel, AutomationIds.WordOccurrencesApprove).IsEffectivelyEnabled);
        });
    }

    [Fact]
    public void ThePlaceRowDrawsItsLabelAndSentenceAtOneSize()
    {
        Show(2, 1, (_, panel, _) =>
        {
            var label = panel.GetVisualDescendants().OfType<TextBlock>().First(block => block.Classes.Contains("occurrencePlaceLabel"));
            var sentence = panel.GetVisualDescendants().OfType<TextBlock>().First(block => block.Classes.Contains("occurrenceSentence"));
            Assert.Equal(label.FontSize, sentence.FontSize);
        });
    }
}
