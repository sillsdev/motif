using System.Diagnostics;
using Avalonia.Controls;
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

    public WalkthroughWindow(string managedRoot, string projectPath, string? folderPath = null)
    {
        _projectPicker = new ScriptedProjectPicker(projectPath);
        _folderPicker = new ScriptedFolderPicker(folderPath);
        _dragSource = new RecordingDragSource();

        var commandClient = new CommandClient(managedRoot);
        var selection = new SelectionViewModel(commandClient);
        Workspace = new HandoffWorkspaceViewModel(
            new ProjectViewModel(commandClient, _projectPicker),
            new BaselineViewModel(commandClient),
            selection,
            new AssessViewModel(commandClient, selection),
            new StatisticsViewModel(commandClient),
            new HandoffViewModel(commandClient, selection, _folderPicker, _dragSource));

        Window = new MainWindow();
        Window.Compose(Workspace);
    }

    public MainWindow Window { get; }

    public HandoffWorkspaceViewModel Workspace { get; }

    public IReadOnlyList<string> DraggedPaths => _dragSource.Paths;

    public void Show()
    {
        Window.Show();
        Window.ApplyTemplate();
        Window.UpdateLayout();
        Pump();
    }

    public T Find<T>(string accessibleName) where T : Control =>
        Window.GetLogicalDescendants().OfType<T>().Single(control =>
            string.Equals(Avalonia.Automation.AutomationProperties.GetName(control), accessibleName,
                StringComparison.Ordinal));

    public void Click(string accessibleName)
    {
        var button = Find<Button>(accessibleName);
        Assert.True(button.IsEffectivelyEnabled, $"'{accessibleName}' is not effectively enabled.");
        button.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Return });
        Pump();
    }

    public void Check(string content)
    {
        var checkBox = Window.GetLogicalDescendants().OfType<CheckBox>().Single(control =>
            Equals(control.Content, content));
        Assert.True(checkBox.IsEffectivelyEnabled, $"'{content}' is not effectively enabled.");
        checkBox.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Return });
        Pump();
    }

    public void Type(string accessibleName, string text)
    {
        var textBox = Find<TextBox>(accessibleName);
        Assert.True(textBox.IsEffectivelyEnabled, $"'{accessibleName}' is not effectively enabled.");
        textBox.Text = text;
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
            Thread.Yield();
        }

        Pump();
        Assert.True(predicate(),
            $"{why}; baseline='{Workspace.Baseline.CapturedTimeText}', " +
            $"baseline refusal='{Workspace.Baseline.RefusalMessage}', " +
            $"selection empty='{Workspace.Selection.TextsEmptyMessage}', " +
            $"selection refusal='{Workspace.Selection.RefusalMessage}', " +
            $"project='{Workspace.Project.KnownProjects.Count}' known projects");
    }

    public void Dispose()
    {
        Window.Close();
        Workspace.DisposeAsync().GetAwaiter().GetResult();
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private sealed class ScriptedProjectPicker(string path) : IProjectPicker
    {
        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(path);
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
