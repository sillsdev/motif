using Avalonia.Threading;
using SIL.TextEditing;
using SIL.TextEditing.Avalonia;

namespace SIL.Motif.App.Preview;

/// <summary>
/// One changed paragraph of a Discussion edit: where it now stands, the key it is matched by (null for a paragraph the
/// person made), and its content.
/// </summary>
/// <param name="Index">The paragraph's position in the Discussion after the edit.</param>
/// <param name="Key">The paragraph's canonical id, or null for a new paragraph.</param>
/// <param name="Content">A copy of the paragraph, with its style and runs.</param>
public sealed record GdrParagraphChange(int Index, string? Key, Paragraph Content)
{
    /// <summary>The paragraph's text.</summary>
    public string Text => Content.Text;
}

/// <summary>
/// A person's pending edit of one Discussion, measured against the text it began from (ADR 0083 §4), so a later reader can
/// tell whether the project changed since. Later sessions on the same record replace it rather than adding another.
/// </summary>
/// <param name="RecordId">The record's canonical id.</param>
/// <param name="Order">Every paragraph's key in order after the edit, null for a new paragraph.</param>
/// <param name="Changed">The paragraphs that are new or whose text, style or runs differ from the starting text.</param>
/// <param name="Removed">The keys of starting paragraphs that are gone.</param>
/// <param name="Starting">The starting text of each changed or removed paragraph, by key.</param>
public sealed record GdrDiscussionEdit(
    string RecordId,
    IReadOnlyList<string?> Order,
    IReadOnlyList<GdrParagraphChange> Changed,
    IReadOnlyList<string> Removed,
    IReadOnlyDictionary<string, string> Starting);

/// <summary>
/// Hosts a person's editing of one Discussion in a field: it keeps the field's undo history, one step per undo group
/// closed on <see cref="DocumentEditor.UndoGroupEnded"/> (SIL.TextEditing ADRs 0003, 0023 and 0024), and when the person
/// leaves the field or pauses it turns everything typed since the record was shown into one <see cref="GdrDiscussionEdit"/>.
/// The field has no formatting commands, so the session makes them: character and paragraph styles, bold, italic,
/// underline, writing systems and links, each one undo step.
/// </summary>
public sealed class GdrDiscussionSession
{
    /// <summary>How long a pause in editing ends the session when the person stays in the field.</summary>
    public static readonly TimeSpan DefaultPause = TimeSpan.FromSeconds(3);

    private readonly RichTextField _field;
    private readonly string _recordId;
    private readonly List<List<EditDelta>> _done = [];
    private readonly List<List<EditDelta>> _undone = [];
    private readonly DispatcherTimer _pause;
    private bool _stepOpen;
    private List<Paragraph> _starting;

    /// <summary>
    /// Starts hosting <paramref name="field"/>, whose document is record <paramref name="recordId"/>'s Discussion as
    /// shown. When the field already shows an edit, <paramref name="starting"/> is the text that edit began from, and
    /// the edit is <see cref="Pending"/> from the start.
    /// </summary>
    public GdrDiscussionSession(RichTextField field, string recordId, TimeSpan? pause = null, IEnumerable<Paragraph>? starting = null)
    {
        _field = field ?? throw new ArgumentNullException(nameof(field));
        _recordId = recordId;
        _starting = starting?.Select(paragraph => paragraph.Clone()).ToList() ?? Copy(field.Editor.Document);
        Pending = starting is null ? null : Measure();
        _pause = new DispatcherTimer { Interval = pause ?? DefaultPause };
        _pause.Tick += (_, _) => EndSession();

        field.Editor.Committed += OnCommitted;
        field.Editor.UndoGroupEnded += _ => _stepOpen = false;
        field.UndoRequested += () => Undo();
        field.RedoRequested += () => Redo();
        field.LostFocus += (_, _) => EndSession();
        // Back from Markdown with clean text is one edit, staged at once as a rich edit is when the person moves on.
        field.SourceModeChanged += (_, change) =>
        {
            if (!change.IsSourceMode && !change.Refused) EndSession();
        };
    }

    /// <summary>
    /// Switches the field to FieldWorks Markdown (SIL.TextEditing ADR 0035), or back to rich text; returns whether it
    /// switched. Back is refused while the Markdown has an error, and the field stays in Markdown with it marked.
    /// </summary>
    public bool ToggleSourceMode() => _field.IsSourceMode ? _field.LeaveSourceMode() : _field.EnterSourceMode();

    /// <summary>The edit the sessions so far add up to, or null when the Discussion is as it began.</summary>
    public GdrDiscussionEdit? Pending { get; private set; }

    /// <summary>Raised when a session ends with <see cref="Pending"/> changed; the window adds it to the Draft or updates it there.</summary>
    public event EventHandler<GdrDiscussionEdit?>? PendingChanged;

    /// <summary>The number of steps Ctrl+Z can undo.</summary>
    public int UndoSteps => _done.Count;

    /// <summary>The number of steps Ctrl+Y can redo.</summary>
    public int RedoSteps => _undone.Count;

    /// <summary>Ends the editing session: closes the open undo step and recomputes <see cref="Pending"/>.</summary>
    public void EndSession()
    {
        _pause.Stop();
        _field.Editor.CloseUndoGroup();
        var pending = Measure();
        if (Equals(Describe(pending), Describe(Pending))) return;
        Pending = pending;
        PendingChanged?.Invoke(this, pending);
    }

    /// <summary>Undoes the last step, if any; returns whether there was one. In Markdown the source view's own undo is used instead.</summary>
    public bool Undo()
    {
        if (_field.IsSourceMode) return false;
        _field.Editor.CloseUndoGroup();
        if (_done.Count == 0) return false;
        var step = _done[^1];
        _done.RemoveAt(_done.Count - 1);
        _undone.Add(step);
        for (var i = step.Count - 1; i >= 0; i--) _field.Editor.Apply(step[i].Inverse());
        RestartPause();
        return true;
    }

    /// <summary>Redoes the last undone step, if any; returns whether there was one.</summary>
    public bool Redo()
    {
        if (_field.IsSourceMode) return false;
        _field.Editor.CloseUndoGroup();
        if (_undone.Count == 0) return false;
        var step = _undone[^1];
        _undone.RemoveAt(_undone.Count - 1);
        _done.Add(step);
        foreach (var delta in step) _field.Editor.Apply(delta);
        RestartPause();
        return true;
    }

    /// <summary>Turns bold on for the selected text, or off when all of it is bold; returns whether anything changed.</summary>
    public bool ToggleBold() => Toggle(TextPropertyNumbers.Bold, TextPropertyNumbers.ToggleOn);

    /// <summary>Turns italic on for the selected text, or off when all of it is italic; returns whether anything changed.</summary>
    public bool ToggleItalic() => Toggle(TextPropertyNumbers.Italic, TextPropertyNumbers.ToggleOn);

    /// <summary>Underlines the selected text, or removes the underline when all of it is underlined; returns whether anything changed.</summary>
    public bool ToggleUnderline() => Toggle(TextPropertyNumbers.Underline, TextPropertyNumbers.UnderlineSingle);

    /// <summary>Gives the selected text the character style <paramref name="style"/>, or none when it is null; returns whether anything changed.</summary>
    public bool ApplyCharacterStyle(string? style) => FormatSelection(_ => run =>
    {
        if (style is null) run.Props.StrProps.Remove(TextPropertyNumbers.NamedStyle);
        else run.Props.SetStr(TextPropertyNumbers.NamedStyle, style);
    });

    /// <summary>
    /// With a selection, puts the selected text in writing system <paramref name="tag"/>; with a caret, makes it the one
    /// typing continues in. Returns whether it did either.
    /// </summary>
    public bool SetWritingSystem(string tag)
    {
        if (_field.IsReadOnly || _field.IsSourceMode) return false;
        if (_field.Editor.HasSelection) return FormatSelection(_ => run => run.Ws = tag);
        _field.Editor.SetActiveWs(tag);
        return true;
    }

    /// <summary>
    /// Gives the paragraphs of the selection, or the caret's paragraph, the paragraph style <paramref name="style"/>;
    /// returns false, adding no undo step, when they all have it already.
    /// </summary>
    public bool ApplyParagraphStyle(string style)
    {
        var editor = _field.Editor;
        if (_field.IsReadOnly || !editor.CanEdit || !_field.IsParagraphStyleAllowed(style)) return false;
        editor.Ordered(out var start, out var end);
        var delta = new EditDelta { Start = start.Paragraph, Label = "paragraph style " + style, CaretBefore = editor.Caret, CaretAfter = editor.Caret };
        for (var i = start.Paragraph; i <= end.Paragraph; i++)
        {
            var edited = editor.Document.Paragraphs[i].Clone();
            edited.Style = style;
            edited.Props.SetStr(TextPropertyNumbers.NamedStyle, style);
            delta.Before.Add(editor.Document.Paragraphs[i].Clone());
            delta.After.Add(edited);
        }
        if (delta.Before.All(before => before.Style == style &&
            (!before.Props.TryGetStr(TextPropertyNumbers.NamedStyle, out var named) || named == style))) return false;
        return Push(delta);
    }

    /// <summary>Puts <paramref name="link"/> at the caret in place of any selection, as one undo step; returns whether it did.</summary>
    public bool InsertLink(Run link)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (_field.IsReadOnly || !_field.Editor.CanEdit) return false;
        _field.Editor.CloseUndoGroup();
        var fragment = new Paragraph { DefaultWs = link.Ws };
        fragment.Runs.Add(link);
        _field.Editor.Paste([fragment]);
        _field.Editor.CloseUndoGroup();
        return true;
    }

    private bool Toggle(int property, int on) => FormatSelection(runs =>
    {
        var allOn = runs.All(run => run.Props.TryGetInt(property, out var value) && value == on);
        return run =>
        {
            if (allOn) run.Props.IntProps.Remove(property);
            else run.Props.SetInt(property, on, TextPropertyNumbers.VariationEnum);
        };
    });

    // An embedded object, such as a picture, keeps its own properties; a link's text formats like any other text.
    private static bool IsEmbedded(Run run) => run.IsObject && run.Text == "￼";

    private bool FormatSelection(Func<IReadOnlyList<Run>, Action<Run>> plan)
    {
        var editor = _field.Editor;
        if (_field.IsReadOnly || !editor.CanFormatSelection) return false;
        editor.Ordered(out var start, out var end);
        var delta = new EditDelta { Start = start.Paragraph, Label = "format", CaretBefore = editor.Caret, CaretAfter = end };
        var targets = new List<Run>();
        for (var i = start.Paragraph; i <= end.Paragraph; i++)
        {
            var edited = editor.Document.Paragraphs[i].Clone();
            var first = edited.SplitRunAt(i == start.Paragraph ? start.Offset : 0);
            var last = edited.SplitRunAt(i == end.Paragraph ? end.Offset : edited.Length);
            for (var r = first; r < last; r++)
                if (!IsEmbedded(edited.Runs[r]) && !GdrDiscussion.IsMarker(edited.Runs[r])) targets.Add(edited.Runs[r]);
            delta.Before.Add(editor.Document.Paragraphs[i].Clone());
            delta.After.Add(edited);
        }
        if (targets.Count == 0) return false;
        var change = plan(targets);
        foreach (var run in targets) change(run);
        foreach (var paragraph in delta.After) paragraph.Normalize();
        return Push(delta, start, end);
    }

    // Records the change as one step on the session's own undo stack, beside the steps typing commits.
    private bool Push(EditDelta delta, TextPosition? anchor = null, TextPosition? caret = null)
    {
        var editor = _field.Editor;
        var (keptAnchor, keptCaret) = (anchor ?? editor.Anchor, caret ?? editor.Caret);
        editor.CloseUndoGroup();
        editor.Apply(delta);
        _done.Add([delta]);
        _stepOpen = false;
        _undone.Clear();
        editor.SetCaret(keptAnchor);
        editor.SetCaret(keptCaret, extend: true);
        RestartPause();
        return true;
    }

    /// <summary>
    /// After Apply has written the edit, the project holds this text: it becomes the starting text, nothing is pending,
    /// and the field's undo history is cleared, because undo now belongs to the Proposal's history.
    /// </summary>
    public void Applied()
    {
        _pause.Stop();
        _field.Editor.CloseUndoGroup();
        _done.Clear();
        _undone.Clear();
        _starting = Copy(_field.Editor.Document);
        Pending = null;
    }

    private void OnCommitted(EditDelta delta)
    {
        if (delta.StartsUndoGroup || !_stepOpen || _done.Count == 0)
        {
            _done.Add([]);
            _stepOpen = delta.UndoGroup != 0;
        }
        _done[^1].Add(delta);
        _undone.Clear();
        RestartPause();
    }

    private void RestartPause()
    {
        _pause.Stop();
        _pause.Start();
    }

    private GdrDiscussionEdit? Measure()
    {
        var paragraphs = _field.Editor.Document.Paragraphs;
        var starting = _starting.Where(paragraph => paragraph.Key is string).ToDictionary(paragraph => (string)paragraph.Key!);
        var changed = new List<GdrParagraphChange>();
        var kept = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < paragraphs.Count; i++)
        {
            var paragraph = paragraphs[i];
            var key = paragraph.Key as string;
            if (key is not null && starting.TryGetValue(key, out var before) && kept.Add(key) && Same(before, paragraph)) continue;
            changed.Add(new GdrParagraphChange(i, key is not null && kept.Contains(key) ? key : null, paragraph.Clone()));
        }
        var removed = starting.Keys.Where(key => !kept.Contains(key)).ToArray();
        if (changed.Count == 0 && removed.Length == 0) return null;
        var startingText = changed.Where(change => change.Key is not null).Select(change => change.Key!).Concat(removed)
            .ToDictionary(key => key, key => GdrDiscussion.ProjectText(starting[key]), StringComparer.Ordinal);
        return new GdrDiscussionEdit(_recordId, paragraphs.Select(paragraph => paragraph.Key as string).ToArray(),
            changed, removed, startingText);
    }

    // Formatting counts as much as text: bolding a run or changing a paragraph's direction is an edit.
    private static bool Same(Paragraph before, Paragraph after) =>
        before.Text == after.Text && before.Style == after.Style && before.Rtl == after.Rtl &&
        before.Props.ContentEquals(after.Props) && before.Runs.Count == after.Runs.Count &&
        before.Runs.Zip(after.Runs).All(pair => pair.First.Text == pair.Second.Text && pair.First.Ws == pair.Second.Ws &&
            pair.First.ObjData == pair.Second.ObjData && pair.First.Props.ContentEquals(pair.Second.Props));

    private static string? Describe(GdrDiscussionEdit? edit) => edit is null ? null : string.Join("\u0001",
        edit.Order.Select(key => key ?? "+").Concat(edit.Changed.Select(change =>
            $"{change.Index}:{change.Key}:{change.Content.Style}:{change.Content.Rtl}:{Describe(change.Content.Props)}:" +
            string.Join("\u0002", change.Content.Runs.Select(run =>
                run.Ws + "\u0003" + run.Text + "\u0003" + run.ObjData + "\u0003" + Describe(run.Props))))).Concat(edit.Removed));

    private static string Describe(PropertyBag props) => string.Join(",",
        props.IntProps.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value.Variation}/{pair.Value.Value}")
            .Concat(props.StrProps.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}:{pair.Value}")));

    private static List<Paragraph> Copy(Document document) => document.Paragraphs.Select(paragraph => paragraph.Clone()).ToList();
}
