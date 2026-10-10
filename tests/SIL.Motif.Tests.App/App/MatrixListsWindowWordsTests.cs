using System.Text.RegularExpressions;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed partial class MatrixListsWindowWordsTests(AvaloniaHeadlessFixture avalonia)
{
    // Retired by ADR 0049 or by the owner's column words; the window's pages never show them.
    [GeneratedRegex(@"\b(candidates?|reject(ed)?|violations?|can[’']?t happen|cannot happen|assessment|assessed|capped|agrees|timed[- ]out|conflicts?|extra)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex RetiredWord();

    // Stored analyses carry a morph identity so the matcher can place each word, as a real Assessment does.
    private static AssessmentWordResult Word(string form, string outcome, string standing, string? grade = null,
        int missedApproved = 0)
    {
        var storedOpinion = standing switch
        {
            ProjectStanding.Approved => ReadingGrade.Approved,
            ProjectStanding.Candidate => ReadingGrade.Candidate,
            ProjectStanding.Rejected => ReadingGrade.Disapproved,
            _ => null,
        };
        var stored = storedOpinion is null ? [] : new[]
        {
            new ParserReading([])
            {
                StoredAnalysisId = "stored-" + form,
                StoredAnalysisOpinion = storedOpinion,
                Identity = new ApprovedMorphology([new ApprovedMorph(form, "n", null, ["entry"])]),
            },
        };
        var parsedForm = grade == "no-opinion" ? form + "-other" : form;
        return WithPriority(new AssessmentWordResult(form, outcome, outcome is "timed-out" or "capped", "Search completed", 10, null)
        {
            ProjectStanding = standing,
            OccurrenceCount = 1,
            ReadingGrades = grade is null ? null : [grade],
            StoredAnalyses = stored,
            Readings = grade is null ? null : [new ParserReading([new ParserReadingMorph(form, "gloss", "n", null, false, null)])],
            MissedApproved = Enumerable.Range(0, missedApproved)
                .Select(_ => new ParserReading([new ParserReadingMorph(form, "gloss", "n", null, false, null)])).ToArray(),
            Morphology = grade is null ? null : new ParseWordEvidence("v1", 0, form, 10,
                false, false, false, [new ParseAnalysis([new ParseMorph(parsedForm, "n", null, null)])], []),
        });
    }

    private static AssessmentWordResult WithPriority(AssessmentWordResult word) => word with
    {
        FixFirst = CompareSemantics.FixFirst(new CompareWordFacts(
            word.ProjectStanding, word.Outcome, word.IsIncomplete, word.Morphology,
            word.ReadingGrades, word.MissedApproved?.Count ?? 0), word.MissedApproved),
    };

    internal static readonly AssessmentWordResult[] EveryKindOfWord =
    [
        Word("approved-kept", "analysed", ProjectStanding.Approved, "approved"),
        Word("approved-empty", "no-analysis", ProjectStanding.Approved),
        Word("approved-other", "analysed", ProjectStanding.Approved, "no-opinion"),
        Word("unknown-kept", "analysed", ProjectStanding.Candidate, "candidate"),
        Word("unknown-other", "analysed", ProjectStanding.Candidate, "no-opinion"),
        Word("unknown-empty", "no-analysis", ProjectStanding.Candidate),
        Word("new-parse", "analysed", ProjectStanding.NotPresent, "no-opinion"),
        Word("nobody", "no-analysis", ProjectStanding.NotPresent),
        Word("disapproved-built", "analysed", ProjectStanding.Rejected, "disapproved"),
        Word("disapproved-empty", "no-analysis", ProjectStanding.Rejected),
        Word("spelling-built", "analysed", ProjectStanding.IncorrectSpelling, "no-opinion"),
        Word("stopped", "timed-out", ProjectStanding.Approved),
        Word("limit-too", "capped", ProjectStanding.Candidate),
        Word("skipped", "skipped", ProjectStanding.NotPresent),
    ];

    internal static CompareViewModel Compare(IEnumerable<AssessmentWordResult> rows) => CompareWithClient(rows).Compare;

    private static (CompareViewModel Compare, FakeCommandClient Client) CompareWithClient(
        IEnumerable<AssessmentWordResult> rows)
    {
        var fake = new FakeCommandClient();
        var changes = new ChangesViewModel(fake);
        changes.OpenProjectAsync("project.fwdata").GetAwaiter().GetResult();
        var words = new AssessWordsViewModel();
        words.Load(rows.ToArray());
        var compare = new CompareViewModel { Changes = changes };
        compare.Load(words.AllRows);
        return (compare, fake);
    }

    [Fact]
    public void MatrixColumnsUseTheOwnersColumnWords() =>
        Assert.Equal(["Same", "Different", "No parse", "Stopped", "Not parsed"],
            new CompareViewModel().Columns.Select(column => column.Label));

    [Theory]
    [InlineData(WordProjectStatus.Approved, CompareColumnKind.Match, "Kept")]
    [InlineData(WordProjectStatus.Approved, CompareColumnKind.NoMatch, "Built something else")]
    [InlineData(WordProjectStatus.Approved, CompareColumnKind.NoParse, "Lost")]
    [InlineData(WordProjectStatus.Rejected, CompareColumnKind.Match, "Built anyway")]
    [InlineData(WordProjectStatus.Candidate, CompareColumnKind.Match, "Parses; nothing in FieldWorks yet")]
    [InlineData(WordProjectStatus.Candidate, CompareColumnKind.NoMatch, "Have a look")]
    [InlineData(WordProjectStatus.NotPresent, CompareColumnKind.Match, "Nothing to compare")]
    [InlineData(WordProjectStatus.NotPresent, CompareColumnKind.NoMatch, "New: PanGloss proposes")]
    [InlineData(WordProjectStatus.Approved, CompareColumnKind.Timeout, "No result")]
    [InlineData(WordProjectStatus.Approved, CompareColumnKind.Skipped, "Not parsed")]
    public void MatrixCellsSayWhatHappenedInPlainPhrases(WordProjectStatus row, CompareColumnKind column, string expected) =>
        Assert.Equal(expected, CompareViewModel.MeaningOf(row, column).Label);

    [Fact]
    public void ThePanGlossLegendUsesTheColumnWords() =>
        Assert.Equal(["Same", "Different", "No parse", "Stopped", "Not parsed"],
            new CompareViewModel().PanGlossLegend.Select(item => item.Label));

    [Theory]
    [InlineData(AnalysisMarkingClass.Conflict, "Different")]
    [InlineData(AnalysisMarkingClass.Extra, "Different, and more")]
    public void AWordsPanGlossLineUsesTheColumnWords(AnalysisMarkingClass markingClass, string expected) =>
        Assert.Equal(expected, CompareViewModel.PanGlossClassLabel(markingClass));

    [Fact]
    public void TheImpossibleCellShowsItsZeroAndNoLabel()
    {
        var cell = new CompareCellViewModel(WordProjectStatus.NotPresent, CompareColumnKind.Match);

        Assert.Equal("0 words", cell.CountText);
        Assert.False(cell.ShowsLabel);
        cell.SetCounts(0, 0);
        Assert.Equal("0 words", cell.CountText);
        Assert.Equal("0 places", cell.PlacesText);
        Assert.True(cell.ShowsPlaces);
        cell.SetCounts(6, 6);
        Assert.Equal("6 words", cell.CountText);
        Assert.False(cell.ShowsLabel);
        Assert.True(new CompareCellViewModel(WordProjectStatus.Approved, CompareColumnKind.Match).ShowsLabel);
    }

    [Fact]
    public void TheImpossibleCellRendersNoLabel()
    {
        avalonia.Invoke(() =>
        {
            var compare = Compare(EveryKindOfWord);
            var labels = WithWindow(new ComparePanel(compare), window => window.GetLogicalDescendants()
                .OfType<MatrixCell>().Where(cell => cell.DataContext is CompareCellViewModel
                    { Row: WordProjectStatus.NotPresent, Column: CompareColumnKind.Match })
                .SelectMany(cell => cell.GetLogicalDescendants().OfType<TextBlock>())
                .Where(text => text.IsEffectivelyVisible).Select(text => text.Text ?? string.Empty).ToArray());

            Assert.Equal(new[] { "0 words", "0 places" }, labels);
        });
    }

    [Fact]
    public void MatrixShortcutsNameTheCellsTheyChoose()
    {
        var compare = Compare(EveryKindOfWord);

        Assert.Equal(["Lost", "Built something else", "Have a look", "Built anyway", "New: PanGloss proposes",
            "Nobody can analyze", "Stopped", "Not parsed"], compare.Presets.Select(preset => preset.Label));
        compare.SelectPresetCommand.Execute(compare.Presets.Single(preset => preset.Label == "Stopped"));
        Assert.Equal(["limit-too", "stopped"], compare.Words.Select(word => word.Word).Order());
        compare.SelectPresetCommand.Execute(compare.Presets.Single(preset => preset.Label == "Built anyway"));
        Assert.Equal(["disapproved-built"], compare.Words.Select(word => word.Word));
    }

    [Fact]
    public void FixTheseFirstIsHiddenWhenItHasNothingToList()
    {
        Assert.False(Compare([Word("approved-kept", "analysed", ProjectStanding.Approved, "approved")]).HasFixFirst);
        Assert.True(Compare(EveryKindOfWord).HasFixFirst);
    }

    [Fact]
    public void ListsUseNamesThatMatchTheMatrix()
    {
        var compare = Compare(EveryKindOfWord);
        Assert.Equal(compare.Presets.Select(preset => preset.Label),
            new TextsListsViewModel(compare).Lists.Select(list => list.Name));
    }

    [Fact]
    public void AListedWordStartsAtTheLeftOfItsRow()
    {
        avalonia.Invoke(() =>
        {
            var compare = Compare([Word("nobody", "no-analysis", ProjectStanding.NotPresent)]);
            var offset = WithWindow(new TextsListsPanel(new TextsListsViewModel(compare)), window =>
            {
                var word = window.GetVisualDescendants().OfType<TextBlock>().Single(text =>
                    text.Classes.Contains("wordPresentationForm") && text.Text == "nobody");
                var tick = window.GetVisualDescendants().OfType<CheckBox>().Single(box =>
                    AutomationProperties.GetName(box) == "Tick nobody");
                return word.TranslatePoint(default, tick)!.Value.X - tick.Bounds.Width;
            });

            Assert.InRange(offset, 0, 24);
        });
    }

    [Fact]
    public void ListsOpenOnTheFirstListThatHasWords()
    {
        var compare = Compare([
            Word("new-parse", "analysed", ProjectStanding.NotPresent, "no-opinion"),
            Word("stopped", "timed-out", ProjectStanding.Approved),
        ]);

        var lists = new TextsListsViewModel(compare);

        Assert.Equal("New: PanGloss proposes", lists.SelectedList?.Name);
        Assert.Equal(["new-parse"], compare.Words.Select(word => word.Word));
    }

    [Fact]
    public void ListsReopenOnAListWithWordsAfterANewParse()
    {
        var compare = Compare([Word("approved-empty", "no-analysis", ProjectStanding.Approved)]);
        var lists = new TextsListsViewModel(compare);
        var words = new AssessWordsViewModel();
        words.Load([Word("nobody", "no-analysis", ProjectStanding.NotPresent)]);

        compare.Load(words.AllRows);
        lists.SelectFirstIfNeeded();

        Assert.Equal("Nobody can analyze", lists.SelectedList?.Name);
        Assert.Equal(["nobody"], compare.Words.Select(word => word.Word));
    }

    [Fact]
    public void ListsHandOffButtonsSayWhichWordsTheySend()
    {
        var compare = Compare([
            Word("nobody", "no-analysis", ProjectStanding.NotPresent),
            Word("nobody-too", "no-analysis", ProjectStanding.NotPresent),
        ]);
        var lists = new TextsListsViewModel(compare);

        Assert.Equal(WindowCopy.AiHandoffForThisList, lists.HandOffListLabel);
        Assert.Equal(WindowCopy.AiHandoff, lists.HandOffCheckedWordsLabel);
        compare.Words[0].IsChecked = true;
        Assert.Equal(WindowCopy.AiHandoffForThisWord, lists.HandOffCheckedWordsLabel);
        compare.Words[1].IsChecked = true;
        Assert.Equal(WindowCopy.AiHandoffForWordCount(2), lists.HandOffCheckedWordsLabel);
    }

    [Fact]
    public async Task NoCommandOnTheMatrixOrListsChangesAnOpinion()
    {
        var (compare, client) = CompareWithClient(EveryKindOfWord);
        var lists = new TextsListsViewModel(compare);
        compare.ClearSelectionCommand.Execute(null);
        object?[] parameters = [null, ChangeKinds.Approve, ChangeKinds.Reject, ChangeKinds.Candidate];
        var commands = new object[] { compare, lists }.SelectMany(owner => owner.GetType().GetProperties()
                .Where(property => typeof(ICommand).IsAssignableFrom(property.PropertyType))
                .Select(property => (ICommand)property.GetValue(owner)!))
            .ToArray();

        foreach (var ticked in compare.Words.ToArray())
        {
            foreach (var word in compare.Words) word.IsChecked = ReferenceEquals(word, ticked);
            foreach (var command in commands)
                foreach (var parameter in parameters)
                {
                    bool can;
                    try { can = command.CanExecute(parameter); }
                    catch (ArgumentException) { continue; }
                    if (!can) continue;
                    if (command is IAsyncRelayCommand asyncCommand) await asyncCommand.ExecuteAsync(parameter);
                    else command.Execute(parameter);
                }
        }

        Assert.DoesNotContain(client.PendingPutRequests, request =>
            request.Change.Kind is ChangeKinds.Approve or ChangeKinds.Reject or ChangeKinds.Candidate);
        Assert.DoesNotContain(compare.Changes.Items, change =>
            change.Kind is ChangeKinds.Approve or ChangeKinds.Reject or ChangeKinds.Candidate);
    }

    [Theory]
    [InlineData(ChangeKinds.Approve)]
    [InlineData(ChangeKinds.Reject)]
    [InlineData(ChangeKinds.Candidate)]
    public async Task AWordFromAListCannotCarryAnOpinionChange(string kind)
    {
        var compare = Compare([Word("new-parse", "analysed", ProjectStanding.NotPresent, "no-opinion")]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => compare.Changes.AddAsync(kind, compare.Words.Single()));
        Assert.Empty(compare.Changes.Items);
    }

    [Fact]
    public void EveryStringTheMatrixShowsUsesWindowWords()
    {
        avalonia.Invoke(() =>
        {
            var compare = Compare(EveryKindOfWord);
            var shown = new List<string>();
            foreach (var cell in compare.Cells)
            {
                compare.Toggle(cell, additive: false);
                shown.AddRange([compare.SelectionText, compare.ListHeading, compare.ListSummary, compare.ListExplanation,
                    compare.HandOffLabel, cell.AccessibleName, cell.PlacesText, cell.Explanation ?? string.Empty]);
            }
            shown.AddRange(compare.FixFirstRows.SelectMany(row => new[] { row.Category, row.Explanation }));
            compare.ClearSelectionCommand.Execute(null);
            shown.AddRange(compare.Words.SelectMany(word => new[] { word.Meaning, word.RowLabel, word.ColumnLabel,
                word.PanGlossLabel, word.AccessibleName }));
            shown.AddRange(Rendered(new ComparePanel(compare)));

            AssertWindowWords(shown);
            Assert.DoesNotContain("Approve one analysis at a time, in the text.", shown);
        });
    }

    [Fact]
    public void EveryStringTheListsShowUsesWindowWords()
    {
        avalonia.Invoke(() =>
        {
            var compare = Compare(EveryKindOfWord);
            var lists = new TextsListsViewModel(compare);
            var shown = new List<string>();
            foreach (var list in lists.Lists)
            {
                lists.SelectListCommand.Execute(list);
                shown.AddRange([list.Name, list.Sentence, list.CountText, lists.HandOffListDisabledReason, lists.ParseAgainHelpText,
                    lists.HandOffCheckedWordsHelpText, lists.HandOffListLabel, lists.HandOffCheckedWordsLabel]);
                shown.AddRange(Rendered(new TextsListsPanel(lists)));
            }

            AssertWindowWords(shown);
        });
    }

    [Fact]
    public void MatrixWordListOffersNoOpinionButtonsAndListsOffersOneHandOffOfEachKind()
    {
        avalonia.Invoke(() =>
        {
            var compare = Compare(EveryKindOfWord);
            var matrixButtons = Buttons(new ComparePanel(compare));
            Assert.DoesNotContain(matrixButtons, button => button.CommandParameter is ChangeKinds.Approve
                or ChangeKinds.Reject or ChangeKinds.Candidate);
            Assert.Contains(matrixButtons, button => Equals(button.Content, "Add as Unknown"));

            var lists = new TextsListsViewModel(compare);
            WithWindow(new TextsListsPanel(lists), window =>
            {
                var listButtons = window.GetLogicalDescendants().OfType<Button>().ToArray();
                Assert.Single(listButtons, button => Equals(button.Content, lists.HandOffListLabel));
                Assert.Single(listButtons, button => Equals(button.Content, WindowCopy.AiHandoff));
                Assert.DoesNotContain(listButtons, button => button.CommandParameter is string);
                return true;
            });
        });
    }

    private static void AssertWindowWords(IEnumerable<string> shown)
    {
        var retired = shown.Where(text => RetiredWord().IsMatch(text)).Distinct().ToArray();
        Assert.True(retired.Length == 0, "Retired words on the page: " + string.Join(" | ", retired));
    }

    private static IReadOnlyList<Button> Buttons(Control panel) => WithWindow(panel,
        window => window.GetLogicalDescendants().OfType<Button>().ToArray());

    private static IReadOnlyList<string> Rendered(Control panel) => WithWindow(panel, window =>
    {
        var texts = new List<string?>();
        foreach (var control in window.GetLogicalDescendants().OfType<Control>())
        {
            texts.Add(AutomationProperties.GetName(control));
            texts.Add(AutomationProperties.GetHelpText(control));
            texts.Add(ToolTip.GetTip(control) as string);
            switch (control)
            {
                case TextBlock block: texts.Add(block.Text); break;
                case ContentControl { Content: string content }: texts.Add(content); break;
                case TextBox box: texts.Add(box.PlaceholderText); break;
                case ComboBox combo: texts.Add(combo.PlaceholderText); break;
            }
        }
        return texts.Where(text => !string.IsNullOrEmpty(text)).Cast<string>().ToArray();
    });

    private static T WithWindow<T>(Control panel, Func<Window, T> read)
    {
        var window = new Window { Content = panel, RequestedThemeVariant = ThemeVariant.Light, Width = 1400, Height = 900 };
        try
        {
            window.Show();
            window.UpdateLayout();
            return read(window);
        }
        finally
        {
            window.Close();
        }
    }
}
