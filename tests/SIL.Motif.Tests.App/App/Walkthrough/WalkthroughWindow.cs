using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

public sealed class WalkthroughWindow : IDisposable
{
    private readonly ScriptedProjectPicker _projectPicker;
    private readonly ScriptedFolderPicker _folderPicker;
    private readonly RecordingDragSource _dragSource;

    public WalkthroughWindow(
        string managedRoot, string projectPath, string? folderPath = null, ICommandClient? commandClient = null)
    {
        _projectPicker = new ScriptedProjectPicker(projectPath);
        _folderPicker = new ScriptedFolderPicker(folderPath);
        _dragSource = new RecordingDragSource();

        commandClient ??= new CommandClient(managedRoot);
        var selection = new SelectionViewModel(commandClient);
        var words = new TextWordsViewModel(commandClient, selection);
        Workspace = new HandoffWorkspaceViewModel(
            new ProjectViewModel(commandClient, _projectPicker),
            new ProjectHistoryViewModel(commandClient),
            new BaselineViewModel(commandClient),
            selection,
            words,
            new AssessViewModel(commandClient, selection),
            _folderPicker, _dragSource,
            commandClient);

        Window = new MainWindow();
        Window.Compose(Workspace);
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

    public void LoadKnownProjects()
    {
        var loading = Workspace.Project.LoadKnownProjectsAsync();
        WaitUntil(() => loading.IsCompleted, TimeSpan.FromSeconds(30), "Known projects did not load");
        loading.GetAwaiter().GetResult();
    }

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
        else if (owners.Contains("DifferenceHost")) ShowTextsTab(TextsTab.WhatChanged);
        else if (owners.Contains("AssessHost")) ShowTextsTab(TextsTab.Words);
        else if (owners.Contains("SelectionHost")) ShowTextsTab(TextsTab.Texts);
        else if (owners.Contains("ResultsInTextHost")) ShowTextsTab(TextsTab.InText);
    }

    /// <summary>Opens a Texts page tab the way a person does, by its tab above the page.</summary>
    public void ShowTextsTab(TextsTab tab)
    {
        if (Workspace.PageModel<TextsPageModel>().Tab == tab) return;

        var name = tab switch
        {
            TextsTab.WhatChanged => "What changed tab",
            TextsTab.InText => "In text tab",
            _ => $"{tab} tab",
        };
        ClickControl(Find<Button>(name), name);
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
        Assert.Equal(page, Workspace.CurrentPage);
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
        ShowTextsTab(TextsTab.Texts);
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
        var comboBox = Find<ComboBox>("Known projects");
        var project = Workspace.Project.KnownProjects.Single(known =>
            string.Equals(known.FullFwDataPath, projectPath, StringComparison.OrdinalIgnoreCase));
        ClickControl(comboBox, "Known projects");
        Assert.True(comboBox.IsDropDownOpen, "The Known projects picker did not open from a mouse click.");
        for (var attempts = 0; attempts <= comboBox.ItemCount; attempts++)
        {
            if (Equals(comboBox.SelectedItem, project)) break;
            Window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            Pump();
        }

        Assert.Equal(project, comboBox.SelectedItem);
        Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
        Pump();
    }

    public void DragAllFiles()
    {
        var allFiles = Find<Button>("Drag all Handoff files");
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
            $"Handoff.State='{Workspace.PageModel<AiHandoffPageModel>().Handoff.State}', " +
            $"Handoff.Refusal?.Message='{Workspace.PageModel<AiHandoffPageModel>().Handoff.Refusal?.Message}', " +
            $"Handoff.Progress='{Workspace.PageModel<AiHandoffPageModel>().Handoff.Progress}', " +
            $"project='{Workspace.Project.KnownProjects.Count}' known projects");
    }

    public void Dispose()
    {
        Window.Close();
        Workspace.DisposeAsync().GetAwaiter().GetResult();
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private void ClickControl(Control control, string accessibleName)
    {
        Assert.True(control.IsEffectivelyEnabled, $"'{accessibleName}' is not effectively enabled.");
        control.BringIntoView();
        Pump();
        Window.UpdateLayout();
        var bounds = control.Bounds;
        var point = control.TranslatePoint(new Point(bounds.Width / 2, bounds.Height / 2), Window)
            ?? throw new InvalidOperationException($"'{accessibleName}' is not positioned in the walkthrough window.");
        Window.MouseMove(point);
        Window.MouseDown(point, MouseButton.Left);
        Window.MouseUp(point, MouseButton.Left);
        Pump();
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
