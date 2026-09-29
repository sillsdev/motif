using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using SIL.Motif.App.Composition;
using SIL.Motif.App;
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
    private readonly string _managedRoot;
    private readonly string? _parserPath;
    private readonly ScriptedProjectPicker _projectPicker;
    private readonly ScriptedFolderPicker _folderPicker;
    private readonly RecordingDragSource _dragSource;
    private readonly ScriptedDiagnosticFiles _diagnosticFiles = new();
    private readonly InProcessRunnerLauncher? _ownedRunner;

    /// <summary>Composes the real window over <paramref name="managedRoot"/> with scripted desktop inputs.</summary>
    /// <param name="startGate">Holds the real client's Assessment or Handoff before it starts.</param>
    /// <param name="parserPath">The parser to run; <see langword="null"/> locates the real one.</param>
    /// <param name="runnerLauncher">
    /// Starts queued work; <see langword="null"/> drains it in this process with the same root and parser.
    /// </param>
    /// <param name="clipboard">
    /// Takes what the window copies; <see langword="null"/> keeps the headless window's own clipboard.
    /// </param>
    public WalkthroughWindow(
        string managedRoot, string projectPath, string? folderPath = null,
        ICommandStartGate? startGate = null, TimeProvider? timeProvider = null,
        string? parserPath = null, IJobRunnerLauncher? runnerLauncher = null, IClipboard? clipboard = null)
    {
        _managedRoot = managedRoot;
        _projectPicker = new ScriptedProjectPicker(projectPath);
        _folderPicker = new ScriptedFolderPicker(folderPath);
        _dragSource = new RecordingDragSource();

        parserPath ??= PanGlossExecutable.TryLocate();
        if (parserPath is not null && string.Equals(
                Path.GetFullPath(parserPath), Path.GetFullPath(FakeParser.ExecutablePath),
                StringComparison.OrdinalIgnoreCase))
            parserPath = FakeParser.Copy(Path.Combine(managedRoot, "fake-pangloss-" + Guid.NewGuid().ToString("N")));
        _parserPath = parserPath;
        if (runnerLauncher is null)
            runnerLauncher = _ownedRunner = new InProcessRunnerLauncher(new JobRunnerLaunchOptions(managedRoot, parserPath));
        var composition = MotifAppComposition.Create(new MotifAppOptions(
            managedRoot,
            parserPath,
            runnerLauncher,
            timeProvider ?? TimeProvider.System,
            _projectPicker,
            _folderPicker,
            _dragSource,
            clipboard,
            _diagnosticFiles), startGate);
        Window = composition.Window;
        Workspace = composition.Workspace;
    }

    public MainWindow Window { get; }

    internal string ManagedRoot => _managedRoot;

    public WorkspaceShellViewModel Workspace { get; }

    public IReadOnlyList<string> DraggedPaths => _dragSource.Paths;

    /// <summary>The diagnostic open and save dialogs; an open with nothing scripted is cancelled.</summary>
    public ScriptedDiagnosticFiles DiagnosticFiles => _diagnosticFiles;

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
        WaitUntil(() => !setup.IsOpen && !SetupDialogIsShown, TimeSpan.FromSeconds(30),
            "skipping setup did not close the dialog");
    }

    internal void SetFakeParserBehavior(object behavior)
    {
        var grammarPaths = Directory.EnumerateFiles(_managedRoot, "*.fwdata", SearchOption.AllDirectories).ToArray();
        Assert.NotEmpty(grammarPaths);
        foreach (var grammarPath in grammarPaths)
            FakeParser.Behave(Path.GetDirectoryName(grammarPath)!, behavior);
        FakeParser.BehaveBesideExecutable(_parserPath
            ?? throw new InvalidOperationException("A fake parser path is required to set its behavior."), behavior);
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
        var selectedPath = _projectPicker.Path;
        ClickProjectMenuEntry("Select a new project");
        var selectionTask = Workspace.SelectNewProjectCommand.ExecutionTask;
        Assert.NotNull(selectionTask);
        WaitUntil(() => !ProjectMenuFlyout.IsOpen,
            TimeSpan.FromSeconds(10), "selecting a new project did not close the project menu");
        WaitUntil(() => selectionTask.IsCompleted,
            TimeSpan.FromSeconds(30), "the project picker did not finish");
        WaitUntil(() => string.Equals(Workspace.Context.ProjectPath, selectedPath,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Workspace.Context.Setup?.ProjectPath, selectedPath,
                StringComparison.OrdinalIgnoreCase) &&
            (Workspace.Baseline.ProjectLastWriteUtc is not null || Workspace.Baseline.ShownRefusal is not null),
            TimeSpan.FromSeconds(60), "the selected project did not finish opening");
    }

    /// <summary>Clicks the project menu's Configure entry through the pointer, in the menu's own popup.</summary>
    public void ConfigureFromProjectMenu() => ClickProjectMenuEntry("Configure the project");

    /// <summary>Opens the project menu and clicks the entry named <paramref name="accessibleName"/> through the pointer.</summary>
    public void ClickProjectMenuEntry(string accessibleName)
    {
        OpenProjectMenu();
        var entry = FindProjectMenuEntry<Button>(accessibleName);
        var menu = TopLevel.GetTopLevel(entry)
            ?? throw new InvalidOperationException($"The project menu's '{accessibleName}' is not in a top level.");
        HeadlessClick.Click(menu, entry, accessibleName);
        Window.UpdateLayout();
        Pump();
        Assert.False(ProjectMenuFlyout.IsOpen, $"Clicking '{accessibleName}' left the project menu open.");
    }

    /// <summary>Focuses the project menu entry named <paramref name="accessibleName"/> and presses <paramref name="key"/> on it.</summary>
    public void PressKeyOnProjectMenuEntry(string accessibleName, Key key, PhysicalKey physicalKey)
    {
        OpenProjectMenu();
        var entry = FindProjectMenuEntry<Button>(accessibleName);
        Assert.True(entry.IsEffectivelyEnabled, $"'{accessibleName}' is not effectively enabled.");
        var menu = TopLevel.GetTopLevel(entry)
            ?? throw new InvalidOperationException($"The project menu's '{accessibleName}' is not in a top level.");
        Assert.True(entry.Focus(NavigationMethod.Tab), $"'{accessibleName}' did not take the keyboard focus.");
        Pump();
        menu.KeyPress(key, RawInputModifiers.None, physicalKey, null);
        menu.KeyRelease(key, RawInputModifiers.None, physicalKey, null);
        Window.UpdateLayout();
        Pump();
        Assert.False(ProjectMenuFlyout.IsOpen, $"Pressing {key} on '{accessibleName}' left the project menu open.");
    }

    /// <summary>Double-clicks the project menu entry named <paramref name="accessibleName"/>; returns its clicks.</summary>
    public int DoubleClickProjectMenuEntry(string accessibleName)
    {
        OpenProjectMenu();
        var entry = FindProjectMenuEntry<Button>(accessibleName);
        var menu = TopLevel.GetTopLevel(entry)
            ?? throw new InvalidOperationException($"The project menu's '{accessibleName}' is not in a top level.");
        var clicks = HeadlessClick.DoubleClick(menu, entry, accessibleName);
        Window.UpdateLayout();
        Pump();
        Assert.False(ProjectMenuFlyout.IsOpen, $"Double-clicking '{accessibleName}' left the project menu open.");
        return clicks;
    }

    /// <summary>Whether the setup dialog is on screen over the window, not merely open in its view model.</summary>
    public bool SetupDialogIsShown =>
        Window.GetLogicalDescendants().OfType<SetupDialog>().Single().IsEffectivelyVisible;

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

    internal Control FindByAutomationId(string automationId)
    {
        return AllControls().Single(control =>
            string.Equals(AutomationProperties.GetAutomationId(control), automationId, StringComparison.Ordinal));
    }

    internal void ClickAutomationId(string automationId)
    {
        var control = FindByAutomationId(automationId);
        Assert.True(control.IsEffectivelyEnabled, $"AutomationId '{automationId}' is disabled.");
        var topLevel = automationId == AutomationIds.SelectNewProject
            ? TopLevel.GetTopLevel(control) ?? Window
            : Window;
        HeadlessClick.Click(topLevel, control, automationId);
        Window.UpdateLayout();
        Pump();
    }

    internal void TypeAutomationId(string automationId, string text)
    {
        var control = Assert.IsType<TextBox>(FindByAutomationId(automationId));
        HeadlessClick.Click(Window, control, automationId);
        Assert.True(control.IsFocused, $"AutomationId '{automationId}' did not receive focus from the click.");
        Window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.None, null);
        Window.KeyTextInput(text);
        Pump();
        Assert.Equal(text, control.Text);
    }

    internal string? TextByAutomationId(string automationId) => FindByAutomationId(automationId) switch
    {
        TextBlock text => text.Text,
        TextBox text => text.Text,
        ContentControl content => content.Content?.ToString(),
        _ => null,
    };

    internal Rect BoundsByAutomationId(string automationId)
    {
        var control = FindByAutomationId(automationId);
        if (!control.IsEffectivelyVisible || control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
            throw new InvalidOperationException($"AutomationId '{automationId}' has no visible bounds.");
        var origin = control.TranslatePoint(new Point(), Window)
            ?? throw new InvalidOperationException($"AutomationId '{automationId}' is outside the main window.");
        return new Rect(origin, control.Bounds.Size);
    }

    private IEnumerable<Control> AllControls()
    {
        yield return Window;
        foreach (var control in Window.GetLogicalDescendants().OfType<Control>()) yield return control;
        foreach (var ownedWindow in Window.OwnedWindows)
        {
            yield return ownedWindow;
            foreach (var control in ownedWindow.GetLogicalDescendants().OfType<Control>()) yield return control;
        }
        if (ProjectMenuFlyout.Content is Control content)
        {
            yield return content;
            foreach (var control in content.GetLogicalDescendants().OfType<Control>()) yield return control;
        }
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
        WalkthroughSteps.EnsureAssessmentForAnalyze(this, TimeSpan.FromMinutes(3));
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
        WalkthroughSteps.EnsureAssessmentForAnalyze(this, TimeSpan.FromMinutes(3));
        ShowPage(WorkspacePage.Texts);
        ShowTextsTab(TextsTab.AnalyzeTexts);
        var checkBox = Window.GetLogicalDescendants().OfType<CheckBox>().Single(control =>
            Equals(control.Content, content));
        ShowStageOwning(checkBox);
        if (checkBox.IsChecked != true) ClickControl(checkBox, content);
        Assert.True(checkBox.IsChecked, $"'{content}' was not checked.");
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
            $"baseline refusal='{Workspace.Baseline.ShownRefusal?.Sentence}', " +
            $"selection empty='{Workspace.Selection.TextsEmptyMessage}', " +
            $"selection refusal='{Workspace.Selection.ShownRefusal?.Sentence}', " +
            $"Assess.State='{Workspace.Assess.State}', " +
            $"Assess.Refusal?.Message='{Workspace.Assess.Refusal?.Message}', " +
            $"Assess.Progress='{Workspace.Assess.Progress}', " +
            $"Refresh.IsRunning='{Workspace.RefreshCommand.IsRunning}', " +
            $"Overview.loaded='{Workspace.PageModel<OverviewPageModel>().Overview is not null}', " +
            $"Overview.refusal='{Workspace.PageModel<OverviewPageModel>().OverviewRefusal?.Sentence}', " +
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
