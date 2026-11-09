using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.TextEditing.Avalonia;

namespace SIL.Motif.App.Preview;

/// <summary>
/// A developer-only window that lists a project's Grammar Design Records and shows one's Discussion read-only, with the
/// project's own styles and writing systems. It reads only through <see cref="GdrQuery"/>.
/// </summary>
public sealed class GdrPreviewWindow : Window
{
    /// <summary>The start-up argument that opens this window, followed by the project file, when the developer flag is set.</summary>
    public const string StartupArgument = "--gdr-preview";

    private readonly Func<string, GdrShowResponse?> _show;
    private readonly ListBox _records = new() { Width = 220 };
    private readonly ScrollViewer _scroll = new()
    {
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
    };
    private readonly TextBlock _heading = new() { FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };

    /// <summary>Creates the window over <paramref name="records"/>; <paramref name="show"/> reads one record by its id.</summary>
    public GdrPreviewWindow(IReadOnlyList<GdrSummary> records, Func<string, GdrShowResponse?> show)
    {
        _show = show;
        Title = "Grammar Design Records preview (developer only)";
        Width = 900;
        Height = 560;

        _records.ItemsSource = records.Select(record => $"{record.Abbreviation}  {record.Title}").ToArray();
        Records = records;
        _records.SelectionChanged += (_, _) =>
        {
            if (_records.SelectedIndex >= 0) Show(records[_records.SelectedIndex].Id);
        };

        var reading = new Border
        {
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Child = _scroll,
        };
        var right = new DockPanel { Margin = new Thickness(12, 0, 0, 0), LastChildFill = true };
        DockPanel.SetDock(_heading, Dock.Top);
        right.Children.Add(_heading);
        right.Children.Add(reading);
        var panel = new DockPanel { Margin = new Thickness(12), LastChildFill = true };
        DockPanel.SetDock(_records, Dock.Left);
        panel.Children.Add(_records);
        panel.Children.Add(right);
        Content = panel;
    }

    /// <summary>The records listed.</summary>
    public IReadOnlyList<GdrSummary> Records { get; }

    /// <summary>The read-only field that shows the selected record's Discussion, or null before one is selected.</summary>
    public RichTextField? Field => _scroll.Content as RichTextField;

    /// <summary>The heading above the field: the selected record's number, title and abstract.</summary>
    public string HeadingText => _heading.Text ?? string.Empty;

    /// <summary>Selects the record <paramref name="id"/> and shows its Discussion.</summary>
    public void Show(string id)
    {
        var shown = _show(id);
        if (shown is null) return;
        var discussion = GdrDiscussion.From(shown);
        // A field edits one document for its whole life, so each record gets a new read-only field.
        _scroll.Content = new RichTextField(discussion.Document)
        {
            WritingSystems = discussion.WritingSystems,
            StyleResolver = discussion.Styles,
            IsReadOnly = true,
        };
        _heading.Text = $"{shown.Record.Abbreviation}  {shown.Record.Title}\n{shown.Record.Description}";
    }

    /// <summary>Whether <paramref name="developerFlag"/> and <paramref name="args"/> ask for this window.</summary>
    public static bool IsRequested(string? developerFlag, IEnumerable<string> args) =>
        CommandSurfacePolicy.FromEnvironment(developerFlag).DeveloperCommandsEnabled &&
        args.Contains(StartupArgument, StringComparer.Ordinal);

    /// <summary>Opens the window for the project that follows the start-up argument, or returns <see langword="null"/>.</summary>
    public static GdrPreviewWindow? ShowIfRequested(IEnumerable<string> args)
    {
        var arguments = args.ToArray();
        if (!IsRequested(Environment.GetEnvironmentVariable(CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable), arguments))
            return null;
        var at = Array.IndexOf(arguments, StartupArgument);
        if (at + 1 >= arguments.Length) return null;
        var project = arguments[at + 1];
        var listed = GdrQuery.List(new GdrListRequest(project));
        if (!listed.Succeeded) return null;
        var window = new GdrPreviewWindow(listed.Value!.Records,
            id => GdrQuery.Show(new GdrShowRequest(project, id)) is { Succeeded: true } shown ? shown.Value : null);
        window.Show();
        return window;
    }
}
