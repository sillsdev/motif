using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SIL.Motif.App.Compare;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;
using SIL.TextEditing;
using SIL.TextEditing.Avalonia;

namespace SIL.Motif.App.Preview;

/// <summary>
/// A developer-only window that lists a project's senses and lets a person edit each one's Definition, one field per
/// writing system, with the project's character styles and writing-system directions. Each field's edits add up to one
/// pending <c>text/setRichString</c> edit, measured against the text the field was shown with, which an
/// <see cref="ISenseDefinitionStore"/> keeps as one operation in the pending-changes Draft and applies.
/// </summary>
public sealed class SenseDefinitionPreviewWindow : Window
{
    /// <summary>The start-up argument that opens this window, followed by the project file, when the developer flag is set.</summary>
    public const string StartupArgument = "--sense-definition-preview";

    /// <summary>What the strip says when one of the shown sense's Definitions was also changed in FieldWorks.</summary>
    public const string StaleMessage = "This Definition was also changed in FieldWorks. Your typing is safe.";

    /// <summary>What Compare says, before the refusal, when the edit of the Definition could not be added to the pending changes.</summary>
    public const string CouldNotStageMessage = "There is nothing to compare yet: your edit could not be added to the pending changes. ";

    /// <summary>What Compare says when the pending changes no longer hold an edit of the Definition.</summary>
    public const string NothingToCompareMessage = "There is nothing to compare: your pending changes no longer hold an edit of this Definition.";

    /// <summary>What the strip says when the shown sense holds an edit the Draft kept from an earlier window.</summary>
    public const string UnappliedMessage = "This Definition shows your edit that is not applied yet.";

    /// <summary>Why an edit is not staged while its field holds more than one paragraph.</summary>
    public const string OneParagraphMessage = "A Definition is one paragraph. Join the paragraphs to keep this edit.";

    /// <summary>What the comparison's footer says when Done would give a Definition more than one paragraph.</summary>
    public const string OneParagraphResultMessage = "A Definition is one paragraph. Join the paragraphs on the right, then press Done.";

    private readonly Func<SenseDefinitionsResponse?> _read;
    private readonly ISenseDefinitionStore? _store;
    private readonly Func<Task<bool>> _confirmStartAgain;
    private readonly Func<CompareWindow, Task<CompareOutcome?>> _compare;
    private readonly Func<string, string> _writingSystemName;
    private SenseDefinitionsResponse _senses;
    private readonly Dictionary<string, OpenedField> _opened = new(StringComparer.Ordinal);
    private readonly HashSet<string> _staged = new(StringComparer.Ordinal);
    private readonly HashSet<string> _stale = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _refused = new(StringComparer.Ordinal);
    // Fields whose Draft edit has not been read yet, or could not be, or no longer fits the project.
    private readonly HashSet<string> _locked = new(StringComparer.Ordinal);
    private bool _applying;
    private Task _staging = Task.CompletedTask;
    private string? _shownId;
    private readonly ListBox _list = new() { Width = 240 };
    private readonly StackPanel _fields = new() { Spacing = 8 };
    private readonly TextBlock _heading = new() { FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly Border _staleStrip;
    private readonly WrapPanel _bar = new() { Margin = new Thickness(0, 0, 0, 6) };
    // The field the bar acts on: the one the person was last in.
    private RichTextField? _focused;
    private readonly Border _unappliedStrip = new()
    {
        Background = Brushes.LightYellow,
        Padding = new Thickness(8, 4),
        IsVisible = false,
        Child = new TextBlock { Text = UnappliedMessage, TextWrapping = TextWrapping.Wrap },
    };

    /// <summary>
    /// Creates the window over <paramref name="senses"/>; <paramref name="read"/> reads them again after Apply or Start
    /// again, and <paramref name="store"/>, when given, stages each edit and offers Apply. <paramref name="confirmStartAgain"/>
    /// asks before an edit overtaken in FieldWorks is thrown away; by default a dialog asks. <paramref name="compare"/>
    /// shows the window that compares such an edit with FieldWorks' text and returns how the person left it; by default
    /// it opens as a dialog. <paramref name="writingSystemName"/> names a writing system by its tag, as the project
    /// does; without it the comparison says the tag.
    /// </summary>
    public SenseDefinitionPreviewWindow(SenseDefinitionsResponse senses, Func<SenseDefinitionsResponse?> read,
        ISenseDefinitionStore? store = null, Func<Task<bool>>? confirmStartAgain = null,
        Func<CompareWindow, Task<CompareOutcome?>>? compare = null, Func<string, string>? writingSystemName = null)
    {
        _writingSystemName = writingSystemName ?? (tag => tag);
        _senses = senses ?? throw new ArgumentNullException(nameof(senses));
        _read = read;
        _store = store;
        _confirmStartAgain = confirmStartAgain ?? ConfirmStartAgainAsync;
        _compare = compare ?? (window => window.ShowDialog<CompareOutcome?>(this));
        Title = "Sense Definitions preview (developer only)";
        Width = 900;
        Height = 560;

        var compareButton = new Button { Content = ChangeWords.Compare };
        compareButton.Click += async (_, _) => await CompareAsync();
        var strip = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12 };
        strip.Children.Add(new TextBlock { Text = StaleMessage, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        strip.Children.Add(compareButton);
        _staleStrip = new Border { Background = Brushes.LightYellow, Padding = new Thickness(8, 4), IsVisible = false, Child = strip };

        Senses = senses.Senses;
        _list.ItemsSource = senses.Senses.Select(sense => sense.Label).ToArray();
        _list.SelectionChanged += (_, _) =>
        {
            if (_list.SelectedIndex >= 0) Show(Senses[_list.SelectedIndex].Id);
        };

        _bar.Children.Add(PreviewSourceMode.Toggle(() => ToggleSourceMode()));
        var right = new DockPanel { Margin = new Thickness(12, 0, 0, 0), LastChildFill = true };
        foreach (var top in new Control[] { _heading, _bar, _staleStrip, _unappliedStrip })
        {
            DockPanel.SetDock(top, Dock.Top);
            right.Children.Add(top);
        }
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
        right.Children.Add(new ScrollViewer { Content = _fields });
        var panel = new DockPanel { Margin = new Thickness(12), LastChildFill = true };
        DockPanel.SetDock(_list, Dock.Left);
        panel.Children.Add(_list);
        panel.Children.Add(right);
        Content = panel;
    }

    /// <summary>The senses listed.</summary>
    public IReadOnlyList<SenseDefinitions> Senses { get; }

    /// <summary>The field editing the shown sense's Definition in <paramref name="ws"/>, or null when none is shown.</summary>
    public RichTextField? FieldFor(string ws) => Shown(ws)?.Field;

    /// <summary>The session hosting the shown sense's <paramref name="ws"/> field, or null when none is shown.</summary>
    public GdrDiscussionSession? SessionFor(string ws) => Shown(ws)?.Session;

    /// <summary>What the last staging or Apply said: a refusal's message, "Applied." or nothing.</summary>
    public string StatusText => _status.Text ?? string.Empty;

    /// <summary>What Compare and Apply say when a field cannot go back to rich text first.</summary>
    public const string MarkdownFixFirstMessage = PreviewSourceMode.FixFirstMessage;

    /// <summary>
    /// Shows the shown sense's Definition in <paramref name="ws"/> as FieldWorks Markdown, or returns it to rich text, as
    /// the bar's Markdown button and Ctrl+Shift+M in the field do; without <paramref name="ws"/>, the field the person was
    /// last in, or else the first. Returns whether it switched; going back is refused while the Markdown has an error.
    /// </summary>
    public bool ToggleSourceMode(string? ws = null)
    {
        var shown = _opened.Values.Where(opened => opened.SenseId == _shownId).ToArray();
        var target = ws is not null ? shown.FirstOrDefault(opened => opened.Ws == ws)
            : shown.FirstOrDefault(opened => opened.Field == _focused) ?? shown.FirstOrDefault();
        return target?.Session.ToggleSourceMode() == true;
    }

    /// <summary>Whether the strip saying a shown Definition was also changed in FieldWorks is showing.</summary>
    public bool IsStaleStripShown => _staleStrip.IsVisible;

    /// <summary>Whether the strip saying the shown sense shows an edit kept in the Draft is showing.</summary>
    public bool IsUnappliedStripShown => _unappliedStrip.IsVisible;

    /// <summary>The newest read of the shown sense's edits from the Draft, which reach the fields when it completes.</summary>
    public Task Loading { get; private set; } = Task.CompletedTask;

    /// <summary>The heading above the fields: the shown sense's label.</summary>
    public string HeadingText => _heading.Text ?? string.Empty;

    /// <summary>
    /// Shows sense <paramref name="senseId"/>'s Definition, one field per writing system, with any edits made to it
    /// already. A field opened for the first time shows the edit the Draft holds for it, once <see cref="Loading"/> has read it.
    /// </summary>
    public void Show(string senseId)
    {
        if (_senses.Senses.FirstOrDefault(sense => sense.Id == senseId) is not { } sense) return;
        _shownId = senseId;
        _heading.Text = sense.Label;
        var loads = new List<Task>();
        foreach (var alternative in sense.Definition)
        {
            var key = Key(senseId, alternative.WritingSystem);
            if (_opened.ContainsKey(key)) continue;
            var opened = Open(sense, alternative, null);
            _opened[key] = opened;
            if (_store is not null) loads.Add(LoadPending(sense, alternative, opened));
        }
        Loading = Task.WhenAll(loads);
        ShowFields();
    }

    /// <summary>
    /// Ends every shown session, waits for every edit to reach the Draft, and applies the pending changes with every
    /// field read-only meanwhile; returns whether Apply wrote them. Apply waits while an edit could not be staged.
    /// </summary>
    public async Task<bool> ApplyAsync()
    {
        if (_store is null) return false;
        if (!PreviewSourceMode.ReturnToRichText(_opened.Values.Select(opened => opened.Field), _status)) return false;
        foreach (var opened in _opened.Values) opened.Session.EndSession();
        // A refused staging is tried again, so a busy Draft only holds Apply back while it stays busy.
        foreach (var key in _refused.Keys.ToArray())
            if (_opened.TryGetValue(key, out var refused)) _ = Stage(refused, refused.Session.Pending is not null);
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
            if (refusal is { StaleRecordId: { } owner, StaleWritingSystem: { } ws, StaleField: SenseDefinitionStore.Field })
            {
                // The Baseline was recaptured, so a field not open yet finds itself stale when it opens.
                if (_read() is { } current) _senses = current;
                var key = Key(owner, ws);
                if (_opened.ContainsKey(key)) _stale.Add(key);
                _status.Text = _opened.ContainsKey(key) ? "Nothing was applied." : "Nothing was applied. " + refusal.Message;
                ShowStrips();
            }
            else
            {
                _status.Text = refusal.Message;
            }
            return false;
        }
        if (_read() is { } applied) _senses = applied;
        foreach (var key in _staged) Restart(key);
        _staged.Clear();
        _status.Text = "Applied.";
        ShowStrips();
        return true;
    }

    /// <summary>
    /// After the person confirms, throws away the shown sense's edits that its Definition in FieldWorks has overtaken,
    /// and shows those writing systems again from the FieldWorks text as the new start; returns whether it did.
    /// </summary>
    public async Task<bool> StartAgainFromFieldWorksAsync()
    {
        if (_store is null || _shownId is not { } senseId) return false;
        var stale = _stale.Where(key => _opened.TryGetValue(key, out var field) && field.SenseId == senseId).ToArray();
        if (stale.Length == 0 || !await _confirmStartAgain()) return false;
        foreach (var key in stale)
        {
            if (!_opened.TryGetValue(key, out var opened)) continue;
            opened.Field.DiscardSourceChanges();
            opened.Session.EndSession();
            if (await Staged(key, pending: false, () => _store.DiscardAsync(senseId, opened.Ws)) is not null) return false;
            _stale.Remove(key);
            _staged.Remove(key);
            _opened.Remove(key);
        }
        if (_read() is { } current) _senses = current;
        Show(senseId);
        return true;
    }

    /// <summary>
    /// Compares the shown sense's first Definition edit that FieldWorks has overtaken with FieldWorks' text, and lets the
    /// person choose change by change. Done stages the result measured against FieldWorks' text, so it fits again;
    /// Decide later leaves the edit as it was, and Apply keeps waiting. Returns whether the edit fits again.
    /// </summary>
    public async Task<bool> CompareAsync()
    {
        if (_store is null || _shownId is not { } senseId) return false;
        var key = _stale.FirstOrDefault(stale => _opened.TryGetValue(stale, out var field) && field.SenseId == senseId);
        if (key is null) return false;
        var ws = _opened[key].Ws;
        if (!PreviewSourceMode.ReturnToRichText([_opened[key].Field], _status)) return false;
        _opened[key].Session.EndSession();
        await _staging;
        if (_refused.TryGetValue(key, out var unstaged))
        {
            _status.Text = CouldNotStageMessage + unstaged;
            return false;
        }
        var read = await _store.PendingAsync(senseId, ws);
        if (read.Refusal is { } refusal)
        {
            _status.Text = refusal;
            return false;
        }
        if (read.RichString is not { } pending || _read() is not { } current)
        {
            _status.Text = NothingToCompareMessage;
            return false;
        }
        _senses = current;
        var sense = current.Senses.FirstOrDefault(item => item.Id == senseId);
        var live = sense?.Definition.FirstOrDefault(item => item.WritingSystem == ws)?.Runs ?? [];
        var name = _writingSystemName(ws);
        var location = $"the {name} Definition";
        var comparison = new TextComparison(TextSort.Sort(TextVersions.FromRichString(pending, live, key,
            _opened[key].Discussion.Source.FirstOrDefault()?.Runs.Select(TextVersions.Run).ToArray()), location));
        var shown = _opened[key].Discussion;
        var window = new CompareWindow($"{sense?.Label}, Definition ({name})", comparison, shown.WritingSystems, shown.Styles,
            checkResult: result => result.Count > 1 ? OneParagraphResultMessage : null, writingSystemName: _writingSystemName);
        var outcome = await _compare(window);
        if (outcome?.End == CompareEnd.StartAgain) return await StartAgainFromFieldWorksAsync();
        if (outcome?.End != CompareEnd.Done) return false;
        if (outcome.Result.Count > 1)
        {
            _status.Text = OneParagraphMessage;
            return false;
        }
        // A result of no paragraph at all is an empty Definition.
        var result = outcome.Result.Count == 0 ? new ParagraphVersion(key, "", TextProps.None, []) : outcome.Result[0];
        var payload = ComparedPayloads.RichString(SenseDefinitionStore.Field, ws, result, live);
        var write = payload is null ? (Func<Task<string?>>)(() => _store.DiscardAsync(senseId, ws)) : () => _store.StageAsync(senseId, payload);
        if (await Staged(key, pending: payload is not null, write) is not null) return false;
        _stale.Remove(key);
        _opened.Remove(key);
        Show(senseId);
        _status.Text = payload is null ? "Your edit now matches FieldWorks; nothing is left to apply." : "Your edit now fits. Apply when ready.";
        return true;
    }

    private OpenedField Open(SenseDefinitions sense, RichStringAlternative alternative, TextRichStringPayload? pending)
    {
        var key = Key(sense.Id, alternative.WritingSystem);
        var discussion = GdrDiscussion.From(Response(sense, alternative.WritingSystem, alternative.Runs));
        IEnumerable<Paragraph>? starting = null;
        if (pending is not null)
        {
            starting = discussion.Document.Paragraphs.Select(paragraph => paragraph.Clone()).ToArray();
            discussion = GdrDiscussion.From(Response(sense, alternative.WritingSystem, pending.Runs.Select(Run).ToArray()));
        }
        // A field edits one document for its whole life, so each alternative gets its own field and keeps it.
        var field = new RichTextField(discussion.Document)
        {
            WritingSystems = discussion.WritingSystems,
            StyleResolver = discussion.Styles,
            MinHeight = 48,
            SourceForm = new DiscussionSourceForm(discussion, projectName: null, richString: true),
        };
        field.GotFocus += (_, _) => _focused = field;
        // The field offers no menu of its own; in Markdown the source view's menu is about the Markdown.
        field.ContextMenuRequested += (_, e) =>
        {
            if (e.Field is { } view && view != field) new ContextMenu { ItemsSource = PreviewSourceMode.Menu(view, e.Info) }.Open(field);
        };
        // Until the Draft has been read, typing could start an edit that would replace the one it holds.
        if (_store is not null && pending is null) _locked.Add(key);
        field.IsReadOnly = _applying || _locked.Contains(key);
        var session = new GdrDiscussionSession(field, key, starting: starting);
        // After the session's handler, which stages the edit and so rewrites the status line.
        field.SourceModeChanged += (_, change) => _status.Text = change.Announcement;
        var opened = new OpenedField(sense.Id, alternative.WritingSystem, field, session, discussion,
            new TextRichStringExpected(alternative.Text, alternative.Digest), pending?.Expected);
        session.PendingChanged += (_, edit) =>
        {
            // A field set aside by "Start again" or by loading the Draft's edit no longer speaks for the alternative.
            if (!_opened.TryGetValue(key, out var current) || current.Field != field) return;
            if (_store is not null) Stage(current, edit is not null);
            ShowStrips();
        };
        return opened;
    }

    // A field the Draft holds an edit of shows that edit, so a later session updates it and keeps its start.
    private async Task LoadPending(SenseDefinitions sense, RichStringAlternative alternative, OpenedField opened)
    {
        var key = Key(sense.Id, alternative.WritingSystem);
        var read = await _store!.PendingAsync(sense.Id, alternative.WritingSystem);
        if (!_opened.TryGetValue(key, out var current) || current != opened) return;
        if (read.Refusal is { } refusal)
        {
            _status.Text = "This Definition stays read-only: its pending edit could not be read. " + refusal;
            return;
        }
        if (read.RichString is not { } pending)
        {
            _locked.Remove(key);
            SetReadOnly();
            return;
        }
        _staged.Add(key);
        if (pending.Expected != opened.Start)
        {
            // Shown over this project, the edit would drop what FieldWorks changed, so it waits for Compare.
            _stale.Add(key);
            ShowStrips();
            return;
        }
        _locked.Remove(key);
        _opened[key] = Open(sense, alternative, pending);
        if (_shownId == sense.Id) ShowFields();
    }

    private Task<string?> Stage(OpenedField opened, bool pending)
    {
        var key = Key(opened.SenseId, opened.Ws);
        if (!pending) return Staged(key, pending: false, () => _store!.DiscardAsync(opened.SenseId, opened.Ws));
        var paragraphs = opened.Field.Editor.Document.Paragraphs;
        if (paragraphs.Count != 1) return Staged(key, pending: true, () => Task.FromResult<string?>(OneParagraphMessage));
        var runs = opened.Discussion.ProjectRuns(paragraphs[0]).Where(run => run.Text.Length > 0).ToArray();
        var payload = new TextRichStringPayload(SenseDefinitionStore.Field, opened.Ws, runs, opened.Began ?? opened.Start);
        return Staged(key, pending: true, () => _store!.StageAsync(opened.SenseId, payload));
    }

    // One staging at a time, in the order the sessions ended, so the Draft ends up with each field's latest edit.
    private Task<string?> Staged(string key, bool pending, Func<Task<string?>> write)
    {
        var staged = StageAfter(_staging, key, pending, write);
        _staging = staged;
        return staged;
    }

    private async Task<string?> StageAfter(Task earlier, string key, bool pending, Func<Task<string?>> write)
    {
        await earlier;
        var refusal = await write();
        if (refusal is null)
        {
            _refused.Remove(key);
            if (pending) _staged.Add(key);
            else _staged.Remove(key);
        }
        else
        {
            _refused[key] = refusal;
        }
        _status.Text = refusal ?? string.Empty;
        return refusal;
    }

    // After Apply the project's text is the next edit's start, read again because liblcm may store it differently.
    private void Restart(string key)
    {
        if (!_opened.TryGetValue(key, out var opened)) return;
        var alternative = _senses.Senses.FirstOrDefault(sense => sense.Id == opened.SenseId)?.Definition
            .FirstOrDefault(item => item.WritingSystem == opened.Ws);
        if (alternative is null)
        {
            _opened.Remove(key);
            _stale.Remove(key);
            _locked.Remove(key);
            _refused.Remove(key);
            return;
        }
        opened.Session.Applied();
        _opened[key] = opened with { Start = new TextRichStringExpected(alternative.Text, alternative.Digest), Began = null };
    }

    // Typing is allowed only when nothing applies and the field's Draft edit was read and fits the project.
    private void SetReadOnly()
    {
        foreach (var (key, opened) in _opened) opened.Field.IsReadOnly = _applying || _locked.Contains(key);
    }

    private void ShowFields()
    {
        foreach (var border in _fields.Children.OfType<Border>()) border.Child = null;
        _fields.Children.Clear();
        foreach (var opened in _opened.Values.Where(opened => opened.SenseId == _shownId))
        {
            _fields.Children.Add(new TextBlock { Text = opened.Ws, FontWeight = FontWeight.SemiBold });
            _fields.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = opened.Field });
        }
        ShowStrips();
    }

    private void ShowStrips()
    {
        var shown = _opened.Where(pair => pair.Value.SenseId == _shownId).ToArray();
        _staleStrip.IsVisible = shown.Any(pair => _stale.Contains(pair.Key));
        _unappliedStrip.IsVisible = !_staleStrip.IsVisible &&
            shown.Any(pair => pair.Value.Began is not null && pair.Value.Session.Pending is not null);
    }

    private OpenedField? Shown(string ws) =>
        _shownId is { } id && _opened.TryGetValue(Key(id, ws), out var opened) ? opened : null;

    private static string Key(string senseId, string ws) => senseId + "\n" + ws;

    // One alternative as a one-paragraph record, so the GDR preview's conversions build its document and styles.
    private GdrShowResponse Response(SenseDefinitions sense, string ws, IReadOnlyList<GdrRun> runs) =>
        new(new GdrSummary(sense.Id, "", sense.Label, ""),
            [new GdrParagraph(null, _senses.WritingSystems.Any(item => item.Tag == ws && item.Direction == "rtl"), ws,
                new GdrTextProperties(new Dictionary<int, GdrIntProperty>(), new Dictionary<int, string>()), runs, "0")])
        {
            Styles = _senses.Styles,
            WritingSystems = _senses.WritingSystems,
        };

    private static GdrRun Run(TextRun run)
    {
        var strings = run.Props?.Strings.ToDictionary(pair => pair.Key, pair => pair.Value) ?? [];
        if (run.CharacterStyle is { } style) strings[TextPropertyNumbers.NamedStyle] = style;
        var ints = run.Props?.Ints.ToDictionary(pair => pair.Key, pair => new GdrIntProperty(pair.Value.Variation, pair.Value.Value)) ?? [];
        return new GdrRun(run.Text, run.Ws, run.CharacterStyle, new GdrTextProperties(ints, strings), run.ObjData);
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
            Text = "Your edit of this Definition will be thrown away and the Definition shown as it is now in FieldWorks.",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420,
        });
        body.Children.Add(buttons);
        dialog.Content = body;
        return await dialog.ShowDialog<bool>(this);
    }

    /// <summary>Whether <paramref name="developerFlag"/> and <paramref name="args"/> ask for this window.</summary>
    public static bool IsRequested(string? developerFlag, IEnumerable<string> args) =>
        CommandSurfacePolicy.FromEnvironment(developerFlag).DeveloperCommandsEnabled &&
        args.Contains(StartupArgument, StringComparer.Ordinal);

    /// <summary>Opens the window for the project that follows the start-up argument, or returns <see langword="null"/>.</summary>
    public static SenseDefinitionPreviewWindow? ShowIfRequested(IEnumerable<string> args)
    {
        var arguments = args.ToArray();
        if (!IsRequested(Environment.GetEnvironmentVariable(CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable), arguments))
            return null;
        var at = Array.IndexOf(arguments, StartupArgument);
        if (at + 1 >= arguments.Length) return null;
        var project = arguments[at + 1];
        SenseDefinitionsResponse? Read() => RichStringQuery.SenseDefinitions(project) is { Succeeded: true } read ? read.Value : null;
        if (Read() is not { } senses) return null;
        var window = new SenseDefinitionPreviewWindow(senses, Read, new SenseDefinitionStore(project),
            writingSystemName: WritingSystemNames.For(project));
        window.Show();
        return window;
    }

    private sealed record OpenedField(string SenseId, string Ws, RichTextField Field, GdrDiscussionSession Session,
        GdrDiscussion Discussion, TextRichStringExpected Start, TextRichStringExpected? Began);
}
