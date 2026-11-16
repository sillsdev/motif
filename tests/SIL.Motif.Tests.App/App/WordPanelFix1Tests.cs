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

/// <summary>What a person reads on the word panel and the Words page: button label, counts, readings, column heading.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "Unit")]
public sealed class WordPanelFix1Tests
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
    public void TheApproveButtonReadsItsLabelNotTheNameOfAControl()
    {
        Show(3, 1, (window, panel, model) =>
        {
            model.CheckShownGaps();
            PageScreenshots.Settle(window);
            var approve = ById(panel, AutomationIds.WordOccurrencesApprove);
            var shown = approve.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text).ToArray();
            Assert.Contains(PluralText.Format(MotifAppStrings.OccurrencesApprovePattern, 3), shown);
            Assert.DoesNotContain(shown, text => text?.Contains("Avalonia.Controls", StringComparison.Ordinal) == true);
        });
    }

    [Fact]
    public void OnePlaceIsNamedInTheSingular()
    {
        Show(1, 1, (window, panel, model) =>
        {
            model.CheckShownGaps();
            PageScreenshots.Settle(window);
            var texts = Texts(panel).ToArray();
            Assert.Contains("1 Place in Your Texts", texts);
            Assert.Contains("1 Place Checked", texts);
            Assert.Contains("Approve 1 Place", texts);
            Assert.DoesNotContain(texts, text => text.Contains("1 Places", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void SeveralReadingsAreChosenInOnePlaceAndSummarisedElsewhere()
    {
        Show(2, 3, (_, panel, _) =>
        {
            Assert.Contains("3 readings", Texts(panel));
            var chooser = Assert.Single(panel.GetVisualDescendants().OfType<ComboBox>());
            Assert.True(chooser.IsEffectivelyVisible);
        });
    }

    [Fact]
    public void OneReadingIsNamedNotOfferedAsAChoice()
    {
        Show(2, 1, (_, panel, _) =>
        {
            Assert.Contains("reading 1", Texts(panel));
            Assert.DoesNotContain(panel.GetVisualDescendants().OfType<ComboBox>(), box => box.IsEffectivelyVisible);
        });
    }

    [Fact]
    public void TheWordsTimeColumnIsHeadedAsParserSteps()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await AnalyzeTextsLayoutTests.OpenAnalyzeTexts(seedReaderToken: false);
            try
            {
                workspace.CurrentPage = WorkspacePage.Words;
                PageScreenshots.Settle(window);
                var heading = window.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(block =>
                    Avalonia.Automation.AutomationProperties.GetAutomationId(block) == AutomationIds.ForWordHeading("Time"));
                Assert.NotNull(heading);
                Assert.Equal(MotifAppStrings.WordsHeadingParserSteps, heading!.Text);
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "Time");
            }
            finally { window.Close(); await workspace.DisposeAsync(); }
        }, TimeSpan.FromMinutes(3));
    }
}
