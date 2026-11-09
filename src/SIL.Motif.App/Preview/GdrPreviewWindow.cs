using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;
using SIL.TextEditing;
using SIL.TextEditing.Avalonia;

namespace SIL.Motif.App.Preview;

/// <summary>
/// A developer-only window that lists a project's Grammar Design Records and lets a person edit one's Discussion, with
/// the project's own styles and writing systems. It reads through <see cref="GdrQuery"/>; each record's edits add up to
/// one pending <see cref="GdrDiscussionEdit"/>, measured against the text the record was shown with, which an
/// <see cref="IGdrEditStore"/> keeps as one operation in the pending-changes Draft and applies.
/// </summary>
public sealed class GdrPreviewWindow : Window
{
    /// <summary>The start-up argument that opens this window, followed by the project file, when the developer flag is set.</summary>
    public const string StartupArgument = "--gdr-preview";

    private readonly Func<string, GdrShowResponse?> _show;
    private readonly IGdrEditStore? _store;
    private readonly Func<Task<bool>> _confirmStartAgain;
    private readonly HashSet<string> _staged = new(StringComparer.Ordinal);
    private readonly HashSet<string> _stale = new(StringComparer.Ordinal);
    private readonly Border _staleStrip;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private Task _staging = Task.CompletedTask;
    private readonly ListBox _records = new() { Width = 220 };
    private readonly ScrollViewer _scroll = new()
    {
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
    };
    private readonly TextBlock _heading = new() { FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _target = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly Dictionary<string, OpenedRecord> _opened = new(StringComparer.Ordinal);
    private string? _shownId;

    /// <summary>
    /// Creates the window over <paramref name="records"/>; <paramref name="show"/> reads one record by its id, and
    /// <paramref name="store"/>, when given, stages each edit and offers Apply. <paramref name="confirmStartAgain"/>
    /// asks the person before their edit of a record changed in FieldWorks is thrown away; by default a dialog asks.
    /// </summary>
    public GdrPreviewWindow(IReadOnlyList<GdrSummary> records, Func<string, GdrShowResponse?> show, IGdrEditStore? store = null,
        Func<Task<bool>>? confirmStartAgain = null)
    {
        _show = show;
        _store = store;
        _confirmStartAgain = confirmStartAgain ?? ConfirmStartAgainAsync;
        var startAgain = new Button { Content = "Start again from the FieldWorks text" };
        startAgain.Click += async (_, _) => await StartAgainFromFieldWorksAsync();
        var strip = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12 };
        strip.Children.Add(new TextBlock
        {
            Text = StaleMessage,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });
        strip.Children.Add(startAgain);
        _staleStrip = new Border
        {
            Background = Brushes.LightYellow,
            Padding = new Thickness(8, 4),
            IsVisible = false,
            Child = strip,
        };
        Title = "Grammar Design Records preview (developer only)";
        Width = 900;
        Height = 560;

        _records.ItemsSource = records.Select(record => $"{record.Abbreviation}  {record.Title}").ToArray();
        Records = records;
        _records.SelectionChanged += (_, _) =>
        {
            if (_records.SelectedIndex >= 0) Show(records[_records.SelectedIndex].Id);
        };

        var inField = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_staleStrip, Dock.Top);
        inField.Children.Add(_staleStrip);
        inField.Children.Add(_scroll);
        var reading = new Border
        {
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Child = inField,
        };
        var right = new DockPanel { Margin = new Thickness(12, 0, 0, 0), LastChildFill = true };
        DockPanel.SetDock(_heading, Dock.Top);
        right.Children.Add(_heading);
        DockPanel.SetDock(_target, Dock.Bottom);
        right.Children.Add(_target);
        if (store is not null)
        {
            var apply = new Button { Content = "Apply", Margin = new Thickness(0, 6, 0, 0) };
            apply.Click += async (_, _) => await ApplyAsync();
            var actions = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12 };
            actions.Children.Add(apply);
            actions.Children.Add(_status);
            DockPanel.SetDock(actions, Dock.Bottom);
            right.Children.Add(actions);
        }
        right.Children.Add(reading);
        var panel = new DockPanel { Margin = new Thickness(12), LastChildFill = true };
        DockPanel.SetDock(_records, Dock.Left);
        panel.Children.Add(_records);
        panel.Children.Add(right);
        Content = panel;
    }

    /// <summary>The records listed.</summary>
    public IReadOnlyList<GdrSummary> Records { get; }

    /// <summary>The field that edits the selected record's Discussion, or null before one is selected.</summary>
    public RichTextField? Field => _scroll.Content as RichTextField;

    /// <summary>The session hosting the selected record's field, or null before one is selected.</summary>
    public GdrDiscussionSession? Session => _shownId is null ? null : _opened[_shownId].Session;

    /// <summary>Every record's pending edit, by record id; a record edited back to how it began has none.</summary>
    public IReadOnlyDictionary<string, GdrDiscussionEdit> PendingEdits => _opened.Values
        .Where(opened => opened.Session.Pending is not null)
        .ToDictionary(opened => opened.Session.Pending!.RecordId, opened => opened.Session.Pending!, StringComparer.Ordinal);

    /// <summary>Raised when a session ends and changes a record's pending edit (null when the record is back as it began).</summary>
    public event EventHandler<GdrDiscussionEdit?>? PendingEditChanged;

    /// <summary>What the last staging or Apply said: a refusal's message, "Applied." or nothing.</summary>
    public string StatusText => _status.Text ?? string.Empty;

    /// <summary>What the strip in the field says when the shown record's text was also changed in FieldWorks.</summary>
    public const string StaleMessage = "This Discussion was also changed in FieldWorks. Your typing is safe.";

    /// <summary>Whether the strip saying the shown record was also changed in FieldWorks is showing.</summary>
    public bool IsStaleStripShown => _staleStrip.IsVisible;

    /// <summary>
    /// Ends the shown record's session, waits for every edit to reach the Draft, and applies the pending changes. Once
    /// they are written, each staged record's paragraphs take the keys the project gave them and its session starts
    /// again from the applied text; returns whether Apply wrote them.
    /// </summary>
    public async Task<bool> ApplyAsync()
    {
        if (_store is null) return false;
        Session?.EndSession();
        await _staging;
        if (await _store.ApplyAsync() is { } refusal)
        {
            if (refusal.StaleRecordId is { } stale)
            {
                _stale.Add(stale);
                _status.Text = "Nothing was applied.";
                ShowStaleStrip();
            }
            else
            {
                _status.Text = refusal.Message;
            }
            return false;
        }
        foreach (var id in _staged.ToArray()) Rekey(id);
        _staged.Clear();
        _status.Text = "Applied.";
        return true;
    }

    /// <summary>
    /// After the person confirms, throws away the shown record's edit, which the record's text in FieldWorks has
    /// overtaken, and shows the record again from the FieldWorks text as the new start; returns whether it did.
    /// </summary>
    public async Task<bool> StartAgainFromFieldWorksAsync()
    {
        if (_store is null || _shownId is not { } id || !_stale.Contains(id)) return false;
        if (!await _confirmStartAgain()) return false;
        var opened = _opened[id];
        opened.Session.EndSession();
        if (await Stage(id, null, opened.Field.Editor.Document) is not null) return false;
        _stale.Remove(id);
        _opened.Remove(id);
        Show(id);
        return true;
    }

    private Task<string?> Stage(string recordId, GdrDiscussionEdit? edit, SIL.TextEditing.Document document)
    {
        var staged = StageAfter(_staging, recordId, edit is not null, GdrDiscussionPayload.From(edit, document));
        _staging = staged;
        return staged;
    }

    // One staging at a time, in the order the sessions ended, so the Draft ends up with each record's latest edit.
    private async Task<string?> StageAfter(Task earlier, string recordId, bool pending, TextParagraphsPayload paragraphs)
    {
        await earlier;
        var refusal = await _store!.StageAsync(recordId, paragraphs);
        if (refusal is null && pending) _staged.Add(recordId);
        else if (refusal is null) _staged.Remove(recordId);
        _status.Text = refusal ?? string.Empty;
        return refusal;
    }

    private void ShowStaleStrip() => _staleStrip.IsVisible = _shownId is { } id && _stale.Contains(id);

    private async Task<bool> ConfirmStartAgainAsync()
    {
        var dialog = new Window
        {
            Title = "Start again from the FieldWorks text",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var keep = new Button { Content = "Keep my edit", IsDefault = true, IsCancel = true };
        var discard = new Button { Content = "Discard my edit" };
        keep.Click += (_, _) => dialog.Close(false);
        discard.Click += (_, _) => dialog.Close(true);
        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
        };
        buttons.Children.Add(keep);
        buttons.Children.Add(discard);
        var body = new StackPanel { Margin = new Thickness(16), Spacing = 12 };
        body.Children.Add(new TextBlock
        {
            Text = "Your edit of this Discussion will be thrown away and the Discussion shown as it is now in FieldWorks.",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420,
        });
        body.Children.Add(buttons);
        dialog.Content = body;
        return await dialog.ShowDialog<bool>(this);
    }

    // New paragraphs get their keys from the project after Apply; a reshaped field is rebuilt instead.
    private void Rekey(string id)
    {
        if (!_opened.TryGetValue(id, out var opened)) return;
        var paragraphs = opened.Field.Editor.Document.Paragraphs;
        if (_show(id) is { } reloaded && reloaded.Discussion.Count == paragraphs.Count)
        {
            for (var i = 0; i < paragraphs.Count; i++) paragraphs[i].Key = reloaded.Discussion[i].Key;
            opened.Session.Applied();
            return;
        }
        _opened.Remove(id);
        if (_shownId == id) Show(id);
    }

    /// <summary>The heading above the field: the selected record's number, title and abstract.</summary>
    public string HeadingText => _heading.Text ?? string.Empty;

    /// <summary>Selects the record <paramref name="id"/> and shows its Discussion, with any edits made to it already.</summary>
    public void Show(string id)
    {
        if (!_opened.TryGetValue(id, out var opened))
        {
            var shown = _show(id);
            if (shown is null) return;
            var discussion = GdrDiscussion.From(shown);
            // A field edits one document for its whole life, so each record gets its own field and keeps it.
            var field = new RichTextField(discussion.Document)
            {
                WritingSystems = discussion.WritingSystems,
                StyleResolver = discussion.Styles,
            };
            field.ContextMenuRequested += OnContextMenuRequested;
            var session = new GdrDiscussionSession(field, shown.Record.Id);
            session.PendingChanged += (_, edit) =>
            {
                // A field set aside by "Start again" no longer speaks for the record.
                if (!_opened.TryGetValue(id, out var current) || current.Field != field) return;
                if (_store is not null) Stage(shown.Record.Id, edit, field.Editor.Document);
                PendingEditChanged?.Invoke(this, edit);
            };
            opened = new(field, session, $"{shown.Record.Abbreviation}  {shown.Record.Title}\n{shown.Record.Description}", discussion);
            _opened[id] = opened;
        }
        _shownId = id;
        _scroll.Content = opened.Field;
        _heading.Text = opened.Heading;
        _target.Text = string.Empty;
        ShowStaleStrip();
    }

    /// <summary>What the last activated link points to; empty before one is activated.</summary>
    public string TargetText => _target.Text ?? string.Empty;

    /// <summary>Shows what the link at <paramref name="place"/> points to; false, and nothing changes, when it is not on a link.</summary>
    public bool ShowTarget(ContextInfo place)
    {
        ArgumentNullException.ThrowIfNull(place);
        if (ShownDiscussion is not { } discussion || place.Run is null ||
            !discussion.Targets.TryGetValue((place.RunRange.Start.Paragraph, place.RunRange.Start.Offset), out var target))
            return false;
        _target.Text = GdrDiscussion.Describe(place.Run.Text, target);
        return true;
    }

    private GdrDiscussion? ShownDiscussion => _shownId is { } id && _opened.TryGetValue(id, out var opened) ? opened.Discussion : null;

    // The field offers links through its context menu, so a link's menu has the one item that shows its target.
    private void OnContextMenuRequested(object? sender, ContextMenuRequestedEventArgs e)
    {
        if (sender is not Control field) return;
        var place = e.Info;
        var item = new MenuItem { Header = "Show target", IsEnabled = ShownDiscussion?.Targets.ContainsKey((place.RunRange.Start.Paragraph, place.RunRange.Start.Offset)) == true };
        item.Click += (_, _) => ShowTarget(place);
        new ContextMenu { ItemsSource = new[] { item } }.Open(field);
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
            id => GdrQuery.Show(new GdrShowRequest(project, id)) is { Succeeded: true } shown ? shown.Value : null,
            new GdrEditStore(project));
        window.Show();
        return window;
    }

    private sealed record OpenedRecord(RichTextField Field, GdrDiscussionSession Session, string Heading, GdrDiscussion Discussion);
}
