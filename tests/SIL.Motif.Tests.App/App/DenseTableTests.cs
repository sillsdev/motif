using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using SIL.Motif.App.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class DenseTableTests
{
    private static double Size(string key) => Assert.IsType<double>(Application.Current!.FindResource(key));

    private static Color BrushColor(string key)
    {
        Assert.True(Application.Current!.TryGetResource(key, ThemeVariant.Light, out var resource),
            $"{key} does not resolve in the light theme");
        return Assert.IsAssignableFrom<ISolidColorBrush>(resource).Color;
    }

    private static Thickness Inset(string key) => Assert.IsType<Thickness>(Application.Current!.FindResource(key));

    private static void Press(Window window, Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable) is not { Length: > 0 } folder) return;
        Directory.CreateDirectory(folder);
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Table frame was not rendered.");
        frame.Save(Path.Combine(folder, name + ".png"), PngBitmapEncoderOptions.Default);
    }

    [Fact]
    public void WarningsUseGroupedDenseRowsWithVisibleActionsAndKeyboardDetails()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var grammar = new GrammarViewModel(new FakeCommandClient());
            var environment = new GrammarWarning(GrammarDiagnosticLevel.Warning, "Unreadable environment",
                [new("environment", GrammarWarningPartRole.Text),
                 new("e2 (/ _ [C])", GrammarWarningPartRole.Object, "11111111-1111-1111-1111-111111111111", "PhEnvironment")],
                [new("cannot be read", GrammarWarningPartRole.Text)], "warning: environment cannot be read")
            {
                Code = "grammar.environment.unreadable",
                Group = "Unreadable environment",
                Description = "PanGloss cannot read this environment.",
            };
            var affix = new GrammarWarning(GrammarDiagnosticLevel.Information, "Unused affix",
                [new("affix", GrammarWarningPartRole.Text),
                 new("-pa", GrammarWarningPartRole.Object, "22222222-2222-2222-2222-222222222222", "MoAffixForm")],
                [new("is not used", GrammarWarningPartRole.Text)], "info: affix is not used")
            {
                Code = "grammar.affix.unused",
                Group = "Unused affix",
                Description = "PanGloss did not use this affix.",
            };
            grammar.LoadStored("dense-table.fwdata", new GrammarCheckResponse(
                [environment, environment, affix], HasBaseline: true));
            var panel = new GrammarPanel(grammar);
            var window = new Window { Content = panel, Width = 640, Height = 780 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);

                var list = Assert.IsType<ListBox>(panel.FindControl<ListBox>("GrammarPanelRowsItems"));
                var details = Assert.IsType<ItemsControl>(panel.FindControl<ItemsControl>("GrammarPanelSelectionDetailsItems"));
                Assert.DoesNotContain(list, details.GetVisualAncestors());
                Assert.Empty(details.Items);
                Assert.IsType<VirtualizingStackPanel>(list.ItemsPanelRoot);
                Assert.Equal(["Environments", "Affixes"], grammar.Warnings.TableEntries
                    .OfType<DenseTableGroup>().Select(header => header.Label));

                var containers = list.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
                var group = Assert.Single(containers, item => item.DataContext is DenseTableGroup header &&
                    header.Label == "Environments");
                var row = Assert.Single(containers, item => item.DataContext is GrammarWarningRowViewModel warning &&
                    warning.Warning.Code == "grammar.environment.unreadable");
                Assert.Equal(Size("Component.DenseTable.GroupHeaderHeight"), group.Bounds.Height);
                Assert.False(group.Focusable);
                Assert.True(group.IsHitTestVisible);
                Assert.True(group.IsEnabled);
                Assert.Equal("2 findings", Assert.IsType<DenseTableGroup>(group.DataContext).CountText);
                Assert.Equal(Size("Component.DenseTable.ColumnHeaderHeight"), Assert.Single(
                    panel.GetVisualDescendants().OfType<Border>(),
                    border => border.Classes.Contains("denseTableColumnHeader")).Bounds.Height);
                var rowBorder = Assert.Single(row.GetVisualDescendants().OfType<Border>(),
                    border => border.Classes.Contains("denseTableRow"));
                Assert.Equal(Size("Component.DenseTable.RowHeight"), rowBorder.Bounds.Height);
                Assert.Equal(Inset("Intent.Stroke.DividerBottom"), rowBorder.BorderThickness);
                Assert.Equal(2, Assert.IsType<GrammarWarningRowViewModel>(row.DataContext).RepeatCount);

                var rowModel = Assert.IsType<GrammarWarningRowViewModel>(row.DataContext);
                Assert.False(row.IsPointerOver);
                var action = Assert.Single(row.GetVisualDescendants().OfType<Button>(), button => button.Classes.Contains("denseTableAction"));
                var menu = Assert.Single(row.GetVisualDescendants().OfType<Button>(), button => button.Classes.Contains("denseTableMenu"));
                Assert.True(action.IsEffectivelyVisible);
                Assert.True(menu.IsEffectivelyVisible);
                Assert.True(action.IsTabStop);
                Assert.True(menu.IsTabStop);
                Assert.Equal(Size("Component.DenseTable.MenuSize"), menu.Bounds.Width);
                Assert.Equal(Size("Component.DenseTable.MenuSize"), menu.Bounds.Height);
                Assert.Equal($"Actions for {rowModel.RowTitleText}", AutomationProperties.GetName(menu));
                Assert.Equal($"Details for {rowModel.RowTitleText}", AutomationProperties.GetName(action));
                var flyout = Assert.IsType<Flyout>(menu.Flyout);
                flyout.ShowAt(menu);
                PageScreenshots.Settle(window);
                Assert.True(flyout.IsOpen);
                var flyoutContent = Assert.IsAssignableFrom<Control>(flyout.Content);
                var openDetails = Assert.Single(flyoutContent.GetVisualDescendants().OfType<Button>(), button =>
                    button.Content is string text && text == "Open details");
                HeadlessClick.Click(window, openDetails, "Open details");
                PageScreenshots.Settle(window);
                Assert.True(rowModel.IsOpen);
                Assert.Same(rowModel, grammar.Warnings.SelectedTableEntry);
                Assert.Same(rowModel, Assert.Single(details.Items));
                Assert.Single(grammar.Warnings.TableEntries.OfType<GrammarWarningRowViewModel>(), item => item.IsOpen);
                rowModel.IsOpen = false;
                flyout.Hide();

                var secondRow = Assert.Single(containers, item => item.DataContext is GrammarWarningRowViewModel warning &&
                    warning.Warning.Code == "grammar.affix.unused");
                var secondAction = Assert.Single(secondRow.GetVisualDescendants().OfType<Button>(),
                    button => button.Classes.Contains("denseTableAction"));
                var secondMenu = Assert.Single(secondRow.GetVisualDescendants().OfType<Button>(),
                    button => button.Classes.Contains("denseTableMenu"));
                Assert.True(secondAction.IsEffectivelyVisible);
                Assert.True(secondMenu.IsEffectivelyVisible);
                Assert.True(secondAction.IsTabStop);
                Assert.True(secondMenu.IsTabStop);

                grammar.Warnings.SelectedTableEntry = null;
                Dispatcher.UIThread.RunJobs();

                Assert.Empty(details.Items);
                foreach (var scroll in window.GetVisualDescendants().OfType<ScrollViewer>())
                    Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1,
                        $"{scroll.Name}: extent {scroll.Extent.Width} exceeds viewport {scroll.Viewport.Width} at 640");
                Assert.True(panel.Bounds.Width <= window.Width);

                IInputElement? Focused() => TopLevel.GetTopLevel(list)!.FocusManager!.GetFocusedElement();
                ListBoxItem Container(object item) => Assert.IsType<ListBoxItem>(list.ContainerFromItem(item));

                list.Focus(NavigationMethod.Tab);
                Dispatcher.UIThread.RunJobs();
                var first = Assert.IsType<GrammarWarningRowViewModel>(grammar.Warnings.SelectedTableEntry);
                Assert.Same(Container(first), Focused());
                Assert.False(first.IsOpen);
                Press(window, Key.Down, PhysicalKey.ArrowDown);
                var second = Assert.IsType<GrammarWarningRowViewModel>(grammar.Warnings.SelectedTableEntry);
                Assert.NotSame(first, second);
                Assert.Same(Container(second), Focused());
                Press(window, Key.Up, PhysicalKey.ArrowUp);
                Assert.Same(first, grammar.Warnings.SelectedTableEntry);
                Assert.Same(Container(first), Focused());

                var ring = Assert.Single(Container(first).GetVisualDescendants().OfType<Border>(),
                    border => border.Classes.Contains("denseTableFocusRing"));
                var idleRing = Assert.Single(Container(second).GetVisualDescendants().OfType<Border>(),
                    border => border.Classes.Contains("denseTableFocusRing"));
                Assert.Equal(BrushColor("Intent.Focus"), Assert.IsAssignableFrom<ISolidColorBrush>(ring.BorderBrush).Color);
                Assert.NotEqual(BrushColor("Intent.Selected.Fill"), BrushColor("Intent.Focus"));
                Assert.Equal(BrushColor("Intent.Clear"), Assert.IsAssignableFrom<ISolidColorBrush>(idleRing.BorderBrush).Color);
                Assert.Equal(ring.BorderThickness, idleRing.BorderThickness);
                Assert.Equal(Inset("Intent.Stroke.DividerBottom"), Assert.Single(
                    Container(first).GetVisualDescendants().OfType<Border>(),
                    border => border.Classes.Contains("denseTableRow")).BorderThickness);

                Press(window, Key.Enter, PhysicalKey.Enter);
                Assert.True(first.IsOpen);
                Assert.Same(first, Assert.Single(details.Items));

                var focusedMenu = Assert.Single(Container(first).GetVisualDescendants().OfType<Button>(),
                    button => button.Classes.Contains("denseTableMenu"));
                list.Focus(NavigationMethod.Directional);
                Container(first).Focus(NavigationMethod.Directional);
                Press(window, Key.Down, PhysicalKey.ArrowDown, RawInputModifiers.Alt);
                Assert.True(Assert.IsType<Flyout>(focusedMenu.Flyout).IsOpen);
                focusedMenu.Flyout.Hide();
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void HeaderAndRowsShareColumnOffsets()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var grammar = new GrammarViewModel(new FakeCommandClient());
            var warning = new GrammarWarning(GrammarDiagnosticLevel.Warning, "Unreadable environment",
                [new("environment", GrammarWarningPartRole.Text)],
                [new("cannot be read", GrammarWarningPartRole.Text)], "warning: environment cannot be read")
            {
                Code = "grammar.environment.unreadable",
                Group = "Unreadable environment",
            };
            var info = new GrammarWarning(GrammarDiagnosticLevel.Information, "Unused affix",
                [new("affix", GrammarWarningPartRole.Text)],
                [new("is not used", GrammarWarningPartRole.Text)], "info: affix is not used")
            {
                Code = "grammar.affix.unused",
                Group = "Unused affix",
            };
            grammar.LoadStored("dense-columns.fwdata", new GrammarCheckResponse([warning, info], HasBaseline: true));
            var panel = new GrammarPanel(grammar);
            var window = new Window { Content = panel, Width = 640, Height = 780 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);

                var list = Assert.IsType<ListBox>(panel.FindControl<ListBox>("GrammarPanelRowsItems"));
                var header = Assert.Single(panel.GetVisualDescendants().OfType<Border>(),
                    border => border.Classes.Contains("denseTableColumnHeader"));
                var rows = list.GetVisualDescendants().OfType<Border>()
                    .Where(border => border.Classes.Contains("denseTableRow")).ToArray();
                Assert.True(rows.Length >= 2);

                Grid Grid(Border border) => Assert.Single(border.GetVisualDescendants().OfType<Grid>(),
                    candidate => candidate.ColumnDefinitions.Count == 5);

                void AssertTableTracks(Grid grid)
                {
                    var widths = grid.ColumnDefinitions.Select(column => column.ActualWidth).ToArray();
                    Assert.Equal(new GridLength(Size("Component.DenseTable.Column.NarrowWidth")), grid.ColumnDefinitions[0].Width);
                    Assert.True(widths[1] >= Size("Component.DenseTable.Column.NarrowWidth"));
                    Assert.Equal(widths[1], widths[2], precision: 1);
                    Assert.Equal(new GridLength(Size("Component.DenseTable.Column.NarrowWidth")), grid.ColumnDefinitions[3].Width);
                    Assert.Equal(new GridLength(Size("Component.DenseTable.Column.ActionsWidth")), grid.ColumnDefinitions[4].Width);
                }

                double[] Offsets(Border border)
                {
                    var grid = Grid(border);
                    var x = grid.TranslatePoint(default, window)!.Value.X;
                    var edges = new List<double>();
                    foreach (var column in grid.ColumnDefinitions) edges.Add(x += column.ActualWidth);
                    return [.. edges];
                }

                var expected = Offsets(header);
                AssertTableTracks(Grid(header));
                foreach (var row in rows)
                {
                    AssertTableTracks(Grid(row));
                    Assert.Equal(expected, Offsets(row));
                }
                Assert.NotEqual(rows[0].DataContext, rows[1].DataContext);
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ColumnsPropertyBuildsNeutralTracksFromRoleTokensAndGridLengths()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var grid = new Grid();
            DenseTable.SetColumns(grid, "Narrow * 2* Narrow Actions");
            Assert.Equal(
                [new GridLength(Size("Component.DenseTable.Column.NarrowWidth")),
                 new GridLength(1, GridUnitType.Star),
                 new GridLength(2, GridUnitType.Star), new GridLength(Size("Component.DenseTable.Column.NarrowWidth")),
                 new GridLength(Size("Component.DenseTable.Column.ActionsWidth"))],
                grid.ColumnDefinitions.Select(column => column.Width));
            DenseTable.SetColumns(grid, "*");
            Assert.Single(grid.ColumnDefinitions);
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ColumnsPropertyResolvesComponentTrackKeysForMatchingHeaderAndRows()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            const string columns = "Component.TextsPage.SelectionPanelWidth Component.DenseTable.Column.ActionsWidth Auto *";
            var header = new Grid();
            var row = new Grid();
            DenseTable.SetColumns(header, columns);
            DenseTable.SetColumns(row, columns);

            var expected = new[]
            {
                new GridLength(Size("Component.TextsPage.SelectionPanelWidth")),
                new GridLength(Size("Component.DenseTable.Column.ActionsWidth")),
                GridLength.Auto,
                new GridLength(1, GridUnitType.Star),
            };
            Assert.Equal(expected, header.ColumnDefinitions.Select(column => column.Width));
            Assert.Equal(expected, row.ColumnDefinitions.Select(column => column.Width));
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }
    private sealed record ActionHeading(string Label) : IDenseTableGroupHeading;

    [Fact]
    public void GroupButtonWorksWithoutSelectingItsHeading()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var group = new ActionHeading("Pending changes");
            var first = new object();
            var second = new object();
            var clicks = 0;
            Button? undo = null;
            var list = new ListBox
            {
                Classes = { "denseTable" },
                ItemsSource = new object[] { group, first, new DenseTableGroup("Other", "1 change"), second },
                ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<object>((item, _) =>
                {
                    if (item is IDenseTableGroupHeading)
                    {
                        var button = new Button { Content = "Undo all", Classes = { "denseTableAction" } };
                        button.Click += (_, _) => clicks++;
                        if (ReferenceEquals(item, group)) undo = button;
                        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                        content.Children.Add(new TextBlock { Text = "Pending changes" });
                        Grid.SetColumn(button, 1);
                        content.Children.Add(button);
                        return new Border { Classes = { "denseTableGroupHeader" }, Child = content };
                    }
                    return new Border { Classes = { "denseTableRow" }, Child = new TextBlock { Text = "Change" } };
                }),
            };
            Avalonia.Input.KeyboardNavigation.SetTabNavigation(list, KeyboardNavigationMode.Once);
            DenseTable.Attach(list);
            var before = new Button { Content = "Before" };
            var after = new Button { Content = "After" };
            var host = new StackPanel { Children = { before, list, after } };
            var window = new Window { Content = host, Width = 420, Height = 400 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                list.SelectedItem = first;
                var heading = Assert.IsType<ListBoxItem>(list.ContainerFromItem(group));
                Assert.False(heading.Focusable);
                Assert.True(heading.IsHitTestVisible);
                var button = Assert.IsType<Button>(undo);
                var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(1, clicks);
                Assert.Same(first, list.SelectedItem);
                var background = heading.TranslatePoint(new Point(4, heading.Bounds.Height / 2), window)!.Value;
                window.MouseDown(background, MouseButton.Left);
                window.MouseUp(background, MouseButton.Left);
                Assert.Same(first, list.SelectedItem);
                before.Focus();
                var traversed = false;
                for (var index = 0; index < 12; index++)
                {
                    Press(window, Key.Tab, PhysicalKey.Tab);
                    if (!ReferenceEquals(window.FocusManager!.GetFocusedElement(), button)) continue;
                    traversed = true;
                    Press(window, Key.Space, PhysicalKey.Space);
                    Press(window, Key.Enter, PhysicalKey.Enter);
                    break;
                }
                Assert.True(traversed, "Real Tab traversal must reach the heading's action.");
                Assert.Equal(3, clicks);
                Assert.Same(first, list.SelectedItem);
                Press(window, Key.Tab, PhysicalKey.Tab);
                Assert.IsType<Button>(window.FocusManager!.GetFocusedElement());
                Assert.NotSame(button, window.FocusManager.GetFocusedElement());
                Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.Shift);
                Assert.Same(button, window.FocusManager.GetFocusedElement());
                Press(window, Key.Tab, PhysicalKey.Tab);
                Press(window, Key.Tab, PhysicalKey.Tab);
                Assert.Same(after, window.FocusManager.GetFocusedElement());
                Assert.IsType<ListBoxItem>(list.ContainerFromItem(first)).Focus();
                Press(window, Key.Down, PhysicalKey.ArrowDown);
                Assert.Same(second, list.SelectedItem);
                Press(window, Key.Up, PhysicalKey.ArrowUp);
                Assert.Same(first, list.SelectedItem);
                list.SelectedItem = group;
                Assert.Same(first, list.SelectedItem);
                list.ItemsSource = new object[] { first, group, second };
                PageScreenshots.Settle(window);
                Assert.True(Assert.IsType<ListBoxItem>(list.ContainerFromItem(first)).Focusable);
                Assert.False(Assert.IsType<ListBoxItem>(list.ContainerFromItem(group)).Focusable);
                Assert.True(Assert.IsType<ListBoxItem>(list.ContainerFromItem(group)).IsHitTestVisible);
            }
            finally { window.Close(); }
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }

    private sealed record TableFact(string Title, string SecondaryEvidence);

    [Theory]
    [InlineData(350)]
    [InlineData(359)]
    public void CompactTracksAlignAndRestoreWithoutReplacingSemanticVisibility(double width)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var semantic = new CheckBox { IsChecked = false };
            var grids = new List<Grid>();
            var cells = new List<(Border Wrapper, TextBlock Inner)>();
            var actions = 0;
            Grid Columns(bool row, string fact = "Secondary evidence")
            {
                var grid = new Grid();
                DenseTable.SetColumns(grid, "Narrow * 2* Narrow Actions");
                DenseTable.SetCompactColumns(grid, "0 * 0 0 Actions");
                for (var column = 0; column < 5; column++)
                {
                    Control cell;
                    if (column == 4)
                    {
                        cell = row ? new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            Children =
                            {
                                new Button { Content = "Details", Classes = { "denseTableAction" } },
                                new Button { Content = "⋯", Classes = { "denseTableMenu" } },
                            },
                        } : new Border();
                    }
                    else
                    {
                        var text = new TextBlock { Text = fact, TextTrimming = TextTrimming.CharacterEllipsis };
                        var wrapper = new Border { Child = text };
                        if (column != 1)
                        {
                            DenseTable.SetSecondary(wrapper, true);
                            if (row && column == 2)
                            {
                                text.Bind(Control.IsVisibleProperty, new Avalonia.Data.Binding("IsChecked") { Source = semantic });
                                cells.Add((wrapper, text));
                            }
                        }
                        cell = wrapper;
                    }
                    if (cell is StackPanel actionPanel)
                        foreach (var button in actionPanel.Children.OfType<Button>())
                            button.Click += (_, _) => actions++;
                    Grid.SetColumn(cell, column);
                    grid.Children.Add(cell);
                }
                grids.Add(grid);
                return grid;
            }
            var header = new Border { Classes = { "denseTableColumnHeader" }, Child = Columns(false) };
            var list = new ListBox
            {
                Classes = { "denseTable" },
                ItemsSource = Enumerable.Range(0, 30).Select(index => new TableFact("Finding " + index, "Named words and severity")).ToArray(),
                ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<TableFact>((item, _) =>
                    new Border { Classes = { "denseTableRow" }, Child = Columns(true, item!.SecondaryEvidence) }),
            };
            DenseTable.Attach(list);
            var host = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Children = { header, list } };
            Grid.SetRow(list, 1);
            DenseTable.SetIsWidthHost(host, true);
            var inspector = new TextBlock();
            inspector.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding("SelectedItem.SecondaryEvidence") { Source = list });
            var content = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Children = { host, inspector } };
            Grid.SetRow(inspector, 1);
            var window = new Window { Width = 900, Height = 400, Content = content };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var row = grids.First(grid => !ReferenceEquals(grid, header.Child) && grid.IsAttachedToVisualTree());
                Assert.True(row.ColumnDefinitions[0].ActualWidth > 0);
                window.Width = width;
                PageScreenshots.Settle(window);
                var head = Assert.IsType<Grid>(header.Child);
                for (var column = 0; column < 5; column++)
                    Assert.Equal(head.ColumnDefinitions[column].ActualWidth, row.ColumnDefinitions[column].ActualWidth, 1);
                Assert.Equal(head.TranslatePoint(default, host)!.Value.X, row.TranslatePoint(default, host)!.Value.X, 1);
                Assert.Equal(new GridLength(0), row.ColumnDefinitions[0].Width);
                Assert.InRange(row.ColumnDefinitions[0].ActualWidth, 0, row.ColumnSpacing);
                Assert.True(row.ColumnDefinitions[1].ActualWidth > 100);
                Assert.All(cells.Where(cell => cell.Wrapper.IsAttachedToVisualTree()), cell => Assert.False(cell.Wrapper.IsVisible));
                list.SelectedIndex = 0;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("Named words and severity", inspector.Text);
                foreach (var button in row.GetVisualDescendants().OfType<Button>())
                {
                    var point = button.TranslatePoint(default, host)!.Value;
                    Assert.True(point.X >= 0 && point.X + button.Bounds.Width <= host.Bounds.Width + 1);
                    Assert.True(button.IsEffectivelyVisible);
                    var center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                    window.MouseDown(center, MouseButton.Left);
                    window.MouseUp(center, MouseButton.Left);
                }
                Assert.Equal(2, actions);
                Capture(window, $"kernel-content-{width}-compact");
                foreach (var scroll in host.GetVisualDescendants().OfType<ScrollViewer>())
                    Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1);
                window.Width = 900;
                PageScreenshots.Settle(window);
                Assert.True(row.ColumnDefinitions[0].ActualWidth > 0);
                Assert.All(cells.Where(cell => cell.Wrapper.IsAttachedToVisualTree()), cell =>
                {
                    Assert.True(cell.Wrapper.IsVisible);
                    Assert.False(cell.Inner.IsVisible);
                });
                semantic.IsChecked = true;
                Dispatcher.UIThread.RunJobs();
                Assert.All(cells.Where(cell => cell.Wrapper.IsAttachedToVisualTree()), cell => Assert.True(cell.Inner.IsVisible));
                for (var column = 0; column < 5; column++)
                    Assert.Equal(head.ColumnDefinitions[column].ActualWidth, row.ColumnDefinitions[column].ActualWidth, 1);
                Capture(window, $"kernel-content-{width}-restored-900");
            }
            finally { window.Close(); }
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData("* Narrow 2* Narrow 2* Narrow Actions", "* 0 0 0 0 Narrow Actions", 1)]
    [InlineData("2* Narrow Narrow * * * * Actions", "2* 0 Narrow * 0 0 0 Actions", 1)]
    public void BusySchemasCollapseBeforeTheirFlexibleHeadingsBecomeUnreadable(
        string wide, string compact, int secondaryColumn)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            Grid Columns()
            {
                var grid = new Grid();
                DenseTable.SetColumns(grid, wide);
                DenseTable.SetCompactColumns(grid, compact);
                var label = new Border { Child = new TextBlock { Text = "Evidence", Classes = { "denseTableHeading" } } };
                Grid.SetColumn(label, secondaryColumn);
                DenseTable.SetSecondary(label, true);
                grid.Children.Add(label);
                grid.Children.Add(new TextBlock
                {
                    Text = "Word / change",
                    Classes = { "denseTableHeading" },
                    TextWrapping = TextWrapping.Wrap,
                });
                return grid;
            }
            var header = Columns();
            var row = Columns();
            var headerBorder = new Border { Classes = { "denseTableColumnHeader" }, Child = header };
            var rowBorder = new Border { Classes = { "denseTableRow" }, Child = row };
            var host = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Children = { headerBorder, rowBorder } };
            Grid.SetRow(rowBorder, 1);
            DenseTable.SetIsWidthHost(host, true);
            var window = new Window { Width = 740, Height = 400, Content = host };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                Assert.Equal(new GridLength(0), header.ColumnDefinitions[secondaryColumn].Width);
                Assert.Equal(new GridLength(0), row.ColumnDefinitions[secondaryColumn].Width);
                Assert.False(header.Children[0].IsVisible);
                Assert.True(row.ColumnDefinitions[0].ActualWidth >= Size("Component.DenseTable.Column.NarrowWidth"));
                Assert.Equal(header.ColumnDefinitions.Select(column => column.ActualWidth),
                    row.ColumnDefinitions.Select(column => column.ActualWidth));
                LayoutAssertions.AssertCurrent(window);
                Capture(window, $"schema-budget-{wide.Split(' ').Length}-compact-740");
                window.Width = 1300;
                PageScreenshots.Settle(window);
                Assert.True(header.Children[0].IsVisible);
                Assert.True(row.ColumnDefinitions[secondaryColumn].ActualWidth > 0);
                var declared = wide.Split(' ');
                for (var index = 0; index < declared.Length; index++)
                    if (declared[index].EndsWith('*'))
                        Assert.True(row.ColumnDefinitions[index].ActualWidth >=
                            Size("Component.DenseTable.Column.NarrowWidth"));
                Assert.Equal(header.ColumnDefinitions.Select(column => column.ActualWidth),
                    row.ColumnDefinitions.Select(column => column.ActualWidth));
                LayoutAssertions.AssertCurrent(window);
                Capture(window, $"schema-budget-{declared.Length}-restored-1300");
            }
            finally { window.Close(); }
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void HomeAndEndRealizeActualRowsAcrossVirtualizedTypedHeadings()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var rows = Enumerable.Range(0, 120).Select(index => new TableFact("Finding " + index, "Evidence")).ToArray();
            var entries = new List<object> { new ActionHeading("Leading heading") };
            for (var index = 0; index < rows.Length; index++)
            {
                if (index > 0 && (index % 8 == 0 || index == rows.Length - 1))
                    entries.Add(new DenseTableGroup("Group", "Findings"));
                entries.Add(rows[index]);
            }
            entries.Add(new ActionHeading("Trailing heading"));
            var list = new ListBox
            {
                Classes = { "denseTable" },
                Focusable = true,
                IsTabStop = true,
                ItemsSource = entries,
                ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<object>((item, _) =>
                    new Border
                    {
                        Classes = { item is IDenseTableGroupHeading ? "denseTableGroupHeader" : "denseTableRow" },
                        Child = new TextBlock { Text = item is TableFact fact ? fact.Title : "Group heading" },
                    }),
            };
            DenseTable.Attach(list);
            var window = new Window { Content = list, Width = 420, Height = 250 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                Assert.IsType<VirtualizingStackPanel>(list.ItemsPanelRoot);
                list.Focus(NavigationMethod.Tab);
                Dispatcher.UIThread.RunJobs();
                Assert.Same(rows[0], list.SelectedItem);
                Assert.Null(list.ContainerFromItem(rows[^1]));
                Press(window, Key.End, PhysicalKey.End);
                PageScreenshots.Settle(window);
                Assert.Same(rows[^1], list.SelectedItem);
                var last = Assert.IsType<ListBoxItem>(list.ContainerFromItem(rows[^1]));
                Assert.Same(last, window.FocusManager!.GetFocusedElement());
                Assert.Null(list.ContainerFromItem(rows[0]));
                Press(window, Key.Up, PhysicalKey.ArrowUp);
                Assert.Same(rows[^2], list.SelectedItem);
                Press(window, Key.Down, PhysicalKey.ArrowDown);
                Assert.Same(rows[^1], list.SelectedItem);
                Press(window, Key.Home, PhysicalKey.Home);
                PageScreenshots.Settle(window);
                Assert.Same(rows[0], list.SelectedItem);
                var first = Assert.IsType<ListBoxItem>(list.ContainerFromItem(rows[0]));
                Assert.Same(first, window.FocusManager.GetFocusedElement());
                Assert.All(list.GetVisualDescendants().OfType<ListBoxItem>()
                    .Where(container => container.DataContext is IDenseTableGroupHeading),
                    container => Assert.False(container.IsSelected));
            }
            finally { window.Close(); }
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }

}
