using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Shape = Avalonia.Controls.Shapes.Path;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Responses;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the page shell as a headless platform can see it: one page's controls showing at a time, a sidebar
/// entry that opens its page, the collapsed sidebar, the project menu, the freshness line, the one changes
/// list, and the AI Handoff drag source that a keyboard can also reach. Also pins that every Semi colour key a
/// view names resolves to a brush in both theme variants, because an unknown key in a <c>DynamicResource</c> is
/// not an error — the setter silently does nothing.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class WorkflowShellTests
{
    private static readonly string[] PageHosts = PageRegistry.Entries.Select(entry => $"{entry.Page}Page").ToArray();

    private readonly AvaloniaHeadlessFixture _avalonia;

    public WorkflowShellTests(AvaloniaHeadlessFixture avalonia) => _avalonia = avalonia;

    [Fact]
    public void EverySemiColourKeyAViewNamesResolvesToABrushInBothThemeVariants()
    {
        var keys = ViewSources()
            .SelectMany(text => Regex.Matches(text, @"DynamicResource\s+(SemiColor\w+)").Select(match => match.Groups[1].Value))
            .Distinct()
            .Order()
            .ToList();
        Assert.NotEmpty(keys);

        _avalonia.Invoke(() =>
        {
            var application = Application.Current!;
            foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            foreach (var key in keys)
            {
                Assert.True(application.TryGetResource(key, variant, out var value), $"{key} is not a Semi resource.");
                Assert.True(value is IBrush, $"{key} is a {value?.GetType().Name}, not a brush, in {variant}.");
            }
        });
    }

    [Fact]
    public void EveryPageHasExactlyOneRegistryEntryAndTheSidebarFollowsItsOrder()
    {
        var registered = PageRegistry.Entries.Select(entry => entry.Page).ToList();
        Assert.Equal(Enum.GetValues<WorkspacePage>().Order(), registered.Order());
        Assert.Equal(registered.Count, registered.Distinct().Count());

        var workspace = NewWorkspace();
        Assert.Equal(registered, workspace.Pages.Select(page => page.Page));
        Assert.Equal(PageRegistry.Entries.Select(entry => entry.Title), workspace.Pages.Select(page => page.Title));
    }

    [Fact]
    public void EveryRegisteredPageCarriesAnIconThatDraws()
    {
        _avalonia.Invoke(() =>
        {
            foreach (var entry in PageRegistry.Entries)
            {
                Assert.False(string.IsNullOrWhiteSpace(entry.Icon), $"{entry.Page} has no icon.");
                var bounds = PageIcons.ToGeometry(entry.Icon).Bounds;
                Assert.True(bounds.Width > 0 && bounds.Height > 0, $"{entry.Page}'s icon draws nothing.");
                Assert.True(bounds.Right <= 24 && bounds.Bottom <= 24, $"{entry.Page}'s icon leaves the 24-unit grid.");
            }
        });
    }

    [Fact]
    public void EveryPageIsBuiltFromTheRegistryAndTheWindowHostsEachOnce()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, window) = NewComposedWindow();
            try
            {
                var host = window.FindControl<Panel>("PageHost")!;
                Assert.Equal(PageHosts, host.Children.Select(child => child.Name));
                foreach (var page in Enum.GetValues<WorkspacePage>())
                {
                    var built = PageRegistry.For(page).CreateView(workspace.PageOf(page).Model);
                    Assert.IsType(built.GetType(), host.Children.Single(child => child.Name == $"{page}Page"));
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void AProjectWithNoStoredGrammarCheckOffersAWorkingCheckButtonOnWarnings()
    {
        var fake = new FakeCommandClient();
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(
            new BaselineToken("p", "sha256:" + new string('a', 64), "1", "2026-09-05T11:02:00Z", "sha256:" + new string('b', 64)),
            DateTimeOffset.UtcNow, false));
        fake.ListTextsCompletesWith(new TextInventoryResponse([], HasBaseline: true));
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = NewComposedWindow(fake);
            try
            {
                window.Show();
                await workspace.SetProjectAsync(@"C:\projects\one.fwdata");
                workspace.CurrentPage = WorkspacePage.Warnings;
                window.UpdateLayout();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                Assert.Empty(fake.CheckGrammarRequests);
                var check = ButtonNamed(window, "Check the grammar");
                Assert.True(check.IsEffectivelyVisible);
                Assert.True(check.IsEffectivelyEnabled);

                Click(window, check);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                Assert.Single(fake.CheckGrammarRequests);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void OnlyTheCurrentPagesHostIsVisible()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, window) = NewComposedWindow();
            try
            {
                foreach (var page in Enum.GetValues<WorkspacePage>())
                {
                    workspace.CurrentPage = page;

                    var visible = PageHosts.Where(name => Host(window, name).IsVisible).ToList();
                    Assert.Equal([$"{page}Page"], visible);
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ChoosingASidebarEntryOpensItsPageAndEveryEntryIsNamedForItsPage()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, window) = NewComposedWindow();
            try
            {
                window.Show();
                window.UpdateLayout();

                var entries = SidebarEntries(window);
                Assert.Equal(
                    ["Overview page", "Texts page", "Try a Word page", "Timing page", "Warnings page",
                        "Review changes page", "AI Handoff page"],
                    entries.Select(AutomationProperties.GetName));

                foreach (var page in Enum.GetValues<WorkspacePage>().Reverse())
                {
                    Click(window, Entry(entries, page));

                    Assert.Equal(page, workspace.CurrentPage);
                    Assert.True(Host(window, $"{page}Page").IsVisible);
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void EachSidebarEntryDrawsItsIconAsAnOutlineInTheEntrysTextColour()
    {
        _avalonia.Invoke(() =>
        {
            var (_, window) = NewComposedWindow();
            try
            {
                window.Show();
                window.UpdateLayout();

                foreach (var entry in SidebarEntries(window))
                {
                    var icon = Assert.Single(entry.GetVisualDescendants().OfType<Shape>());
                    Assert.NotNull(icon.Data);
                    Assert.Null(icon.Fill);
                    Assert.Equal(1.8, icon.StrokeThickness);
                    Assert.Same(entry.Foreground, icon.Stroke);
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ANarrowWindowCollapsesTheSidebarToIconsWithTooltipsAndKeepsTheBadges()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, window) = NewComposedWindow();
            try
            {
                workspace.Context.Changes.Items.Add(new ChangeViewModel(ChangeKinds.Approve, "kitabu", "Candidate", "kitabu"));
                window.Width = 1240;
                window.Show();
                window.UpdateLayout();

                var sidebar = window.FindControl<Border>("Sidebar")!;
                Assert.False(workspace.IsSidebarCollapsed);
                Assert.Equal(204, sidebar.Bounds.Width);
                Assert.All(SidebarEntries(window), entry => Assert.True(Label(entry).IsEffectivelyVisible));
                Assert.All(SidebarEntries(window), entry => Assert.Null(ToolTip.GetTip(entry)));

                window.Width = 900;
                window.UpdateLayout();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                Assert.True(workspace.IsSidebarCollapsed);
                Assert.Equal(52, sidebar.Bounds.Width);
                var entries = SidebarEntries(window);
                Assert.All(entries, entry => Assert.False(Label(entry).IsEffectivelyVisible));
                Assert.Equal(workspace.Pages.Select(page => (object)page.Title), entries.Select(entry => ToolTip.GetTip(entry)));
                var reviewBadge = Entry(entries, WorkspacePage.Review).GetVisualDescendants().OfType<Border>()
                    .Single(border => border.Classes.Contains("pageBadge"));
                Assert.True(reviewBadge.IsEffectivelyVisible);

                Click(window, Entry(entries, WorkspacePage.Review));
                Assert.Equal(WorkspacePage.Review, workspace.CurrentPage);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheShellTakesItsSizesAndMenuLookFromTheTokens()
    {
        _avalonia.Invoke(() =>
        {
            var (_, window) = NewComposedWindow();
            try
            {
                window.Show();
                Assert.Equal((1240d, 780d, 820d, 600d), (window.Width, window.Height, window.MinWidth, window.MinHeight));
                var banner = window.GetLogicalDescendants().OfType<Border>().Single(border => border.Classes.Contains("banner"));
                Assert.Equal(new Thickness(16, 12, 16, 0), banner.Margin);

                var menuButton = window.FindControl<Button>("ProjectMenuButton")!;
                var flyout = Assert.IsType<Flyout>(menuButton.Flyout);
                flyout.ShowAt(menuButton);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var texts = Assert.IsAssignableFrom<Panel>(flyout.Content).GetLogicalDescendants().OfType<TextBlock>().ToList();
                Assert.All(texts.Where(text => text.Classes.Contains("menuTitle")), text => Assert.Equal(13.5, text.FontSize));
                Assert.True(Application.Current!.TryGetResource("SemiColorText2", window.ActualThemeVariant, out var muted));
                Assert.All(texts.Where(text => text.Classes.Contains("menuDetail")), text => Assert.Same(muted, text.Foreground));
                flyout.Hide();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheProjectMenuOffersSelectNewOpenRecentAndConfigure()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, window) = NewComposedWindow();
            try
            {
                window.Show();
                var menuButton = window.FindControl<Button>("ProjectMenuButton")!;
                var flyout = Assert.IsType<Flyout>(menuButton.Flyout);
                flyout.ShowAt(menuButton);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var entries = Assert.IsAssignableFrom<Panel>(flyout.Content).Children.OfType<Button>().ToList();

                Assert.Equal(
                    ["Select a new project", "Open a recent project", "Configure the project"],
                    entries.Select(AutomationProperties.GetName));
                Assert.Same(workspace.SelectNewProjectCommand, entries[0].Command);
                Assert.Same(workspace.ConfigureCommand, entries[2].Command);
                Assert.IsType<MenuFlyout>(entries[1].Flyout);

                workspace.Project.KnownProjects.Add(new KnownProjectSummary(@"C:\p\mbugwe.fwdata", DateTimeOffset.UtcNow));
                workspace.Project.KnownProjects.Add(new KnownProjectSummary(@"C:\p\sena.fwdata", DateTimeOffset.UtcNow));

                Assert.Equal(["Open mbugwe", "Open sena"], window.RecentProjectItems.Select(AutomationProperties.GetName));
                Assert.All(window.RecentProjectItems, item => Assert.Same(workspace.OpenRecentProjectCommand, item.Command));
                Assert.Equal("mbugwe · sena", workspace.RecentProjectsText);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheFreshnessLineHidesUntilAProjectIsOpenAndRefreshIsTheOnlyAction()
    {
        _avalonia.Invoke(() =>
        {
            var (_, window) = NewComposedWindow();
            try
            {
                window.Show();
                window.UpdateLayout();

                Assert.False(window.FindControl<StackPanel>("FreshnessLine")!.IsEffectivelyVisible);
                Assert.True(ButtonNamed(window, "Refresh the project").IsEffectivelyVisible);
                Assert.False(ButtonNamed(window, "Refresh the project").IsEffectivelyEnabled);
                Assert.False(ButtonNamed(window, "Cancel the refresh").IsEffectivelyVisible);
                Assert.False(ButtonNamed(window, "See what the refresh changed").IsEffectivelyVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheMatrixAndTheReviewPageShowTheSameChangesList()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, window) = NewComposedWindow();
            try
            {
                workspace.CurrentPage = WorkspacePage.Review;
                window.Show();
                window.UpdateLayout();

                var review = Assert.Single(window.GetLogicalDescendants().OfType<ReviewPanel>());
                var list = review.GetLogicalDescendants().OfType<ItemsControl>()
                    .Single(control => AutomationProperties.GetName(control) == "Changes to review");
                Assert.Same(workspace.Context.Changes, workspace.Assess.Compare.Changes);
                Assert.Same(workspace.Context.Changes.Items, list.ItemsSource);

                workspace.Context.Changes.Items.Add(new ChangeViewModel(ChangeKinds.Reject, "kitabu", "Approved", "kitabu"));
                window.UpdateLayout();

                Assert.Contains(review.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "kitabu");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void OnlyTheCurrentTextsTabShowsAndItsTabIsMarkedActive()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, window) = NewComposedWindow();
            try
            {
                workspace.CurrentPage = WorkspacePage.Texts;
                window.Show();
                window.UpdateLayout();

                // The matrix is the first view Texts opens on; the others are one click away.
                Assert.Contains("active", ButtonNamed(window, "Matrix tab").Classes);
                Assert.DoesNotContain("active", ButtonNamed(window, "Words tab").Classes);
                Assert.False(Host(window, "AssessHost").IsEffectivelyVisible);

                ButtonNamed(window, "Words tab").Command!.Execute(TextsTab.Words);
                window.UpdateLayout();

                Assert.Contains("active", ButtonNamed(window, "Words tab").Classes);
                Assert.DoesNotContain("active", ButtonNamed(window, "Texts tab").Classes);
                Assert.True(Host(window, "AssessHost").IsEffectivelyVisible);
                Assert.False(Host(window, "TextsReader").IsEffectivelyVisible);

                ButtonNamed(window, "Texts tab").Command!.Execute(TextsTab.Texts);
                window.UpdateLayout();

                Assert.False(Host(window, "AssessHost").IsEffectivelyVisible);
                Assert.True(Host(window, "TextsReader").IsEffectivelyVisible);
                Assert.Contains("active", ButtonNamed(window, "Texts tab").Classes);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TryAWordIsAPageOfItsOwnAndTheWordsViewNoLongerHostsIt()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, window) = NewComposedWindow();
            try
            {
                window.Show();
                window.UpdateLayout();

                var tryPage = Assert.Single(window.GetLogicalDescendants().OfType<TryAWordPage>());
                var tryWord = Assert.Single(window.GetLogicalDescendants().OfType<TryWordPanel>());
                Assert.Same(workspace.Assess.Trace, tryWord.Trace);
                Assert.Single(window.GetLogicalDescendants().OfType<DiagnosticPanel>());
                var assess = Assert.Single(window.GetLogicalDescendants().OfType<AssessPanel>());
                Assert.Empty(assess.GetLogicalDescendants().OfType<DiagnosticPanel>());
                Assert.Equal("Root", tryPage.Name);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void PressingTheAllFilesButtonStartsADragOfEveryFileAndActivatingItFromTheKeyboardCopiesTheFolder()
    {
        string? clipboardText = null;
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = NewComposedWindow();
            try
            {
                workspace.PageModel<AiHandoffPageModel>().Handoff.Files.Add(new HandoffFileViewModel("handoff.md", @"C:\handoff\handoff.md"));
                workspace.PageModel<AiHandoffPageModel>().Handoff.Files.Add(new HandoffFileViewModel("grammar.json", @"C:\handoff\grammar.json"));
                workspace.PageModel<AiHandoffPageModel>().Handoff.OutputDirectory = @"C:\handoff";
                workspace.PageModel<AiHandoffPageModel>().Handoff.State = RunState.Completed;
                workspace.CurrentPage = WorkspacePage.AiHandoff;
                window.Show();
                window.UpdateLayout();

                var button = ButtonNamed(window, "Drag all Handoff files");
                Assert.True(button.Focusable);
                using var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
                var press = new PointerPressedEventArgs(
                    button, pointer, window, new Point(), 0, PointerPointProperties.None, KeyModifiers.None);
                button.RaiseEvent(press);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                Assert.Equal([@"C:\handoff\handoff.md", @"C:\handoff\grammar.json"], DragSource.LastPaths);
                Assert.True(press.Handled);

                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                clipboardText = await window.Clipboard!.TryGetTextAsync();
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(5));

        Assert.Equal(@"C:\handoff", clipboardText);
    }

    private static readonly RecordingDragSource DragSource = new();

    private static Control Host(MainWindow window, string name) =>
        window.GetLogicalDescendants().OfType<Control>().SingleOrDefault(control => control.Name == name)
        ?? throw new InvalidOperationException($"No control named '{name}'.");

    private static List<ListBoxItem> SidebarEntries(MainWindow window) =>
        window.FindControl<ListBox>("PageList")!.GetLogicalDescendants().OfType<ListBoxItem>().ToList();

    private static ListBoxItem Entry(IEnumerable<ListBoxItem> entries, WorkspacePage page) =>
        entries.Single(entry => entry.DataContext is PageViewModel model && model.Page == page);

    private static TextBlock Label(ListBoxItem entry) =>
        entry.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Classes.Contains("pageLabel"));

    private static void Click(MainWindow window, Control control)
    {
        var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        window.UpdateLayout();
    }

    private static Button ButtonNamed(MainWindow window, string accessibleName) =>
        window.GetLogicalDescendants().OfType<Button>().Single(control =>
            AutomationProperties.GetName(control) == accessibleName);

    private static IEnumerable<string> ViewSources()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Motif.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var views = Path.Combine(root.FullName, "src", "SIL.Motif.App");
        return Directory.EnumerateFiles(views, "*.axaml", SearchOption.AllDirectories).Select(File.ReadAllText);
    }

    private static HandoffWorkspaceViewModel NewWorkspace()
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake);
        return new HandoffWorkspaceViewModel(
            new ProjectViewModel(fake, new NoProjectPicker()),
            new BaselineViewModel(fake),
            selection,
            new AssessViewModel(fake, selection),
            new NoFolderPicker(), DragSource,
            fake);
    }

    private static (HandoffWorkspaceViewModel Workspace, MainWindow Window) NewComposedWindow() =>
        NewComposedWindow(new FakeCommandClient());

    private static (HandoffWorkspaceViewModel Workspace, MainWindow Window) NewComposedWindow(FakeCommandClient fake)
    {
        var selection = new SelectionViewModel(fake);
        var workspace = new HandoffWorkspaceViewModel(
            new ProjectViewModel(fake, new NoProjectPicker()),
            new BaselineViewModel(fake),
            selection,
            new AssessViewModel(fake, selection),
            new NoFolderPicker(), DragSource,
            fake);

        var window = new MainWindow();
        window.Compose(workspace);
        return (workspace, window);
    }

    private sealed class NoProjectPicker : IProjectPicker
    {
        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class NoFolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class RecordingDragSource : IFileDragSource
    {
        public IReadOnlyList<string>? LastPaths { get; private set; }

        public Task<DragDropEffects> StartDragAsync(
            PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths, DragDropEffects allowedEffects)
        {
            LastPaths = filePaths;
            return Task.FromResult(allowedEffects);
        }
    }
}
