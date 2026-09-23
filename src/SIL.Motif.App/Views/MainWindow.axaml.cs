using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// The single-window shell: a minimal frame until <see cref="Compose"/> supplies its workspace and
/// builds the panels, so a caller can construct and inspect the bare shell (<c>AppSmokeTests</c>) without
/// standing up any command client or desktop adapter.
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

    /// <summary>Builds the workflow's panels from <paramref name="workspace"/> and binds the window to it.</summary>
    public void Compose(HandoffWorkspaceViewModel workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        DataContext = workspace;

        Host("ProjectHost").Content = new ProjectPanel(workspace);
        Host("GrammarHost").Content = new GrammarPanel(workspace.Grammar);
        Host("SelectionHost").Content = new SelectionPanel(workspace.Selection, workspace.Words);
        Host("AssessHost").Content = new AssessPanel(workspace.Assess);
        Host("ResultsInTextHost").Content = new ResultsInTextPanel(workspace.ResultsInText);
        Host("StatisticsHost").Content = new StatisticsPanel(workspace.Statistics);
        Host("HandoffHost").Content = new HandoffPanel(workspace.Handoff);
    }

    private void OnChangeProjectClick(object? sender, RoutedEventArgs e) =>
        this.FindControl<Button>("ProjectMenuButton")?.Flyout?.Hide();
    private async void OnOpenDiagnosticClick(object? sender, RoutedEventArgs e)
    {
        this.FindControl<Button>("ProjectMenuButton")?.Flyout?.Hide();
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open diagnostic JSON",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Motif diagnostic JSON") { Patterns = ["*.json"] }],
            });
            if (files.Count == 0) return;

            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            var trace = TraceWordViewModel.FromDiagnosticJson(await reader.ReadToEndAsync());
            new DiagnosticWindow(trace).Show(this);
        }
        catch (Exception exception)
        {
            var window = new DiagnosticWindow(new TraceWordViewModel());
            window.Show(this);
            window.ShowDiagnosticError($"Unable to open diagnostic: {exception.Message}");
        }
    }

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
