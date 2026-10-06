using System.Text.Json;
using System.Xml.Linq;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Tests.App.Walkthrough;
using WordRow = SIL.Motif.App.Views.WordRow;
using TraceStep = SIL.Motif.Contract.Responses.TraceStep;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProgressiveDisplayTests
{
    private const int WordRowControlCeiling = 111;
    private const int WordRowDepthCeiling = 22;
    private const int WordRowCardControlCeiling = 122;
    private const int WordRowCardDepthCeiling = 20;
    private const int WordStripControlCeiling = 76;
    private const int WordStripDepthCeiling = 12;
    private const int MarkChipControlCeiling = 4;
    private const int MarkChipDepthCeiling = 3;
    private readonly ITestOutputHelper _output;

    public ProgressiveDisplayTests(ITestOutputHelper output) => _output = output;

    private static readonly string[] ViewNames =
    [
        "ComparePanel", "DiagnosticPanel", "DifferencePanel", "ExpertTracePanel", "GrammarPanel", "HandoffPanel",
        "Inspector", "KeyboardShortcutsFlyoutView", "MainWindow", "MiniMatrix", "Pages/OverviewPage", "Pages/TimingPage",
        "RefusalBlock", "ResultsInTextPanel",
        "ReviewPanel", "SelectionPanel", "SetupDialog", "StatisticsPanel", "TextWordsPanel", "TextsListsPanel",
        "TraceAnalysesView", "TryWordPanel", "WordRow", "WordRowCard",
    ];

    public static IEnumerable<object[]> CollectionViews() => ViewNames.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(CollectionViews))]
    public void EveryCollectionViewRealizesOnlyNearbyRows(string viewName)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, sampleWindow) = await PageScreenshots.OpenOverSampleData();
            var window = new Window { Width = 1240, Height = 780 };
            try
            {
                if (viewName is "ComparePanel" or "MiniMatrix" or "TextsListsPanel")
                {
                    var source = workspace.Assess.Words.AllRows[0].Source;
                    workspace.Assess.Compare.Load(Enumerable.Range(0, 2000).Select(index =>
                        new AssessWordRowViewModel(source with { Word = $"word-{index}" })).ToArray());
                }
                var root = Build(viewName, workspace, sampleWindow);
                var entries = Inventory().Where(entry => entry.View == viewName).ToArray();
                var targets = Fragments(root).SelectMany(fragment => Controls(fragment)
                    .OfType<ItemsControl>().Select(items => (items, fragment)))
                    .Where(pair => entries.Any(entry => entry.Name == pair.items.Name))
                    .GroupBy(pair => pair.items.Name).ToDictionary(group => group.Key!, group => group.First());
                foreach (var entry in entries)
                {
                    if (entry.Mode == "grid") continue;
                    Assert.True(targets.TryGetValue(entry.Name, out var target), $"Missing {viewName}/{entry.Name}");
                    var (items, fragment) = target;
                    if (entry.Mode is "finite" or "inline")
                    {
                        Assert.InRange(items.ItemCount, 0, entry.Mode == "finite" ? 32 : 128);
                        _output.WriteLine($"{entry.View}/{entry.Source}: fixed collection, {items.ItemCount} items");
                        continue;
                    }
                    if (entry.Mode is "tree" or "grid") continue;
                    var isFragment = !ReferenceEquals(root, fragment);
                    var externalScroll = isFragment || viewName is "DiagnosticPanel" or "ExpertTracePanel" or
                        "TraceAnalysesView" or "WordRowCard";
                    window.Content = externalScroll ? new ScrollViewer { Content = fragment } : fragment;
                    foreach (var ancestor in items.GetLogicalAncestors().OfType<Control>())
                    {
                        ancestor.IsVisible = true;
                        if (ancestor is Expander expander) expander.IsExpanded = true;
                    }
                    items.IsVisible = true;
                    var count = isFragment ? 500 : 2000;
                    items.ItemTemplate = new FuncDataTemplate<object>((_, _) =>
                        new TextBlock { Text = "Scale row", Height = 32, Focusable = true }, supportsRecycling: true);
                    var source = Enumerable.Range(0, count).Cast<object>().ToArray();
                    window.Show();
                    PageScreenshots.Settle(window);
                    if (items is ProgressiveItemsControl paged) paged.FullItemsSource = source;
                    else items.ItemsSource = source;
                    if (items is ComboBox combo) combo.IsDropDownOpen = true;
                    PageScreenshots.Settle(window);
                    items.ScrollIntoView(0);
                    PageScreenshots.Settle(window);
                    AssertBounded(items, entry);
                    var first = items.GetRealizedContainers().Count();
                    if (items is ProgressiveItemsControl progressive)
                    {
                        progressive.ShowItem(source[^1]);
                        PageScreenshots.Settle(window);
                        items.ScrollIntoView(items.ItemCount - 1);
                    }
                    else items.ScrollIntoView(count - 1);
                    PageScreenshots.Settle(window);
                    AssertBounded(items, entry);
                    Assert.True(items.ContainerFromIndex(items is ProgressiveItemsControl ? items.ItemCount - 1 : count - 1) is not null,
                        $"{entry.View}/{entry.Source}: the final row is not realized");
                    _output.WriteLine($"{entry.View}/{entry.Source}: {count} source items, {first} first / {items.GetRealizedContainers().Count()} last realized");
                    if (items is ComboBox opened) opened.IsDropDownOpen = false;
                    items.ItemsSource = null;
                    if (window.Content is ScrollViewer wrapper && ReferenceEquals(wrapper.Content, fragment))
                        wrapper.Content = null;
                    window.Content = null;
                    PageScreenshots.Settle(window);
                }
            }
            finally
            {
                window.Close();
                sampleWindow.Close();
            }
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void RefusalIssuesRealizeNearbyRowsAndKeepTheFinalIssueReachable()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var block = new RefusalBlock { DataContext = CompileRefusal(2000) };
            var window = new Window { Width = 1240, Height = 780, Content = block };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var items = block.FindControl<ItemsControl>("RefusalBlockCompileIssuesItems")!;
                var entry = Assert.Single(Inventory(), entry => entry.View == "RefusalBlock");
                Assert.Equal(2000, items.ItemCount);
                AssertBounded(items, entry);
                items.ScrollIntoView(1999);
                PageScreenshots.Settle(window);
                AssertBounded(items, entry);
                var last = Assert.IsAssignableFrom<Control>(items.ContainerFromIndex(1999));
                var text = last.GetVisualDescendants().OfType<TextBlock>().Select(control => control.Text).ToArray();
                Assert.Contains("Allomorph á-1999 has an invalid environment.", text);
                Assert.Contains("Repair environment 1999 in FieldWorks.", text);
                _output.WriteLine($"RefusalBlock: {items.ItemCount} issues, {items.GetRealizedContainers().Count()} final rows realized");
            }
            finally { window.Close(); }
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void InventoryIncludesEveryCollectionAndEveryViewHasAScaleTest()
    {
        var inventory = Inventory();
        var names = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var collections = new[] { "ItemsControl", "ProgressiveItemsControl", "ListBox", "TreeView", "DataGrid", "ComboBox", "ItemsRepeater" };
        var actual = Directory.EnumerateFiles(Path.Combine(AppDirectory(), "Views"), "*.axaml", SearchOption.AllDirectories)
            .SelectMany(path => XDocument.Load(path).Descendants()
                .Where(node => collections.Contains(node.Name.LocalName) &&
                    (node.Attribute("ItemsSource") ?? node.Attribute("FullItemsSource")) is not null)
                .Select(node =>
                {
                    var binding = (node.Attribute("ItemsSource") ?? node.Attribute("FullItemsSource"))!.Value;
                    var source = binding.Split([' ', ',', '}'], StringSplitOptions.RemoveEmptyEntries)[1];
                    return $"{Path.GetRelativePath(Path.Combine(AppDirectory(), "Views"), path).Replace('\\', '/')[..^6]}|{node.Attribute(names + "Name")?.Value}|{node.Name.LocalName}|{source}";
                }))
            .Order().ToArray();
        Assert.Equal(actual, inventory.Select(entry => $"{entry.View}|{entry.Name}|{entry.Control}|{entry.Source}").Order());
        Assert.Equal(ViewNames.Order(), inventory.Select(entry => entry.View).Distinct().Order());
        var codeBuilt = Directory.EnumerateFiles(Path.Combine(AppDirectory(), "Views"), "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("Children.Add(", StringComparison.Ordinal) ||
                File.ReadAllText(path).Contains("ProgressivePanel.Populate(this", StringComparison.Ordinal))
            .Select(path => Path.GetFileNameWithoutExtension(path).Replace(".axaml", string.Empty)).Order().ToArray();
        Assert.Equal(new[] { "FilterChip", "GrammarWarningPartsBlock", "HeatCell", "ListWordCard", "MainWindow", "MarkChip", "MorphemeRow", "OutcomeBar", "ProgressivePanel" }, codeBuilt);
    }

    [Fact]
    public void WordVisualRootsStayWithinControlAndDepthBudgets()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, sampleWindow) = await PageScreenshots.OpenOverSampleData();
            var texts = workspace.PageModel<TextsPageModel>();
            texts.Tab = TextsTab.AnalyzeTexts;
            workspace.CurrentPage = WorkspacePage.Texts;
            texts.ShowAnalyzeViewCommand.Execute(AnalyzeTextsView.TextReader);
            var reader = texts.ResultsInText;
            var selectedText = Assert.IsType<ResultsTextViewModel>(reader.SelectedText);
            Assert.Equal("Hadithi ya sungura", selectedText.Title);
            var token = Assert.Single(reader.VisibleLines.SelectMany(line => line.Tokens), item => item.Form == "hawajafika");
            reader.SelectToken(token);
            token.IsCardOpen = false;
            Assert.Same(token, reader.SelectedToken);
            Assert.False(token.IsCardOpen);
            PageScreenshots.Settle(sampleWindow);

            var row = FixedWordRow(workspace);
            var card = new WordRowCard { CardToken = token, IsOpen = true };
            Assert.Equal("hawajafika", card.CardToken?.Form);
            Assert.True(card.IsOpen);
            var window = new Window
            {
                Width = 1240,
                Height = 1200,
                Content = new StackPanel { Children = { row, card } },
            };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var rowTree = AvaloniaScaleCounts.CaptureSubtree(row);
                var cardTree = AvaloniaScaleCounts.CaptureSubtree(card);
                var chip = row.FindControl<MarkChip>("OutcomeBesideMorphemes")!;
                var chipTree = AvaloniaScaleCounts.CaptureSubtree(chip);
                var panel = Assert.Single(sampleWindow.GetVisualDescendants().OfType<ResultsInTextPanel>());
                var strip = Assert.Single(panel.GetVisualDescendants().OfType<Border>(),
                    border => border.Name == "WordStrip" && ReferenceEquals(border.Tag, token));
                var stripTree = AvaloniaScaleCounts.CaptureSubtree(strip);

                WriteSubtree("WordRow", rowTree);
                WriteSubtree("WordRowCard", cardTree);
                WriteSubtree("WordStrip", stripTree);
                WriteSubtree("MarkChip", chipTree);
                AssertSubtreeBudget("WordRow", rowTree, WordRowControlCeiling, WordRowDepthCeiling);
                AssertSubtreeBudget("WordRowCard", cardTree, WordRowCardControlCeiling, WordRowCardDepthCeiling);
                AssertSubtreeBudget("WordStrip", stripTree, WordStripControlCeiling, WordStripDepthCeiling);
                AssertSubtreeBudget("MarkChip", chipTree, MarkChipControlCeiling, MarkChipDepthCeiling);
                Assert.Contains("Avalonia.Controls.Grid", rowTree.ControlTypes.Select(type => type.Key));
                Assert.Contains("Avalonia.Controls.Border", rowTree.ControlTypes.Select(type => type.Key));
                Assert.Contains("Avalonia.Controls.StackPanel", rowTree.ControlTypes.Select(type => type.Key));
                Assert.Contains(rowTree.ControlTypes, type => type.Key.EndsWith("TextBlock", StringComparison.Ordinal));
                Assert.NotEmpty(rowTree.TemplateParts);
                Assert.NotEmpty(cardTree.TemplateParts);
                Assert.Equal(rowTree.ControlCount, rowTree.ControlTypes.Sum(type => type.Count));
                Assert.Equal(cardTree.ControlCount, cardTree.ControlTypes.Sum(type => type.Count));
                Assert.Equal(stripTree.ControlCount, stripTree.ControlTypes.Sum(type => type.Count));
                Assert.Equal(chipTree.ControlCount, chipTree.ControlTypes.Sum(type => type.Count));
                Assert.Equal(row.Row?.OutcomeMark, chip.Mark);
                Assert.Equal(row.Row?.OutcomeLabel, chip.Text);
                Assert.True(chip.Compact);
            }
            finally
            {
                window.Close();
                sampleWindow.Close();
            }
        }, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void WordRowSubtreeBudgetRejectsAddedSiblingBorders()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, sampleWindow) = await PageScreenshots.OpenOverSampleData();
            var row = FixedWordRow(workspace);
            var window = new Window { Width = 1240, Height = 780, Content = row };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var baseline = AvaloniaScaleCounts.CaptureSubtree(row);
                AvaloniaScaleCounts.AssertSubtreeBudget(
                    baseline, "WordRow", WordRowControlCeiling, WordRowDepthCeiling);
                var cells = row.FindControl<Grid>("Cells")!;
                var bordersToAdd = Math.Max(1, WordRowControlCeiling + 1 - baseline.ControlCount);
                for (var index = 0; index < bordersToAdd; index++) cells.Children.Add(new Border());
                var mutated = AvaloniaScaleCounts.CaptureSubtree(row);
                Assert.Equal(baseline.ControlCount + bordersToAdd, mutated.ControlCount);
                var rejection = Assert.Throws<InvalidOperationException>(() =>
                    AvaloniaScaleCounts.AssertSubtreeBudget(
                        mutated, "WordRow", WordRowControlCeiling, WordRowDepthCeiling));
                Assert.Contains($"exceeded {WordRowControlCeiling} controls/depth {WordRowDepthCeiling}", rejection.Message);
                _output.WriteLine($"Sibling Border probe: {baseline.ControlCount} became {mutated.ControlCount}; budget rejected it.");
            }
            finally
            {
                window.Close();
                sampleWindow.Close();
            }
        }, TimeSpan.FromMinutes(1));
    }

    private static WordRow FixedWordRow(WorkspaceShellViewModel workspace)
    {
        var model = Assert.Single(workspace.Assess.Words.Rows, row => row.Word == "Sungura");
        var control = new WordRow
        {
            Row = model.WordRow,
            Columns = WordRowColumns.All,
            ShowsTick = true,
            ShowsMeaning = true,
            IsChecked = false,
            IsOpen = false,
            Card = null,
            StagedText = null,
            Note = null,
            Actions = null,
            TimeText = null,
        };
        Assert.Equal("Sungura", control.Row?.Word);
        Assert.Equal(WordRowColumns.All, control.Columns);
        Assert.True(control.ShowsTick);
        Assert.True(control.ShowsMeaning);
        Assert.False(control.IsOpen);
        Assert.False(control.IsChecked);
        return control;
    }

    private void AssertSubtreeBudget(string rootName, ControlSubtreeSnapshot tree, int maximumControls, int maximumDepth)
    {
        _output.WriteLine($"Visual subtree | {rootName} | {tree.ControlCount} Controls | depth {tree.MaximumDepth} | " +
            $"budget {maximumControls}/{maximumDepth}");
        Assert.InRange(tree.ControlCount, 1, maximumControls);
        Assert.InRange(tree.MaximumDepth, 0, maximumDepth);
    }

    private void WriteSubtree(string rootName, ControlSubtreeSnapshot tree)
    {
        var types = string.Join(", ", tree.ControlTypes.Select(type => $"{type.Key}={type.Count}"));
        var owners = tree.TemplateParts.Count == 0 ? "none" : string.Join(", ", tree.TemplateParts.Select(part =>
            $"{part.Owner}={part.Count}@{part.MaximumDepth}"));
        _output.WriteLine($"Visual subtree types | {rootName} | {types}");
        _output.WriteLine($"Template-owned parts | {rootName} | {owners}");
    }

    [Fact]
    public void ReviewRealizesNearbyChangesAndKeyboardTargetsAcrossGroups()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, sample) = await PageScreenshots.OpenOverSampleData(configure: (fake, assessment) =>
            {
                var source = assessment.Words.First(word => word.StoredAnalyses.Count > 0);
                var words = Enumerable.Range(0, 200).Select(index => source with { Word = $"review-{index:D3}" }).ToArray();
                fake.AssessCompletesWith(assessment with { Words = [.. assessment.Words, .. words] });
                var changes = words.Select((word, index) => new PendingChange($"change-{index}", $"wordform-{index}",
                    word.Word, index < 100 ? ChangeKinds.AddCandidate : ChangeKinds.RemoveAnalysis,
                    "assessment/one", word.Word, [$"operation-{index}"])
                {
                    Analyses = [new ReviewAnalysis(word.StoredAnalyses[0], ReadingGrade.Candidate, true, true)],
                    StoredAnalysisId = $"analysis-{index}",
                }).ToArray();
                fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one", changes,
                    changes.Select(change => new ChangeFit(change.ChangeId, true, [])).ToArray()));
            });
            var panel = new ReviewPanel(workspace.PageModel<ReviewPageModel>());
            var window = new Window { Width = 1240, Height = 780, Content = panel };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var groups = panel.FindControl<ItemsControl>("ReviewGroups")!;
                Assert.Equal(2, groups.ItemCount);
                Assert.All(groups.Items.Cast<ReviewChangeGroupViewModel>(), group => Assert.Equal(100, group.Items.Count));
                AssertBoundedRows("first");
                var firstList = ChangeList(0);
                firstList.ScrollIntoView(0);
                PageScreenshots.Settle(window);
                var first = ChangeRow(firstList, 0);
                first.FocusRow();
                AssertFocus(0, 0);
                for (var index = 1; index <= 12; index++)
                {
                    Press(Key.Down);
                    AssertFocus(0, index);
                    AssertBoundedRows($"down {index}");
                }

                Press(Key.End);
                AssertFocus(1, 99);
                AssertBoundedRows("end");
                Press(Key.Up);
                AssertFocus(1, 98);
                Press(Key.Home);
                AssertFocus(0, 0);

                firstList = ChangeList(0);
                firstList.ScrollIntoView(99);
                PageScreenshots.Settle(window);
                ChangeRow(firstList, 99).FocusRow();
                Press(Key.Down);
                AssertFocus(1, 0);
                Press(Key.Up);
                AssertFocus(0, 99);
                AssertBoundedRows("group boundary");

                Press(Key.Home);
                first = ChangeRow(ChangeList(0), 0);
                first.IsOpen = true;
                PageScreenshots.Settle(window);
                Assert.True(((ReviewChangeGroupViewModel)groups.Items[0]!).Items[0].Listed!.IsOpen);
                Press(Key.End);
                AssertFocus(1, 99);
                Press(Key.Home);
                AssertFocus(0, 0);
                Assert.True(ChangeRow(ChangeList(0), 0).IsOpen);
                AssertBoundedRows("expanded return");

                void Press(Key key)
                {
                    window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
                    window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
                    PageScreenshots.Settle(window);
                }

                ItemsControl ChangeList(int index) => Assert.Single(groups.ContainerFromIndex(index)!
                    .GetVisualDescendants().OfType<ItemsControl>(), control => control.Name == "ReviewPanelItemsItems");

                static WordRow ChangeRow(ItemsControl list, int index) => Assert.Single(list.ContainerFromIndex(index)!
                    .GetVisualDescendants().OfType<WordRow>());

                void AssertFocus(int groupIndex, int rowIndex)
                {
                    var rows = panel.GetVisualDescendants().OfType<WordRow>().ToArray();
                    var focusedRows = rows.Where(row => row.FindControl<Border>("Body")!.IsFocused).ToArray();
                    Assert.True(focusedRows.Length == 1,
                        $"Expected focus on {groupIndex}/{rowIndex}; realized {string.Join(',', rows.Select(row => ((ChangeViewModel)row.DataContext!).ChangeId))}");
                    var focused = focusedRows[0];
                    Assert.Same(((ReviewChangeGroupViewModel)groups.Items[groupIndex]!).Items[rowIndex], focused.DataContext);
                    var body = focused.FindControl<Border>("Body")!;
                    var top = body.TranslatePoint(default, window)!.Value.Y;
                    Assert.InRange(top, 0, window.Bounds.Height - body.Bounds.Height);
                }

                void AssertBoundedRows(string position)
                {
                    var count = panel.GetVisualDescendants().OfType<WordRow>().Count();
                    Assert.InRange(count, 1, 16);
                    Assert.InRange(groups.GetRealizedContainers().Count(), 1, 2);
                    _output.WriteLine($"Review {position}: {count} real WordRows for 200 changes");
                }
            }
            finally
            {
                window.Close();
                sample.Close();
            }
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void TimingRuleTableRealizesOnlyNearbyRowsWithItsRealRowTemplate()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData();
            try
            {
                window.Width = 1240;
                window.Height = 780;
                workspace.CurrentPage = WorkspacePage.Timing;
                PageScreenshots.Settle(window);
                var table = Assert.Single(window.GetVisualDescendants().OfType<ItemsControl>(),
                    control => AutomationProperties.GetName(control) == "Timing by rule");
                var row = table.Items[0];
                table.ItemsSource = Enumerable.Repeat(row, 2000).ToArray();
                PageScreenshots.Settle(window);
                Assert.InRange(table.GetRealizedContainers().Count(), 1, 48);
            }
            finally { window.Close(); }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ExpandedDerivationTreeRealizesOnlyNearbyChildren()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, sample) = await PageScreenshots.OpenOverSampleData();
            var panel = new DiagnosticPanel(workspace.PageModel<TryWordPageModel>().Diagnostics);
            var window = new Window { Width = 1240, Height = 780, Content = new ScrollViewer { Content = panel } };
            try
            {
                var leaf = new TraceStepViewModel(new TraceStep("Failed", "rule", "word", null, null, []), null);
                var children = Enumerable.Range(0, 500).Select(_ => leaf.WithChildren([])).ToArray();
                var tree = panel.FindControl<TreeView>("TreeHost")!;
                foreach (var ancestor in tree.GetLogicalAncestors().OfType<Control>())
                {
                    ancestor.IsVisible = true;
                    if (ancestor is Expander expander) expander.IsExpanded = true;
                }
                tree.IsVisible = true;
                tree.ItemsSource = new[] { leaf.WithChildren(children, expandForFilter: true) };
                window.Show();
                PageScreenshots.Settle(window);
                tree.ScrollIntoView(0);
                PageScreenshots.Settle(window);
                var root = Assert.IsType<TreeViewItem>(tree.ContainerFromIndex(0));
                Assert.InRange(root.GetRealizedContainers().Count(), 1, 64);
                tree.SelectedItem = children[0];
                Assert.True(Assert.IsType<TreeViewItem>(root.ContainerFromIndex(0)).Focus());
                for (var index = 0; index < 60; index++)
                {
                    window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
                    window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
                    PageScreenshots.Settle(window);
                }
                Assert.Same(children[60], tree.SelectedItem);
                for (var index = 0; index < 60; index++)
                {
                    window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.None, null);
                    window.KeyRelease(Key.Up, RawInputModifiers.None, PhysicalKey.None, null);
                    PageScreenshots.Settle(window);
                }
                Assert.Same(children[0], tree.SelectedItem);
                Assert.InRange(root.GetRealizedContainers().Count(), 1, 64);
                root.ScrollIntoView(499);
                PageScreenshots.Settle(window);
                Assert.NotNull(root.ContainerFromIndex(499));
                Assert.InRange(root.GetRealizedContainers().Count(), 1, 64);
                Assert.InRange(tree.GetVisualDescendants().OfType<TreeViewItem>().Count(), 2, 65);
                root.IsExpanded = false;
                Assert.False(Assert.IsType<TraceStepViewModel>(tree.Items[0]).IsExpanded);
                root.IsExpanded = true;
                Assert.True(Assert.IsType<TraceStepViewModel>(tree.Items[0]).IsExpanded);
                var roots = Enumerable.Range(0, 2000).Select(_ => leaf.WithChildren([])).ToArray();
                tree.ItemsSource = roots;
                PageScreenshots.Settle(window);
                tree.ScrollIntoView(0);
                PageScreenshots.Settle(window);
                tree.SelectedItem = roots[0];
                Assert.True(Assert.IsType<TreeViewItem>(tree.ContainerFromIndex(0)).Focus());
                for (var index = 0; index < 60; index++)
                {
                    window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
                    window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
                    PageScreenshots.Settle(window);
                }
                Assert.Same(roots[60], tree.SelectedItem);
                tree.ScrollIntoView(1999);
                PageScreenshots.Settle(window);
                Assert.NotNull(tree.ContainerFromIndex(1999));
                Assert.InRange(tree.GetRealizedContainers().Count(), 1, 64);
            }
            finally { window.Close(); sample.Close(); }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void DetailedStatisticsKeepsItsWordCellsReadable()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData();
            try
            {
                window.Width = 1040;
                window.Height = 1300;
                workspace.CurrentPage = WorkspacePage.Timing;
                PageScreenshots.Settle(window);
                var page = Assert.Single(window.GetVisualDescendants().OfType<TimingPage>());
                var expander = Assert.Single(page.GetLogicalDescendants().OfType<Expander>(),
                    control => Equals(control.Header, "Detailed statistics"));
                expander.IsExpanded = true;
                if (page.Page.Statistics.LoadCommand.ExecutionTask is { } loading) await loading;
                PageScreenshots.Settle(window);
                var grid = Assert.Single(page.GetVisualDescendants().OfType<DataGrid>());
                var pageScroll = Assert.Single(grid.GetVisualAncestors().OfType<ScrollViewer>());
                pageScroll.Offset = new Vector(0, Math.Max(0, pageScroll.Extent.Height - pageScroll.Viewport.Height));
                PageScreenshots.Settle(window);
                var numericColumns = grid.Columns.Where(column => column.Tag is "attempts" or "passes" or "elapsedMs").ToArray();
                Assert.Equal(3, numericColumns.Length);
                var numericColumnWidths = numericColumns.ToDictionary(column => column, column => column.Width);
                foreach (var column in numericColumns)
                {
                    Assert.Equal(DataGridLengthUnitType.Auto, column.Width.UnitType);
                    Assert.True(column.ActualWidth > 0);
                    column.Width = new DataGridLength(column.ActualWidth, DataGridLengthUnitType.Pixel);
                }
                grid.ScrollIntoView(grid.ItemsSource!.Cast<StatsRowViewModel>().First(), numericColumns[0]);
                PageScreenshots.Settle(window);
                var heatCellTexts = grid.GetVisualDescendants().OfType<HeatCell>()
                    .Where(cell => cell.IsEffectivelyVisible)
                    .Select(cell => Assert.Single(cell.GetVisualDescendants().OfType<CopyableTextBlock>()))
                    .Where(text => !string.IsNullOrEmpty(text.Text))
                    .ToArray();
                Assert.NotEmpty(heatCellTexts);
                var heatCellFontSizes = heatCellTexts.ToDictionary(text => text, text => text.FontSize);
                foreach (var text in heatCellTexts) text.FontSize *= 3;
                PageScreenshots.Settle(window);
                LayoutAssertions.AssertCurrent(grid);
                Assert.All(heatCellTexts, text =>
                {
                    Assert.Equal(TextTrimming.CharacterEllipsis, text.TextTrimming);
                    Assert.Equal(text.Text, ToolTip.GetTip(text));
                });
                foreach (var (text, fontSize) in heatCellFontSizes) text.FontSize = fontSize;
                foreach (var (column, width) in numericColumnWidths) column.Width = width;
                PageScreenshots.Settle(window);
                grid.MinHeight = 0;
                grid.MaxHeight = 200;
                var existingRows = Assert.IsAssignableFrom<IEnumerable<StatsRowViewModel>>(grid.ItemsSource).ToArray();
                var rows = existingRows.Concat(Enumerable.Range(0, 200).Select(index => new StatsRowViewModel(
                    JsonSerializer.SerializeToElement(new
                    {
                        form = $"extra-word-{index}", elapsed_ns = 1000, attempts = 1, passes = 1,
                    })))).ToArray();
                grid.ItemsSource = rows;
                var wordColumn = grid.Columns.Single(column => Equals(column.Tag, "word"));
                wordColumn.Width = new DataGridLength(80, DataGridLengthUnitType.Pixel);
                PageScreenshots.Settle(window);
                var presenter = Assert.Single(grid.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.DataGridRowsPresenter>());
                var realizedRows = grid.GetVisualDescendants().OfType<DataGridRow>()
                    .Where(row => row.DataContext is StatsRowViewModel)
                    .ToArray();
                var visibleWordRow = realizedRows.First(row => ((StatsRowViewModel)row.DataContext!).Word is not null);
                var visibleWord = (StatsRowViewModel)visibleWordRow.DataContext!;
                var wordCell = Assert.Single(visibleWordRow.GetVisualDescendants().OfType<CopyableTextBlock>(),
                    text => text.Text == visibleWord.Word);
                Assert.Equal(TextTrimming.CharacterEllipsis, wordCell.TextTrimming);
                Assert.Equal(visibleWord.Word, ToolTip.GetTip(wordCell));
                var realizedIndexes = realizedRows
                    .Select(row => Array.IndexOf(rows, (StatsRowViewModel)row.DataContext!))
                    .Where(index => index >= 0)
                    .ToArray();
                Assert.Contains(0, realizedIndexes);
                var targetRow = rows.Skip(realizedIndexes.Max() + 1).LastOrDefault(row => row.Word is not null);
                Assert.NotNull(targetRow);
                Assert.DoesNotContain(realizedRows, row => ReferenceEquals(row.DataContext, targetRow));
                Assert.DoesNotContain(grid.GetVisualDescendants().OfType<CopyableTextBlock>(),
                    text => ReferenceEquals(text.DataContext, targetRow));
                LayoutAssertions.AssertCurrent(grid);
                grid.ScrollIntoView(targetRow, grid.Columns.Single(column => Equals(column.Tag, "word")));
                PageScreenshots.Settle(window);
                var word = Assert.Single(grid.GetVisualDescendants().OfType<CopyableTextBlock>(),
                    text => ReferenceEquals(text.DataContext, targetRow) &&
                        text.Text == targetRow.Word);
                Assert.Equal(targetRow.Word, word.Text);
                Assert.Equal(TextTrimming.CharacterEllipsis, word.TextTrimming);
                Assert.Equal(targetRow.Word, ToolTip.GetTip(word));
                var wordOrigin = word.TranslatePoint(default, presenter)!.Value;
                Assert.True(wordOrigin.Y >= -2);
                Assert.True(wordOrigin.Y + word.Bounds.Height <= presenter.Bounds.Height + 2,
                    $"The scrolled word ends at {wordOrigin.Y + word.Bounds.Height}px in a " +
                    $"{presenter.Bounds.Height}px rows viewport.");
                foreach (var ancestor in word.GetVisualAncestors().OfType<Control>().Where(control => control.ClipToBounds))
                {
                    var origin = word.TranslatePoint(default, ancestor)!.Value;
                    Assert.True(origin.X >= -2 && origin.Y >= -2 &&
                        origin.X + word.Bounds.Width <= ancestor.Bounds.Width + 2 &&
                        origin.Y + word.Bounds.Height <= ancestor.Bounds.Height + 2,
                        $"The revealed word at {origin} with size {word.Bounds.Size} is clipped by " +
                        $"{ancestor.GetType().Name} with bounds {ancestor.Bounds}.");
                }
                LayoutAssertions.AssertCurrent(grid);

                var knownRow = Assert.Single(grid.ItemsSource!.Cast<StatsRowViewModel>(), item => item.Word == "alikula");
                grid.ScrollIntoView(knownRow, wordColumn);
                PageScreenshots.Settle(window);
                var knownWord = Assert.Single(grid.GetVisualDescendants().OfType<CopyableTextBlock>(),
                    text => text.Text == knownRow.Word);
                LayoutAssertions.AssertCurrent(grid);
                foreach (var ancestor in knownWord.GetVisualAncestors().OfType<Control>().Where(control => control.ClipToBounds))
                {
                    var origin = knownWord.TranslatePoint(default, ancestor)!.Value;
                    Assert.True(origin.X >= -2 && origin.Y >= -2 &&
                        origin.X + knownWord.Bounds.Width <= ancestor.Bounds.Width + 2 &&
                        origin.Y + knownWord.Bounds.Height <= ancestor.Bounds.Height + 2,
                        $"The revealed word is clipped by {ancestor.GetType().Name}.");
                }
                LayoutAssertions.AssertCurrent(grid);

                var completedRow = Assert.IsAssignableFrom<IEnumerable<StatsRowViewModel>>(grid.ItemsSource)
                    .First(row => row.Word is not null);
                grid.ScrollIntoView(completedRow, grid.Columns.Single(column => Equals(column.Tag, "completion")));
                PageScreenshots.Settle(window);
                var completedChip = grid.GetVisualDescendants().OfType<MarkChip>()
                    .First(chip => chip.Text == "Completed");
                LayoutAssertions.AssertCurrent(grid);
                var completedText = Assert.Single(completedChip.GetVisualDescendants().OfType<CopyableTextBlock>());
                LayoutAssertions.AssertTextFits(completedText);
            }
            finally { window.Close(); }
        }, TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData(1040, 780)]
    [InlineData(1040, 1300)]
    public void DetailedStatisticsShowsSeveralRowsWithinScrollablePage(int width, int height)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData();
            try
            {
                window.Width = width;
                window.Height = height;
                workspace.CurrentPage = WorkspacePage.Timing;
                PageScreenshots.Settle(window);
                var page = Assert.Single(window.GetVisualDescendants().OfType<TimingPage>());
                var expander = Assert.Single(page.GetLogicalDescendants().OfType<Expander>(),
                    control => Equals(control.Header, "Detailed statistics"));
                expander.IsExpanded = true;
                PageScreenshots.Settle(window);
                var grid = Assert.Single(page.GetVisualDescendants().OfType<DataGrid>());
                var rows = Enumerable.Range(0, 2000).Select(index => new StatsRowViewModel(
                    JsonSerializer.SerializeToElement(new { form = $"word-{index}", elapsed_ns = 1000, attempts = 1, passes = 1 })))
                    .ToArray();
                grid.ItemsSource = rows;
                PageScreenshots.Settle(window);
                var pageScroll = Assert.Single(grid.GetVisualAncestors().OfType<ScrollViewer>());
                var rowsPresenter = Assert.Single(grid.GetVisualDescendants()
                    .OfType<Avalonia.Controls.Primitives.DataGridRowsPresenter>());
                var firstRow = grid.GetVisualDescendants().OfType<DataGridRow>().First();
                Assert.True(grid.Bounds.Height >= grid.MinHeight - 1,
                    $"The statistics table is only {grid.Bounds.Height}px tall at {width}×{height}.");
                Assert.True(rowsPresenter.Bounds.Height >= firstRow.Bounds.Height * 8,
                    $"The {rowsPresenter.Bounds.Height}px rows viewport shows fewer than eight " +
                    $"{firstRow.Bounds.Height}px rows at {width}×{height}.");
                Assert.InRange(grid.GetVisualDescendants().OfType<DataGridRow>().Count(), 8, 64);
                if (height <= 780)
                    Assert.True(pageScroll.Extent.Height > pageScroll.Viewport.Height,
                        "The Timing page should scroll to reach detailed statistics at this window height.");
                Assert.InRange(grid.GetVisualDescendants().OfType<DataGridRow>().Count(), 1, 64);
                grid.ScrollIntoView(rows[^1], grid.Columns[0]);
                PageScreenshots.Settle(window);
                Assert.Contains(grid.GetVisualDescendants().OfType<DataGridRow>(), row => ReferenceEquals(row.DataContext, rows[^1]));
                Assert.InRange(grid.GetVisualDescendants().OfType<DataGridRow>().Count(), 1, 40);
            }
            finally { window.Close(); }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void RecentProjectMenuKeepsOnePageAndCanReachTheLastProject()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData();
            try
            {
                workspace.RecentProjects.Clear();
                for (var index = 0; index < 2000; index++)
                    workspace.RecentProjects.Add(new RecentProjectViewModel(Path.Combine(Path.GetTempPath(), $"project-{index}.fwdata")));
                var button = window.FindControl<Button>("OpenRecentButton")!;
                var menu = Assert.IsType<MenuFlyout>(button.Flyout);
                var projectButton = window.FindControl<Button>("ProjectMenuButton")!;
                projectButton.Flyout!.ShowAt(projectButton);
                PageScreenshots.Settle(window);
                menu.ShowAt(button);
                PageScreenshots.Settle(window);
                var first = workspace.RecentProjects[0];
                for (var page = 0; page < 100; page++)
                {
                    Assert.InRange(menu.Items.Count, 20, 22);
                    Assert.Equal(workspace.RecentProjects.Skip(page * 20).Take(20),
                        window.RecentProjectItems.Select(item => item.CommandParameter));
                    if (page == 99) break;
                    var next = Assert.Single(menu.Items.OfType<MenuItem>(), item => Equals(item.Header, "Show 20 more"));
                    Assert.True(next.StaysOpenOnClick);
                    Assert.True(next.Focus());
                    next.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
                    PageScreenshots.Settle(window);
                    Assert.True(menu.IsOpen);
                    Assert.Contains(menu.Items.OfType<MenuItem>(), item => item.IsFocused);
                }
                Assert.Same(workspace.OpenRecentProjectCommand, window.RecentProjectItems[^1].Command);
                Assert.Same(workspace.RecentProjects[^1], window.RecentProjectItems[^1].CommandParameter);
                Assert.NotSame(first, window.RecentProjectItems[0].CommandParameter);
                var previous = Assert.Single(menu.Items.OfType<MenuItem>(), item => Equals(item.Header, "Previous 20"));
                previous.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
                PageScreenshots.Settle(window);
                Assert.Same(workspace.RecentProjects[1960], window.RecentProjectItems[0].CommandParameter);
            }
            finally { window.Close(); }
        }, TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData("MorphemeRow")]
    [InlineData("GrammarWarningPartsBlock")]
    public void CodeBuiltWrappingPanelsKeepOnePageOfLargeSources(string viewName)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var morph = new ParserReadingMorphViewModel(new SIL.Motif.Contract.Responses.ParserReadingMorph(
                "word", "gloss", "noun", null, false, null));
            var morphemes = new MorphemeRow { Morphs = Enumerable.Repeat(morph, 500).ToArray() };
            var parts = new GrammarWarningPartsBlock
            {
                Parts = Enumerable.Range(0, 500).Select(index => new SIL.Motif.Contract.Responses.GrammarWarningPart(
                    $"part-{index}", SIL.Motif.Contract.Responses.GrammarWarningPartRole.Text)).ToArray(),
            };
            var panel = viewName == "MorphemeRow" ? (Panel)morphemes : parts;
            var window = new Window { Width = 1240, Height = 780, Content = panel };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                {
                    for (var page = 0; page < 25; page++)
                    {
                        Assert.InRange(panel.Children.Count, 20, 22);
                        if (page == 24) break;
                        var next = Assert.Single(panel.Children.OfType<Button>(), button => Equals(button.Content, "Show 20 more"));
                        Assert.True(next.Focus());
                        next.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                        PageScreenshots.Settle(window);
                        Assert.Contains(panel.Children.OfType<Button>(), button => button.IsFocused);
                    }
                    var previous = Assert.Single(panel.Children.OfType<Button>(), button => Equals(button.Content, "Previous 20"));
                    previous.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal(22, panel.Children.Count);
                }
                PageScreenshots.Settle(window);
            }
            finally { window.Close(); }
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ListWordCardPagesAlignedSegmentsAndLargeSpans()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var morphs = Enumerable.Range(0, 500).Select(index => new SIL.Motif.Contract.Responses.ParserReadingMorph(
                $"part-{index}", "gloss", "noun", null, false, null)).ToArray();
            var card = new ListWordCard { DataContext = WordRowViewModel.NotParsed("word", fieldWorks: morphs) };
            var window = new Window { Width = 1240, Height = 780, Content = card };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var host = card.FindControl<StackPanel>("AlignmentHost")!;
                for (var page = 0; page < 25; page++)
                {
                    var grid = Assert.Single(card.GetVisualDescendants().OfType<Grid>(),
                        grid => grid.Classes.Contains("listCardAlignment"));
                    Assert.InRange(grid.ColumnDefinitions.Count, 3, 22);
                    Assert.Equal(20, card.GetVisualDescendants().OfType<Border>()
                        .Count(border => border.Classes.Contains("listCardMorph")));
                    Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == $"part-{page * 20}");
                    if (page == 24) break;
                    var next = Assert.Single(host.Children.OfType<Button>(), button => Equals(button.Content, "Show 20 more"));
                    Assert.True(next.Focus());
                    next.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    PageScreenshots.Settle(window);
                    Assert.Contains(host.Children.OfType<Button>(), button => button.IsFocused);
                }
                Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "part-499");
                card.DataContext = new WordRowViewModel(new SIL.Motif.Contract.Responses.WordRow(
                    "word", SIL.Motif.Contract.Responses.WordRowOutcome.Different, "Built something else",
                    SIL.Motif.Contract.Responses.WordRowTone.Problem)
                {
                    FieldWorksMorphemes = [new("whole", "gloss", "noun", null, false, null)],
                    PanGlossMorphemes = morphs,
                    PanGlossReadingCount = 1,
                });
                PageScreenshots.Settle(window);
                var span = Assert.Single(card.GetVisualDescendants().OfType<StackPanel>(),
                    panel => panel.Classes.Contains("listCardChips") && panel.Children.Count > 1);
                for (var page = 0; page < 25; page++)
                {
                    Assert.InRange(span.Children.Count, 20, 22);
                    if (page == 24) break;
                    var next = Assert.Single(span.Children.OfType<Button>(), button => Equals(button.Content, "Show 20 more"));
                    next.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    PageScreenshots.Settle(window);
                }
                Assert.Contains(span.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "part-499");
            }
            finally { window.Close(); }
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void WrappingCollectionChangesResetThePageAndPreserveEveryItem()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var source = new ObservableCollection<object>(Enumerable.Range(0, 500).Cast<object>());
            var items = new ProgressiveItemsControl { FullItemsSource = source };
            var window = new Window { Width = 1240, Height = 780, Content = items };
            try
            {
                window.Show();
                items.ShowItem(source[^1]);
                Assert.Contains(source[^1], items.Items);
                Assert.InRange(items.ItemCount, 20, 22);
                source.RemoveAt(499);
                Assert.Contains(source[0], items.Items);
                Assert.InRange(items.ItemCount, 20, 22);
                PageScreenshots.Settle(window);
                var next = Assert.Single(items.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Show 20 more"));
                Assert.True(next.Focus());
                next.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                PageScreenshots.Settle(window);
                Assert.Contains(items.GetVisualDescendants().OfType<Button>(), button => button.IsFocused);
                window.Content = null;
                PageScreenshots.Settle(window);
                source.Clear();
                window.Content = items;
                PageScreenshots.Settle(window);
                Assert.Empty(items.Items);
            }
            finally { window.Close(); }
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }

    private void AssertBounded(ItemsControl items, InventoryEntry entry)
    {
        var realized = items.GetRealizedContainers().Count();
        Assert.True(realized is > 0 and <= 64,
            $"{entry.View}/{entry.Source}: realized {realized} of {items.ItemCount} rows");
        var tree = AvaloniaScaleCounts.CaptureRealizedControls(items);
        var wordRows = tree.MotifControl(typeof(WordRow).FullName!);
        Assert.InRange(wordRows.Count, 0, 64);
        _output.WriteLine($"{entry.View}/{entry.Source}: {realized} realized rows; " +
            $"{wordRows.Count} realized Motif WordRows at depth {wordRows.MaximumDepth}");
    }

    private static Control Build(string name, WorkspaceShellViewModel workspace, MainWindow sample)
    {
        var texts = workspace.PageModel<TextsPageModel>();
        var trace = workspace.PageModel<TryWordPageModel>();
        if (name == "MainWindow")
        {
            var shell = (Control)sample.Content!;
            sample.Content = null;
            PageScreenshots.Settle(sample);
            shell.DataContext = workspace;
            return shell;
        }
        return name switch
        {
            "ComparePanel" => new ComparePanel(workspace.Assess.Compare),
            "DiagnosticPanel" => new DiagnosticPanel(trace.Diagnostics),
            "DifferencePanel" => new DifferencePanel(workspace.Assess.Difference),
            "ExpertTracePanel" => new ExpertTracePanel { DataContext = trace.Trace },
            "GrammarPanel" => new GrammarPanel(workspace.PageModel<WarningsPageModel>().Grammar),
            "HandoffPanel" => new HandoffPanel(workspace.PageModel<AiHandoffPageModel>().Handoff),
            "Inspector" => new Inspector { DataContext = workspace.Inspector },
            "KeyboardShortcutsFlyoutView" => new KeyboardShortcutsFlyoutView(),
            "MiniMatrix" => new MiniMatrix { DataContext = workspace.Assess.Compare },
            "Pages/OverviewPage" => new OverviewPage(workspace.PageModel<OverviewPageModel>()),
            "Pages/TimingPage" => new TimingPage(workspace.PageModel<TimingPageModel>()),
            "RefusalBlock" => new RefusalBlock { DataContext = CompileRefusal(1) },
            "ResultsInTextPanel" => new ResultsInTextPanel(texts.ResultsInText),
            "ReviewPanel" => new ReviewPanel(workspace.PageModel<ReviewPageModel>()),
            "SelectionPanel" => new SelectionPanel(workspace.Selection, texts.Words),
            "SetupDialog" => new SetupDialog { DataContext = workspace.Context.Setup },
            "StatisticsPanel" => new StatisticsPanel(workspace.PageModel<TimingPageModel>().Statistics),
            "TextWordsPanel" => new TextWordsPanel(texts.Words),
            "TextsListsPanel" => new TextsListsPanel(texts.TextsLists),
            "TraceAnalysesView" => new TraceAnalysesView { DataContext = trace.Trace },
            "TryWordPanel" => new TryWordPanel(trace),
            "WordRow" => new WordRow { Row = workspace.Assess.Words.AllRows[0].WordRow },
            "WordRowCard" => new WordRowCard { DataContext = workspace.Assess.Compare.Words[0] },
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
        };
    }

    private static WindowRefusal CompileRefusal(int count)
    {
        var issues = Enumerable.Range(0, count).Select(index => new ParserCompileIssue(
            "grammar.environment.invalid", "invalidSource", "11111111-1111-1111-1111-111111111111", "PhoneEnv",
            $"Allomorph á-{index} has an invalid environment.", $"Repair environment {index} in FieldWorks.",
            "MoForm")).ToArray();
        return WindowRefusal.From(new Refusal(RefusalCodes.AssessParserUnavailable, FailureReason.Refused,
            "PanGloss can't use this grammar.", parserDiagnostic: new ParserCompileDiagnostic(
                "PanGloss can't use this grammar.", issues, "Fatal grammar issues")));
    }

    private static IEnumerable<Control> Fragments(Control root, int depth = 0)
    {
        yield return root;
        if (depth >= 8) yield break;
        foreach (var items in Controls(root).OfType<ItemsControl>())
        {
            if (items.ItemTemplate?.Build(null) is { } row)
                foreach (var fragment in Fragments(row, depth + 1)) yield return fragment;
        }
        foreach (var content in Controls(root).OfType<ContentControl>())
        {
            if (content.ContentTemplate?.Build(null) is { } row)
                foreach (var fragment in Fragments(row, depth + 1)) yield return fragment;
        }
        foreach (var button in Controls(root).OfType<Button>())
        {
            if (button.Flyout is Flyout { Content: Control content })
                foreach (var fragment in Fragments(content, depth + 1)) yield return fragment;
        }
    }

    private static IEnumerable<Control> Controls(Control root) => new[] { root }
        .Concat(root.GetLogicalDescendants().OfType<Control>());

    private static InventoryEntry[] Inventory() => JsonSerializer.Deserialize<InventoryEntry[]>(
        File.ReadAllText(Path.Combine(AppDirectory(), "..", "..", "tests", "SIL.Motif.Tests.App", "App",
            "ProgressiveDisplayInventory.json")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    private static string AppDirectory()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Motif.sln"))) root = root.Parent;
        Assert.NotNull(root);
        return Path.Combine(root.FullName, "src", "SIL.Motif.App");
    }

    private sealed record InventoryEntry(string View, string Name, string Source, string Mode, string Control);
}
