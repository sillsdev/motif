using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using SIL.Motif.App.Composition;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

public sealed class WalkthroughWindow : IDisposable
{
    private readonly ScriptedProjectPicker _projectPicker;
    private readonly ScriptedFolderPicker _folderPicker;
    private readonly RecordingDragSource _dragSource;
    private readonly InProcessRunnerLauncher? _ownedRunner;

    /// <summary>Composes the real window over <paramref name="managedRoot"/> with scripted desktop inputs.</summary>
    /// <param name="startGate">Holds the real client's Assessment or Handoff before it starts.</param>
    /// <param name="parserPath">The parser to run; <see langword="null"/> locates the real one.</param>
    /// <param name="runnerLauncher">
    /// Starts queued work; <see langword="null"/> drains it in this process with the same root and parser.
    /// </param>
    public WalkthroughWindow(
        string managedRoot, string projectPath, string? folderPath = null,
        ICommandStartGate? startGate = null, TimeProvider? timeProvider = null,
        string? parserPath = null, IJobRunnerLauncher? runnerLauncher = null)
    {
        _projectPicker = new ScriptedProjectPicker(projectPath);
        _folderPicker = new ScriptedFolderPicker(folderPath);
        _dragSource = new RecordingDragSource();

        parserPath ??= PanGlossExecutable.TryLocate();
        if (runnerLauncher is null)
            runnerLauncher = _ownedRunner = new InProcessRunnerLauncher(new JobRunnerLaunchOptions(managedRoot, parserPath));
        var composition = MotifAppComposition.Create(new MotifAppOptions(
            managedRoot,
            parserPath,
            runnerLauncher,
            timeProvider ?? TimeProvider.System,
            _projectPicker,
            _folderPicker,
            _dragSource), startGate);
        Window = composition.Window;
        Workspace = composition.Workspace;
    }

    public MainWindow Window { get; }

    public HandoffWorkspaceViewModel Workspace { get; }

    public IReadOnlyList<string> DraggedPaths => _dragSource.Paths;

    public string ProjectPath
    {
        get => _projectPicker.Path;
        set => _projectPicker.Path = value;
    }

    public void Show()
    {
        Window.Show();
        Window.ApplyTemplate();
        Window.UpdateLayout();
        Pump();
        RealizeEveryStage();
    }

    public void SkipSetup()
    {
        if (Workspace.Context.Setup is not { IsOpen: true } setup) return;
        Click("Skip setup for now");
        WaitUntil(() => !setup.IsOpen, TimeSpan.FromSeconds(30), "skipping setup did not close the dialog");
    }

    public void LoadKnownProjects()
    {
        var loading = Workspace.Project.LoadKnownProjectsAsync();
        WaitUntil(() => loading.IsCompleted, TimeSpan.FromSeconds(30), "Known projects did not load");
        loading.GetAwaiter().GetResult();
    }

    public void OpenProjectMenu()
    {
        if (!ProjectMenuFlyout.IsOpen) Click("Project menu");
    }

    /// <summary>Closes the project menu by clicking its button again, a press the open menu's light dismiss takes.</summary>
    public void CloseProjectMenu()
    {
        if (!ProjectMenuFlyout.IsOpen) return;
        HeadlessClick.PressOver(Window, Find<Button>("Project menu"), "Project menu");
        Assert.False(ProjectMenuFlyout.IsOpen, "Clicking 'Project menu' again did not close the menu.");
    }

    public void ChooseNewProject()
    {
        OpenProjectMenu();
        var entry = FindProjectMenuEntry<Button>("Select a new project");
        Assert.True(entry.IsEffectivelyEnabled, "'Select a new project' is not effectively enabled.");
        Assert.Same(Workspace.SelectNewProjectCommand, entry.Command);
        entry.Command!.Execute(entry.CommandParameter);
        ProjectMenuFlyout.Hide();
        Pump();
    }

    public T FindProjectMenuEntry<T>(string accessibleName) where T : Control =>
        (ProjectMenuFlyout.Content as Control)?.GetLogicalDescendants().OfType<T>().Single(control =>
            string.Equals(Avalonia.Automation.AutomationProperties.GetName(control), accessibleName,
                StringComparison.Ordinal))
        ?? throw new InvalidOperationException("The project menu has no content.");

    /// <summary>The one control named <paramref name="name"/>, wherever it sits: each page's view has names of its own.</summary>
    public T Named<T>(string name) where T : Control =>
        Window.GetLogicalDescendants().OfType<T>().Single(control => control.Name == name);

    public T Find<T>(string accessibleName) where T : Control
    {
        var control = Window.GetLogicalDescendants().OfType<T>().Single(candidate =>
            string.Equals(Avalonia.Automation.AutomationProperties.GetName(candidate), accessibleName,
                StringComparison.Ordinal));
        ShowStageOwning(control);
        return control;
    }

    // A person reaches a control on another page through the sidebar, and one on another tab by its tab.
    private void ShowStageOwning(Control control)
    {
        var owners = control.GetLogicalAncestors().OfType<Control>().Select(ancestor => ancestor.Name).ToList();
        var page = PageRegistry.Entries.Select(entry => (WorkspacePage?)entry.Page)
            .FirstOrDefault(candidate => owners.Contains($"{candidate}Page"));
        if (page is not { } owning) return;

        ShowPage(owning);
        if (owning != WorkspacePage.Texts) return;

        if (owners.Contains("CompareHost")) ShowTextsTab(TextsTab.Matrix);
        else if (owners.Contains("ListsHost")) ShowTextsTab(TextsTab.Lists);
        else if (owners.Contains("SelectionHost") || owners.Contains("ResultsInTextHost")) ShowTextsTab(TextsTab.AnalyzeTexts);
    }

    /// <summary>Opens a Texts page tab the way a person does, by its tab above the page.</summary>
    public void ShowTextsTab(TextsTab tab)
    {
        if (Workspace.PageModel<TextsPageModel>().Tab == tab) return;

        var name = tab switch
        {
            TextsTab.Matrix => "Matrix tab",
            TextsTab.AnalyzeTexts => "Analyze texts tab",
            TextsTab.Lists => "Lists tab",
            _ => throw new ArgumentOutOfRangeException(nameof(tab), tab, null),
        };
        ClickControl(Find<Button>(name), name);
        Window.UpdateLayout();
        Pump();
        Assert.Equal(tab, Workspace.PageModel<TextsPageModel>().Tab);
    }

    /// <summary>Opens a page the way a person does, by its entry in the sidebar.</summary>
    public void ShowPage(WorkspacePage page)
    {
        if (Workspace.CurrentPage == page) return;

        var name = Workspace.PageOf(page).AutomationName;
        var entry = Window.GetLogicalDescendants().OfType<ListBoxItem>().Single(item =>
            string.Equals(Avalonia.Automation.AutomationProperties.GetName(item), name, StringComparison.Ordinal));
        ClickControl(entry, name);
        Window.UpdateLayout();
        Pump();
        Assert.Equal(page, Workspace.CurrentPage);
    }

    public void TypePastedWords(string text)
    {
        ShowPage(WorkspacePage.Texts);
        ShowTextsTab(TextsTab.AnalyzeTexts);
        // A Text's counts arrive with its words and push this header down, so a click aimed earlier misses it.
        WaitUntil(() => Workspace.Assess.TextWords is not { IsLoading: true }, TimeSpan.FromSeconds(30),
            "the chosen Texts' words did not finish loading");
        var addWords = Window.GetLogicalDescendants().OfType<Expander>().Single(expander =>
            Equals(expander.Header, "Add words"));
        ClickControl(addWords, "Add words");
        Window.UpdateLayout();
        Pump();
        Assert.True(addWords.IsExpanded, "Clicking the Add words header should expand it.");
        Type("Words to analyze, one per line", text);
    }

    // A page nobody has opened has no template applied, so its controls are not in the tree until it is.
    private void RealizeEveryStage()
    {
        var opening = Workspace.CurrentPage;
        foreach (var page in Enum.GetValues<WorkspacePage>())
        {
            Workspace.CurrentPage = page;
            foreach (var tab in Enum.GetValues<TextsTab>())
            {
                Workspace.PageModel<TextsPageModel>().Tab = tab;
                Window.UpdateLayout();
                Pump();
            }
        }

        Workspace.PageModel<TextsPageModel>().Tab = TextsTab.Matrix;
        Workspace.CurrentPage = opening;
        Window.UpdateLayout();
    }

    public void Click(string accessibleName)
    {
        var button = Find<Button>(accessibleName);
        ClickControl(button, accessibleName);
    }

    public void Check(string content)
    {
        ShowPage(WorkspacePage.Texts);
        ShowTextsTab(TextsTab.AnalyzeTexts);
        var checkBox = Window.GetLogicalDescendants().OfType<CheckBox>().Single(control =>
            Equals(control.Content, content));
        ShowStageOwning(checkBox);
        var before = checkBox.IsChecked;
        ClickControl(checkBox, content);
        Assert.NotEqual(before, checkBox.IsChecked);
    }

    public void Type(string accessibleName, string text)
    {
        var textBox = Find<TextBox>(accessibleName);
        ClickControl(textBox, accessibleName);
        Assert.True(textBox.IsFocused, $"'{accessibleName}' did not receive focus from the click.");
        Window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.None, null);
        Window.KeyTextInput(text);
        Pump();
        Assert.Equal(text, textBox.Text);
    }

    public void SelectKnownProject(string projectPath)
    {
        var project = Workspace.RecentProjects.Single(known =>
            string.Equals(known.FullFwDataPath, projectPath, StringComparison.OrdinalIgnoreCase));
        OpenProjectMenu();
        var openRecent = FindProjectMenuEntry<Button>("Open a recent project");
        Assert.True(openRecent.IsEffectivelyEnabled, "'Open a recent project' is not effectively enabled.");
        Assert.IsType<MenuFlyout>(openRecent.Flyout).ShowAt(openRecent);
        var item = Window.RecentProjectItems.Single(candidate =>
            string.Equals(Avalonia.Automation.AutomationProperties.GetName(candidate),
                project.AutomationName, StringComparison.Ordinal));
        Assert.True(item.IsEffectivelyEnabled, $"'{project.AutomationName}' is not effectively enabled.");
        Assert.Same(Workspace.OpenRecentProjectCommand, item.Command);
        Assert.Same(project, item.CommandParameter);
        item.Command!.Execute(item.CommandParameter);
        ProjectMenuFlyout.Hide();
        Pump();
    }

    private Flyout ProjectMenuFlyout =>
        Window.FindControl<Button>("ProjectMenuButton")?.Flyout as Flyout
        ?? throw new InvalidOperationException("The MainWindow has no project menu flyout.");

    public void DragAllFiles()
    {
        var allFiles = Find<Button>("Drag all AI Handoff files");
        using var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
        var args = new PointerPressedEventArgs(
            allFiles, pointer, Window, new Point(), 0, PointerPointProperties.None, KeyModifiers.None);
        allFiles.RaiseEvent(args);
        Pump();
    }

    public void WaitUntil(Func<bool> predicate, TimeSpan timeout, string why)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentException.ThrowIfNullOrWhiteSpace(why);
        var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        while (!predicate() && Stopwatch.GetTimestamp() < deadline)
        {
            Pump();
            Thread.Sleep(15);
        }

        Pump();
        Assert.True(predicate(),
            $"{why}; baseline='{Workspace.Baseline.CapturedTimeText}', " +
            $"baseline refusal='{Workspace.Baseline.RefusalMessage}', " +
            $"selection empty='{Workspace.Selection.TextsEmptyMessage}', " +
            $"selection refusal='{Workspace.Selection.RefusalMessage}', " +
            $"Assess.State='{Workspace.Assess.State}', " +
            $"Assess.Refusal?.Message='{Workspace.Assess.Refusal?.Message}', " +
            $"Assess.Progress='{Workspace.Assess.Progress}', " +
            $"Refresh.IsRunning='{Workspace.RefreshCommand.IsRunning}', " +
            $"Overview.loaded='{Workspace.PageModel<OverviewPageModel>().Overview is not null}', " +
            $"Overview.refusal='{Workspace.PageModel<OverviewPageModel>().OverviewRefusalMessage}', " +
            $"Handoff.State='{Workspace.PageModel<AiHandoffPageModel>().Handoff.State}', " +
            $"Handoff.Refusal?.Message='{Workspace.PageModel<AiHandoffPageModel>().Handoff.Refusal?.Message}', " +
            $"Handoff.Progress='{Workspace.PageModel<AiHandoffPageModel>().Handoff.Progress}', " +
            $"project='{Workspace.Project.KnownProjects.Count}' known projects");
    }

    public void Dispose()
    {
        Window.Close();
        var disposal = Workspace.DisposeAsync().AsTask();
        var timeout = TimeSpan.FromSeconds(30);
        var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        while (!disposal.IsCompleted && Stopwatch.GetTimestamp() < deadline)
        {
            Pump();
            Thread.Sleep(15);
        }

        Pump();
        if (!disposal.IsCompleted)
            throw new TimeoutException(
                $"The walkthrough workspace did not stop within {timeout}; Assessment state is '{Workspace.Assess.State}'.");
        disposal.GetAwaiter().GetResult();
        if (_ownedRunner is not null && !_ownedRunner.DisposeAsync().AsTask().Wait(timeout))
            throw new TimeoutException($"The walkthrough's in-process runner did not stop within {timeout}.");
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private void ClickControl(Control control, string accessibleName) =>
        HeadlessClick.Click(Window, control, accessibleName);

    private sealed class ScriptedProjectPicker(string path) : IProjectPicker
    {
        public string Path { get; set; } = path;

        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(Path);
    }

    private sealed class ScriptedFolderPicker(string? path) : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(path);
    }

    private sealed class RecordingDragSource : IFileDragSource
    {
        public List<string> Paths { get; } = [];

        public Task<DragDropEffects> StartDragAsync(
            PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths, DragDropEffects allowedEffects)
        {
            Paths.AddRange(filePaths);
            return Task.FromResult(allowedEffects);
        }
    }
}
