using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Compare;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.RichText;
using SIL.Motif.Projection.Store;
using SIL.TextEditing;
using SIL.TextEditing.Markdown;
using TextDocument = SIL.TextEditing.Document;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// A Proposal's Rationale in the shared rich field (ADR 0099): shown read-only, and for an open Proposal made editable
/// with Edit, then saved through the rationale command or cancelled. A save with findings shows them under the
/// field; when a Ready Proposal does not take it, the person's text stays in the field. The kept versions can be
/// restored into the field until the Proposal is decided. A Rationale that does not read as FieldWorks Markdown is
/// shown as plain text.
/// </summary>
public sealed partial class RationaleEditorModel : ObservableObject
{
    /// <summary>The character style a link is read with.</summary>
    public const string HyperlinkStyle = "Hyperlink";

    private readonly WorkspaceContext _context;
    // Unmarked text reads as a tag no project has, so an explicit ws= is always written back.
    private const string UnmarkedAnalysis = "qaa-x-unmarked";
    private const string UnmarkedVernacular = "qaa-x-unmarked-v";

    private readonly KnownPaths _paths = new();
    private string _markdown;
    private string? _began;
    private readonly List<RationaleRevisionChoice> _unsavedVersions = [];
    private bool _isComment;
    private long? _commentId;
    private Action<RationaleEditorModel>? _deleted;
    private Action<RationaleEditorModel>? _afterCancel;
    private bool _commentUnavailable;
    private IReadOnlyList<string> _storedCommentFindings = [];

    internal event Action? EditFinished;

    /// <summary>Creates a Comment editor using the same rich field as the Rationale.</summary>
    public RationaleEditorModel(WorkspaceContext context, ProposalDetailProjection detail, CommentView? comment,
        Action<RationaleEditorModel> deleted) : this(context, detail)
    {
        _isComment = true;
        _commentId = comment?.CommentId;
        _deleted = deleted;
        Author = comment?.AuthorLabel ?? (string.IsNullOrEmpty(comment?.PersonName) ? MotifAppStrings.RationaleRevisionAPerson : comment.PersonName);
        _markdown = comment?.Markdown ?? string.Empty;
        _document = Read(_markdown, out _isPlainText);
        Findings = _storedCommentFindings = (comment?.Findings ?? ProposalTextChecker.Check(_markdown, null, null).Findings)
            .Select(Describe).ToArray();
        if (comment is null && CanEdit) Edit();
    }

    /// <summary>Whether this field holds a Comment.</summary>
    public bool IsComment => _isComment;
    internal long? CommentId => _commentId;

    internal void ReconcileComment(CommentView? current, ProposalDetailProjection detail, Action<RationaleEditorModel> afterCancel)
    {
        CanEdit = !IsDecided(detail.Status);
        _afterCancel = afterCancel;
        _commentUnavailable = (current is null && _commentId.HasValue) ||
            (current is not null && current.Markdown != _markdown) ||
            IsDecided(detail.Status);
        IsChangedElsewhere = IsEditing && _commentUnavailable;
        SaveCommand.NotifyCanExecuteChanged();
    }
    /// <summary>The Comment's displayed author.</summary>
    public string Author { get; } = string.Empty;
    /// <summary>Whether a stored Comment can be deleted.</summary>
    public bool CanDeleteComment => _isComment && _commentId.HasValue && CanEdit && !IsEditing;
    /// <summary>Why the field is shown as plain text.</summary>
    public string PlainTextNotice => _isComment ? MotifAppStrings.CommentPlainText : MotifAppStrings.RationalePlainText;
    /// <summary>The warning shown when another Proposal cannot be selected.</summary>
    public string SwitchWarning => _isComment ? MotifAppStrings.CommentSaveOrCancelFirst : MotifAppStrings.RationaleSaveOrCancelFirst;
    /// <summary>Why an edit cannot be saved after the stored text or Proposal changed.</summary>
    public string ChangedElsewhereWarning => _isComment ? MotifAppStrings.CommentChangedElsewhere : MotifAppStrings.RationaleChangedElsewhere;

    /// <summary>Deletes this Comment and its attachments.</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteComment))]
    private Task DeleteComment() => Working = DeleteCommentAsync();

    private async Task DeleteCommentAsync()
    {
        if (_context.ProjectPath is not { } path || _commentId is not { } id) return;
        var result = await _context.Commands.DeleteCommentAsync(new DeleteCommentRequest(path, MotifProductVersion.CurrentText, ProposalId, id), CancellationToken.None).ConfigureAwait(true);
        if (result.Succeeded) _deleted?.Invoke(this);
        else Refusal = WindowRefusal.From(result.Refusal!);
    }

    // The text the field was read from, its blank-line-separated blocks and the paragraphs they read as.
    private string _source = string.Empty;
    private IReadOnlyList<SourceBlock> _blocks = [];
    private IReadOnlyList<Paragraph> _read = [];

    /// <summary>The Rationale of <paramref name="detail"/>, editable when its Proposal is still open.</summary>
    public RationaleEditorModel(WorkspaceContext context, ProposalDetailProjection detail)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(detail);
        _context = context;
        ProposalId = detail.ProposalId;
        _markdown = detail.Rationale?.Markdown ?? string.Empty;
        CanEdit = !IsDecided(detail.Status);
        _document = Read(_markdown, out _isPlainText);
    }

    /// <summary>The text the field shows and edits; replaced on Edit, Cancel and Restore.</summary>
    [ObservableProperty]
    private TextDocument _document;

    /// <summary>Whether the text is shown and edited as plain text, as it did not read.</summary>
    [ObservableProperty]
    private bool _isPlainText;

    /// <summary>Whether the field is being edited.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCompare))]
    [NotifyPropertyChangedFor(nameof(CanStartEdit))]
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(SaveCommand), nameof(CancelCommand), nameof(RestoreCommand), nameof(DeleteCommentCommand))]
    [NotifyPropertyChangedFor(nameof(CanDeleteComment))]
    private bool _isEditing;

    /// <summary>Whether a save is under way.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCompare))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(CancelCommand), nameof(RestoreCommand))]
    private bool _isSaving;

    /// <summary>What a reader would see as wrong in the last save, one per line with its line and column.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFindings))]
    private IReadOnlyList<string> _findings = [];

    /// <summary>Why the last save was refused, if it was.</summary>
    [ObservableProperty]
    private WindowRefusal? _refusal;

    /// <summary>Whether the stored Rationale was changed elsewhere while this edit was being made.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCompare))]
    [NotifyPropertyChangedFor(nameof(IsRationaleChangedElsewhere))]
    private bool _isChangedElsewhere;

    /// <summary>Whether choosing another Proposal was refused because this edit is not saved.</summary>
    [ObservableProperty]
    private bool _isSwitchRefused;

    /// <summary>The local unsaved edits kept for Restore, then the saved earlier versions, newest first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRevisions))]
    private IReadOnlyList<RationaleRevisionChoice> _revisions = [];

    /// <summary>The Proposal whose Rationale this is.</summary>
    public string ProposalId { get; }

    private bool _canEdit;

    /// <summary>Whether the Proposal is open, so its Rationale may be edited.</summary>
    public bool CanEdit
    {
        get => _canEdit;
        private set
        {
            if (!SetProperty(ref _canEdit, value)) return;
            OnPropertyChanged(nameof(CanStartEdit));
            OnPropertyChanged(nameof(CanDeleteComment));
            OnPropertyChanged(nameof(CanCompare));
            EditCommand.NotifyCanExecuteChanged();
            SaveCommand.NotifyCanExecuteChanged();
            RestoreCommand.NotifyCanExecuteChanged();
            DeleteCommentCommand.NotifyCanExecuteChanged();
        }
    }

    internal void ReconcileRationale(ProposalDetailProjection detail)
    {
        CanEdit = !IsDecided(detail.Status);
        if (_markdown != (detail.Rationale?.Markdown ?? string.Empty) || !CanEdit) MarkChangedElsewhere();
        SaveCommand.NotifyCanExecuteChanged();
    }

    internal void Freeze() => CanEdit = false;

    /// <summary>Whether Edit is offered now.</summary>
    public bool CanStartEdit => CanEdit && !IsEditing;

    /// <summary>Whether the unsaved text can be compared with the Rationale that changed elsewhere.</summary>
    public bool CanCompare => CanEdit && IsEditing && IsRationaleChangedElsewhere && !IsSaving;

    /// <summary>Whether the stale edit is a Rationale, which offers comparison rather than Comment reconciliation.</summary>
    public bool IsRationaleChangedElsewhere => !_isComment && IsChangedElsewhere;

    /// <summary>Whether the last save had findings to show.</summary>
    public bool HasFindings => Findings.Count > 0;

    /// <summary>Whether there are earlier versions to restore.</summary>
    public bool HasRevisions => Revisions.Count > 0;

    /// <summary>Shows links as links; the style itself is not stored.</summary>
    public IStyleResolver Styles { get; } = new LinkStyles();

    /// <summary>The Rationale as it is stored.</summary>
    public string Markdown => _markdown;

    /// <summary>Whether this editing session holds unsaved text, including an edit kept for Restore.</summary>
    public bool HasUnsavedChanges => IsEditing && (Text() != _began || _unsavedVersions.Count > 0);

    /// <summary>The latest save, reload or version read, which tests await.</summary>
    public Task Working { get; private set; } = Task.CompletedTask;

    /// <summary>Makes the field editable and reads the earlier versions to restore.</summary>
    [RelayCommand(CanExecute = nameof(CanStartEdit))]
    private void Edit()
    {
        Document = Read(_markdown, out var plain);
        IsPlainText = plain;
        Findings = [];
        Refusal = null;
        IsChangedElsewhere = false;
        IsSwitchRefused = false;
        IsEditing = true;
        _began = Text();
        _unsavedVersions.Clear();
        if (!_isComment) Working = ReadRevisionsAsync();
    }

    private bool CanSaveOrCancel() => IsEditing && !IsSaving;
    private bool CanSave() => CanEdit && CanSaveOrCancel() && !_commentUnavailable;
    private bool CanRestore() => CanEdit && CanSaveOrCancel();

    /// <summary>Writes the field's text as the Rationale; on a refusal the text stays in the field.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task Save() => Working = SaveAsync();

    /// <summary>Compares with the latest attributed revision; Done saves with that revision as the expected text.</summary>
    public Task CompareAsync(Func<RationaleComparison, Task<CompareOutcome?>> show) => Working = CompareCoreAsync(show);

    private async Task CompareCoreAsync(Func<RationaleComparison, Task<CompareOutcome?>> show)
    {
        if (!CanCompare || _context.ProjectPath is not { } path) return;
        IsSaving = true;
        try
        {
            var read = await _context.Commands.ListRationaleRevisionsAsync(
                new RationaleRevisionsRequest(path, MotifProductVersion.CurrentText, ProposalId), CancellationToken.None)
                .ConfigureAwait(true);
            if (!read.Succeeded)
            {
                Refusal = WindowRefusal.From(read.Refusal!);
                return;
            }
            var latest = read.Value!.MaxBy(revision => revision.RevisionId);
            if (latest is null)
            {
                Refusal = WindowRefusal.Plain(MotifAppStrings.RationaleCompareReload);
                return;
            }
            var shown = await _context.Commands.ShowProposalAsync(
                new ShowProposalRequest(path, MotifProductVersion.CurrentText, ProposalId), CancellationToken.None)
                .ConfigureAwait(true);
            if (!shown.Succeeded)
            {
                Refusal = WindowRefusal.From(shown.Refusal!);
                return;
            }
            var mine = Text();
            if (latest.Markdown != (shown.Value!.Rationale?.Markdown ?? string.Empty) ||
                latest.Markdown == _markdown || latest.Markdown == mine)
            {
                Refusal = WindowRefusal.Plain(MotifAppStrings.RationaleCompareReload);
                return;
            }
            var comparison = new RationaleComparison(_markdown, mine, latest);
            var outcome = await show(comparison).ConfigureAwait(true);
            if (!CanEdit || outcome?.End is not (CompareEnd.Done or CompareEnd.StartAgain)) return;
            _markdown = latest.Markdown;
            OnPropertyChanged(nameof(Markdown));
            Document = Read(_markdown, out var plain);
            IsPlainText = plain;
            _began = Text();
            if (outcome.End == CompareEnd.StartAgain)
            {
                var kept = new RationaleRevisionChoice(mine, string.Format(CultureInfo.CurrentCulture,
                    MotifAppStrings.RationaleUnsavedRestoreFormat, _unsavedVersions.Count + 1));
                _unsavedVersions.Insert(0, kept);
                Revisions = [kept, .. Revisions];
                IsChangedElsewhere = false;
                Findings = [];
                Refusal = null;
                return;
            }
            var merged = RationaleComparison.Text(outcome.Result);
            if (merged == latest.Markdown)
            {
                IsChangedElsewhere = false;
                Cancel();
                return;
            }
            Document = Read(merged, out plain);
            IsPlainText = plain;
            await SaveAsync(compared: true).ConfigureAwait(true);
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>Discards the edit and shows the stored Rationale, read anew if it changed elsewhere.</summary>
    [RelayCommand(CanExecute = nameof(CanSaveOrCancel))]
    private void Cancel()
    {
        var changedElsewhere = IsChangedElsewhere;
        IsChangedElsewhere = false;
        IsSwitchRefused = false;
        Document = Read(_markdown, out var plain);
        IsPlainText = plain;
        Findings = _isComment ? _storedCommentFindings : [];
        Refusal = null;
        Revisions = [];
        _unsavedVersions.Clear();
        IsEditing = false;
        if (_isComment && !_commentId.HasValue) _deleted?.Invoke(this);
        else if (_isComment && _afterCancel is { } refresh) refresh(this);
        else if (changedElsewhere) Working = ReloadAsync();
        EditFinished?.Invoke();
    }

    /// <summary>Puts an earlier version into the field; it is saved only when the person saves.</summary>
    [RelayCommand(CanExecute = nameof(CanRestore))]
    private void Restore(RationaleRevisionChoice? revision)
    {
        if (revision is null) return;
        Document = Read(revision.Markdown, out var plain);
        IsPlainText = plain;
        Findings = [];
        Refusal = null;
    }

    /// <summary>
    /// The Rationale's text as the field now holds it, as it would be saved: every block of the text it was read from
    /// whose paragraphs are unchanged keeps its exact spelling, and only the changed ones are written anew.
    /// </summary>
    public string Text() => IsPlainText
        ? string.Join('\n', Document.Paragraphs.Select(paragraph => string.Concat(paragraph.Runs.Select(run => run.Text))))
        : Spliced(Document.Paragraphs);

    /// <summary>Says the stored Rationale changed while this edit was being made; the edit stays in the field.</summary>
    public void MarkChangedElsewhere() => IsChangedElsewhere = IsEditing;

    /// <summary>Says another Proposal was not chosen because this edit is not saved.</summary>
    public void MarkSwitchRefused() => IsSwitchRefused = true;

    /// <summary>The link a run of the field opens, or null when it is not a link.</summary>
    public static Uri? LinkOf(string? objData)
    {
        if (objData is not { Length: > 1 } || objData[0] != DefaultObjectMapper.ExternalPathNameType) return null;
        return Uri.TryCreate(objData[1..], UriKind.Absolute, out var uri) ? uri : null;
    }

    /// <summary>Opens what a link run names: an object of the project in FieldWorks, or a web page.</summary>
    public Task<bool> OpenLinkAsync(string? objData) =>
        LinkOf(objData) is { } uri && _context.UriLauncher is { } launcher ? launcher.LaunchAsync(uri) : Task.FromResult(false);

    private async Task SaveAsync(bool compared = false)
    {
        if (!CanEdit || _context.ProjectPath is not { } path) return;
        var text = Text();
        if (!compared && text == _began && !_isComment)
        {
            // Nothing was changed: the stored text stays exactly as it is, and no revision is written.
            Cancel();
            return;
        }
        IsSaving = true;
        try
        {
            if (_isComment)
            {
                var result = _commentId is { } id
                    ? await _context.Commands.EditCommentAsync(new EditCommentRequest(path, MotifProductVersion.CurrentText, ProposalId, id, text), CancellationToken.None).ConfigureAwait(true)
                    : await _context.Commands.AddCommentAsync(new AddCommentRequest(path, MotifProductVersion.CurrentText, ProposalId, text, ""), CancellationToken.None).ConfigureAwait(true);
                if (!result.Succeeded) { Refusal = WindowRefusal.From(result.Refusal!); return; }
                _commentId = result.Value!.CommentId;
                _afterCancel = null;
                _markdown = text;
                Findings = _storedCommentFindings = result.Value.Findings.Select(Describe).ToArray();
                Refusal = null;
                Document = Read(_markdown, out var commentPlain);
                IsPlainText = commentPlain;
                IsSwitchRefused = false;
                IsEditing = false;
                OnPropertyChanged(nameof(Markdown));
                OnPropertyChanged(nameof(CanDeleteComment));
                DeleteCommentCommand.NotifyCanExecuteChanged();
                return;
            }
            var saved = await _context.Commands.SetRationaleAsync(
                new SetRationaleRequest(path, MotifProductVersion.CurrentText, ProposalId, text, ExpectedRationale: _markdown),
                CancellationToken.None).ConfigureAwait(true);
            if (!saved.Succeeded)
            {
                IsChangedElsewhere = saved.Refusal!.Code == RefusalCodes.ProposalRationaleStale;
                Refusal = IsChangedElsewhere ? null : WindowRefusal.From(saved.Refusal!);
                Findings = FindingsOf(saved.Refusal!);
                return;
            }
            // The stored, maybe tidied, text is the next base; a save onto the base means nothing changed elsewhere.
            _markdown = saved.Value!.Text ?? text;
            OnPropertyChanged(nameof(Markdown));
            IsChangedElsewhere = false;
            Refusal = null;
            Findings = saved.Value!.Findings.Select(Describe).ToArray();
            Revisions = [];
            _unsavedVersions.Clear();
            Document = Read(_markdown, out var plain);
            IsPlainText = plain;
            IsEditing = false;
        }
        finally
        {
            IsSaving = false;
            if (!IsEditing) EditFinished?.Invoke();
        }
    }

    private async Task ReloadAsync()
    {
        if (_context.ProjectPath is not { } path) return;
        var shown = await _context.Commands.ShowProposalAsync(
            new ShowProposalRequest(path, MotifProductVersion.CurrentText, ProposalId), CancellationToken.None)
            .ConfigureAwait(true);
        if (!shown.Succeeded || IsEditing) return;
        _markdown = shown.Value!.Rationale?.Markdown ?? string.Empty;
        OnPropertyChanged(nameof(Markdown));
        Document = Read(_markdown, out var plain);
        IsPlainText = plain;
    }

    private async Task ReadRevisionsAsync()
    {
        if (_context.ProjectPath is not { } path) return;
        var read = await _context.Commands.ListRationaleRevisionsAsync(
            new RationaleRevisionsRequest(path, MotifProductVersion.CurrentText, ProposalId), CancellationToken.None)
            .ConfigureAwait(true);
        if (!read.Succeeded || !IsEditing) return;
        Revisions = [.. _unsavedVersions, .. read.Value!.Where(revision => revision.Markdown != _markdown)
            .Select(revision => new RationaleRevisionChoice(revision))];
    }

    private static IReadOnlyList<string> FindingsOf(Refusal refusal) =>
        refusal.Code == RefusalCodes.ProposalRationaleFindings && refusal.Facts.TryGetValue("findings", out var listed)
            ? listed.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            : [];

    private static string Describe(TextFinding finding) =>
        string.Format(CultureInfo.CurrentCulture, "{0}:{1} {2}", finding.Line, finding.Column, finding.Message);

    private static bool IsDecided(string status) => status is ManifestStatus.Applied or ManifestStatus.Rejected
        or ManifestStatus.Superseded or ManifestStatus.Withdrawn;

    // Read as the checker reads it with no project; a link is written back with the path it was read with.
    private MarkdownOptions Options() =>
        MotifMarkdownOptions.Create(new FixedWritingSystems(UnmarkedAnalysis, UnmarkedVernacular), paths: _paths);

    private TextDocument Read(string markdown, out bool plainText)
    {
        var withoutPaths = Options();
        withoutPaths.Paths = null;
        foreach (var link in MarkdownConverter.Parse(markdown, withoutPaths).Objects)
            if (link is { Guid: { } guid, Path: { } spelled } && ProjectPath.Parse(spelled) is { } path) _paths.Add(guid, path);
        var parsed = MarkdownConverter.Parse(markdown, Options());
        // Edited as written when the checker has findings or a block reads otherwise alone: a save could not keep it.
        plainText = parsed.HasErrors || !ProposalTextChecker.Check(markdown, null, null).IsClean || !Segment(markdown);
        if (!plainText)
        {
            // The rich field requires a paragraph even when an empty discussion is read-only.
            if (parsed.Document.Paragraphs.Count == 0)
                parsed.Document.Paragraphs.Add(new Paragraph
                {
                    Style = "Normal",
                    DefaultWs = parsed.DefaultAnalysis,
                    Runs = [new Run { Text = string.Empty, Ws = parsed.DefaultAnalysis }],
                });
            return parsed.Document;
        }
        var document = new TextDocument();
        foreach (var line in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            document.Paragraphs.Add(new Paragraph
            {
                Style = "Normal",
                DefaultWs = parsed.DefaultAnalysis,
                Runs = [new Run { Text = line, Ws = parsed.DefaultAnalysis }],
            });
        return document;
    }

    // Splits the text into blocks of lines between blank lines, each read alone; false when they read differently.
    private bool Segment(string markdown)
    {
        var read = MarkdownConverter.Parse(markdown, Options()).Document.Paragraphs;
        var blocks = new List<SourceBlock>();
        var at = 0;
        var paragraph = 0;
        while (at < markdown.Length)
        {
            var lineEnd = markdown.IndexOf('\n', at) is var end and >= 0 ? end : markdown.Length;
            if (markdown.AsSpan(at, lineEnd - at).IsWhiteSpace())
            {
                at = lineEnd + 1;
                continue;
            }
            var start = at;
            while (at < markdown.Length)
            {
                lineEnd = markdown.IndexOf('\n', at) is var next and >= 0 ? next : markdown.Length;
                if (markdown.AsSpan(at, lineEnd - at).IsWhiteSpace()) break;
                at = lineEnd + 1;
            }
            var stop = Math.Min(at, markdown.Length);
            while (stop > start && markdown[stop - 1] is '\n' or '\r') stop--;
            var alone = MarkdownConverter.Parse(markdown[start..stop], Options()).Document.Paragraphs;
            if (paragraph + alone.Count > read.Count ||
                !MarkdownConverter.AreEquivalent(alone, read.Skip(paragraph).Take(alone.Count).ToArray())) return false;
            blocks.Add(new SourceBlock(start, stop, paragraph, alone.Count));
            paragraph += alone.Count;
        }
        if (paragraph != read.Count) return false;
        _source = markdown;
        _blocks = blocks;
        _read = read;
        return true;
    }

    // Keeps every block whose paragraphs all reached the edited text unchanged and in order, and writes the rest.
    private string Spliced(IReadOnlyList<Paragraph> edited)
    {
        var match = Matched(_read, edited);
        var pieces = new System.Text.StringBuilder();
        var next = 0;
        SourceBlock? kept = null;
        foreach (var block in _blocks)
        {
            var first = match[block.First];
            var intact = first >= next && Enumerable.Range(0, block.Count).All(i => match[block.First + i] == first + i);
            if (!intact) continue;
            if (first > next)
            {
                Append(Written(edited.Skip(next).Take(first - next)));
                kept = null;
            }
            if (kept is { } previous && previous.First + previous.Count == block.First)
                pieces.Append(_source, previous.End, block.Start - previous.End);
            else if (pieces.Length > 0) pieces.Append("\n\n");
            pieces.Append(_source, block.Start, block.End - block.Start);
            kept = block;
            next = first + block.Count;
        }
        if (next < edited.Count) Append(Written(edited.Skip(next)));
        var result = pieces.ToString();
        // A splice that reads otherwise than the field is written whole, which is always right.
        return MarkdownConverter.AreEquivalent(edited, MarkdownConverter.Parse(result, Options()).Document.Paragraphs)
            ? result : Written(edited);

        void Append(string written)
        {
            if (written.Length == 0) return;
            if (pieces.Length > 0) pieces.Append("\n\n");
            pieces.Append(written);
        }
    }

    private string Written(IEnumerable<Paragraph> paragraphs) =>
        MarkdownConverter.ToMarkdown(paragraphs, Options()).TrimEnd('\n');

    // For each paragraph read, the index of the edited paragraph it stayed as, or -1: a longest common subsequence.
    private static int[] Matched(IReadOnlyList<Paragraph> read, IReadOnlyList<Paragraph> edited)
    {
        var lengths = new int[read.Count + 1, edited.Count + 1];
        for (var i = read.Count - 1; i >= 0; i--)
            for (var j = edited.Count - 1; j >= 0; j--)
                lengths[i, j] = MarkdownConverter.AreEquivalent(read[i], edited[j])
                    ? lengths[i + 1, j + 1] + 1 : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
        var match = Enumerable.Repeat(-1, read.Count).ToArray();
        for (int i = 0, j = 0; i < read.Count && j < edited.Count;)
        {
            if (MarkdownConverter.AreEquivalent(read[i], edited[j]) && lengths[i, j] == lengths[i + 1, j + 1] + 1) match[i++] = j++;
            else if (lengths[i + 1, j] >= lengths[i, j + 1]) i++;
            else j++;
        }
        return match;
    }

    private readonly record struct SourceBlock(int Start, int End, int First, int Count);

    // The paths the Rationale's links were written with: no project is open to say where an object is.
    private sealed class KnownPaths : IProjectPathResolver
    {
        private readonly Dictionary<Guid, ProjectPath> _byGuid = [];

        public void Add(Guid guid, ProjectPath path)
        {
            if (path.TopFolder != "objects") _byGuid[guid] = path;
        }

        public Guid? Resolve(ProjectPath path, out IReadOnlyList<Guid> candidates)
        {
            var matches = _byGuid.Where(pair => pair.Value.Equals(path)).Select(pair => pair.Key).ToArray();
            candidates = matches.Length > 1 ? matches : [];
            return matches.Length == 1 ? matches[0] : null;
        }

        public ProjectPath? PathOf(Guid guid) => _byGuid.GetValueOrDefault(guid);

        public string? ToolOf(Guid guid) => null;
    }

    private sealed class LinkStyles : IStyleResolver
    {
        private static readonly PropertyBag Link = LinkLook();

        public PropertyBag? Resolve(string styleName, string ws) => styleName == HyperlinkStyle ? Link : null;

        private static PropertyBag LinkLook()
        {
            var props = new PropertyBag();
            props.SetInt(TextPropertyNumbers.Underline, TextPropertyNumbers.UnderlineSingle, 0);
            props.SetInt(TextPropertyNumbers.ForeColor, 0xC06000, 0);
            return props;
        }
    }
}

/// <summary>A saved Rationale or a local unsaved edit offered to restore.</summary>
public sealed class RationaleRevisionChoice
{
    /// <summary>A saved Rationale, named by when it was saved and who wrote it.</summary>
    public RationaleRevisionChoice(RationaleRevisionView revision) : this(revision.Markdown,
        string.Format(CultureInfo.CurrentCulture, MotifAppStrings.RationaleRevisionFormat,
            DateTimeOffset.TryParse(revision.CreatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when)
                ? when.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : revision.CreatedUtc,
            revision.AuthorLabel ?? (revision.PersonName.Length > 0 ? revision.PersonName : MotifAppStrings.RationaleRevisionAPerson)))
    {
    }

    internal RationaleRevisionChoice(string markdown, string label)
    {
        Markdown = markdown;
        Label = label;
    }

    /// <summary>The version's text.</summary>
    public string Markdown { get; }

    /// <summary>The saved version's time and author, or the name of the local unsaved edit.</summary>
    public string Label { get; }
}
