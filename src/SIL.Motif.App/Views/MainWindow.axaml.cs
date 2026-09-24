using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// The single-window shell — a top bar, a sidebar of pages, and the page on screen — as a minimal frame
/// until <see cref="Compose"/> supplies its workspace and builds the panels, so a caller can construct and
/// inspect the bare shell (<c>AppSmokeTests</c>) without standing up any command client or desktop adapter.
/// </summary>
public sealed partial class MainWindow : Window
{
    private static readonly string PreferencesFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Motif", "window-bounds.json");

    /// <summary>A window at the XAML's own size that neither reads nor writes the remembered bounds, as tests need.</summary>
    public MainWindow() : this(rememberBounds: false)
    {
    }

    /// <summary>
    /// A window that, when <paramref name="rememberBounds"/> is true, reopens at the size and place the person
    /// last left it and saves them again on close.
    /// </summary>
    public MainWindow(bool rememberBounds)
    {
        AvaloniaXamlLoader.Load(this);
        if (!rememberBounds) return;
        RestoreBounds();
        Closing += (_, _) => SaveBounds();
    }

    /// <summary>Builds each page's panel from <paramref name="workspace"/> and binds the window to it.</summary>
    public void Compose(HandoffWorkspaceViewModel workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        DataContext = workspace;

        Host("ProjectHost").Content = new ProjectPanel(workspace);
        Host("GrammarHost").Content = new GrammarPanel(workspace.Grammar);
        Host("SelectionHost").Content = new SelectionPanel(workspace.Selection, workspace.Words);
        Host("AssessHost").Content = new AssessPanel(workspace.Assess);
        Host("CompareHost").Content = new ComparePanel(workspace.Assess.Compare);
        Host("DifferenceHost").Content = new DifferencePanel(workspace.Assess.Difference);
        Host("ResultsInTextHost").Content = new ResultsInTextPanel(workspace.ResultsInText);
        Host("TryWordHost").Content = new TryWordPanel(workspace.Assess.Trace);
        Host("StatisticsHost").Content = new StatisticsPanel(workspace.Statistics);
        Host("ReviewHost").Content = new ReviewPanel(workspace);
        Host("HandoffHost").Content = new HandoffPanel(workspace.Handoff);

        workspace.UpdateWindowWidth(Width);
        SizeChanged += (_, e) => workspace.UpdateWindowWidth(e.NewSize.Width);
        // Coming back from FieldWorks is when a save it made is news; this reads, it never reruns.
        Activated += (_, _) => _ = workspace.CheckFreshnessAsync();
        workspace.RecentProjects.CollectionChanged += (_, _) => RebuildRecentProjects(workspace);
        RebuildRecentProjects(workspace);
    }

    /// <summary>The project menu's Open recent entries, one per recent project, as the menu shows them.</summary>
    public IReadOnlyList<MenuItem> RecentProjectItems =>
        this.FindControl<Button>("OpenRecentButton")?.Flyout is MenuFlyout menu
            ? menu.Items.OfType<MenuItem>().ToList()
            : [];

    private void RebuildRecentProjects(HandoffWorkspaceViewModel workspace)
    {
        if (this.FindControl<Button>("OpenRecentButton")?.Flyout is not MenuFlyout menu) return;
        menu.Items.Clear();
        foreach (var recent in workspace.RecentProjects)
        {
            var item = new MenuItem
            {
                Header = recent.Name,
                Command = workspace.OpenRecentProjectCommand,
                CommandParameter = recent,
            };
            ToolTip.SetTip(item, recent.FullFwDataPath);
            Avalonia.Automation.AutomationProperties.SetName(item, recent.AutomationName);
            item.Click += (_, _) => HideProjectMenu();
            menu.Items.Add(item);
        }
    }

    private void OnProjectMenuEntryClick(object? sender, RoutedEventArgs e) => HideProjectMenu();

    private void HideProjectMenu() => this.FindControl<Button>("ProjectMenuButton")?.Flyout?.Hide();

    // Looked up by name rather than a generated field, so this never depends on the compiler's own codegen.
    private ContentControl Host(string name) =>
        this.FindControl<ContentControl>(name)
        ?? throw new InvalidOperationException($"MainWindow.axaml has no element named '{name}'.");

    // A preferences read failure must not block startup; an unreadable or absent file just keeps defaults.
    private void RestoreBounds()
    {
        try
        {
            if (!File.Exists(PreferencesFilePath)) return;
            var bounds = JsonSerializer.Deserialize<WindowBounds>(File.ReadAllText(PreferencesFilePath));
            if (bounds is null) return;

            Width = Math.Max(bounds.Width, MinWidth);
            Height = Math.Max(bounds.Height, MinHeight);
            if (bounds.X is { } x && bounds.Y is { } y) Position = new PixelPoint((int)x, (int)y);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // Falling back to the XAML-declared default bounds is preferable to a startup crash.
        }
    }

    // A preferences write failure must not block shutdown.
    private void SaveBounds()
    {
        try
        {
            var directory = Path.GetDirectoryName(PreferencesFilePath);
            if (directory is not null) Directory.CreateDirectory(directory);
            var bounds = new WindowBounds(Width, Height, Position.X, Position.Y);
            File.WriteAllText(PreferencesFilePath, JsonSerializer.Serialize(bounds));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Losing the remembered window position is preferable to a crash on close.
        }
    }

    private sealed record WindowBounds(double Width, double Height, double? X, double? Y);
}
