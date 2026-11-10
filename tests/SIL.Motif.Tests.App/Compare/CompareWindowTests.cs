using System.Text.RegularExpressions;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using SIL.Motif.App.Compare;
using SIL.Motif.Runner.Operations;
using Xunit;

namespace SIL.Motif.Tests.App.Compare;

/// <summary>The window that compares two versions of a field, headless, with invented placeholder text (ADR 0076).</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class CompareWindowTests(AvaloniaHeadlessFixture avalonia)
{
    private static TextRun R(string text) => new(text, "en", null, TextProps.None);

    private static ParagraphVersion P(string key, string text) => new(key, "Normal", TextProps.None, [R(text)]);

    private static StartingParagraph S(string key, string text) => new(key, text, "Normal", P(key, text));

    // Paragraph one: you changed one word and FieldWorks another; paragraph two: both changed the same word.
    private static TextComparison TwoParagraphs() => new(TextSort.Sort(new TextVersions(
        [S("p1", "tavu mena kolo lasi"), S("p2", "pala noka")],
        [P("p1", "tavu sipa kolo lasi"), P("p2", "pala vesi")],
        [P("p1", "tavu mena kolo rupa"), P("p2", "pala tumo")])));

    // Both added a word at the same place, so the two can be used together.
    private static TextComparison SameInsertionPoint() => new(TextSort.Sort(new TextVersions(
        [S("p1", "tavu kolo")], [P("p1", "tavu sipa kolo")], [P("p1", "tavu rupa kolo")])));

    private static CompareWindow Opened(TextComparison comparison, out IDisposable closer)
    {
        var window = new CompareWindow("GDR-1  Word boundaries, Discussion", comparison);
        closer = window.CloseOnExit();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static TextChange BothChange(CompareWindow window) => window.Comparison.Sorted.Changes.Single(change => change.Author == ChangeAuthor.Both);

    private static void Press(CompareWindow window, Key key, RawInputModifiers modifiers = RawInputModifiers.None) =>
        window.KeyPress(key, modifiers, PhysicalKey.None, null);

    private static string Result(CompareWindow window) => string.Join("\n", window.Comparison.Result().Select(paragraph => paragraph.Text));

    [Fact]
    public void EveryChangeHasAMarkAndDoneWaitsForTheOneBothMade()
    {
        avalonia.Invoke(() =>
        {
            var window = Opened(TwoParagraphs(), out var closer);
            using var _ = closer;

            Assert.Equal(window.Comparison.Sorted.Changes.Count, window.Marks.Count);
            Assert.Equal("3 changes: 1 yours · 1 from FieldWorks · 1 needs your choice", window.SummaryText);
            Assert.False(window.IsDoneEnabled);
            Assert.Equal("1 change still needs a choice (paragraph 2)", window.WaitingText);
            Assert.Equal(BothChange(window), window.Selected);
            Assert.False(window.Finish());
            Assert.Null(window.Outcome);
        });
    }

    [Fact]
    public void AltMChoosesYoursForTheFocusedChangeAndDoneThenEndsWithTheResult()
    {
        avalonia.Invoke(() =>
        {
            var window = Opened(TwoParagraphs(), out var closer);
            using var _ = closer;
            window.Select(BothChange(window));

            Press(window, Key.M, RawInputModifiers.Alt);

            Assert.Equal(Choice.Mine, window.Comparison.ChoiceFor(BothChange(window)));
            Assert.True(window.IsDoneEnabled);
            Assert.Equal("", window.WaitingText);
            Assert.True(window.Finish());
            Assert.Equal(CompareEnd.Done, window.Outcome!.End);
            Assert.Equal(["tavu sipa kolo rupa", "pala vesi"], window.Outcome.Result.Select(paragraph => paragraph.Text));
        });
    }

    [Fact]
    public void AltFSwitchesAOneSidedChangeOffAndAltMBackOn()
    {
        avalonia.Invoke(() =>
        {
            var window = Opened(TwoParagraphs(), out var closer);
            using var _ = closer;
            var yours = window.Comparison.Sorted.Changes.Single(change => change.Author == ChangeAuthor.Mine);
            window.MarkOf(yours)!.Focus();

            Press(window, Key.F, RawInputModifiers.Alt);
            Assert.StartsWith("tavu mena kolo rupa", Result(window), StringComparison.Ordinal);
            Assert.Contains("Not in use.", AutomationProperties.GetName(window.MarkOf(yours)!), StringComparison.Ordinal);

            Press(window, Key.M, RawInputModifiers.Alt);
            Assert.StartsWith("tavu sipa kolo rupa", Result(window), StringComparison.Ordinal);
            Assert.Contains("In use.", AutomationProperties.GetName(window.MarkOf(yours)!), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void AltBUsesBothInsertionsYoursFirst()
    {
        avalonia.Invoke(() =>
        {
            var window = Opened(SameInsertionPoint(), out var closer);
            using var _ = closer;
            var both = BothChange(window);
            Assert.True(both.CanUseBoth);
            window.Select(both);

            Press(window, Key.B, RawInputModifiers.Alt);

            Assert.Equal(Choice.Both, window.Comparison.ChoiceFor(both));
            Assert.Equal("tavu sipa rupa kolo", Result(window));
        });
    }

    [Fact]
    public void F8GoesToTheNextChangeThatNeedsAChoiceAndEscapeDecidesLater()
    {
        avalonia.Invoke(() =>
        {
            var window = Opened(TwoParagraphs(), out var closer);
            using var _ = closer;
            window.Select(window.Comparison.Sorted.Changes[0]);

            Press(window, Key.F8);
            Assert.Equal(BothChange(window), window.Selected);

            Press(window, Key.Escape);
            Assert.Equal(CompareEnd.DecideLater, window.Outcome!.End);
            Assert.Empty(window.Outcome.Result);
        });
    }

    [Fact]
    public void AScreenReaderHearsTheCleanTextFirstThenTheSummaryAndEachMarkSaysWhatItIs()
    {
        avalonia.Invoke(() =>
        {
            var window = Opened(TwoParagraphs(), out var closer);
            using var _ = closer;

            Assert.StartsWith("tavu sipa kolo rupa\npala vesi\n3 changes", window.ReadableText, StringComparison.Ordinal);
            var both = BothChange(window);
            Assert.Equal(ChangeWords.Announce(both, null), AutomationProperties.GetName(window.MarkOf(both)!));
            Assert.Equal("Both changed this. Yours ‘vesi’, FieldWorks ‘tumo’, paragraph 2. Needs a choice.",
                AutomationProperties.GetName(window.MarkOf(both)!));
        });
    }

    [Fact]
    public void TabGoesSummaryShowResultOpenSideBySideMoreEachChangeTheListThenDecideLaterAndDone()
    {
        avalonia.Invoke(() =>
        {
            var window = Opened(TwoParagraphs(), out var closer);
            using var _ = closer;
            window.Comparison.Choose(BothChange(window), Choice.Mine);
            var summary = window.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Focusable && block.Text == window.SummaryText);
            Assert.Equal(window.SummaryText, AutomationProperties.GetName(summary));
            summary.Focus(NavigationMethod.Tab);

            var stops = new List<string>();
            for (var pressed = 0; pressed < 40; pressed++)
            {
                var focused = window.FocusManager!.GetFocusedElement() as Control;
                var stop = focused switch
                {
                    TextBlock => "summary",
                    Button { Tag: TextChange change } mark when window.Marks.Contains(mark) => $"mark {change.Id}",
                    { Tag: TextChange } => "list",
                    ContentControl { Content: string content } => content,
                    _ => focused?.GetType().Name ?? "nothing",
                };
                if (stops.Count > 0 && stop == "summary") break;
                if (stops.Count == 0 || stops[^1] != stop || stop.StartsWith("mark", StringComparison.Ordinal)) stops.Add(stop);
                Press(window, Key.Tab);
            }

            var marks = window.Marks.Select(mark => $"mark {((TextChange)mark.Tag!).Id}");
            Assert.Equal(["summary", ChangeWords.ShowResult, ChangeWords.OpenSideBySide, "More", ..marks, "list", ChangeWords.DecideLater, ChangeWords.Done],
                stops);
        });
    }

    [Fact]
    public void DoneAResultTheFieldCannotHoldStaysOpenAndSaysWhy()
    {
        avalonia.Invoke(() =>
        {
            var window = new CompareWindow("A field of one paragraph", TwoParagraphs(),
                checkResult: result => result.Count > 1 ? "One paragraph only." : null);
            using var closer = window.CloseOnExit();
            window.Show();
            window.Comparison.Choose(BothChange(window), Choice.Mine);

            Assert.False(window.Finish());

            Assert.Null(window.Outcome);
            Assert.Equal("One paragraph only.", window.WaitingText);
            Assert.Equal(Choice.Mine, window.Comparison.ChoiceFor(BothChange(window)));
        });
    }

    [Fact]
    public void ShowResultShowsTheCleanTextWithoutMarks()
    {
        avalonia.Invoke(() =>
        {
            var window = Opened(TwoParagraphs(), out var closer);
            using var _ = closer;

            window.ShowResult = true;

            Assert.Empty(window.Marks);
            window.ShowResult = false;
            Assert.Equal(3, window.Marks.Count);
        });
    }

    [Fact]
    public void NoWordOnScreenOrForAScreenReaderSaysConflictKeepAcceptOrReject()
    {
        avalonia.Invoke(() =>
        {
            var window = Opened(TwoParagraphs(), out var closer);
            using var _ = closer;
            var said = Said(window).ToList();
            var popover = (Control)((Flyout)window.MarkOf(BothChange(window))!.Flyout!).Content!;
            said.AddRange(Said(popover));
            var more = window.GetLogicalDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == "More choices");
            said.AddRange(((MenuFlyout)more.Flyout!).ItemsSource!.OfType<MenuItem>().Select(item => (string)item.Header!));
            window.OpenSideBySide();
            said.AddRange(Said(window));

            Assert.Contains(ChangeWords.Done, said);
            Assert.Contains(ChangeWords.DecideLater, said);
            Assert.Contains(ChangeWords.UseBoth, said);
            Assert.Contains(ChangeWords.StartAgain, said);
            var banned = new Regex(@"\b(conflict|keep|accept|reject)", RegexOptions.IgnoreCase);
            Assert.DoesNotContain(said, words => banned.IsMatch(words));
        });
    }

    private static IEnumerable<string> Said(Control root)
    {
        if (root is Window window) yield return window.Title ?? "";
        foreach (var control in root.GetSelfAndLogicalDescendants().OfType<Control>()
                     .Concat(root.GetVisualDescendants().OfType<Control>()).Distinct())
        {
            if (AutomationProperties.GetName(control) is { Length: > 0 } name) yield return name;
            if (ToolTip.GetTip(control) is string tip) yield return tip;
            switch (control)
            {
                case TextBlock block:
                    yield return block.Text ?? "";
                    if (block.Inlines is { } inlines)
                        foreach (var run in inlines.OfType<Run>()) yield return run.Text ?? "";
                    break;
                case ContentControl { Content: string content }:
                    yield return content;
                    break;
                case MenuItem { Header: string header }:
                    yield return header;
                    break;
            }
        }
    }

    [Fact]
    public void SideBySideUseThisCopiesFieldWorksAcrossAndUndoPutsItBack()
    {
        avalonia.Invoke(() =>
        {
            var window = Opened(TwoParagraphs(), out var closer);
            using var _ = closer;

            window.OpenSideBySide();

            Assert.True(window.IsSideBySide);
            Assert.True(window.Width >= 1600);
            Assert.Equal(2, window.ResultFields.Count);
            Assert.Equal("tavu sipa kolo rupa", window.ResultFields[0].Editor.Document.Paragraphs[0].Text);

            window.UseThis(0);
            Assert.Equal("tavu mena kolo rupa", window.ResultFields[0].Editor.Document.Paragraphs[0].Text);
            Assert.Equal("tavu mena kolo rupa", Assert.Single(window.Comparison.RewrittenAt(0)!).Text);

            Assert.True(window.UndoUseThis(0));
            Assert.Null(window.Comparison.RewrittenAt(0));
            Assert.Equal("tavu sipa kolo rupa", window.ResultFields[0].Editor.Document.Paragraphs[0].Text);
            Assert.False(window.UndoUseThis(0));
        });
    }

    [Fact]
    public void BackToTheCombinedViewKeepsEveryChoice()
    {
        avalonia.Invoke(() =>
        {
            var window = Opened(TwoParagraphs(), out var closer);
            using var _ = closer;
            var width = window.Width;
            window.Comparison.Choose(BothChange(window), Choice.FieldWorks);

            window.OpenSideBySide();
            Assert.Equal("pala tumo", window.ResultFields[1].Editor.Document.Paragraphs[0].Text);
            Press(window, Key.Escape);

            Assert.False(window.IsSideBySide);
            Assert.Null(window.Outcome);
            Assert.Equal(width, window.Width);
            Assert.Equal(Choice.FieldWorks, window.Comparison.ChoiceFor(BothChange(window)));
            Assert.Equal(3, window.Marks.Count);
        });
    }

    [Fact]
    public void DoneInSideBySideWaitsForARowBothChangedThatNobodyHasWrittenAndMarksIt()
    {
        avalonia.Invoke(() =>
        {
            var window = Opened(TwoParagraphs(), out var closer);
            using var _ = closer;

            window.OpenSideBySide();

            Assert.False(window.IsDoneEnabled);
            Assert.False(window.Finish());
            Assert.Null(window.Outcome);
            Assert.Null(window.Comparison.ChoiceFor(BothChange(window)));
            Assert.Equal("1 change still needs a choice (paragraph 2): type on the right, use Use this →, or go back to choose", window.WaitingText);
            Assert.EndsWith("needs a choice", AutomationProperties.GetName(window.ResultFields[1]), StringComparison.Ordinal);
            Assert.DoesNotContain("needs a choice", AutomationProperties.GetName(window.ResultFields[0]), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ADoneTheFieldRefusesInSideBySideChangesNothingAndBackStillOffersEveryChoice()
    {
        avalonia.Invoke(() =>
        {
            var window = new CompareWindow("A field of one paragraph", TwoParagraphs(),
                checkResult: result => result.Count > 1 ? "One paragraph only." : null);
            using var closer = window.CloseOnExit();
            window.Show();
            window.Comparison.Choose(BothChange(window), Choice.Mine);
            window.OpenSideBySide();

            Assert.False(window.Finish());

            Assert.Equal("One paragraph only.", window.WaitingText);
            Assert.Null(window.Comparison.RewrittenAt(0));
            Assert.Null(window.Comparison.RewrittenAt(1));
            Press(window, Key.Escape);
            Assert.False(window.IsSideBySide);
            Assert.Equal(Choice.Mine, window.Comparison.ChoiceFor(BothChange(window)));
            window.Select(BothChange(window));
            Press(window, Key.F, RawInputModifiers.Alt);
            Assert.Equal(Choice.FieldWorks, window.Comparison.ChoiceFor(BothChange(window)));
            Assert.EndsWith("pala tumo", Result(window), StringComparison.Ordinal);
            var yours = window.Comparison.Sorted.Changes.Single(change => change.Author == ChangeAuthor.Mine);
            window.MarkOf(yours)!.Focus();
            Press(window, Key.F, RawInputModifiers.Alt);
            Assert.StartsWith("tavu mena kolo rupa", Result(window), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void DoneInSideBySideTakesTheRightSideAsWrittenOnceTheRowBothChangedIsWritten()
    {
        avalonia.Invoke(() =>
        {
            var window = Opened(TwoParagraphs(), out var closer);
            using var _ = closer;
            window.OpenSideBySide();
            var field = window.ResultFields[1];
            field.Focus();
            Press(window, Key.A, RawInputModifiers.Control);
            window.KeyTextInput("pala sinu");
            Assert.True(window.IsDoneEnabled);
            Assert.Equal("", window.WaitingText);

            Assert.True(window.Finish());

            Assert.Equal(CompareEnd.Done, window.Outcome!.End);
            Assert.Equal(["tavu sipa kolo rupa", "pala sinu"], window.Outcome.Result.Select(paragraph => paragraph.Text));
        });
    }
}
