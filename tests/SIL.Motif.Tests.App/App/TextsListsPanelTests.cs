using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Xunit;
using WordRow = SIL.Motif.App.Views.WordRow;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TextsListsPanelTests(AvaloniaHeadlessFixture avalonia)
{
    [Fact]
    public void EachListedWordIsTheOneWordRow_WithItsOutcome_AndNoMeaningTheListAlreadyNames()
    {
        avalonia.Invoke(() =>
        {
            var compare = new CompareViewModel();
            compare.Load([
                Word("approved-empty", "no-analysis", ProjectStanding.Approved),
                Word("approved-timeout", "timed-out", ProjectStanding.Approved),
                Word("candidate-capped", "capped", ProjectStanding.Candidate),
            ]);
            var lists = new TextsListsViewModel(compare);
            var panel = new TextsListsPanel(lists);
            var window = new Window { Content = panel, Width = 1200, Height = 800 };
            try
            {
                window.Show();
                lists.SelectListCommand.Execute(lists.Lists.Single(list => list.Name == "Stopped"));
                window.UpdateLayout();

                var box = panel.GetLogicalDescendants().OfType<ListBox>()
                    .Single(list => AutomationProperties.GetName(list) == "Words in the selected list");
                var rows = box.GetVisualDescendants().OfType<WordRow>().ToArray();
                Assert.Equal(["approved-timeout", "candidate-capped"], rows.Select(row => row.Row!.Word).Order());
                Assert.All(rows, row =>
                {
                    Assert.Equal("lists", row.List);
                    Assert.True(row.ShowsTick);
                    var chips = row.GetVisualDescendants().OfType<MarkChip>().Where(chip => chip.IsEffectivelyVisible)
                        .Select(chip => chip.Mark?.Kind).ToArray();
                    Assert.Contains(MarkKind.Outcome, chips);
                    Assert.DoesNotContain(MarkKind.Meaning, chips);
                });
                Assert.Single(panel.GetVisualDescendants().OfType<WordRowHeader>());
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TickingARowTicksTheWordForTheListsHandoff()
    {
        avalonia.Invoke(() =>
        {
            var compare = new CompareViewModel();
            compare.Load([Word("approved-empty", "no-analysis", ProjectStanding.Approved)]);
            var lists = new TextsListsViewModel(compare);
            var window = new Window { Content = new TextsListsPanel(lists), Width = 1200, Height = 800 };
            try
            {
                window.Show();
                window.UpdateLayout();

                var row = window.GetVisualDescendants().OfType<WordRow>().Single();
                row.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked = true;

                Assert.True(compare.Words.Single().IsChecked);
                Assert.Equal(WindowCopy.AiHandoffForThisWord, lists.HandOffCheckedWordsLabel);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ListsHideEmptyChoicesAndNameHandoffsByTheirScope()
    {
        avalonia.Invoke(() =>
        {
            var compare = new CompareViewModel();
            compare.Load([Word("approved-empty", "no-analysis", ProjectStanding.Approved)]);
            var lists = new TextsListsViewModel(compare) { HandOff = _ => { } };
            var panel = new TextsListsPanel(lists);
            var window = new Window { Content = panel, Width = 1200, Height = 800 };
            try
            {
                window.Show();
                window.UpdateLayout();

                var visibleLists = panel.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.IsEffectivelyVisible && button.DataContext is TextsListDefinitionViewModel)
                    .Select(button => ((TextsListDefinitionViewModel)button.DataContext!).Name).ToArray();
                Assert.Equal(lists.Lists.Where(list => list.HasWords).Select(list => list.Name), visibleLists);
                Assert.Equal(WindowCopy.AiHandoffForThisList, lists.HandOffListLabel);
                Assert.Equal(WindowCopy.AiHandoff, lists.HandOffCheckedWordsLabel);
                Assert.Equal(WindowCopy.TickWordsFirst, lists.HandOffCheckedWordsHelpText);

                var checkedWords = panel.GetVisualDescendants().OfType<Button>()
                    .Single(button => ReferenceEquals(button.Command, lists.HandOffCheckedWordsCommand));
                Assert.Equal(lists.HandOffCheckedWordsHelpText, ToolTip.GetTip(checkedWords));
                Assert.True(ToolTip.GetShowOnDisabled(checkedWords));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(1040)]
    [InlineData(1240)]
    public void TheHeaderIsOneLine_AndTheListsControlsAreOneRow_WithNoNotesBesideThem(int width)
    {
        avalonia.Invoke(() =>
        {
            var compare = new CompareViewModel { Rerun = (_, _) => Task.CompletedTask };
            compare.Load([
                Word("approved-timeout", "timed-out", ProjectStanding.Approved),
                Word("candidate-capped", "capped", ProjectStanding.Candidate),
            ]);
            var lists = new TextsListsViewModel(compare) { HandOff = _ => { } };
            var panel = new TextsListsPanel(lists);
            var window = new Window { Content = panel, Width = width - SidebarWidth, Height = 800 };
            try
            {
                window.Show();
                lists.SelectListCommand.Execute(lists.Lists.Single(list => list.Name == "Stopped"));
                window.UpdateLayout();

                var header = panel.FindControl<DockPanel>("ListHeader")!;
                var lines = header.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).ToArray();
                Assert.Equal(["Stopped", "2 words · 2 places", lists.SelectedList!.Sentence], lines.Select(text => text.Text));
                var lineHeight = lines.Max(text => text.Bounds.Height);
                Assert.True(header.Bounds.Height <= lineHeight + 1, $"The header is {header.Bounds.Height} high, not one line");

                var controls = panel.FindControl<DockPanel>("ListControls")!;
                var shown = controls.GetVisualDescendants().OfType<Control>()
                    .Where(control => control is Button or ComboBox && control.IsEffectivelyVisible).ToArray();
                Assert.Equal(4, shown.Length);
                var middles = shown.Select(control =>
                    control.TranslatePoint(new Point(0, control.Bounds.Height / 2), controls)!.Value.Y).ToArray();
                Assert.All(middles, middle => Assert.InRange(middle, middles[0] - 1, middles[0] + 1));
                Assert.Contains(shown, control => control is Button { Content: "Parse again" });

                var notes = new[]
                {
                    "Opinions change one analysis at a time, in the text.",
                    lists.HandOffCheckedWordsHelpText,
                    lists.Compare.CheckedWordText,
                };
                Assert.DoesNotContain(panel.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.IsEffectivelyVisible && notes.Contains(text.Text));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void OnAListOfOneMeaning_TheMeaningColumnAndItsHeadAreHidden_AndTheOtherColumnsKeepTheirOrder()
    {
        avalonia.Invoke(() =>
        {
            var compare = new CompareViewModel();
            compare.Load([
                Word("approved-timeout", "timed-out", ProjectStanding.Approved),
                Word("candidate-empty", "no-analysis", ProjectStanding.Candidate),
                Word("candidate-other", "analysed", ProjectStanding.Candidate, parsedAs: "other"),
            ]);
            var lists = new TextsListsViewModel(compare);
            var panel = new TextsListsPanel(lists);
            var window = new Window { Content = panel, Width = 1200, Height = 800 };
            try
            {
                window.Show();
                lists.SelectListCommand.Execute(lists.Lists.Single(list => list.Name == "Stopped"));
                window.UpdateLayout();

                Assert.Equal(["Word", "FieldWorks", "PanGloss", "Places", "Time", "Next"], Heads(panel));
                Assert.All(panel.GetVisualDescendants().OfType<WordRow>(), row =>
                {
                    Assert.False(row.ShowsMeaning);
                    Assert.DoesNotContain(row.GetVisualDescendants().OfType<MarkChip>(),
                        chip => chip.IsEffectivelyVisible && chip.Mark?.Kind == MarkKind.Meaning);
                });

                lists.SelectListCommand.Execute(lists.Lists.Single(list => list.Name == "Have a look"));
                window.UpdateLayout();

                Assert.Equal(["Word", "FieldWorks", "PanGloss", "Meaning", "Places", "Time", "Next"], Heads(panel));
                Assert.All(panel.GetVisualDescendants().OfType<WordRow>(), row => Assert.Contains(
                    row.GetVisualDescendants().OfType<MarkChip>(), chip => chip.IsEffectivelyVisible && chip.Mark?.Kind == MarkKind.Meaning));
            }
            finally
            {
                window.Close();
            }
        });
    }

    // The visible column heads in reading order, left to right.
    private static string[] Heads(Control panel) =>
        panel.GetVisualDescendants().OfType<WordRowHeader>().Single().GetVisualDescendants().OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && text.Classes.Contains("wordRowHeading") && text.Text is { Length: > 1 })
            .OrderBy(text => text.TranslatePoint(default, panel)!.Value.X).Select(text => text.Text!).ToArray();

    [Fact]
    public void AnOpenedRowIsTheListCard_AndNothingElse()
    {
        avalonia.Invoke(() =>
        {
            var compare = new CompareViewModel();
            compare.Load([Word("approved-empty", "no-analysis", ProjectStanding.Approved)]);
            var lists = new TextsListsViewModel(compare);
            var window = new Window { Content = new TextsListsPanel(lists), Width = 1200, Height = 800 };
            try
            {
                window.Show();
                compare.Words.Single().IsExpanded = true;
                window.UpdateLayout();

                var card = Assert.Single(window.GetVisualDescendants().OfType<WordRowCard>());
                Assert.True(card.IsEffectivelyVisible);
                Assert.Empty(window.GetVisualDescendants().OfType<ListWordCard>());
                var texts = card.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible)
                    .Select(text => text.Text).Distinct().ToArray();
                Assert.Contains("FieldWorks", texts);
                Assert.Contains("PanGloss", texts);
                Assert.Contains(texts, text => text?.StartsWith("FieldWorks holds no analysis", StringComparison.Ordinal) == true);
                Assert.Contains("No parse", texts);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void InTheListCard_KulSitsOverKuAndL_AndThoseTwoAreBlue()
    {
        avalonia.Invoke(() =>
        {
            var card = new ListWordCard { DataContext = new WordRowViewModel(Alikula()) };
            var window = new Window { Content = card, Width = 900, Height = 400, RequestedThemeVariant = ThemeVariant.Light };
            try
            {
                window.Show();
                window.UpdateLayout();

                var chips = card.GetVisualDescendants().OfType<Border>()
                    .Where(border => border.Classes.Contains("listCardMorph")).ToArray();
                string Form(Border chip) => chip.GetVisualDescendants().OfType<TextBlock>().First().Text!;
                int Line(Border chip) => Grid.GetRow(Span(chip));
                Border Chip(int line, string form) => chips.Single(chip => Line(chip) == line && Form(chip) == form);
                double Left(Control control) => control.TranslatePoint(default, card)!.Value.X;
                double Right(Control control) => Left(control) + control.Bounds.Width;

                foreach (var form in new[] { "a-", "li-", "-a" })
                    Assert.Equal(Left(Chip(0, form)), Left(Chip(1, form)), 1);
                var kulSpan = Span(Chip(0, "kul"));
                Assert.Contains("parted", kulSpan.Classes);
                Assert.Equal(Left(Span(Chip(1, "ku-"))), Left(kulSpan), 1);
                Assert.True(Right(kulSpan) >= Right(Chip(1, "l")) - 1, "kul's edge stops short of the pieces it is read as");
                Assert.Equal(["ku-", "l"], chips.Where(chip => Line(chip) == 1 && chip.Classes.Contains("different")).Select(Form));
                Assert.DoesNotContain(chips, chip => Line(chip) == 0 && chip.Classes.Contains("different"));
                var blue = (ISolidColorBrush)Chip(1, "ku-").GetVisualDescendants().OfType<TextBlock>().First().Foreground!;
                Assert.True(card.TryFindResource("Intent.Outcome.Different", ThemeVariant.Light, out var expected));
                Assert.Equal(((ISolidColorBrush)expected!).Color, blue.Color);
                Assert.True(card.TryFindResource("Intent.Text", ThemeVariant.Light, out var plain));
                var linkedForm = (ISolidColorBrush)Chip(1, "a-").GetVisualDescendants().OfType<TextBlock>().First().Foreground!;
                Assert.Equal(((ISolidColorBrush)plain!).Color, linkedForm.Color);
                Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.Text == "PanGloss reads FieldWorks' kul 'eat' as ku- 'INF' + l 'eat'. Every other morpheme matches.");
            }
            finally
            {
                window.Close();
            }
        });
    }

    private const int SidebarWidth = 220;

    private static Border Span(Border chip) =>
        chip.GetVisualAncestors().OfType<Border>().First(border => border.Classes.Contains("listCardSpan"));

    private static ParserReadingMorph Morph(string form, string gloss) =>
        new(form, gloss, "v", null, false, "silfw://localhost/link?tool=lexiconEdit")
        {
            AllomorphId = "form-" + form,
            GrammaticalInfoId = "msa-" + form,
        };

    private static SIL.Motif.Contract.Responses.WordRow Alikula()
    {
        IReadOnlyList<ParserReadingMorph> fieldWorks = [Morph("a-", "3SG"), Morph("li-", "PST"), Morph("kul", "eat"), Morph("-a", "FV")];
        IReadOnlyList<ParserReadingMorph> panGloss =
            [Morph("a-", "3SG"), Morph("li-", "PST"), Morph("ku-", "INF"), Morph("l", "eat"), Morph("-a", "FV")];
        return new("alikula", WordRowOutcome.Different, "Built something else", WordRowTone.Problem)
        {
            Opinion = ProjectStanding.Approved,
            FieldWorksMorphemes = fieldWorks,
            PanGlossMorphemes = panGloss,
            DifferingPositions = WordRowProjection.DifferingPositions(fieldWorks, panGloss),
            PanGlossReadingCount = 1,
        };
    }

    private static AssessWordRowViewModel Word(string form, string outcome, string standing, string? parsedAs = null) =>
        new(new AssessmentWordResult(form, outcome, outcome is "timed-out" or "capped", "Search completed", 1, null)
        {
            ProjectStanding = standing,
            OccurrenceCount = 1,
            ReadingGrades = parsedAs is null ? null : [ReadingGrade.NoOpinion],
            Readings = parsedAs is null ? null : [new ParserReading([])],
            Morphology = parsedAs is null ? null : new ParseWordEvidence("v1", 0, form, 1, false, false, false,
                [new ParseAnalysis([new ParseMorph(parsedAs, "n", null, null)])], []),
        });
}
