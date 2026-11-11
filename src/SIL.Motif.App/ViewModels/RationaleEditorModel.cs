using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private readonly string _proposalId;
    private readonly KnownPaths _paths = new();
    private string _markdown;

    /// <summary>The Rationale of <paramref name="detail"/>, editable when its Proposal is still open.</summary>
    public RationaleEditorModel(WorkspaceContext context, ProposalDetailProjection detail)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(detail);
        _context = context;
        _proposalId = detail.ProposalId;
        _markdown = detail.Rationale?.Markdown ?? string.Empty;
        CanEdit = detail.DraftName is not null || !IsDecided(detail.Status);
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
    [NotifyPropertyChangedFor(nameof(CanStartEdit))]
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(SaveCommand), nameof(CancelCommand), nameof(RestoreCommand))]
    private bool _isEditing;

    /// <summary>Whether a save is under way.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(CancelCommand))]
    private bool _isSaving;

    /// <summary>What a reader would see as wrong in the last save, one per line with its line and column.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFindings))]
    private IReadOnlyList<string> _findings = [];

    /// <summary>Why the last save was refused, if it was.</summary>
    [ObservableProperty]
    private WindowRefusal? _refusal;

    /// <summary>The kept earlier versions, newest first, once read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRevisions))]
    private IReadOnlyList<RationaleRevisionChoice> _revisions = [];

    /// <summary>Whether the Proposal is open, so its Rationale may be edited.</summary>
    public bool CanEdit { get; }

    /// <summary>Whether Edit is offered now.</summary>
    public bool CanStartEdit => CanEdit && !IsEditing;

    /// <summary>Whether the last save had findings to show.</summary>
    public bool HasFindings => Findings.Count > 0;

    /// <summary>Whether there are earlier versions to restore.</summary>
    public bool HasRevisions => Revisions.Count > 0;

    /// <summary>Shows links as links; the style itself is not stored.</summary>
    public IStyleResolver Styles { get; } = new LinkStyles();

    /// <summary>The Rationale as it is stored.</summary>
    public string Markdown => _markdown;

    /// <summary>The latest save or version read, which tests await.</summary>
    public Task Working { get; private set; } = Task.CompletedTask;

    /// <summary>Makes the field editable and reads the earlier versions to restore.</summary>
    [RelayCommand(CanExecute = nameof(CanStartEdit))]
    private void Edit()
    {
        Document = Read(_markdown, out var plain);
        IsPlainText = plain;
        Findings = [];
        Refusal = null;
        IsEditing = true;
        Working = ReadRevisionsAsync();
    }

    private bool CanSaveOrCancel() => IsEditing && !IsSaving;

    /// <summary>Writes the field's text as the Rationale; on a refusal the text stays in the field.</summary>
    [RelayCommand(CanExecute = nameof(CanSaveOrCancel))]
    private Task Save() => Working = SaveAsync();

    /// <summary>Discards the edit and shows the stored Rationale again.</summary>
    [RelayCommand(CanExecute = nameof(CanSaveOrCancel))]
    private void Cancel()
    {
        Document = Read(_markdown, out var plain);
        IsPlainText = plain;
        Findings = [];
        Refusal = null;
        Revisions = [];
        IsEditing = false;
    }

    /// <summary>Puts an earlier version into the field; it is saved only when the person saves.</summary>
    [RelayCommand(CanExecute = nameof(IsEditing))]
    private void Restore(RationaleRevisionChoice? revision)
    {
        if (revision is null) return;
        Document = Read(revision.Markdown, out var plain);
        IsPlainText = plain;
        Findings = [];
        Refusal = null;
    }

    /// <summary>The Rationale's text as the field now holds it, as it would be saved.</summary>
    public string Text() => IsPlainText
        ? string.Join('\n', Document.Paragraphs.Select(paragraph => string.Concat(paragraph.Runs.Select(run => run.Text))))
        : MarkdownConverter.ToMarkdown(Document, Options()).TrimEnd('\n');

    /// <summary>The link a run of the field opens, or null when it is not a link.</summary>
    public static Uri? LinkOf(string? objData)
    {
        if (objData is not { Length: > 1 } || objData[0] != DefaultObjectMapper.ExternalPathNameType) return null;
        return Uri.TryCreate(objData[1..], UriKind.Absolute, out var uri) ? uri : null;
    }

    /// <summary>Opens what a link run names: an object of the project in FieldWorks, or a web page.</summary>
    public Task<bool> OpenLinkAsync(string? objData) =>
        LinkOf(objData) is { } uri && _context.UriLauncher is { } launcher ? launcher.LaunchAsync(uri) : Task.FromResult(false);

    private async Task SaveAsync()
    {
        if (_context.ProjectPath is not { } path) return;
        var text = Text();
        IsSaving = true;
        try
        {
            var saved = await _context.Commands.SetRationaleAsync(
                new SetRationaleRequest(path, MotifProductVersion.CurrentText, _proposalId, text), CancellationToken.None)
                .ConfigureAwait(true);
            if (!saved.Succeeded)
            {
                Refusal = WindowRefusal.From(saved.Refusal!);
                Findings = FindingsOf(saved.Refusal!);
                return;
            }
            _markdown = text;
            OnPropertyChanged(nameof(Markdown));
            Refusal = null;
            Findings = saved.Value!.Findings.Select(Describe).ToArray();
            Revisions = [];
            Document = Read(_markdown, out var plain);
            IsPlainText = plain;
            IsEditing = false;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task ReadRevisionsAsync()
    {
        if (_context.ProjectPath is not { } path) return;
        var read = await _context.Commands.ListRationaleRevisionsAsync(
            new RationaleRevisionsRequest(path, MotifProductVersion.CurrentText, _proposalId), CancellationToken.None)
            .ConfigureAwait(true);
        if (!read.Succeeded || !IsEditing) return;
        Revisions = read.Value!.Where(revision => revision.Markdown != _markdown)
            .Select(revision => new RationaleRevisionChoice(revision)).ToArray();
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
    private MarkdownOptions Options() => new()
    {
        WriteFrontMatter = false,
        UnknownGuids = UnknownGuidPolicy.MarkBroken,
        StyleNames = MotifMarkdownStyles.Names(),
        Paths = _paths,
    };

    private TextDocument Read(string markdown, out bool plainText)
    {
        var withoutPaths = Options();
        withoutPaths.Paths = null;
        foreach (var link in MarkdownConverter.Parse(markdown, withoutPaths).Objects)
            if (link is { Guid: { } guid, Path: { } spelled } && ProjectPath.Parse(spelled) is { } path) _paths.Add(guid, path);
        var parsed = MarkdownConverter.Parse(markdown, Options());
        plainText = parsed.HasErrors;
        if (!plainText) return parsed.Document;
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

/// <summary>An earlier version of a Rationale offered to restore, named by when it was saved and who wrote it.</summary>
public sealed class RationaleRevisionChoice(RationaleRevisionView revision)
{
    /// <summary>The version's text.</summary>
    public string Markdown { get; } = revision.Markdown;

    /// <summary>When it was saved and by whom, as the list shows it.</summary>
    public string Label { get; } = string.Format(CultureInfo.CurrentCulture, MotifAppStrings.RationaleRevisionFormat,
        DateTimeOffset.TryParse(revision.CreatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when)
            ? when.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : revision.CreatedUtc,
        revision.AuthorLabel ?? (revision.PersonName.Length > 0 ? revision.PersonName : MotifAppStrings.RationaleRevisionAPerson));
}
