using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using SIL.Motif.App.Compare;
using SIL.Motif.App.Preview;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Gdr;
using SIL.Motif.Runner.Operations;
using SIL.TextEditing;
using SIL.TextEditing.Avalonia;

namespace SIL.Motif.App.Views.Guide;

/// <summary>
/// The Grammar Guide's embedded editor for one section's Discussion, with the project's own styles and writing systems,
/// which its tool bar offers beside bold, italic, underline and links to other sections or, by project path, to any
/// project object. Each section's edits add up to one pending <see cref="GdrDiscussionEdit"/>, measured against the text the
/// section was shown with, which an <see cref="IGdrEditStore"/> keeps as one operation in the pending-changes Draft; the
/// view itself never writes to the project. A section whose text also changed in FieldWorks keeps what the person typed
/// and offers Compare.
/// </summary>
public sealed partial class GuideSectionEditor : UserControl
{
    private readonly Func<string, Task<GdrShowResponse?>> _show;
    private readonly Func<string, BaselineToken?, Task<CommandOutcome<GdrPathLink>>>? _linkToPath;
    private readonly string? _projectName;
    private readonly IGdrEditStore? _store;
    private readonly Func<Task<bool>> _confirmStartAgain;
    private readonly Func<CompareWindow, Task<CompareOutcome?>>? _compare;
    private readonly Func<string, string>? _writingSystemName;
    private readonly HashSet<string> _staged = new(StringComparer.Ordinal);
    private readonly HashSet<string> _stale = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _refused = new(StringComparer.Ordinal);
    // Sections whose Draft edit has not been read yet, or could not be, or no longer fits the project.
    private readonly HashSet<string> _locked = new(StringComparer.Ordinal);
    // Sections whose Draft edit could not be read, with what they were shown from, read again when next selected.
    private readonly Dictionary<string, GdrShowResponse> _unread = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OpenedRecord> _opened = new(StringComparer.Ordinal);
    private readonly Border _staleStrip;
    private readonly Border _unappliedStrip;
    private readonly TextBlock _status;
    private readonly TextBlock _target;
    private readonly ContentControl _fieldHost;
    private readonly WrapPanel _toolbar;
    private readonly List<(ToggleButton Button, int Property)> _formatToggles = [];
    private Task _staging = Task.CompletedTask;
    private bool _applying;
    private bool _isEditing = true;
    private int _showGeneration;
    private string? _shownId;
    private Window? _closingWindow;

    /// <summary>
    /// Creates the editor over <paramref name="records"/>; <paramref name="show"/> reads one section by its id, and
    /// <paramref name="store"/>, when given, stages each edit and offers Apply. <paramref name="confirmStartAgain"/>
    /// asks the person before their edit of a section changed in FieldWorks is thrown away; by default a dialog asks.
    /// <paramref name="projectName"/> is the FieldWorks project a new link names; without it no link can be inserted.
    /// <paramref name="compare"/> shows the window that compares that edit with FieldWorks' text and returns how the
    /// person left it; by default it opens as a dialog. <paramref name="writingSystemName"/> names a writing system by
    /// its tag, as the project does; without it the comparison says the tag.
    /// <paramref name="linkToPath"/> finds the object at a project path for a link, in the Baseline the shown section was read
    /// from; without it the path box finds nothing.
    /// </summary>
    public GuideSectionEditor(IReadOnlyList<GdrSummary> records, Func<string, Task<GdrShowResponse?>> show,
        IGdrEditStore? store = null, Func<Task<bool>>? confirmStartAgain = null, string? projectName = null,
        Func<CompareWindow, Task<CompareOutcome?>>? compare = null, Func<string, string>? writingSystemName = null,
        Func<string, BaselineToken?, Task<CommandOutcome<GdrPathLink>>>? linkToPath = null)
    {
        AvaloniaXamlLoader.Load(this);
        _writingSystemName = writingSystemName;
        _show = show;
        _linkToPath = linkToPath;
        _projectName = projectName;
        _store = store;
        _confirmStartAgain = confirmStartAgain ?? ConfirmStartAgainAsync;
        _compare = compare;
        Records = records;
        _staleStrip = this.FindControl<Border>("StaleStrip")!;
        _unappliedStrip = this.FindControl<Border>("UnappliedStrip")!;
        _status = this.FindControl<TextBlock>("Status")!;
        _target = this.FindControl<TextBlock>("Target")!;
        _fieldHost = this.FindControl<ContentControl>("FieldHost")!;
        _toolbar = this.FindControl<WrapPanel>("Toolbar")!;
        _sourceToggle = SourceToggle();
        this.FindControl<Button>("CompareButton")!.Click += async (_, _) => await CompareAsync();
        var apply = this.FindControl<Button>("ApplyButton")!;
        apply.IsVisible = store is not null;
        apply.Click += async (_, _) => await ApplyAsync();
        this.FindControl<Button>("UndoMarkdownButton")!.Click += async (_, _) => await UndoMarkdownAsync();
        BuildToolbar();
        AddHandler(KeyDownEvent, OnUndoMarkdownKeyDown, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) => WatchClosing();
        DetachedFromVisualTree += (_, _) => StopWatchingClosing();
    }

    [TextEditingKeyHandler]
    private async void OnUndoMarkdownKeyDown(object? sender, KeyEventArgs e)
    {
        if (_markdownDocument is null && _markdownUndo is not null && Session?.UndoSteps == 0 &&
            e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            await UndoMarkdownAsync();
        }
    }

    /// <summary>The sections listed, which a link can point to.</summary>
    public IReadOnlyList<GdrSummary> Records { get; private set; }

    /// <summary>
    /// Raised when the shown section stays shown because it holds Markdown or text that cannot be left yet; the argument is
    /// the id of the section that is still shown, so the list can select it again.
    /// </summary>
    public event EventHandler<string?>? SelectionRefused;

    /// <summary>What the character-style list offers to take a run's character style away.</summary>
    public static string NoCharacterStyle => MotifAppStrings.GuideNoCharacterStyle;

    private readonly ComboBox _paragraphStyles = Choice(MotifAppStrings.GuideParagraphStyle);
    private readonly ComboBox _characterStyles = Choice(MotifAppStrings.GuideCharacterStyle);
    private readonly ComboBox _writingSystems = Choice(MotifAppStrings.GuideWritingSystem);
    private readonly ComboBox _links = Choice(MotifAppStrings.GuideLinkToSection);
    private readonly TextBox _linkPath = new() { PlaceholderText = MotifAppStrings.GuideLinkPathPlaceholder, Classes = { "guidePath" } };
    private readonly TextBlock _linkHint = new()
    {
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        Classes = { "guideHint" },
        TextWrapping = TextWrapping.Wrap,
    };
    private CancellationTokenSource? _hintCancel;
    private bool _resettingChoices;
    private readonly Button _sourceToggle;
    // The last thing source mode said on the status line.
    private string? _announced;

    /// <summary>The paragraph styles the toolbar offers: the project's.</summary>
    public IReadOnlyList<string> ParagraphStyleChoices => Items(_paragraphStyles);

    /// <summary>The character styles the toolbar offers: the project's, after <see cref="NoCharacterStyle"/>.</summary>
    public IReadOnlyList<string> CharacterStyleChoices => Items(_characterStyles);

    /// <summary>The writing systems the toolbar offers: the project's, by tag.</summary>
    public IReadOnlyList<string> WritingSystemChoices => Items(_writingSystems);

    /// <summary>The sections a link can point to, as the toolbar lists them: every section but the one shown.</summary>
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
        var found = await find(path, baseline);
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
        var found = await find(path, baseline);
        if (!cancel.IsCancellationRequested) _linkHint.Text = Hint(found);
    }

    private static string Hint(CommandOutcome<GdrPathLink> found) => found.Value is { } link
        ? string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.GuideLinkResolvedFormat, link.Kind, link.Name) +
          (link.IsStale ? MotifAppStrings.GuideLinkStaleSuffix : "")
        : string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.GuideLinkBrokenFormat, found.Refusal?.Message);

    /// <summary>Gives the shown Discussion's selected paragraphs the paragraph style <paramref name="style"/>.</summary>
    public bool ApplyParagraphStyle(string style) => Session?.ApplyParagraphStyle(style) == true;

    /// <summary>Gives the selected text the character style <paramref name="style"/>, or takes it away when null.</summary>
    public bool ApplyCharacterStyle(string? style) => Session?.ApplyCharacterStyle(style) == true;

    /// <summary>
    /// Shows the selected section's Discussion as FieldWorks Markdown, or returns it to rich text, as the bar's Markdown
    /// button and Ctrl+Shift+M in the field do; returns whether it switched. Going back is refused while the Markdown has
    /// an error.
    /// </summary>
    public bool ToggleSourceMode() => Session?.ToggleSourceMode() == true;

    /// <summary>What Compare and Apply say when a field cannot go back to rich text first.</summary>
    public const string MarkdownFixFirstMessage = PreviewSourceMode.FixFirstMessage;

    /// <summary>What Compare and Apply say when a field holding Markdown was made read-only and cannot take it back.</summary>
    public const string MarkdownReadOnlyMessage = PreviewSourceMode.ReadOnlyMessage;

    /// <summary>Turns bold on or off for the selected text.</summary>
    public bool ToggleBold() => Session?.ToggleBold() == true;

    /// <summary>Turns italic on or off for the selected text.</summary>
    public bool ToggleItalic() => Session?.ToggleItalic() == true;

    /// <summary>Turns underline on or off for the selected text.</summary>
    public bool ToggleUnderline() => Session?.ToggleUnderline() == true;

    /// <summary>Puts the selected text in writing system <paramref name="tag"/>, or with no selection, types on in it.</summary>
    public bool SetWritingSystem(string tag) => Session?.SetWritingSystem(tag) == true;

    /// <summary>
    /// Puts a link to the section <paramref name="recordId"/> at the caret, named by the section's number or, without one,
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
        Classes = { "guideChoice" },
        // The field keeps focus, so choosing a format does not end the editing session.
        Focusable = false,
    };

    private static IReadOnlyList<string> Items(ComboBox box) => (box.ItemsSource as IReadOnlyList<string>) ?? [];

    private static Button SourceToggle()
    {
        var button = new Button { Content = MotifAppStrings.GuideMarkdownToggle, Focusable = false, Classes = { "guideTool" } };
        Avalonia.Automation.AutomationProperties.SetName(button, MotifAppStrings.GuideMarkdownToggleName);
        return button;
    }

    private void BuildToolbar()
    {
        ToggleButton Toggle(string text, string name, FontWeight weight, FontStyle style, TextDecorationCollection? decorations, int property, Func<bool> action)
        {
            var button = new ToggleButton
            {
                Content = new TextBlock { Text = text, FontWeight = weight, FontStyle = style, TextDecorations = decorations },
                Focusable = false,
                Classes = { "guideTool" },
            };
            Avalonia.Automation.AutomationProperties.SetName(button, name);
            button.Click += (_, _) => { action(); UpdateFormatButtons(); };
            _formatToggles.Add((button, property));
            return button;
        }

        _sourceToggle.Click += (_, _) => ToggleSourceMode();
        PreviewSourceMode.Speak(_status);
        var wholeMarkdown = new Button { Content = MotifAppStrings.GuideOpenMarkdown };
        wholeMarkdown.Click += async (_, _) => await OpenMarkdownAsync();
        _toolbar.Children.Add(wholeMarkdown);
        _toolbar.Children.Add(_sourceToggle);
        _toolbar.Children.Add(_paragraphStyles);
        _toolbar.Children.Add(_characterStyles);
        _toolbar.Children.Add(Toggle("B", MotifAppStrings.GuideBold, FontWeight.Bold, FontStyle.Normal, null, TextPropertyNumbers.Bold, ToggleBold));
        _toolbar.Children.Add(Toggle("I", MotifAppStrings.GuideItalic, FontWeight.Normal, FontStyle.Italic, null, TextPropertyNumbers.Italic, ToggleItalic));
        _toolbar.Children.Add(Toggle("U", MotifAppStrings.GuideUnderline, FontWeight.Normal, FontStyle.Normal, TextDecorations.Underline, TextPropertyNumbers.Underline, ToggleUnderline));
        _toolbar.Children.Add(_writingSystems);
        _toolbar.Children.Add(_links);
        var insertPathLink = new Button { Content = MotifAppStrings.GuideInsertLink, Focusable = false };
        insertPathLink.Click += async (_, _) => await InsertLinkToPathAsync(LinkPath);
        // TextChanged is raised later; the property change is not, so the look-up starts with the text.
        _linkPath.PropertyChanged += (_, change) =>
        {
            if (change.Property == TextBox.TextProperty) LinkPathResolving = ShowLinkPathHintAsync(LinkPath);
        };
        var byPath = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Classes = { "guidePathRow" } };
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
        _toolbar.IsVisible = true;
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
        _toolbar.IsVisible = _isEditing && _markdownDocument is null;
        UpdateFormatButtons();
    }

    private void UpdateFormatButtons()
    {
        foreach (var (button, property) in _formatToggles)
            button.IsChecked = Field is not null && Session?.IsFormatOn(property) == true;
    }

    private void ShowChoices(GdrDiscussion discussion)
    {
        _paragraphStyles.ItemsSource = discussion.ParagraphStyles.ToArray();
        _characterStyles.ItemsSource = new[] { NoCharacterStyle }.Concat(discussion.CharacterStyles).ToArray();
        _writingSystems.ItemsSource = discussion.WritingSystemChoices.Select(system => system.Tag).ToArray();
        _links.ItemsSource = Records.Where(record => record.Id != _shownId)
            .Select(record => $"{record.Abbreviation}  {record.Title}".Trim()).ToArray();
    }

    /// <summary>
    /// Whether the person may type: false shows the section for reading only, with no tool bar. Leaving the edit ends
    /// its editing session, so what was typed is in the Draft.
    /// </summary>
    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (_isEditing == value) return;
            if (!value) Session?.EndSession();
            _isEditing = value;
            SetReadOnly();
            ShowToolbar();
        }
    }

    /// <summary>The field that edits the selected section's Discussion, or null before one is selected.</summary>
    public RichTextField? Field => _fieldHost.Content as RichTextField;

    /// <summary>The session hosting the selected section's field, or null before one is selected.</summary>
    public GdrDiscussionSession? Session => _shownId is null ? null : _opened[_shownId].Session;

    /// <summary>The id of the section shown, or null before one is selected.</summary>
    public string? ShownId => _shownId;

    /// <summary>Every section's pending edit, by section id; a section edited back to how it began has none.</summary>
    public IReadOnlyDictionary<string, GdrDiscussionEdit> PendingEdits => _opened.Values
        .Where(opened => opened.Session.Pending is not null)
        .ToDictionary(opened => opened.Session.Pending!.RecordId, opened => opened.Session.Pending!, StringComparer.Ordinal);

    /// <summary>Whether any section holds typing, staged text, Markdown or a refusal that leaving would lose or hide.</summary>
    public bool HasUnsavedWork => PendingEdits.Count > 0 || _staged.Count > 0 || _stale.Count > 0 || MarkdownDirty ||
        _markdownSaving || _markdownOpening || _refused.Count > 0;

    /// <summary>Raised when a session ends and changes a section's pending edit (null when the section is back as it began).</summary>
    public event EventHandler<GdrDiscussionEdit?>? PendingEditChanged;

    /// <summary>What the last staging or Apply said: a refusal's message, "Applied." or nothing.</summary>
    public string StatusText => _status.Text ?? string.Empty;

    /// <summary>What the strip in the field says when the shown section's text was also changed in FieldWorks.</summary>
    public static string StaleMessage => MotifAppStrings.GuideStaleMessage;

    /// <summary>What Compare says, before the refusal, when the edit of the Discussion could not be added to the pending changes.</summary>
    public static string CouldNotStageMessage => MotifAppStrings.GuideCouldNotStage;

    /// <summary>What Compare says when the pending changes no longer hold an edit of the shown Discussion.</summary>
    public static string NothingToCompareMessage => MotifAppStrings.GuideNothingToCompare;

    /// <summary>Whether the strip saying the shown section was also changed in FieldWorks is showing.</summary>
    public bool IsStaleStripShown => _staleStrip.IsVisible;

    /// <summary>What the strip in the field says when the shown section holds an edit the Draft kept from earlier.</summary>
    public static string UnappliedMessage => MotifAppStrings.GuideUnappliedMessage;

    /// <summary>Whether the strip saying the shown section shows an edit kept in the Draft is showing.</summary>
    public bool IsUnappliedStripShown => _unappliedStrip.IsVisible;

    /// <summary>The newest read of a section's edit from the Draft, which reaches the field when it completes.</summary>
    public Task Loading { get; private set; } = Task.CompletedTask;

    /// <summary>The newest read of a section for <see cref="Show(string)"/>, complete once its field is shown.</summary>
    public Task Showing { get; private set; } = Task.CompletedTask;

    /// <summary>The heading above the field: the selected section's number, title and abstract.</summary>
    public string HeadingText => _shownId is { } id && _opened.TryGetValue(id, out var opened) ? opened.Heading : string.Empty;

    /// <summary>
    /// Replaces the sections listed. A section nobody has edited is read again the next time it is shown, so it reads as
    /// the project now is; a section holding typing, a staged edit or a refusal keeps its field, and Apply tells whether
    /// FieldWorks changed it meanwhile.
    /// </summary>
    public void SetRecords(IReadOnlyList<GdrSummary> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        Records = records;
        foreach (var (id, opened) in _opened.ToArray())
        {
            var keeps = opened.Session.Pending is not null || _staged.Contains(id) || _stale.Contains(id) ||
                _refused.ContainsKey(id) || _locked.Contains(id) || (id == _shownId && _markdownDocument is not null);
            if (!keeps) _opened.Remove(id);
        }
        if (_shownId is { } shown && !_opened.ContainsKey(shown))
        {
            _shownId = null;
            _fieldHost.Content = null;
            _showGeneration++;
        }
        if (_shownId is { } kept) ShowChoices(_opened[kept].Discussion);
        ShowStrips();
    }

    /// <summary>Forgets every section and what was typed in it, as when the project is closed.</summary>
    public void Clear()
    {
        _showGeneration++;
        foreach (var opened in _opened.Values) opened.Field.IsReadOnly = true;
        _opened.Clear();
        _staged.Clear();
        _stale.Clear();
        _refused.Clear();
        _locked.Clear();
        _unread.Clear();
        _markdownDocument = null;
        _markdownBox = null;
        _markdownStart = null;
        _markdownUndo = null;
        _shownId = null;
        _fieldHost.Content = null;
        _status.Text = string.Empty;
        _target.Text = string.Empty;
        ShowStrips();
        ShowToolbar();
    }

    /// <summary>
    /// Ends the shown section's session, waits for every edit to reach the Draft, and applies the pending changes, with
    /// every field read-only meanwhile. Once they are written, each staged section's paragraphs take the keys the project
    /// gave them and its session starts again from the applied text; returns whether Apply wrote them. Apply waits while
    /// a section's latest edit could not be staged, since the Draft still holds an older one.
    /// </summary>
    public async Task<bool> ApplyAsync()
    {
        if (_store is null) return false;
        if (_markdownDocument is not null && !BackToFields()) return false;
        if (!PreviewSourceMode.ReturnToRichText(_opened.Values.Select(opened => opened.Field), _status)) return false;
        Session?.EndSession();
        // A refused staging is tried again, so a busy Draft only holds Apply back while it stays busy.
        foreach (var id in _refused.Keys.ToArray())
            if (_opened.TryGetValue(id, out var refused)) _ = Stage(refused, refused.Session.Pending);
        await _staging;
        if (_refused.Count > 0)
        {
            _status.Text = MotifAppStrings.GuideNothingAppliedCouldNotStage + _refused.Values.First();
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
                _status.Text = MotifAppStrings.GuideNothingApplied;
                ShowStrips();
            }
            else
            {
                _status.Text = refusal.Message;
            }
            return false;
        }
        foreach (var id in _staged.ToArray()) await RekeyAsync(id);
        _staged.Clear();
        _status.Text = MotifAppStrings.GuideApplied;
        ShowStrips();
        return true;
    }

    /// <summary>
    /// After the person confirms, throws away the shown section's edit, which the section's text in FieldWorks has
    /// overtaken, and shows the section again from the FieldWorks text as the new start; returns whether it did.
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
        await ShowAsync(id);
        return true;
    }

    /// <summary>
    /// Compares the shown section's edit, which the section's text in FieldWorks has overtaken, with that text, and lets
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
        if (read.Paragraphs is not { } pending || await _show(id) is not { } live)
        {
            _status.Text = NothingToCompareMessage;
            return false;
        }
        var discussion = GdrDiscussion.From(live);
        var comparison = new TextComparison(TextSort.Sort(TextVersions.FromParagraphs(pending, live.Discussion,
            _opened[id].Discussion.Source.Select(TextVersions.Paragraph))));
        var window = new CompareWindow($"{live.Record.Abbreviation}  {live.Record.Title}, Discussion", comparison,
            discussion.WritingSystems, discussion.Styles, writingSystemName: _writingSystemName);
        var outcome = await (_compare?.Invoke(window) ?? window.ShowDialog<CompareOutcome?>(Owner()!));
        if (outcome?.End == CompareEnd.StartAgain) return await StartAgainFromFieldWorksAsync();
        if (outcome?.End != CompareEnd.Done) return false;
        var payload = ComparedPayloads.Paragraphs(GdrDiscussionPayload.Field, outcome.Result, live.Discussion);
        var write = payload is null ? (Func<Task<string?>>)(() => _store.DiscardAsync(id)) : () => _store.StageAsync(id, payload);
        if (await Staged(id, pending: payload is not null, write) is not null) return false;
        _stale.Remove(id);
        _opened.Remove(id);
        await ShowAsync(id);
        _status.Text = payload is null ? MotifAppStrings.GuideEditMatchesFieldWorks : MotifAppStrings.GuideEditFits;
        return true;
    }

    private Window? Owner() => TopLevel.GetTopLevel(this) as Window;

    private Task<string?> Stage(OpenedRecord opened, GdrDiscussionEdit? edit) => edit is null
        ? Staged(opened.RecordId, pending: false, () => _store!.DiscardAsync(opened.RecordId))
        : Staged(opened.RecordId, pending: true,
            () => _store!.StageAsync(opened.RecordId, GdrDiscussionPayload.From(edit, opened.Discussion, opened.Began)));

    // One staging at a time, in the order the sessions ended, so the Draft ends up with each section's latest edit.
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
        // A stage that went through leaves the source-mode announcement for the screen reader to finish.
        if (refusal is not null || _status.Text != _announced) _status.Text = refusal ?? string.Empty;
        return refusal;
    }

    // Typing needs editing on, no Apply running, and a Draft edit that was read and fits.
    private void SetReadOnly()
    {
        foreach (var opened in _opened.Values)
            opened.Field.IsReadOnly = _applying || !_isEditing || _locked.Contains(opened.RecordId);
    }

    private void ShowStrips()
    {
        _staleStrip.IsVisible = _shownId is { } id && _stale.Contains(id);
        _unappliedStrip.IsVisible = !_staleStrip.IsVisible && _shownId is { } shown &&
            _opened.TryGetValue(shown, out var opened) && opened.Began is not null && opened.Session.Pending is not null;
    }

    private async Task<bool> ConfirmStartAgainAsync()
    {
        if (Owner() is not { } owner) return false;
        var dialog = new Window
        {
            Title = MotifAppStrings.GuideStartAgainTitle,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        dialog.Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://SIL.Motif.App/"))
        {
            Source = new Uri("avares://SIL.Motif.App/Tokens/Components/GrammarGuideScoped.axaml"),
        });
        var keep = new Button { Content = MotifAppStrings.GuideGoBack, IsDefault = true, IsCancel = true };
        var discard = new Button { Content = MotifAppStrings.GuideDiscardMyEdit };
        keep.Click += (_, _) => dialog.Close(false);
        discard.Click += (_, _) => dialog.Close(true);
        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Classes = { "guideDialogButtons" },
        };
        buttons.Children.Add(keep);
        buttons.Children.Add(discard);
        var body = new StackPanel { Classes = { "guideDialogBody" } };
        body.Children.Add(new TextBlock { Text = MotifAppStrings.GuideStartAgainBody, TextWrapping = TextWrapping.Wrap, Classes = { "guideDialogText" } });
        body.Children.Add(buttons);
        dialog.Content = body;
        return await dialog.ShowDialog<bool>(owner);
    }

    // After Apply, new paragraphs take the project's keys and the applied text is the next edit's start.
    private async Task RekeyAsync(string id)
    {
        if (!_opened.TryGetValue(id, out var opened)) return;
        var paragraphs = opened.Field.Editor.Document.Paragraphs;
        if (await _show(id) is { } reloaded && reloaded.Discussion.Count == paragraphs.Count)
        {
            if (!_opened.TryGetValue(id, out var current) || current != opened) return;
            for (var i = 0; i < paragraphs.Count; i++) paragraphs[i].Key = reloaded.Discussion[i].Key;
            opened.Session.Applied();
            var discussion = GdrDiscussion.From(reloaded);
            opened.Field.SourceForm = new DiscussionSourceForm(discussion, _projectName);
            var heading = $"{reloaded.Record.Abbreviation}  {reloaded.Record.Title}\n{reloaded.Record.Description}";
            _opened[id] = opened with { Discussion = discussion, Began = null, Heading = heading };
            return;
        }
        _opened.Remove(id);
        _unread.Remove(id);
        if (_shownId == id) await ShowAsync(id);
    }

    private void WatchClosing()
    {
        if (TopLevel.GetTopLevel(this) is not Window window || ReferenceEquals(window, _closingWindow)) return;
        StopWatchingClosing();
        _closingWindow = window;
        window.Closing += OnWindowClosing;
    }

    private void StopWatchingClosing()
    {
        if (_closingWindow is { } window) window.Closing -= OnWindowClosing;
        _closingWindow = null;
    }

    // Closes unless a Discussion holds Markdown that cannot go back to rich text, which stays with the window.
    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (MarkdownDirty || _markdownSaving || _markdownOpening)
        {
            e.Cancel = true;
            _status.Text = MotifAppStrings.GuideMarkdownSaveFirstClosing;
        }
        PreviewSourceMode.KeepUnreturnedMarkdown(e, _opened.Values.Select(opened => opened.Field), _status);
    }

    /// <summary>
    /// Selects the section <paramref name="id"/> and shows its Discussion, with any edits made to it already. A section
    /// opened for the first time shows the edit the pending-changes Draft holds for it, once <see cref="Loading"/>
    /// has read it. While the shown section holds Markdown that cannot go back to rich text, it stays shown.
    /// </summary>
    public void Show(string id) => Showing = ShowAsync(id);

    /// <summary>As <see cref="Show(string)"/>, complete once the section's field is in place.</summary>
    public async Task ShowAsync(string id)
    {
        if (!CanLeaveFor(id)) return;
        var generation = ++_showGeneration;
        GdrShowResponse? first = null;
        if (!_opened.ContainsKey(id))
        {
            first = await _show(id);
            if (first is null || generation != _showGeneration) return;
        }
        Activate(id, first);
    }

    /// <summary>
    /// Shows the section <paramref name="response"/> was read for, as <see cref="Show(string)"/> does; a section
    /// already open keeps its field, and so what was typed in it, rather than take the response's text.
    /// </summary>
    public void Show(GdrShowResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var id = response.Record.Id;
        if (!CanLeaveFor(id)) return;
        _showGeneration++;
        Activate(id, _opened.ContainsKey(id) ? null : response);
    }

    // Markdown is never dropped: the shown section goes back to rich text first, or stays shown while it cannot.
    private bool CanLeaveFor(string id)
    {
        if (_shownId == id && _markdownDocument is not null) return false;
        if (_shownId != id && (_markdownOpening || _markdownDocument is not null && !BackToFields()))
        {
            SelectionRefused?.Invoke(this, _shownId);
            return false;
        }
        if (_shownId is { } shown && shown != id && _opened.TryGetValue(shown, out var leaving) &&
            !PreviewSourceMode.ReturnToRichText([leaving.Field], _status))
        {
            SelectionRefused?.Invoke(this, shown);
            return false;
        }
        return true;
    }

    private void Activate(string id, GdrShowResponse? first)
    {
        if (!_opened.TryGetValue(id, out var opened))
        {
            if (first is null) return;
            opened = Open(first, null);
            _opened[id] = opened;
        }
        _shownId = id;
        _fieldHost.Content = opened.Field;
        ShowChoices(opened.Discussion);
        ShowToolbar();
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
        // A field edits one document for its whole life, so each section gets its own field and keeps it.
        var field = new RichTextField(discussion.Document)
        {
            WritingSystems = discussion.WritingSystems,
            StyleResolver = discussion.Styles,
        };
        field.SourceForm = new DiscussionSourceForm(discussion, _projectName);
        field.ContextMenuRequested += OnContextMenuRequested;
        field.Editor.CaretMoved += () => { if (Field == field) UpdateFormatButtons(); };
        field.Editor.DocumentChanged += (_, _, _) => { if (Field == field) UpdateFormatButtons(); };
        field.SourceModeChanged += (_, change) =>
        {
            _status.Text = _announced = change.Announcement;
            if (_opened.TryGetValue(id, out var current) && current.Field == field) ShowToolbar();
        };
        // Until the Draft has been read, typing could start an edit that would replace the one it holds.
        if (_store is not null && pending is null) _locked.Add(id);
        field.IsReadOnly = _applying || !_isEditing || _locked.Contains(id);
        var session = new GdrDiscussionSession(field, id, starting: starting);
        var opened = new OpenedRecord(id, field, session, $"{shown.Record.Abbreviation}  {shown.Record.Title}\n{shown.Record.Description}",
            discussion, pending?.Expected, shown.Baseline);
        session.PendingChanged += (_, edit) =>
        {
            // A field set aside by "Start again" or by loading the Draft's edit no longer speaks for the section.
            if (!_opened.TryGetValue(id, out var current) || current.Field != field) return;
            if (_store is not null) _ = Stage(current, edit);
            PendingEditChanged?.Invoke(this, edit);
            ShowStrips();
        };
        return opened;
    }

    // A section the Draft holds an edit of shows that edit, so a later session updates it.
    private async Task LoadPending(GdrShowResponse shown, OpenedRecord opened)
    {
        var read = await _store!.PendingAsync(opened.RecordId);
        if (!_opened.TryGetValue(opened.RecordId, out var current) || current != opened) return;
        if (read.Refusal is { } refusal)
        {
            _unread[opened.RecordId] = shown;
            _status.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.GuideStaysReadOnlyFormat, refusal);
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
        if (_shownId == opened.RecordId) _fieldHost.Content = reopened.Field;
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
        var item = new MenuItem
        {
            Header = MotifAppStrings.GuideShowTarget,
            IsEnabled = ShownDiscussion?.Targets.ContainsKey((place.RunRange.Start.Paragraph, place.RunRange.Start.Offset)) == true,
        };
        item.Click += (_, _) => ShowTarget(place);
        new ContextMenu { ItemsSource = new[] { item } }.Open(field);
    }

    private sealed record OpenedRecord(string RecordId, RichTextField Field, GdrDiscussionSession Session, string Heading,
        GdrDiscussion Discussion, IReadOnlyDictionary<string, TextParagraphExpected>? Began, BaselineToken? Baseline);
}
