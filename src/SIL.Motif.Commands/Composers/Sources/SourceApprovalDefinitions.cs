using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Resolution;
using SIL.Motif.Scripture;

namespace SIL.Motif.Commands;

/// <summary>Why a bulk Approve leaves an Occurrence alone.</summary>
internal enum OccurrenceSkip
{
    /// <summary>It already has this analysis approved.</summary>
    AlreadyApproved,

    /// <summary>A different analysis is approved there, and bulk never replaces an approval.</summary>
    ApprovedOtherwise,

    /// <summary>Its approval is stale, and a stale approval is never re-approved automatically.</summary>
    Stale,

    /// <summary>The word has no letter.</summary>
    NotEligible,

    /// <summary>It is not the same Wordform, or the Wordform's guess there is a different analysis.</summary>
    OtherGuess,
}

/// <summary>
/// The project the composers of one staging call run for, which a composer cannot learn from the project copy it is
/// given: where the Source folders are on this machine. It also collects what a call leaves alone, so the caller can
/// report the count before anything is applied.
/// </summary>
internal sealed class ComposeProjectScope : IDisposable
{
    private static readonly AsyncLocal<ComposeProjectScope?> Active = new();
    private readonly Dictionary<(string Source, string Passage), ParatextPassage> _passages = new();
    private readonly HashSet<(string Source, string Book)> _books = new();
    private readonly ComposeProjectScope? _outer;

    private ComposeProjectScope(SIL.Motif.Host.Store.MotifDatabase database, ProjectLocator project)
    {
        Database = database;
        Project = project;
        _outer = Active.Value;
        Active.Value = this;
    }

    /// <summary>The scope of the call in progress, or null outside a staging call.</summary>
    public static ComposeProjectScope? Current => Active.Value;

    public SIL.Motif.Host.Store.MotifDatabase Database { get; }

    public ProjectLocator Project { get; }

    /// <summary>The Occurrences a bulk call left alone.</summary>
    public Dictionary<string, OccurrenceSkip> Skipped { get; } = new(StringComparer.Ordinal);

    /// <summary>The Occurrences the call staged an approval for.</summary>
    public SortedSet<string> Approved { get; } = new(StringComparer.Ordinal);

    /// <summary>The Occurrences the call staged a clear for.</summary>
    public SortedSet<string> Cleared { get; } = new(StringComparer.Ordinal);

    /// <summary>The Occurrences approved while their analysis is Disapproved.</summary>
    public SortedSet<string> Conflicts { get; } = new(StringComparer.Ordinal);

    /// <summary>Whether an approval was staged in a project that does not have the approvals field yet.</summary>
    public bool AddsApprovalsField { get; set; }

    private readonly Dictionary<Guid, HashSet<Guid>> _approvedBefore = new();
    private readonly Dictionary<string, SourceApprovalSet> _approvals = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Source, string Passage), PassageReading> _readings = new();
    private Guid? _installation;
    private bool? _hasField;

    /// <summary>This installation of Motif, which every approval staged here is stored under.</summary>
    public Guid Installation => _installation ??= ScriptureLocalStore.ForInstallation().InstallationId();

    /// <summary>Whether the project had the approvals field before this call; read once.</summary>
    public bool HasApprovalsField(LcmCache cache) => _hasField ??= SourceApprovals.HasField(cache);

    /// <summary>The Source's approvals as the project held them before this call; read once per Source.</summary>
    public SourceApprovalSet Approvals(LcmCache cache, string sourceId)
    {
        if (!_approvals.TryGetValue(sourceId, out var set)) _approvals[sourceId] = set = SourceApprovals.ReadReporting(cache, sourceId);
        return set;
    }

    /// <summary>A Passage's words by location, verse fingerprints and judged approvals; worked out once per Passage.</summary>
    public PassageReading Reading(LcmCache cache, string sourceId, ParatextPassage passage)
    {
        if (_readings.TryGetValue((sourceId, passage.Locator), out var known)) return known;
        var words = ParatextPassages.Tokenize(passage, SourceApprovals.Tokeniser(cache))
            .GroupBy(word => word.Anchor).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var judged = Approvals(cache, sourceId).Documents.TryGetValue(passage.Locator, out var document)
            ? SourceApprovals.Evaluate(cache, document, passage).ToDictionary(approval => approval.Anchor, StringComparer.Ordinal)
            : new Dictionary<string, OccurrenceApproval>(StringComparer.Ordinal);
        return _readings[(sourceId, passage.Locator)] = new(words, SourceApprovals.VerseFingerprints(passage), judged);
    }

    /// <summary>
    /// The analyses of a Wordform a person had approved before this call changed anything, which FieldWorks offers as
    /// its guess; none when nobody has approved one. It is fixed by the first Occurrence staged, so the count of a bulk
    /// Approve does not depend on the order its Occurrences are staged in.
    /// </summary>
    public IReadOnlySet<Guid> ApprovedBefore(IWfiWordform wordform, LcmCache cache)
    {
        if (_approvedBefore.TryGetValue(wordform.Guid, out var known)) return known;
        var agent = cache.LangProject.DefaultUserAgent;
        return _approvedBefore[wordform.Guid] = wordform.AnalysesOC
            .Where(analysis => analysis.GetAgentOpinion(agent) == Opinions.approves).Select(analysis => analysis.Guid).ToHashSet();
    }

    public static ComposeProjectScope Enter(SIL.Motif.Host.Store.MotifDatabase database, ProjectLocator project) =>
        new(database, project);

    /// <summary>The scope already in progress for this project, or a new one; disposing the result ends only a new one.</summary>
    public static IDisposable Ensure(SIL.Motif.Host.Store.MotifDatabase database, ProjectLocator project) =>
        Current is { } existing && existing.Project == project ? new NoScope() : Enter(database, project);

    private sealed class NoScope : IDisposable
    {
        public void Dispose() { }
    }

    /// <summary>The Passage as the Source's text reads now; null when the Source has no such Passage.</summary>
    public ParatextPassage? Passage(string sourceId, string locator)
    {
        var book = locator.Split(' ')[0];
        if (_books.Add((sourceId, book)))
        {
            var folder = ScriptureLocalStore.ForInstallation().Read(SIL.Motif.Worker.Projects.ProjectWorkspaceKey.Compute(Project))
                .SourcePaths.GetValueOrDefault(sourceId);
            if (folder is null || !Directory.Exists(folder))
                throw SourceRefusalException.Of(RefusalCodes.SourceFolderMissing, FailureReason.NotFound,
                    $"The Source '{sourceId}' has no folder on this machine. Record it with source set-folder.", ("id", sourceId));
            foreach (var passage in ParatextPassages.Read(folder, books: new HashSet<string>([book])))
                _passages[(sourceId, passage.Locator)] = passage;
        }
        return _passages.GetValueOrDefault((sourceId, locator));
    }

    /// <summary>Every Passage of the Source's text as it reads now.</summary>
    public IReadOnlyList<ParatextPassage> AllPassages(string sourceId)
    {
        var folder = ScriptureLocalStore.ForInstallation().Read(SIL.Motif.Worker.Projects.ProjectWorkspaceKey.Compute(Project))
            .SourcePaths.GetValueOrDefault(sourceId);
        if (folder is null || !Directory.Exists(folder))
            throw SourceRefusalException.Of(RefusalCodes.SourceFolderMissing, FailureReason.NotFound,
                $"The Source '{sourceId}' has no folder on this machine. Record it with source set-folder.", ("id", sourceId));
        return ParatextPassages.Read(folder);
    }

    public void Dispose() => Active.Value = _outer;
}

/// <summary>
/// Stages the approval of one Occurrence of a Source, or clears it. To Approve an Occurrence is to choose the analysis
/// it has, which also approves that analysis when its Opinion is Unknown; a Disapproved Opinion is left as it is and
/// the conflict is reported. A parser reading with no stored analysis is added from the parser and approved in the
/// same Draft, so Apply writes both or neither.
/// </summary>
internal sealed class SetOccurrenceApprovalDefinition : ComposerDefinition<SetOccurrenceApprovalIntent>
{
    public const string ApproveAction = "Approve";
    public const string ClearAction = "Clear";

    /// <inheritdoc />
    public override string Verb => "compose-set-occurrence-approval";
    /// <inheritdoc />
    public override string Composer => "SetOccurrenceApproval";
    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects => SourceIntents.Subjects;
    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Text("source", required: true),
        Field.Text("anchor", required: true),
        Field.Choice("action", [ApproveAction, ClearAction], required: true),
        Field.Text("analysis", nullable: true),
        Field.Text("wordform", nullable: true),
        Field.Nested("reading", IntentShape.Of(Field.Nested("morphs", IntentShape.Of(
            Field.Text("form", nullable: true), Field.Text("msa", nullable: true),
            Field.Text("inflType", nullable: true), Field.Text("guessedString", nullable: true)), many: true)), required: false),
        Field.Flag("bulk"));
    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_set_occurrence_approval");
    /// <inheritdoc />
    public override ReplacementRule? Replaces { get; } = ReplacementRule.SameIdentity(authored =>
        authored.ValueKind == JsonValueKind.Object && authored.TryGetProperty("source", out var source) &&
        source.ValueKind == JsonValueKind.String && authored.TryGetProperty("anchor", out var anchor) &&
        anchor.ValueKind == JsonValueKind.String
            ? source.GetString()!.Trim().ToLowerInvariant() + "|" + anchor.GetString()!.Trim()
            : null,
        "An earlier intent in this batch already approves or clears the same Occurrence; author one edit.");

    /// <inheritdoc />
    public override SetOccurrenceApprovalIntent Parse(JsonElement authored)
    {
        string? Optional(string name) => authored.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
        ParseAnalysis? reading = authored.TryGetProperty("reading", out var readingValue) && readingValue.ValueKind == JsonValueKind.Object
            ? JsonSerializer.Deserialize<ParseAnalysis>(readingValue.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            : null;
        return new(SourceIntents.Text(authored, "source").Trim(), SourceIntents.Text(authored, "anchor").Trim(),
            SourceIntents.Text(authored, "action"), Optional("analysis"), Optional("wordform"), reading,
            authored.TryGetProperty("bulk", out var bulk) && bulk.ValueKind == JsonValueKind.True);
    }

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, SetOccurrenceApprovalIntent intent)
    {
        var scope = ComposeProjectScope.Current ?? throw new InvalidOperationException(
            "Approving an Occurrence needs the project's Source folders, so it can only be staged through the project store.");
        var (item, entry) = SourceComposerSupport.Find(context.Cache, intent.Source);
        if (entry.Kind != SourceKind.Paratext || !entry.IsValid)
            throw SourceRefusalException.Of(RefusalCodes.SourceKindUnsupported, FailureReason.InvalidArgument,
                entry.Error ?? "Only a Paratext Source has Occurrences to approve.", ("kind", entry.Kind.ToString()));
        if (intent.Action == ApproveAction && entry.Status == SourceStatus.Removed)
            throw SourceRefusalException.Of(RefusalCodes.SourceIdRemoved, FailureReason.Refused,
                $"The Source '{entry.Id}' was removed, so none of its Occurrences can be approved.", ("id", entry.Id));
        if (!ScriptureApprovalDocument.IsAnchor(intent.Anchor))
            throw SourceRefusalException.Of(RefusalCodes.SourceRefInvalid, FailureReason.InvalidArgument,
                $"'{intent.Anchor}' is not an Occurrence location such as MAT 5:3#2.", ("anchor", intent.Anchor));
        var locator = ScriptureApprovalDocument.PassageOf(intent.Anchor);
        var passage = scope.Passage(entry.Id, locator) ?? throw SourceRefusalException.Of(RefusalCodes.SourceChapterNotInSource,
            FailureReason.InvalidArgument, $"The Source '{entry.Id}' has no Passage {locator}.", ("ref", locator));
        SourceApprovalSet approvals;
        try { approvals = scope.Approvals(context.Cache, entry.Id); }
        catch (SourceListAmbiguousException ambiguous)
        {
            throw SourceRefusalException.Of(RefusalCodes.SourceListAmbiguous, FailureReason.Refused, ambiguous.Message);
        }
        var reading = scope.Reading(context.Cache, entry.Id, passage);
        var word = reading.Words.GetValueOrDefault(intent.Anchor)
            ?? throw SourceRefusalException.Of(RefusalCodes.SourceOccurrenceNotFound, FailureReason.NotFound,
                $"The Source text has no word at {intent.Anchor}.", ("anchor", intent.Anchor));

        var existing = reading.Approvals.GetValueOrDefault(intent.Anchor);
        // A conflict is approved otherwise: Clear removes every analysis there, and Approve chooses one.
        var conflicted = approvals.Conflicts.ContainsKey(intent.Anchor);

        if (intent.Action == ClearAction)
        {
            if (existing is null && !conflicted) return [];
            scope.Cleared.Add(intent.Anchor);
            return [new OperationEnvelope(context.MintId(), ScriptureApprovalOperationKinds.Clear,
                target: CanonicalId.FromGuid(item.Guid),
                after: JsonSerializer.SerializeToElement(new { passage = locator, anchor = intent.Anchor, installation = scope.Installation.ToString("D") }))];
        }

        if (!word.Surface.EnumerateRunes().Any(System.Text.Rune.IsLetter))
        {
            if (intent.Bulk) return Skip(scope, intent.Anchor, OccurrenceSkip.NotEligible);
            throw SourceRefusalException.Of(RefusalCodes.SourceOccurrenceNotEligible, FailureReason.InvalidArgument,
                $"The word '{word.Surface}' at {intent.Anchor} has no letter.", ("anchor", intent.Anchor));
        }

        var operations = new List<OperationEnvelope>();
        CanonicalId analysisId;
        var added = false;
        if (intent.Analysis is not null && intent.Reading is null)
        {
            if (!CanonicalId.TryParse(intent.Analysis, out analysisId) ||
                Find(context.Cache, analysisId) is not IWfiAnalysis stored)
                throw SourceRefusalException.Of(RefusalCodes.SourceAnalysisNeeded, FailureReason.InvalidArgument,
                    $"'{intent.Analysis}' is not a stored analysis of the project.", ("analysis", intent.Analysis));
            if (stored.Owner is not IWfiWordform owner) throw new InvalidOperationException("An analysis is owned by a wordform.");
            var approvedBefore = scope.ApprovedBefore(owner, context.Cache);
            if (intent.Bulk)
            {
                // Same Wordform (case-sensitive) and a guess that is this analysis, as FieldWorks would offer it.
                var guessIsThis = ExactForm(owner, word.Surface) &&
                    stored.GetAgentOpinion(context.Cache.LangProject.DefaultUserAgent) != Opinions.disapproves &&
                    (approvedBefore.Count == 0 || approvedBefore.Contains(stored.Guid));
                if (!guessIsThis) return Skip(scope, intent.Anchor, NotTheGuess(existing, conflicted, stored.Guid));
            }
            else RequireWordform(stored, word.Surface, intent.Anchor);
        }
        else if (intent.Analysis is null && intent.Reading is not null && intent.Wordform is not null)
        {
            if (!CanonicalId.TryParse(intent.Wordform, out var wordformId) ||
                Find(context.Cache, wordformId) is not IWfiWordform wordform)
                throw SourceRefusalException.Of(RefusalCodes.SourceWordformMissing, FailureReason.NotFound,
                    $"'{intent.Wordform}' is not a wordform of the project.", ("wordform", intent.Wordform));
            var approvedBefore = scope.ApprovedBefore(wordform, context.Cache);
            if (intent.Bulk && !ExactForm(wordform, word.Surface)) return Skip(scope, intent.Anchor, NotTheGuess(existing, conflicted, null));
            if (!SameForm(wordform, word.Surface))
                throw SourceRefusalException.Of(RefusalCodes.SourceAnalysisMismatch, FailureReason.InvalidArgument,
                    $"The wordform is not the word '{word.Surface}' at {intent.Anchor}.", ("anchor", intent.Anchor));
            var matches = wordform.AnalysesOC.Where(analysis => AnalysisChangeComposer.Matches(analysis, intent.Reading)).ToArray();
            // A reading is the guess only if no other analysis of the Wordform was approved before.
            if (intent.Bulk && approvedBefore.Count > 0 && !(matches.Length == 1 && approvedBefore.Contains(matches[0].Guid)))
                return Skip(scope, intent.Anchor, NotTheGuess(existing, conflicted, matches.Length == 1 ? matches[0].Guid : null));
            if (matches.Length > 1)
                throw SourceRefusalException.Of(RefusalCodes.SourceAnalysisNeeded, FailureReason.InvalidArgument,
                    "Several stored analyses match this reading. Choose one by its stored identity.");
            if (matches.Length == 1) analysisId = CanonicalId.FromGuid(matches[0].Guid);
            else
            {
                // Add analysis from PanGloss: the analysis and its approved Opinion are staged with the approval.
                var created = AnalysisChangeComposer.Build(context.Cache,
                    new AnalysisChangeIntent(AnalysisChangeKinds.Approve, wordformId, intent.Reading));
                analysisId = created.First(operation => operation.EntityId is not null).EntityId!.Value;
                operations.AddRange(created);
                added = true;
            }
        }
        else
            throw SourceRefusalException.Of(RefusalCodes.SourceAnalysisNeeded, FailureReason.InvalidArgument,
                "Give a stored analysis, or a parser reading together with its wordform, not both and not neither.");

        if (conflicted && intent.Bulk) return Skip(scope, intent.Anchor, OccurrenceSkip.ApprovedOtherwise);
        if (existing is not null)
        {
            var same = existing.Analysis == analysisId.ToGuid() && existing.State == ApprovalState.Approved;
            if (same && !added) return Skip(scope, intent.Anchor, OccurrenceSkip.AlreadyApproved);
            if (intent.Bulk)
                return Skip(scope, intent.Anchor, existing.State == ApprovalState.Stale ? OccurrenceSkip.Stale : OccurrenceSkip.ApprovedOtherwise);
        }

        if (!added && Find(context.Cache, analysisId) is IWfiAnalysis current)
        {
            var opinion = current.GetAgentOpinion(context.Cache.LangProject.DefaultUserAgent);
            if (opinion == Opinions.noopinion)
                operations.Add(new OperationEnvelope(context.MintId(), WfiAnalysisOperationKinds.AddRefEvaluations,
                    target: analysisId, after: JsonSerializer.SerializeToElement(new { member = HumanEvaluationPayload.Approves })));
            else if (opinion == Opinions.disapproves)
                scope.Conflicts.Add(intent.Anchor);
        }

        scope.Approved.Add(intent.Anchor);
        if (!scope.HasApprovalsField(context.Cache)) scope.AddsApprovalsField = true;
        operations.Add(new OperationEnvelope(context.MintId(), ScriptureApprovalOperationKinds.Approve,
            target: CanonicalId.FromGuid(item.Guid),
            after: JsonSerializer.SerializeToElement(new
            {
                passage = locator, anchor = intent.Anchor, installation = scope.Installation.ToString("D"), analysis = analysisId.Value,
                verse = reading.Verses[word.Verse], word = word.Surface.Normalize(NormalizationForm.FormD),
            })));
        return operations;
    }

    // An Occurrence with its own approval shows no guess, so that approval is the skip reason.
    private static OccurrenceSkip NotTheGuess(OccurrenceApproval? existing, bool conflicted, Guid? analysis) => existing switch
    {
        null when conflicted => OccurrenceSkip.ApprovedOtherwise,
        null => OccurrenceSkip.OtherGuess,
        { State: ApprovalState.Stale } => OccurrenceSkip.Stale,
        _ when existing.Analysis == analysis => OccurrenceSkip.AlreadyApproved,
        _ => OccurrenceSkip.ApprovedOtherwise,
    };

    private static IReadOnlyList<OperationEnvelope> Skip(ComposeProjectScope scope, string anchor, OccurrenceSkip reason)
    {
        scope.Skipped[anchor] = reason;
        return [];
    }

    private static ICmObject? Find(LcmCache cache, CanonicalId id) =>
        cache.ServiceLocator.GetInstance<ICmObjectRepository>().TryGetObject(id.ToGuid(), out var found) ? found : null;

    internal static string Normal(string text) => text.Normalize(NormalizationForm.FormC).ToLowerInvariant();

    private static bool SameForm(IWfiWordform wordform, string surface) =>
        wordform.Form.AvailableWritingSystemIds.Any(ws => Normal(wordform.Form.get_String(ws).Text ?? "") == Normal(surface));

    private static bool ExactForm(IWfiWordform wordform, string surface) =>
        wordform.Form.AvailableWritingSystemIds.Any(ws =>
            (wordform.Form.get_String(ws).Text ?? "").Normalize(NormalizationForm.FormC) == surface.Normalize(NormalizationForm.FormC));

    private static void RequireWordform(IWfiAnalysis analysis, string surface, string anchor)
    {
        if (analysis.Owner is not IWfiWordform wordform || !SameForm(wordform, surface))
            throw SourceRefusalException.Of(RefusalCodes.SourceAnalysisMismatch, FailureReason.InvalidArgument,
                $"The analysis belongs to a different wordform than the word at {anchor}.", ("anchor", anchor));
    }
}

/// <summary>What staging reads from one Passage: its words by location, its verse fingerprints and its judged approvals.</summary>
internal sealed record PassageReading(IReadOnlyDictionary<string, ParatextWord> Words,
    IReadOnlyDictionary<string, string> Verses, IReadOnlyDictionary<string, OccurrenceApproval> Approvals);

internal sealed record SetOccurrenceApprovalIntent(
    string Source, string Anchor, string Action, string? Analysis, string? Wordform, ParseAnalysis? Reading, bool Bulk);
