using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SIL.Motif.App.Compare;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Gdr;
using SIL.Motif.Runner.Operations;
using SIL.TextEditing;
using SIL.TextEditing.Avalonia;

namespace SIL.Motif.App.Preview;

/// <summary>
/// A developer-only window that lists a project's Grammar Design Records and lets a person edit one's Discussion, with
/// the project's own styles and writing systems, which its toolbar also offers with bold, italic, underline and links to
/// other records or, by project path, to any project object. It reads through <see cref="GdrQuery"/>; each record's edits add up to
/// one pending <see cref="GdrDiscussionEdit"/>, measured against the text the record was shown with, which an
/// <see cref="IGdrEditStore"/> keeps as one operation in the pending-changes Draft and applies.
/// </summary>
public sealed class GdrPreviewWindow : Window
{
    /// <summary>The start-up argument that opens this window, followed by the project file, when the developer flag is set.</summary>
    public const string StartupArgument = "--gdr-preview";

    private readonly Func<string, GdrShowResponse?> _show;
    private readonly Func<string, BaselineToken?, CommandOutcome<GdrPathLink>>? _linkToPath;
    private readonly string? _projectName;
    private readonly IGdrEditStore? _store;
    private readonly Func<Task<bool>> _confirmStartAgain;
    private readonly Func<CompareWindow, Task<CompareOutcome?>> _compare;
    private readonly Func<string, string>? _writingSystemName;
    private readonly HashSet<string> _staged = new(StringComparer.Ordinal);
    private readonly HashSet<string> _stale = new(StringComparer.Ordinal);
    private readonly Border _staleStrip;
    private readonly Border _unappliedStrip = new()
    {
        Background = Brushes.LightYellow,
        Padding = new Thickness(8, 4),
        IsVisible = false,
        Child = new TextBlock { Text = UnappliedMessage, TextWrapping = TextWrapping.Wrap },
    };
    private readonly Dictionary<string, string> _refused = new(StringComparer.Ordinal);
    // Records whose Draft edit has not been read yet, or could not be, or no longer fits the project.
    private readonly HashSet<string> _locked = new(StringComparer.Ordinal);
    // Records whose Draft edit could not be read, with what they were shown from, read again when next selected.
    private readonly Dictionary<string, GdrShowResponse> _unread = new(StringComparer.Ordinal);
    private bool _applying;
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
    /// <paramref name="projectName"/> is the FieldWorks project a new link names; without it no link can be inserted.
    /// <paramref name="compare"/> shows the window that compares that edit with FieldWorks' text and returns how the
    /// person left it; by default it opens as a dialog. <paramref name="writingSystemName"/> names a writing system by
    /// its tag, as the project does; without it the comparison says the tag.
    /// <paramref name="linkToPath"/> finds the object at a project path for a link, in the Baseline the shown record was read
    /// from; without it the path box finds nothing.
    /// </summary>
    public GdrPreviewWindow(IReadOnlyList<GdrSummary> records, Func<string, GdrShowResponse?> show, IGdrEditStore? store = null,
        Func<Task<bool>>? confirmStartAgain = null, string? projectName = null, Func<CompareWindow, Task<CompareOutcome?>>? compare = null,
        Func<string, string>? writingSystemName = null, Func<string, BaselineToken?, CommandOutcome<GdrPathLink>>? linkToPath = null)
    {
        _writingSystemName = writingSystemName;
        _sourceToggle = PreviewSourceMode.Toggle(ToggleSourceMode);
        _show = show;
        _linkToPath = linkToPath;
        _projectName = projectName;
        _store = store;
        _confirmStartAgain = confirmStartAgain ?? ConfirmStartAgainAsync;
        _compare = compare ?? (window => window.ShowDialog<CompareOutcome?>(this));
        var compareButton = new Button { Content = ChangeWords.Compare };
        compareButton.Click += async (_, _) => await CompareAsync();
        var strip = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12 };
        strip.Children.Add(new TextBlock
        {
            Text = StaleMessage,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });
        strip.Children.Add(compareButton);
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
        DockPanel.SetDock(_toolbar, Dock.Top);
        inField.Children.Add(BuildToolbar());
        DockPanel.SetDock(_staleStrip, Dock.Top);
        inField.Children.Add(_staleStrip);
        DockPanel.SetDock(_unappliedStrip, Dock.Top);
        inField.Children.Add(_unappliedStrip);
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
        PreviewSourceMode.Speak(_status);
        var actions = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12 };
        if (store is not null)
        {
            var apply = new Button { Content = "Apply", Margin = new Thickness(0, 6, 0, 0) };
            apply.Click += async (_, _) => await ApplyAsync();
            actions.Children.Add(apply);
        }
        actions.Children.Add(_status);
        DockPanel.SetDock(actions, Dock.Bottom);
        right.Children.Add(actions);
        right.Children.Add(reading);
        var panel = new DockPanel { Margin = new Thickness(12), LastChildFill = true };
        DockPanel.SetDock(_records, Dock.Left);
        panel.Children.Add(_records);
        panel.Children.Add(right);
        Content = panel;
    }

    /// <summary>The records listed.</summary>
    public IReadOnlyList<GdrSummary> Records { get; }

    /// <summary>What the character-style list offers to take a run's character style away.</summary>
    public const string NoCharacterStyle = "(no character style)";

    private readonly WrapPanel _toolbar = new() { Margin = new Thickness(4) };
    private readonly ComboBox _paragraphStyles = Choice("Paragraph style");
    private readonly ComboBox _characterStyles = Choice("Character style");
    private readonly ComboBox _writingSystems = Choice("Writing system");
    private readonly ComboBox _links = Choice("Link to a record");
    private readonly TextBox _linkPath = new() { PlaceholderText = "Link to a project path, such as /lexicon/entry", MinWidth = 260 };
    private readonly TextBlock _linkHint = new()
    {
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        Margin = new Thickness(6, 0, 0, 0),
        TextWrapping = TextWrapping.Wrap,
    };
    private CancellationTokenSource? _hintCancel;
    private bool _resettingChoices;
    private readonly Button _sourceToggle;

    /// <summary>The paragraph styles the toolbar offers: the project's.</summary>
    public IReadOnlyList<string> ParagraphStyleChoices => Items(_paragraphStyles);

    /// <summary>The character styles the toolbar offers: the project's, after <see cref="NoCharacterStyle"/>.</summary>
    public IReadOnlyList<string> CharacterStyleChoices => Items(_characterStyles);

    /// <summary>The writing systems the toolbar offers: the project's, by tag.</summary>
    public IReadOnlyList<string> WritingSystemChoices => Items(_writingSystems);

    /// <summary>The records a link can point to, as the toolbar lists them: every record but the one shown.</summary>
    public IReadOnlyList<string> LinkChoices => Items(_links);

    /// <summary>The time typing in the path box must pause before the path is looked up.</summary>
    public static readonly TimeSpan LinkPathPause = TimeSpan.FromMilliseconds(250);

    /// <summary>The path box's text; setting it looks the path up as typing does.</summary>
    public string LinkPath
    {
        get => _linkPath.Text ?? "";
        set => _linkPath.Text = value;
    }

    /// <summary>What the typed path names, after <c>Resolved:</c>, or why it names nothing, after <c>Broken:</c>.</summary>
    public string LinkPathHint => _linkHint.Text ?? "";

    /// <summary>The look-up of the latest typed path, complete once <see cref="LinkPathHint"/> speaks for it.</summary>
    public Task LinkPathResolving { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Puts a link to the object at the project path <paramref name="path"/> at the caret, named by the object's name in
    /// the writing system typing is in; false, with the reason in <see cref="LinkPathHint"/>, when the path finds none.
    /// </summary>
    public async Task<bool> InsertLinkToPathAsync(string path)
    {
        if (_linkToPath is not { } find || _shownId is not { } shown) return false;
        var baseline = _opened.TryGetValue(shown, out var reading) ? reading.Baseline : null;
        var found = await Task.Run(() => find(path, baseline));
        _linkHint.Text = Hint(found);
        if (found.Value is not { } link || _shownId != shown || !_opened.TryGetValue(shown, out var opened)) return false;
        return opened.Session.InsertLink(opened.Discussion.LinkRun(link.Name, opened.Field.Editor.ActiveWs, link.ObjData));
    }

    private async Task ShowLinkPathHintAsync(string path)
    {
        _hintCancel?.Cancel();
        var cancel = _hintCancel = new CancellationTokenSource();
        _linkHint.Text = "";
        if (_linkToPath is not { } find || string.IsNullOrWhiteSpace(path)) return;
        try
        {
            await Task.Delay(LinkPathPause, cancel.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        var baseline = _shownId is { } shown && _opened.TryGetValue(shown, out var reading) ? reading.Baseline : null;
        var found = await Task.Run(() => find(path, baseline));
        if (!cancel.IsCancellationRequested) _linkHint.Text = Hint(found);
    }

    private static string Hint(CommandOutcome<GdrPathLink> found) => found.Value is { } link
        ? $"Resolved: {link.Kind} {link.Name}" + (link.IsStale ? " (the project has been saved since this Baseline)" : "")
        : $"Broken: {found.Refusal?.Message}";

    /// <summary>Gives the shown Discussion's selected paragraphs the paragraph style <paramref name="style"/>.</summary>
    public bool ApplyParagraphStyle(string style) => Session?.ApplyParagraphStyle(style) == true;

    /// <summary>Gives the selected text the character style <paramref name="style"/>, or takes it away when null.</summary>
    public bool ApplyCharacterStyle(string? style) => Session?.ApplyCharacterStyle(style) == true;

    /// <summary>
    /// Shows the selected record's Discussion as FieldWorks Markdown, or returns it to rich text, as the bar's Markdown
    /// button and Ctrl+Shift+M in the field do; returns whether it switched. Going back is refused while the Markdown has
    /// an error.
    /// </summary>
    public bool ToggleSourceMode() => Session?.ToggleSourceMode() == true;

    /// <summary>What Compare and Apply say when a field cannot go back to rich text first.</summary>
    public const string MarkdownFixFirstMessage = PreviewSourceMode.FixFirstMessage;

    /// <summary>Turns bold on or off for the selected text.</summary>
    public bool ToggleBold() => Session?.ToggleBold() == true;

    /// <summary>Turns italic on or off for the selected text.</summary>
    public bool ToggleItalic() => Session?.ToggleItalic() == true;

    /// <summary>Turns underline on or off for the selected text.</summary>
    public bool ToggleUnderline() => Session?.ToggleUnderline() == true;

    /// <summary>Puts the selected text in writing system <paramref name="tag"/>, or with no selection, types on in it.</summary>
    public bool SetWritingSystem(string tag) => Session?.SetWritingSystem(tag) == true;

    /// <summary>
    /// Puts a link to the record <paramref name="recordId"/> at the caret, named by the record's number or, without one,
    /// its title or else its id, in the writing system typing is in.
    /// </summary>
    public bool InsertLink(string recordId)
    {
        if (_projectName is not { } project || _shownId is not { } shown || Records.FirstOrDefault(record => record.Id == recordId) is not { } target ||
            !SIL.Motif.Contract.Ids.CanonicalId.TryParse(target.Id, out _)) return false;
        var opened = _opened[shown];
        var name = new[] { target.Abbreviation, target.Title, target.Id }.First(text => !string.IsNullOrWhiteSpace(text));
        return opened.Session.InsertLink(opened.Discussion.LinkRun(name, opened.Field.Editor.ActiveWs, GdrReader.LinkData(project, target.Id)));
    }

    private static ComboBox Choice(string label) => new()
    {
        PlaceholderText = label,
        MinWidth = 140,
        Margin = new Thickness(0, 0, 6, 0),
        // The field keeps focus, so choosing a format does not end the editing session.
        Focusable = false,
    };

    private static IReadOnlyList<string> Items(ComboBox box) => (box.ItemsSource as IReadOnlyList<string>) ?? [];

    private Control BuildToolbar()
    {
        Button Toggle(string text, string name, FontWeight weight, FontStyle style, TextDecorationCollection? decorations, Func<bool> action)
        {
            var button = new Button
            {
                Content = new TextBlock { Text = text, FontWeight = weight, FontStyle = style, TextDecorations = decorations },
                Focusable = false,
                Margin = new Thickness(0, 0, 6, 0),
            };
            Avalonia.Automation.AutomationProperties.SetName(button, name);
            button.Click += (_, _) => action();
            return button;
        }

        _toolbar.Children.Add(_sourceToggle);
        _toolbar.Children.Add(_paragraphStyles);
        _toolbar.Children.Add(_characterStyles);
        _toolbar.Children.Add(Toggle("B", "Bold", FontWeight.Bold, FontStyle.Normal, null, ToggleBold));
        _toolbar.Children.Add(Toggle("I", "Italic", FontWeight.Normal, FontStyle.Italic, null, ToggleItalic));
        _toolbar.Children.Add(Toggle("U", "Underline", FontWeight.Normal, FontStyle.Normal, TextDecorations.Underline, ToggleUnderline));
        _toolbar.Children.Add(_writingSystems);
        _toolbar.Children.Add(_links);
        var insertPathLink = new Button { Content = "Insert link", Focusable = false };
        insertPathLink.Click += async (_, _) => await InsertLinkToPathAsync(LinkPath);
        // TextChanged is raised later; the property change is not, so the look-up starts with the text.
        _linkPath.PropertyChanged += (_, change) =>
        {
            if (change.Property == TextBox.TextProperty) LinkPathResolving = ShowLinkPathHintAsync(LinkPath);
        };
        var byPath = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 4, 0, 0) };
        byPath.Children.Add(_linkPath);
        byPath.Children.Add(insertPathLink);
        byPath.Children.Add(_linkHint);
        _toolbar.Children.Add(byPath);
        Chosen(_paragraphStyles, (item, _) => ApplyParagraphStyle(item));
        Chosen(_characterStyles, (item, _) => ApplyCharacterStyle(item == NoCharacterStyle ? null : item));
        Chosen(_writingSystems, (item, _) => SetWritingSystem(item));
        Chosen(_links, (_, at) =>
        {
            var targets = Records.Where(record => record.Id != _shownId).ToArray();
            if (at < targets.Length) InsertLink(targets[at].Id);
        });
        return _toolbar;
    }

    // A list acts once per choice and then shows its label again, so the same choice can be made twice.
    private void Chosen(ComboBox box, Action<string, int> act) => box.SelectionChanged += (_, _) =>
    {
        if (_resettingChoices || box.SelectedItem is not string item || box.SelectedIndex < 0) return;
        act(item, box.SelectedIndex);
        _resettingChoices = true;
        box.SelectedIndex = -1;
        _resettingChoices = false;
    };

    // In Markdown the formatting is typed, so only the toggle back is offered.
    private void ShowToolbar()
    {
        var markdown = Field?.IsSourceMode == true;
        foreach (var control in _toolbar.Children) control.IsEnabled = control == _sourceToggle || !markdown;
    }

    private void ShowChoices(GdrDiscussion discussion)
    {
        _paragraphStyles.ItemsSource = discussion.ParagraphStyles.ToArray();
        _characterStyles.ItemsSource = new[] { NoCharacterStyle }.Concat(discussion.CharacterStyles).ToArray();
        _writingSystems.ItemsSource = discussion.WritingSystemChoices.Select(system => system.Tag).ToArray();
        _links.ItemsSource = Records.Where(record => record.Id != _shownId)
            .Select(record => $"{record.Abbreviation}  {record.Title}".Trim()).ToArray();
    }


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

    /// <summary>What Compare says, before the refusal, when the edit of the Discussion could not be added to the pending changes.</summary>
    public const string CouldNotStageMessage = "There is nothing to compare yet: your edit could not be added to the pending changes. ";

    /// <summary>What Compare says when the pending changes no longer hold an edit of the shown Discussion.</summary>
    public const string NothingToCompareMessage = "There is nothing to compare: your pending changes no longer hold an edit of this Discussion.";

    /// <summary>Whether the strip saying the shown record was also changed in FieldWorks is showing.</summary>
    public bool IsStaleStripShown => _staleStrip.IsVisible;

    /// <summary>What the strip in the field says when the shown record holds an edit the Draft kept from an earlier window.</summary>
    public const string UnappliedMessage = "This Discussion shows your edit that is not applied yet.";

    /// <summary>Whether the strip saying the shown record shows an edit kept in the Draft is showing.</summary>
    public bool IsUnappliedStripShown => _unappliedStrip.IsVisible;

    /// <summary>The newest read of a record's edit from the Draft, which reaches the field when it completes.</summary>
    public Task Loading { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Ends the shown record's session, waits for every edit to reach the Draft, and applies the pending changes, with
    /// every field read-only meanwhile. Once they are written, each staged record's paragraphs take the keys the project
    /// gave them and its session starts again from the applied text; returns whether Apply wrote them. Apply waits while
    /// a record's latest edit could not be staged, since the Draft still holds an older one.
    /// </summary>
    public async Task<bool> ApplyAsync()
    {
        if (_store is null) return false;
        if (!PreviewSourceMode.ReturnToRichText(_opened.Values.Select(opened => opened.Field), _status)) return false;
        Session?.EndSession();
        // A refused staging is tried again, so a busy Draft only holds Apply back while it stays busy.
        foreach (var id in _refused.Keys.ToArray())
            if (_opened.TryGetValue(id, out var refused)) _ = Stage(refused, refused.Session.Pending);
        await _staging;
        if (_refused.Count > 0)
        {
            _status.Text = "Nothing was applied: an edit could not be added to the pending changes. " + _refused.Values.First();
            return false;
        }
        _applying = true;
        SetReadOnly();
        GdrApplyRefusal? refusal;
        try
        {
            refusal = await _store.ApplyAsync();
        }
        finally
        {
            _applying = false;
            SetReadOnly();
        }
        if (refusal is not null)
        {
            if (refusal is { StaleRecordId: { } stale, StaleField: GdrDiscussionPayload.Field })
            {
                _stale.Add(stale);
                _status.Text = "Nothing was applied.";
                ShowStrips();
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
        ShowStrips();
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
        _opened[id].Field.DiscardSourceChanges();
        _opened[id].Session.EndSession();
        if (await Staged(id, pending: false, () => _store.DiscardAsync(id)) is not null) return false;
        _stale.Remove(id);
        _opened.Remove(id);
        _unread.Remove(id);
        Show(id);
        return true;
    }

    /// <summary>
    /// Compares the shown record's edit, which the record's text in FieldWorks has overtaken, with that text, and lets
    /// the person choose change by change. Done stages the result measured against FieldWorks' text, so it fits again;
    /// Decide later leaves the edit as it was, and Apply keeps waiting. Returns whether the edit fits again.
    /// </summary>
    public async Task<bool> CompareAsync()
    {
        if (_store is null || _shownId is not { } id || !_stale.Contains(id)) return false;
        if (!PreviewSourceMode.ReturnToRichText([_opened[id].Field], _status)) return false;
        _opened[id].Session.EndSession();
        await _staging;
        if (_refused.TryGetValue(id, out var unstaged))
        {
            _status.Text = CouldNotStageMessage + unstaged;
            return false;
        }
        var read = await _store.PendingAsync(id);
        if (read.Refusal is { } refusal)
        {
            _status.Text = refusal;
            return false;
        }
        if (read.Paragraphs is not { } pending || _show(id) is not { } live)
        {
            _status.Text = NothingToCompareMessage;
            return false;
        }
        var discussion = GdrDiscussion.From(live);
        var comparison = new TextComparison(TextSort.Sort(TextVersions.FromParagraphs(pending, live.Discussion,
            _opened[id].Discussion.Source.Select(TextVersions.Paragraph))));
        var window = new CompareWindow($"{live.Record.Abbreviation}  {live.Record.Title}, Discussion", comparison,
            discussion.WritingSystems, discussion.Styles, writingSystemName: _writingSystemName);
        var outcome = await _compare(window);
        if (outcome?.End == CompareEnd.StartAgain) return await StartAgainFromFieldWorksAsync();
        if (outcome?.End != CompareEnd.Done) return false;
        var payload = ComparedPayloads.Paragraphs(GdrDiscussionPayload.Field, outcome.Result, live.Discussion);
        var write = payload is null ? (Func<Task<string?>>)(() => _store.DiscardAsync(id)) : () => _store.StageAsync(id, payload);
        if (await Staged(id, pending: payload is not null, write) is not null) return false;
        _stale.Remove(id);
        _opened.Remove(id);
        Show(id);
        _status.Text = payload is null ? "Your edit now matches FieldWorks; nothing is left to apply." : "Your edit now fits. Apply when ready.";
        return true;
    }

    private Task<string?> Stage(OpenedRecord opened, GdrDiscussionEdit? edit) => edit is null
        ? Staged(opened.RecordId, pending: false, () => _store!.DiscardAsync(opened.RecordId))
        : Staged(opened.RecordId, pending: true,
            () => _store!.StageAsync(opened.RecordId, GdrDiscussionPayload.From(edit, opened.Discussion, opened.Began)));

    // One staging at a time, in the order the sessions ended, so the Draft ends up with each record's latest edit.
    private Task<string?> Staged(string recordId, bool pending, Func<Task<string?>> write)
    {
        var staged = StageAfter(_staging, recordId, pending, write);
        _staging = staged;
        return staged;
    }

    private async Task<string?> StageAfter(Task earlier, string recordId, bool pending, Func<Task<string?>> write)
    {
        await earlier;
        var refusal = await write();
        if (refusal is null)
        {
            _refused.Remove(recordId);
            if (pending) _staged.Add(recordId);
            else _staged.Remove(recordId);
        }
        else
        {
            _refused[recordId] = refusal;
        }
        _status.Text = refusal ?? string.Empty;
        return refusal;
    }

    // Typing is allowed only when nothing applies and the record's Draft edit was read and fits the project.
    private void SetReadOnly()
    {
        foreach (var opened in _opened.Values) opened.Field.IsReadOnly = _applying || _locked.Contains(opened.RecordId);
    }

    private void ShowStrips()
    {
        _staleStrip.IsVisible = _shownId is { } id && _stale.Contains(id);
        _unappliedStrip.IsVisible = !_staleStrip.IsVisible && _shownId is { } shown &&
            _opened.TryGetValue(shown, out var opened) && opened.Began is not null && opened.Session.Pending is not null;
    }

    private async Task<bool> ConfirmStartAgainAsync()
    {
        var dialog = new Window
        {
            Title = "Start again from the FieldWorks text",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var keep = new Button { Content = "Go back", IsDefault = true, IsCancel = true };
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

    // After Apply, new paragraphs take the project's keys and the applied text is the next edit's start.
    private void Rekey(string id)
    {
        if (!_opened.TryGetValue(id, out var opened)) return;
        var paragraphs = opened.Field.Editor.Document.Paragraphs;
        if (_show(id) is { } reloaded && reloaded.Discussion.Count == paragraphs.Count)
        {
            for (var i = 0; i < paragraphs.Count; i++) paragraphs[i].Key = reloaded.Discussion[i].Key;
            opened.Session.Applied();
            var discussion = GdrDiscussion.From(reloaded);
            opened.Field.SourceForm = new DiscussionSourceForm(discussion, _projectName);
            _opened[id] = opened with { Discussion = discussion, Began = null };
            return;
        }
        _opened.Remove(id);
        _unread.Remove(id);
        if (_shownId == id) Show(id);
    }

    /// <summary>The heading above the field: the selected record's number, title and abstract.</summary>
    public string HeadingText => _heading.Text ?? string.Empty;

    /// <summary>
    /// Selects the record <paramref name="id"/> and shows its Discussion, with any edits made to it already. A record
    /// opened for the first time shows the edit the pending-changes Draft holds for it, once <see cref="Loading"/>
    /// has read it.
    /// </summary>
    public void Show(string id)
    {
        GdrShowResponse? first = null;
        if (!_opened.TryGetValue(id, out var opened))
        {
            first = _show(id);
            if (first is null) return;
            opened = Open(first, null);
            _opened[id] = opened;
        }
        _shownId = id;
        _scroll.Content = opened.Field;
        ShowChoices(opened.Discussion);
        ShowToolbar();
        _heading.Text = opened.Heading;
        _target.Text = string.Empty;
        ShowStrips();
        if (_store is null) return;
        if (first is not null) Loading = LoadPending(first, opened);
        else if (_unread.Remove(id, out var unread)) Loading = LoadPending(unread, opened);
    }

    private OpenedRecord Open(GdrShowResponse shown, TextParagraphsPayload? pending)
    {
        var id = shown.Record.Id;
        var discussion = GdrDiscussion.From(shown);
        var starting = pending is null ? null : discussion.Document.Paragraphs.Select(paragraph => paragraph.Clone()).ToList();
        if (pending is not null) ShowEdit(discussion.Document, pending);
        // A field edits one document for its whole life, so each record gets its own field and keeps it.
        var field = new RichTextField(discussion.Document)
        {
            WritingSystems = discussion.WritingSystems,
            StyleResolver = discussion.Styles,
        };
        field.SourceForm = new DiscussionSourceForm(discussion, _projectName);
        field.ContextMenuRequested += OnContextMenuRequested;
        field.SourceModeChanged += (_, change) =>
        {
            _status.Text = change.Announcement;
            if (_opened.TryGetValue(id, out var current) && current.Field == field) ShowToolbar();
        };
        // Until the Draft has been read, typing could start an edit that would replace the one it holds.
        if (_store is not null && pending is null) _locked.Add(id);
        field.IsReadOnly = _applying || _locked.Contains(id);
        var session = new GdrDiscussionSession(field, id, starting: starting);
        var opened = new OpenedRecord(id, field, session, $"{shown.Record.Abbreviation}  {shown.Record.Title}\n{shown.Record.Description}",
            discussion, pending?.Expected, shown.Baseline);
        session.PendingChanged += (_, edit) =>
        {
            // A field set aside by "Start again" or by loading the Draft's edit no longer speaks for the record.
            if (!_opened.TryGetValue(id, out var current) || current.Field != field) return;
            if (_store is not null) Stage(current, edit);
            PendingEditChanged?.Invoke(this, edit);
            ShowStrips();
        };
        return opened;
    }

    // A record the Draft holds an edit of shows that edit, so a later session updates it.
    private async Task LoadPending(GdrShowResponse shown, OpenedRecord opened)
    {
        var read = await _store!.PendingAsync(opened.RecordId);
        if (!_opened.TryGetValue(opened.RecordId, out var current) || current != opened) return;
        if (read.Refusal is { } refusal)
        {
            _unread[opened.RecordId] = shown;
            _status.Text = "This Discussion stays read-only: its pending edit could not be read. Select it again to retry. " + refusal;
            return;
        }
        if (read.Paragraphs is not { } pending)
        {
            _locked.Remove(opened.RecordId);
            SetReadOnly();
            return;
        }
        if (!Fits(pending, shown, opened.Discussion))
        {
            // Shown over this project, the edit would drop what FieldWorks changed, so it waits for Compare.
            _stale.Add(opened.RecordId);
            _staged.Add(opened.RecordId);
            ShowStrips();
            return;
        }
        _locked.Remove(opened.RecordId);
        var reopened = Open(shown, pending);
        _opened[opened.RecordId] = reopened;
        _staged.Add(opened.RecordId);
        if (_shownId == opened.RecordId) _scroll.Content = reopened.Field;
        ShowStrips();
    }

    // Apply's own check: the same paragraphs, kept ones in order, each as the edit began.
    private static bool Fits(TextParagraphsPayload pending, GdrShowResponse shown, GdrDiscussion discussion)
    {
        var live = shown.Discussion.Select(paragraph => paragraph.Key).ToArray();
        var kept = pending.Order.OfType<string>().ToArray();
        var began = kept.Concat(pending.Removed).ToHashSet(StringComparer.Ordinal);
        return live.Length == began.Count && live.All(key => key is not null && began.Contains(key)) &&
            live.Where(key => kept.Contains(key)).SequenceEqual(kept) &&
            pending.Expected.All(expected => discussion.Expected.TryGetValue(expected.Key, out var now) && now == expected.Value);
    }

    private static void ShowEdit(SIL.TextEditing.Document document, TextParagraphsPayload pending)
    {
        var byKey = document.Paragraphs.Where(paragraph => paragraph.Key is string)
            .ToDictionary(paragraph => (string)paragraph.Key!, StringComparer.Ordinal);
        var changes = pending.Changed.ToDictionary(change => change.Index);
        var edited = pending.Order.Select((key, index) =>
            changes.TryGetValue(index, out var change) ? Paragraph(change) : byKey[key!]).ToList();
        document.Paragraphs.Clear();
        foreach (var paragraph in edited) document.Paragraphs.Add(paragraph);
    }

    private static Paragraph Paragraph(TextParagraphChange change)
    {
        var paragraph = new Paragraph { Key = change.Key, Style = change.Style ?? string.Empty, Props = Bag(change.Props) };
        paragraph.Rtl = paragraph.Props.TryGetInt(128, out var rtl) && rtl != 0;
        if (change.Style is { } style) paragraph.Props.SetStr(SIL.TextEditing.TextPropertyNumbers.NamedStyle, style);
        foreach (var run in change.Runs)
        {
            var props = Bag(run.Props);
            if (run.CharacterStyle is { } named) props.SetStr(SIL.TextEditing.TextPropertyNumbers.NamedStyle, named);
            paragraph.Runs.Add(new Run { Text = run.Text, Ws = run.Ws, Props = props, ObjData = run.ObjData });
        }
        if (change.Runs.Count > 0) paragraph.DefaultWs = change.Runs[0].Ws;
        return paragraph;
    }

    private static PropertyBag Bag(TextProps? props)
    {
        var bag = new PropertyBag();
        if (props is null) return bag;
        foreach (var (number, (variation, value)) in props.Ints) bag.SetInt(number, value, variation);
        foreach (var (number, value) in props.Strings) bag.SetStr(number, value);
        return bag;
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
        if (e.Field is { } view && view != sender)
        {
            new ContextMenu { ItemsSource = PreviewSourceMode.Menu(view, e.Info) }.Open(field);
            return;
        }
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
            new GdrEditStore(project), projectName: Path.GetFileNameWithoutExtension(project),
            writingSystemName: WritingSystemNames.For(project),
            linkToPath: (path, baseline) => GdrQuery.LinkToPath(new GdrLinkToPathRequest(project, path, baseline)));
        window.Show();
        return window;
    }


    private sealed record OpenedRecord(string RecordId, RichTextField Field, GdrDiscussionSession Session, string Heading,
        GdrDiscussion Discussion, IReadOnlyDictionary<string, TextParagraphExpected>? Began, BaselineToken? Baseline);
}
