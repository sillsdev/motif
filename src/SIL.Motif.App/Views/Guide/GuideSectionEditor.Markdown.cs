using Avalonia.Controls;
using Avalonia.Layout;
using SIL.Motif.App.GuideQueries;
using SIL.Motif.App.Preview;
using SIL.Motif.App.Services;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;
using SIL.TextEditing;
using SIL.TextEditing.Avalonia;

namespace SIL.Motif.App.Views.Guide;

public sealed partial class GuideSectionEditor
{
    private GdrMarkdownDocument? _markdownDocument;
    private GdrShowResponse? _markdownStart;
    private TextBox? _markdownBox;
    private readonly Dictionary<(string Field, string Ws), TextRichStringPayload?> _markdownPending = new();
    private TextParagraphsPayload? _markdownPendingParagraphs;
    private GdrMarkdownEdits? _markdownUndo;
    private bool _markdownSaving;
    private bool _markdownOpening;

    private bool MarkdownDirty => _markdownDocument is not null && MarkdownText != _markdownDocument.Text;

    /// <summary>The whole-record Markdown being edited, or empty while the field view is shown.</summary>
    public string MarkdownText
    {
        get => _markdownBox?.Text ?? "";
        set { if (_markdownBox is not null) _markdownBox.Text = value; }
    }

    /// <summary>Whether the whole-record Markdown view is open.</summary>
    public bool IsMarkdownOpen => _markdownDocument is not null;

    /// <summary>Opens the shown record as Markdown, after returning any single field to rich text.</summary>
    public async Task<bool> OpenMarkdownAsync()
    {
        if (!_isEditing || _markdownOpening || _shownId is not { } id || _store is not IGdrMarkdownEditStore store || _markdownDocument is not null) return false;
        _markdownOpening = true;
        try
        {
            var opened = _opened[id];
            if (!PreviewSourceMode.ReturnToRichText([opened.Field], _status)) return false;
            opened.Session.EndSession();
            await _staging;
            if (_shownId != id) return false;
            if (_refused.TryGetValue(id, out var refusal)) { _status.Text = refusal; return false; }
            if (_locked.Contains(id) || await _show(id) is not { } shown) return false;
            _markdownPending.Clear();
            async Task<RichStringAlternative[]> Read(string field, IReadOnlyList<RichStringAlternative> alternatives)
            {
                var values = new List<RichStringAlternative>();
                foreach (var alternative in alternatives)
                {
                    var pending = await store.PendingRichStringAsync(id, field, alternative.WritingSystem);
                    if (pending.Refusal is not null) throw new InvalidOperationException(pending.Refusal);
                    _markdownPending[(field, alternative.WritingSystem)] = pending.RichString;
                    values.Add(pending.RichString is { } edit ? Alternative(alternative.WritingSystem, edit.Runs) : alternative);
                }
                return values.ToArray();
            }
            try
            {
                var title = await Read("CmPossibility.Name", shown.TitleAlternatives);
                var description = await Read("CmPossibility.Description", shown.DescriptionAlternatives);
                var paragraphs = await _store.PendingAsync(id);
                if (paragraphs.Refusal is not null) throw new InvalidOperationException(paragraphs.Refusal);
                _markdownPendingParagraphs = paragraphs.Paragraphs;
                _markdownStart = shown;
                var withPending = shown with { TitleAlternatives = title, DescriptionAlternatives = description };
                _markdownDocument = new GdrMarkdownDocument(withPending, _projectName, opened.Field.Editor.Document.Paragraphs);
            }
            catch (InvalidOperationException ex) { _status.Text = ex.Message; return false; }
            _markdownBox = new TextBox { AcceptsReturn = true, Text = _markdownDocument.Text, TextWrapping = Avalonia.Media.TextWrapping.NoWrap, Classes = { "guideMarkdown" } };
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Classes = { "guideBar" } };
            Button Button(string title, Action action)
            {
                var button = new Button { Content = title };
                button.Click += (_, _) => action();
                return button;
            }
            bar.Children.Add(Button(MotifAppStrings.GuideMarkdownSave, async () => await SaveMarkdownAsync()));
            bar.Children.Add(Button(MotifAppStrings.GuideMarkdownBack, () => BackToFields()));
            bar.Children.Add(Button(MotifAppStrings.GuideMarkdownThrowAway, DiscardMarkdown));
            bar.Children.Add(Button(MotifAppStrings.GuideMarkdownReload, async () => await ReloadMarkdownAsync()));
            bar.Children.Add(Button(MotifAppStrings.GuideMarkdownCopy, async () =>
            {
                if (TopLevel.GetTopLevel(this) is { } top) await new AvaloniaClipboard(top).SetTextAsync(MarkdownText);
            }));
            var panel = new DockPanel();
            DockPanel.SetDock(bar, Dock.Top);
            panel.Children.Add(bar);
            panel.Children.Add(_markdownBox);
            _fieldHost.Content = panel;
            ShowToolbar();
            _status.Text = MotifAppStrings.GuideEditingAsMarkdown;
            return true;
        }
        finally { _markdownOpening = false; }
    }

    /// <summary>Saves all changed fields to one Draft, or keeps the Markdown and shows the findings.</summary>
    public async Task<bool> SaveMarkdownAsync()
    {
        if (_markdownSaving || _markdownDocument is not { } document || _markdownStart is not { } start || _shownId is not { } id ||
            _store is not IGdrMarkdownEditStore store) return false;
        var read = document.Read(MarkdownText);
        if (read.HasErrors) { _status.Text = string.Join(" ", read.Errors.Concat(read.Warnings)); return false; }
        if (read.Changes.Count == 0) { DiscardMarkdown(); _status.Text = string.Join(" ", read.Warnings); return true; }
        return await StageReadAsync(read, start, id, store);
    }

    private async Task<bool> StageReadAsync(GdrMarkdownReading read, GdrShowResponse start, string id, IGdrMarkdownEditStore store)
    {
        var edits = new List<TextRichStringPayload>();
        var undo = new List<TextRichStringPayload>();
        TextParagraphsPayload? paragraphs = null, undoParagraphs = null;
        foreach (var change in read.Changes)
        {
            if (change.Field == "discussion")
            {
                var discussion = GdrDiscussion.From(start);
                var field = new RichTextField(discussion.Document);
                var session = new GdrDiscussionSession(field, id);
                var form = new DiscussionSourceForm(discussion, _projectName);
                field.Editor.ReplaceParagraphs(change.Paragraphs, form.AreEquivalent, form.Fingerprint, MotifAppStrings.GuideEditAsMarkdown);
                session.EndSession();
                paragraphs = session.Pending is { } pending ? GdrDiscussionPayload.From(pending, discussion, _markdownPendingParagraphs?.Expected)
                    : EmptyParagraphs(start);
                undoParagraphs = _markdownPendingParagraphs ?? EmptyParagraphs(start);
                continue;
            }
            var fieldName = change.Field == "title" ? "CmPossibility.Name" : "CmPossibility.Description";
            var ws = change.WritingSystem!;
            var alternative = (change.Field == "title" ? start.TitleAlternatives : start.DescriptionAlternatives)
                .Single(item => item.WritingSystem == ws);
            var previous = _markdownPending.GetValueOrDefault((fieldName, ws));
            var expected = previous?.Expected ?? new TextRichStringExpected(alternative.Text, alternative.Digest);
            edits.Add(new TextRichStringPayload(fieldName, ws, change.Runs!, expected));
            undo.Add(previous ?? new TextRichStringPayload(fieldName, ws, Carried(alternative), expected));
        }
        var batch = new GdrMarkdownEdits(id, edits, paragraphs,
            new GdrMarkdownIdentity(start.Record.Abbreviation, start.Record.ParentId, start.Position));
        _markdownSaving = true;
        if (_markdownBox is not null) _markdownBox.IsReadOnly = true;
        GdrMarkdownStageResult result;
        try { result = await store.StageMarkdownAsync(batch); }
        finally
        {
            _markdownSaving = false;
            if (_markdownBox is not null) _markdownBox.IsReadOnly = false;
        }
        if (result.Refusal is { } error) { _status.Text = error + MotifAppStrings.GuideMarkdownReloadOrCopy; return false; }
        _markdownUndo = new GdrMarkdownEdits(id, undo, undoParagraphs);
        _staged.Add(id);
        DiscardMarkdown();
        _opened.Remove(id);
        await ShowAsync(id);
        _status.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.GuideMarkdownSavedFormat, result.Count, string.Join(" ", read.Warnings));
        return true;
    }

    /// <summary>
    /// Replaces the body of the query block <paramref name="queryId"/> in the shown section's Discussion, one paragraph to a
    /// line, and stages the section as a Markdown save does; returns why nothing was staged, or null.
    /// </summary>
    public async Task<string?> ReplaceQueryBodyAsync(string queryId, string body)
    {
        ArgumentNullException.ThrowIfNull(queryId);
        ArgumentNullException.ThrowIfNull(body);
        if (_markdownDocument is not null || _markdownSaving || _markdownOpening) return MotifAppStrings.GuideQueryCloseMarkdownFirst;
        var wasEditing = _isEditing;
        _isEditing = true;
        try
        {
            if (!await OpenMarkdownAsync()) return _status.Text is { Length: > 0 } text ? text : MotifAppStrings.GuideQuerySaveUnavailable;
            if (_markdownDocument is not { } document || _markdownStart is not { } start || _shownId is not { } id ||
                _store is not IGdrMarkdownEditStore store) return MotifAppStrings.GuideQuerySaveUnavailable;
            var paragraphs = document.DiscussionParagraphs.ToList();
            var found = GuideQueryDocument.From(paragraphs.Select(paragraph => paragraph.Text).ToArray());
            var query = found.Blocks.Queries.FirstOrDefault(item => item.Id == queryId);
            if (query is null || !found.TryParagraphsOf(query.Span, out var first, out var last))
            {
                DiscardMarkdown();
                return query is null ? MotifAppStrings.GuideQueryBlockGone : MotifAppStrings.GuideQueryBlockShared;
            }
            var lines = found.WithBody(query.Span, body).Split('\n');
            var replaced = new List<Paragraph>();
            for (var i = 0; i < lines.Length; i++)
            {
                var source = paragraphs[Math.Min(first + i, last)];
                var line = source.Clone();
                if (first + i > last) line.Key = null;
                if (line.Text != lines[i])
                {
                    var run = line.Runs.FirstOrDefault()?.CloneWithText(lines[i]) ?? new Run { Text = lines[i], Ws = line.DefaultWs };
                    line.Runs.Clear();
                    line.Runs.Add(run);
                }
                replaced.Add(line);
            }
            paragraphs.RemoveRange(first, last - first + 1);
            paragraphs.InsertRange(first, replaced);
            var read = document.ReadDiscussion(paragraphs);
            if (read.Changes.Count == 0) { DiscardMarkdown(); return null; }
            if (await StageReadAsync(read, start, id, store)) return null;
            var message = _status.Text;
            DiscardMarkdown();
            return message;
        }
        finally
        {
            _isEditing = wasEditing;
            ShowToolbar();
        }
    }

    /// <summary>Undoes the whole most recent Markdown save as one Draft update.</summary>
    public async Task<bool> UndoMarkdownAsync()
    {
        if (_markdownUndo is not { } undo || _shownId != undo.Target || _store is not IGdrMarkdownEditStore store ||
            _markdownDocument is not null || Session?.UndoSteps > 0) return false;
        await Loading;
        Session?.EndSession();
        await _staging;
        var result = await store.StageMarkdownAsync(undo);
        if (result.Refusal is { } error) { _status.Text = error; return false; }
        _markdownUndo = null;
        _opened.Remove(undo.Target);
        if (_shownId == undo.Target) await ShowAsync(undo.Target);
        _status.Text = MotifAppStrings.GuideMarkdownUndone;
        return true;
    }

    /// <summary>Returns to the field view if no unsaved Markdown would be lost.</summary>
    public bool BackToFields()
    {
        if (_markdownDocument is null) return true;
        if (_markdownSaving || _markdownOpening) { _status.Text = MotifAppStrings.GuideMarkdownWait; return false; }
        if (MarkdownDirty) { _status.Text = MotifAppStrings.GuideMarkdownSaveFirstLeaving; return false; }
        DiscardMarkdown();
        return true;
    }

    /// <summary>Throws away the Markdown changes and returns to the field view.</summary>
    public void DiscardMarkdown()
    {
        if (_markdownSaving || _markdownOpening) return;
        _markdownDocument = null;
        _markdownBox = null;
        _markdownStart = null;
        if (_shownId is { } id && _opened.TryGetValue(id, out var opened)) _fieldHost.Content = opened.Field;
        ShowToolbar();
    }

    /// <summary>Refreshes the project and opens the record again, discarding the current Markdown.</summary>
    public async Task<bool> ReloadMarkdownAsync()
    {
        if (_markdownSaving || _markdownOpening || _shownId is not { } id || _store is not IGdrMarkdownEditStore store) return false;
        _markdownOpening = true;
        if (_markdownBox is not null) _markdownBox.IsReadOnly = true;
        string? error;
        try { error = await store.RefreshAsync(); }
        finally
        {
            _markdownOpening = false;
            if (_markdownBox is not null) _markdownBox.IsReadOnly = false;
        }
        if (error is not null) { _status.Text = error; return false; }
        DiscardMarkdown();
        _opened.Remove(id);
        await ShowAsync(id);
        await Loading;
        return await OpenMarkdownAsync();
    }

    private static TextParagraphsPayload EmptyParagraphs(GdrShowResponse shown) => new(GdrDiscussionPayload.Field,
        shown.Discussion.Select(item => item.Key).ToArray(), [], [], new Dictionary<string, TextParagraphExpected>());

    private static TextRun[] Carried(RichStringAlternative alternative) => alternative.Runs.Select(run => new TextRun(run.Text,
        run.WritingSystem, run.Style, new TextProps(run.Properties.Ints.ToDictionary(item => item.Key,
            item => (item.Value.Variation, item.Value.Value)), run.Properties.Strings.Where(item => item.Key != TextPropertyNumbers.NamedStyle)
            .ToDictionary(item => item.Key, item => item.Value)), run.ObjData)).ToArray();

    private static RichStringAlternative Alternative(string ws, IReadOnlyList<TextRun> runs) => new(ws, runs.Select(run =>
    {
        var ints = (run.Props?.Ints ?? new Dictionary<int, (int, int)>()).ToDictionary(item => item.Key,
            item => new GdrIntProperty(item.Value.Item1, item.Value.Item2));
        var strings = (run.Props?.Strings ?? new Dictionary<int, string>()).ToDictionary(item => item.Key, item => item.Value);
        if (run.CharacterStyle is { } style) strings[TextPropertyNumbers.NamedStyle] = style;
        return new GdrRun(run.Text, run.Ws, run.CharacterStyle, new GdrTextProperties(ints, strings), run.ObjData);
    }).ToArray());
}
