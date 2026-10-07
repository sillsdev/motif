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
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Tests.App.Walkthrough;
using PresentationWordRow = SIL.Motif.App.Controls.WordPresentation.WordRow;
using TraceStep = SIL.Motif.Contract.Responses.TraceStep;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProgressiveDisplayTests
{
    private const int WordRowControlCeiling = 64;
    private const int WordRowDepthCeiling = 10;
    private const int WordCardControlCeiling = 64;
    private const int WordCardDepthCeiling = 18;
    private const int WordStripControlCeiling = 40;
    private const int WordStripDepthCeiling = 9;
    private readonly ITestOutputHelper _output;

    public ProgressiveDisplayTests(ITestOutputHelper output) => _output = output;

    private static readonly string[] ViewNames =
    [
        "ComparePanel", "DiagnosticPanel", "DifferencePanel", "ExpertTracePanel", "GrammarPanel", "HandoffPanel",
        "Inspector", "MainWindow", "MiniMatrix", "Pages/OverviewPage", "Pages/TimingPage",
        "RefusalBlock", "ResultsInTextPanel", "ReviewWordRow",
        "ReviewPanel", "SelectionPanel", "SettingsPopupView", "SetupDialog", "StatisticsPanel", "TextWordsPanel", "TextsListsPanel",
        "TraceAnalysesView", "TryWordPanel", "WordCard",
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
                if (viewName == "ResultsInTextPanel" && entries.Any(entry =>
                        entry.Name == "ResultsInTextPanelFixChoicesItems"))
                {
                    window.Content = root;
                    window.Show();
                    PageScreenshots.Settle(window);
                    var fixButton = root.GetVisualDescendants().OfType<Button>().First(button =>
                        Avalonia.Automation.AutomationProperties.GetName(button) == "Fix actions from the word strip");
                    var strip = Assert.IsType<WordStripToken>(fixButton.GetVisualAncestors()
                        .OfType<WordStripToken>().First());
                    var flyout = Assert.IsType<Flyout>(fixButton.Flyout);
                    var menu = Assert.IsAssignableFrom<Control>(flyout.ContentTemplate!.Build(flyout.Content));
                    menu.DataContext = flyout.Content;
                    window.Content = menu;
                    PageScreenshots.Settle(window);
                    var choices = Assert.Single(menu.GetVisualDescendants().OfType<ItemsControl>(), items =>
                        items.Name == "ResultsInTextPanelFixChoicesItems");
                    targets.Add(choices.Name!, (choices, menu));
                    window.Content = null;
                    PageScreenshots.Settle(window);
                }
                foreach (var entry in entries)
                {
                    if (entry.Mode == "grid") continue;
                    Assert.True(targets.TryGetValue(entry.Name, out var target), $"Missing {viewName}/{entry.Name}");
                    var (items, fragment) = target;
                    if (entry.Mode is "finite" or "inline")
                    {
                        Assert.InRange(items.ItemCount, 0, entry.Mode == "finite" ? 32 : 128);
                        if (entry.View == "SettingsPopupView" && entry.Mode == "finite")
                            Assert.False(string.IsNullOrWhiteSpace(entry.Reason),
                                $"{entry.View}/{entry.Source}: a finite collection needs a bound reason.");
                        _output.WriteLine($"{entry.View}/{entry.Source}: fixed collection, {items.ItemCount} items");
                        continue;
                    }
                    if (entry.Mode is "tree" or "grid") continue;
                    var isFragment = !ReferenceEquals(root, fragment);
                    var externalScroll = isFragment || viewName is "DiagnosticPanel" or "ExpertTracePanel" or
                        "TraceAnalysesView";
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
        var viewsDirectory = Path.Combine(AppDirectory(), "Views");
        var wordCardPath = Path.Combine(AppDirectory(), "Controls", "WordPresentation", "WordCard.axaml");
        var wordStripPath = Path.Combine(AppDirectory(), "Controls", "WordPresentation", "WordStripToken.axaml");
        var actual = Directory.EnumerateFiles(viewsDirectory, "*.axaml", SearchOption.AllDirectories)
            .SelectMany(path => CollectionEntries(path,
                Path.GetRelativePath(viewsDirectory, path).Replace('\\', '/')[..^6]))
            .Concat(CollectionEntries(wordCardPath, "WordCard"))
            .Concat(CollectionEntries(wordStripPath, "ResultsInTextPanel"))
            .Concat(["ReviewWordRow|WordRowPendingAfterWordsItems|ProgressiveItemsControl|AfterWords"])
            .Order().ToArray();

        IEnumerable<string> CollectionEntries(string path, string view) => XDocument.Load(path).Descendants()
                .Where(node => collections.Contains(node.Name.LocalName) &&
                    (node.Attribute("ItemsSource") ?? node.Attribute("FullItemsSource")) is not null)
                .Select(node =>
                {
                    var binding = (node.Attribute("ItemsSource") ?? node.Attribute("FullItemsSource"))!.Value;
                    var source = binding.Split([' ', ',', '}'], StringSplitOptions.RemoveEmptyEntries)[1];
                    return $"{view}|{node.Attribute(names + "Name")?.Value}|{node.Name.LocalName}|{source}";
                });
        Assert.Equal(actual, inventory.Select(entry => $"{entry.View}|{entry.Name}|{entry.Control}|{entry.Source}").Order());
        Assert.Equal(ViewNames.Order(), inventory.Select(entry => entry.View).Distinct().Order());
        var codeBuilt = Directory.EnumerateFiles(Path.Combine(AppDirectory(), "Views"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(
                Path.Combine(AppDirectory(), "Controls", "WordPresentation"), "*.cs", SearchOption.AllDirectories))
            .Where(path => File.ReadAllText(path).Contains("Children.Add(", StringComparison.Ordinal) ||
                File.ReadAllText(path).Contains("ProgressivePanel.Populate(this", StringComparison.Ordinal))
            .Select(path => Path.GetFileNameWithoutExtension(path).Replace(".axaml", string.Empty)).Order().ToArray();
        Assert.Equal(new[] { "FilterChip", "GrammarWarningPartsBlock", "HeatCell", "MainWindow", "MarkChip", "MorphemePanel", "NamedMark", "OutcomeBar", "ProgressivePanel", "WordCard", "WordRow", "WordRowHeader" },
            codeBuilt);
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
            var card = new WordCard
            {
                Document = new WordCardDocument(new WordPresentationKey("scale:empty-card"), 0, []),
            };
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
                var panel = Assert.Single(sampleWindow.GetVisualDescendants().OfType<ResultsInTextPanel>());
                var strip = Assert.Single(panel.GetVisualDescendants().OfType<WordStripToken>(),
                    candidate => ReferenceEquals(candidate.Data, token));
                var stripTree = AvaloniaScaleCounts.CaptureSubtree(strip);

                WriteSubtree("WordRow", rowTree, row);
                WriteSubtree("WordCard", cardTree, card);
                WriteSubtree("WordStripToken", stripTree, strip);
                AssertSubtreeBudget("WordRow", rowTree, WordRowControlCeiling, WordRowDepthCeiling);
                AssertSubtreeBudget("WordCard", cardTree, WordCardControlCeiling, WordCardDepthCeiling);
                AssertSubtreeBudget("WordStripToken", stripTree, WordStripControlCeiling, WordStripDepthCeiling);
                Assert.Contains("Avalonia.Controls.Grid", rowTree.ControlTypes.Select(type => type.Key));
                Assert.Contains("Avalonia.Controls.Border", rowTree.ControlTypes.Select(type => type.Key));
                Assert.Contains("Avalonia.Controls.StackPanel", rowTree.ControlTypes.Select(type => type.Key));
                Assert.Contains(rowTree.ControlTypes, type => type.Key.EndsWith("TextBlock", StringComparison.Ordinal));
                Assert.NotEmpty(rowTree.TemplateParts);
                Assert.NotEmpty(cardTree.TemplateParts);
                Assert.Equal(rowTree.ControlCount, rowTree.ControlTypes.Sum(type => type.Count));
                Assert.Equal(cardTree.ControlCount, cardTree.ControlTypes.Sum(type => type.Count));
                Assert.Equal(stripTree.ControlCount, stripTree.ControlTypes.Sum(type => type.Count));
                Assert.Equal(stripTree.ControlCount, stripTree.ControlTypes.Sum(type => type.Count));
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
                var cells = Assert.Single(row.GetVisualDescendants().OfType<Grid>(),
                    grid => grid.Classes.Contains("wordPresentationShell"));
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

    [Fact]
    public void ScrollingWordRowsBuildsEachRecycledRowOncePerWord()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, sampleWindow) = await PageScreenshots.OpenOverSampleData();
            var facts = workspace.Assess.Words.Rows.Select(row => row.WordRow).ToArray();
            Assert.NotEmpty(facts);
            var words = Enumerable.Range(0, 240).Select(index => new WordPresentation(
                new WordPresentationKey($"scroll:{index}"), 0, facts[index % facts.Length], WordListOwner.WordList)).ToArray();
            var list = new ListBox
            {
                ItemsSource = words,
                ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
                ItemTemplate = new FuncDataTemplate<WordPresentation>((_, _) => new PresentationWordRow
                {
                    [!PresentationWordRow.DataProperty] = new Avalonia.Data.Binding(),
                }),
            };
            var window = new Window { Width = 1240, Height = 600, Content = list };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();
                var seen = new Dictionary<PresentationWordRow, (WordPresentation? Data, int Builds)>(
                    ReferenceEqualityComparer.Instance);
                var steps = 0;
                for (var offset = 0d; offset < scroll.Extent.Height - scroll.Viewport.Height && steps < 30;
                     offset += scroll.Viewport.Height / 3, steps++)
                {
                    scroll.Offset = scroll.Offset.WithY(offset);
                    PageScreenshots.Settle(window);
                    foreach (var row in list.GetVisualDescendants().OfType<PresentationWordRow>())
                    {
                        if (seen.TryGetValue(row, out var before))
                        {
                            var allowed = ReferenceEquals(before.Data, row.Data) ? 0 : 1;
                            Assert.True(row.CellBuildCount - before.Builds <= allowed,
                                $"Row {row.Data?.Key.Value} built its cells {row.CellBuildCount - before.Builds} times " +
                                $"in one scroll step; it may build them only when its word changes.");
                        }
                        seen[row] = (row.Data, row.CellBuildCount);
                        foreach (var panel in row.GetVisualDescendants().OfType<MorphemePanel>())
                            Assert.True(panel.PartBuildCount == 1,
                                $"A morpheme panel in {row.Data?.Key.Value} built its parts {panel.PartBuildCount} times.");
                    }
                }
                Assert.True(steps > 3, "The list should scroll through several screens of recycled rows.");
                Assert.Contains(seen.Keys, row => row.GetVisualDescendants().OfType<MorphemePanel>().Any());
            }
            finally
            {
                window.Close();
                sampleWindow.Close();
            }
        }, TimeSpan.FromMinutes(1));
    }

    private static PresentationWordRow FixedWordRow(WorkspaceShellViewModel workspace)
    {
        var model = Assert.Single(workspace.Assess.Words.Rows, row => row.Word == "Sungura");
        var key = new WordPresentationKey("scale:row");
        var control = new PresentationWordRow
        {
            Data = new WordPresentation(key, 0, model.WordRow, WordListOwner.Matrix),
            State = new WordInteractionState(key),
        };
        Assert.Equal("Sungura", control.Data?.Facts.Word);
        Assert.False(control.State?.IsOpen);
        Assert.False(control.State?.IsChecked);
        return control;
    }

    private void AssertSubtreeBudget(string rootName, ControlSubtreeSnapshot tree, int maximumControls, int maximumDepth)
    {
        _output.WriteLine($"Visual subtree | {rootName} | {tree.ControlCount} Controls | depth {tree.MaximumDepth} | " +
            $"budget {maximumControls}/{maximumDepth}");
        Assert.InRange(tree.ControlCount, 1, maximumControls);
        Assert.InRange(tree.MaximumDepth, 0, maximumDepth);
    }

    private void WriteSubtree(string rootName, ControlSubtreeSnapshot tree, Control root)
    {
        var types = string.Join(", ", tree.ControlTypes.Select(type => $"{type.Key}={type.Count}"));
        var owners = tree.TemplateParts.Count == 0 ? "none" : string.Join(", ", tree.TemplateParts.Select(part =>
            $"{part.Owner}={part.Count}@{part.MaximumDepth}"));
        var deepest = new[] { root }.Concat(root.GetVisualDescendants().OfType<Control>())
            .OrderByDescending(control => ReferenceEquals(control, root) ? 0 :
                control.GetVisualAncestors().TakeWhile(ancestor => !ReferenceEquals(ancestor, root)).Count() + 1)
            .First();
        var path = string.Join(" → ", deepest.GetVisualAncestors()
            .TakeWhile(ancestor => !ReferenceEquals(ancestor, root)).OfType<Control>()
            .Reverse().Prepend(root).Append(deepest)
            .Select(control => $"{control.GetType().Name}{(string.IsNullOrEmpty(control.Name) ? "" : $"#{control.Name}")}"));
        _output.WriteLine($"Visual subtree types | {rootName} | {types}");
        _output.WriteLine($"Template-owned parts | {rootName} | {owners}");
        _output.WriteLine($"Deepest visual path | {rootName} | {path}");
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
                var page = workspace.PageModel<ReviewPageModel>();
                var panel = new ReviewPanel(page);
                var window = new Window { Width = 1240, Height = 780, Content = panel };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var items = panel.FindControl<ListBox>("ReviewItems")!;
                var groups = page.ReviewGroups;
                Assert.Equal(2, groups.Count);
                Assert.All(groups, group => Assert.Equal(100, group.Items.Count));
                AssertBoundedRows("first");
                items.ScrollIntoView(EntryIndex(groups[0].Items[0]));
                PageScreenshots.Settle(window);
                var first = ChangeRow(groups[0].Items[0]);
                first.FocusWord();
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

                items.ScrollIntoView(EntryIndex(groups[0].Items[99]));
                PageScreenshots.Settle(window);
                ChangeRow(groups[0].Items[99]).FocusWord();
                Press(Key.Down);
                AssertFocus(1, 0);
                Press(Key.Up);
                AssertFocus(0, 99);
                AssertBoundedRows("group boundary");

                Press(Key.Home);
                first = ChangeRow(groups[0].Items[0]);
                first.State = first.State! with { IsOpen = true };
                PageScreenshots.Settle(window);
                Assert.True(groups[0].Items[0].Listed!.IsOpen);
                Press(Key.End);
                AssertFocus(1, 99);
                Press(Key.Home);
                AssertFocus(0, 0);
                Assert.True(ChangeRow(groups[0].Items[0]).State?.IsOpen);
                AssertBoundedRows("expanded return");

                void Press(Key key)
                {
                    window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
                    window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
                    PageScreenshots.Settle(window);
                }

                int EntryIndex(ChangeViewModel change) => page.ReviewEntries
                    .Select((item, index) => (item, index))
                    .Single(pair => ReferenceEquals(pair.item.Change, change)).index;

                PresentationWordRow ChangeRow(ChangeViewModel change) => Assert.Single(items.ContainerFromIndex(EntryIndex(change))!
                    .GetVisualDescendants().OfType<PresentationWordRow>());

                void AssertFocus(int groupIndex, int rowIndex)
                {
                    var rows = panel.GetVisualDescendants().OfType<PresentationWordRow>().ToArray();
                    var focusedRows = rows.Where(row => row.IsFocused).ToArray();
                    Assert.True(focusedRows.Length == 1,
                        $"Expected focus on {groupIndex}/{rowIndex}; realized {string.Join(',', rows.Select(row => row.Data?.Key.Value))}");
                    var focused = focusedRows[0];
                    Assert.Equal($"change:{groups[groupIndex].Items[rowIndex].ChangeId}", focused.Data?.Key.Value);
                    var top = focused.TranslatePoint(default, window)!.Value.Y;
                    Assert.InRange(top, 0, window.Bounds.Height - focused.Bounds.Height);
                }

                void AssertBoundedRows(string position)
                {
                    var count = panel.GetVisualDescendants().OfType<PresentationWordRow>().Count();
                    Assert.InRange(count, 1, 16);
                    Assert.InRange(items.GetRealizedContainers().Count(), 1, 18);
                    _output.WriteLine($"Review {position}: {count} shared word rows for 200 changes");
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

    [Fact]
    public void CodeBuiltWrappingPanelsKeepOnePageOfLargeSources()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var parts = new GrammarWarningPartsBlock
            {
                Parts = Enumerable.Range(0, 500).Select(index => new SIL.Motif.Contract.Responses.GrammarWarningPart(
                    $"part-{index}", SIL.Motif.Contract.Responses.GrammarWarningPartRole.Text)).ToArray(),
            };
            var panel = parts;
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
        var wordRows = tree.MotifControl(typeof(PresentationWordRow).FullName!);
        var presentationRows = tree.MotifControl(typeof(PresentationWordRow).FullName!);
        Assert.InRange(wordRows.Count + presentationRows.Count, 0, 64);
        _output.WriteLine($"{entry.View}/{entry.Source}: {realized} realized rows; " +
            $"{wordRows.Count + presentationRows.Count} realized Motif WordRows at depth " +
            $"{Math.Max(wordRows.MaximumDepth, presentationRows.MaximumDepth)}");
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
            "SettingsPopupView" => BuildSettingsPopup(workspace),
            "MiniMatrix" => new MiniMatrix { DataContext = workspace.Assess.Compare },
            "Pages/OverviewPage" => new OverviewPage(workspace.PageModel<OverviewPageModel>()),
            "Pages/TimingPage" => new TimingPage(workspace.PageModel<TimingPageModel>()),
            "ReviewWordRow" => new PresentationWordRow
            {
                Data = new WordPresentation(new WordPresentationKey("review:scale"), 1,
                    workspace.Assess.Words.AllRows[0].WordRow, WordListOwner.Review,
                    PendingChange: new WordPendingChange(true, false,
                        Enumerable.Range(0, 500).Select(index =>
                            new WordCardSentenceToken($"sentence-{index}", null, index == 499)).ToArray(),
                        "Check again", "Undo")),
            },
            "RefusalBlock" => new RefusalBlock { DataContext = CompileRefusal(1) },
            "ResultsInTextPanel" => new ResultsInTextPanel(texts.ResultsInText),
            "ReviewPanel" => new ReviewPanel(workspace.PageModel<ReviewPageModel>()),
            "SelectionPanel" => new SelectionPanel(workspace.Selection, texts.Words),
            "SetupDialog" => new SetupDialog { DataContext = workspace.Context.Setup },
            "StatisticsPanel" => new StatisticsPanel(workspace.PageModel<TimingPageModel>().Statistics),
            "TextWordsPanel" => BuildTextWordsPanel(texts.Words),
            "TextsListsPanel" => new TextsListsPanel(texts.TextsLists),
            "TraceAnalysesView" => new TraceAnalysesView { DataContext = trace.Trace },
            "TryWordPanel" => new TryWordPanel(trace),
            "WordCard" => BuildWordCard(workspace),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
        };
    }

    private static WordCard BuildWordCard(WorkspaceShellViewModel workspace)
    {
        var comparison = workspace.Assess.Compare.Words[0];
        var morphs = Enumerable.Range(0, 500).Select(index => new ParserReadingMorphViewModel(
            new ParserReadingMorph($"part-{index}", $"gloss-{index}", "n", null, false, null))).ToArray();
        var occurrences = Enumerable.Range(1, 12).Select(line => new WordOccurrenceRowViewModel(
            new WordOccurrence(Guid.NewGuid(), "Sample", line, $"Occurrence {line}.", "approved", null))).ToArray();
        return new WordCard
        {
            Document = new WordCardDocument(new WordPresentationKey("scale:card"), 0,
                [new WordCardPendingChange("Scale test", true, true, true, morphs, false, [], [], false,
                    workspace.Context), new WordCardAnalysis(comparison, comparison.CardToken),
                    new WordCardOccurrences(occurrences)]),
        };
    }

    private static TextWordsPanel BuildTextWordsPanel(TextWordsViewModel words)
    {
        if (words.Rows.FirstOrDefault() is { } first) first.Listed.IsOpen = true;
        return new TextWordsPanel(words);
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

    private static Control BuildSettingsPopup(WorkspaceShellViewModel workspace)
    {
        var viewModel = new SettingsViewModel(workspace);
        viewModel.OpenGroup(SettingsGroup.KeyboardShortcuts);
        return new SettingsPopupView { DataContext = viewModel };
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

    private sealed record InventoryEntry(string View, string Name, string Source, string Mode, string Control,
        string? Reason = null);
}
