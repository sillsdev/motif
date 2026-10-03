using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls;
using SIL.Motif.App.Composition;
using SIL.Motif.App;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using AvaloniaEllipse = Avalonia.Controls.Shapes.Ellipse;

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
        // The Baseline's answer comes first; later open stages still read the project's Motif file on the pool.
        WaitUntilProjectOpened(TimeSpan.FromSeconds(60), "the selected project's open stages did not finish");
    }

    /// <summary>
    /// Waits until the window has stopped reading the project's files: no open stage, Refresh or evidence
    /// publication is still running. On Windows a test cannot read a file the window still has open, pinned by
    /// <c>ACorruptStoreIsShownInTheWindowWithoutADeleteButtonOrByteChanges</c>.
    /// </summary>
    public void WaitUntilProjectIsQuiet(TimeSpan timeout, string why) =>
        WaitUntil(() => !Workspace.Context.IsOpeningProject && Workspace.Context.EvidencePublication.IsCompleted &&
            !Workspace.RefreshCommand.IsRunning, timeout, why);

    private void WaitUntilProjectOpened(TimeSpan timeout, string why) =>
        WaitUntil(() => !Workspace.Context.IsOpeningProject && Workspace.Context.EvidencePublication.IsCompleted,
            timeout, why);

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

    internal Control? FindOptionalByAutomationId(string automationId) =>
        AllControls().SingleOrDefault(control =>
            string.Equals(AutomationProperties.GetAutomationId(control), automationId, StringComparison.Ordinal));

    internal string DescribeAutomationId(string automationId)
    {
        var control = FindOptionalByAutomationId(automationId);
        if (control is null) return $"AutomationId '{automationId}' is missing.";
        if (control.DataContext is not ResultsTokenViewModel token)
            return $"AutomationId '{automationId}' visible={control.IsEffectivelyVisible}.";
        var opinions = string.Join(", ", token.Marking.FieldWorksAnalyses.Select(analysis => analysis.Opinion));
        var readings = string.Join(", ", token.Marking.PanGlossReadings.Select(reading =>
            $"stored={reading.MatchesStored}, opinion={reading.MatchingOpinions ?? "none"}"));
        return $"Word='{token.Form}', visible={control.IsEffectivelyVisible}, verdict={token.Verdict}, " +
            $"marking={token.Marking.PanGlossClass}, opinions=[{opinions}], readings=[{readings}], " +
            $"action='{token.Marking.PrimaryAction?.Label ?? "none"}'.";
    }

    internal void ScrollIntoView(string automationId)
    {
        if (FindOptionalByAutomationId(automationId) is null)
        {
            foreach (var panel in Window.GetLogicalDescendants().OfType<ResultsInTextPanel>()
                         .Where(panel => panel.IsEffectivelyVisible))
            {
                var lines = panel.FindControl<ItemsControl>("TextLineItems")!;
                for (var index = 0; index < lines.ItemCount; index++)
                {
                    lines.ScrollIntoView(index);
                    Window.UpdateLayout();
                    Pump();
                    if (FindOptionalByAutomationId(automationId) is not null) break;
                }
                if (FindOptionalByAutomationId(automationId) is not null) break;
            }
        }
        var target = FindByAutomationId(automationId);
        target.BringIntoView();
        Pump();
        Window.UpdateLayout();
    }

    internal void ClickAutomationId(string automationId)
    {
        var control = FindByAutomationId(automationId);
        Assert.True(control.IsEffectivelyEnabled, $"AutomationId '{automationId}' is disabled.");
        var topLevel = automationId == AutomationIds.SelectNewProject
            ? TopLevel.GetTopLevel(control) ?? Window
            : Window;
        ClickControl(control, automationId, topLevel);
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

    internal IReadOnlyList<string> VisibleTextUnderAutomationId(string automationId) =>
        FindByAutomationId(automationId).GetLogicalDescendants().OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(text.Text))
            .Select(text => text.Text!).ToArray();

    internal bool HasVisibleTextOrMark(string automationId)
    {
        var target = FindByAutomationId(automationId);
        if (!target.IsEffectivelyVisible) return false;
        var controls = target.GetVisualDescendants().OfType<Control>().Prepend(target)
            .Where(control => control.IsEffectivelyVisible).ToArray();
        return controls.OfType<TextBlock>().Any(text => !string.IsNullOrWhiteSpace(text.Text)) ||
            controls.Any(control => control is OpinionMark { Kind: not null } or UnreadMark or AvaloniaEllipse);
    }

    internal Rect BoundsByAutomationId(string automationId)
    {
        var control = FindByAutomationId(automationId);
        if (!control.IsEffectivelyVisible || control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
        {
            var state = control.DataContext is ResultsTokenViewModel token
                ? $" Word='{token.Form}', verdict={token.Verdict}, marking={token.Marking.PanGlossClass}, " +
                    $"opinion='{string.Join(", ", token.Marking.FieldWorksAnalyses.Select(analysis => analysis.Opinion))}', " +
                    $"readings={token.Readings.Count}, action='{token.Marking.PrimaryAction?.Label ?? "none"}'."
                : string.Empty;
            throw new InvalidOperationException($"AutomationId '{automationId}' has no visible bounds.{state}");
        }
        var clippedBy = control.GetVisualAncestors().OfType<ScrollViewer>()
            .FirstOrDefault(viewer =>
            {
                var viewportOrigin = control.TranslatePoint(new Point(), viewer);
                return viewportOrigin is not { } point ||
                    !FitsViewport(new Rect(point, control.Bounds.Size), viewer.Viewport);
            });
        if (clippedBy is not null)
        {
            var viewportOrigin = control.TranslatePoint(new Point(), clippedBy);
            var targetBounds = viewportOrigin is { } point ? new Rect(point, control.Bounds.Size) : default;
            throw new InvalidOperationException(
                $"AutomationId '{automationId}' bounds {targetBounds} are outside its " +
                $"ScrollViewer viewport {clippedBy.Viewport.Width}x{clippedBy.Viewport.Height}.");
        }
        var origin = control.TranslatePoint(new Point(), Window)
            ?? throw new InvalidOperationException($"AutomationId '{automationId}' is outside the main window.");
        return new Rect(origin, control.Bounds.Size);
    }

    internal static bool FitsViewport(Rect target, Size viewport) =>
        target.X >= 0 && target.Y >= 0 && target.Right <= viewport.Width && target.Bottom <= viewport.Height;

    internal IReadOnlyList<(string AutomationId, Rect Bounds)> VisibleWordStripBounds()
    {
        var strips = new List<(string AutomationId, Rect Bounds)>();
        foreach (var control in AllControls())
        {
            var automationId = AutomationProperties.GetAutomationId(control);
            if (automationId?.EndsWith("-strip", StringComparison.Ordinal) != true ||
                !control.IsEffectivelyVisible || control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
                continue;
            if (control.TranslatePoint(new Point(), Window) is { } origin)
                strips.Add((automationId, new Rect(origin, control.Bounds.Size)));
        }
        return strips;
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

    public void WaitUntil(Func<bool> predicate, TimeSpan timeout, string why, Func<string>? failureDetail = null)
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
            $"{why}; {failureDetail?.Invoke()} baseline='{Workspace.Baseline.CapturedTimeText}', " +
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

    private void ClickControl(Control control, string accessibleName, TopLevel? topLevel = null)
    {
        // Stored evidence lands after the project's own counts and can push the target down mid-click.
        WaitUntilProjectOpened(
            TimeSpan.FromSeconds(60), $"the project was still opening when '{accessibleName}' was to be clicked");
        var root = topLevel ?? Window;
        Rect? previousBounds = null;
        var stablePasses = 0;
        WaitUntil(() =>
        {
            root.UpdateLayout();
            var topLeft = control.TranslatePoint(new Point(0, 0), root);
            if (!control.IsEffectivelyVisible || topLeft is null)
            {
                previousBounds = null;
                stablePasses = 0;
                return false;
            }

            var bounds = new Rect(topLeft.Value, control.Bounds.Size);
            if (bounds == previousBounds) stablePasses++;
            else
            {
                previousBounds = bounds;
                stablePasses = 0;
            }

            return stablePasses >= 2;
        }, TimeSpan.FromSeconds(10), $"'{accessibleName}' did not settle before clicking");
        HeadlessClick.Click(root, control, accessibleName);
    }

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
