using SIL.Motif.Host;
using System.Buffers.Binary;
using SIL.LCModel;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Host.LcmUtils;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Texts;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Parser;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.SelectionReading;

internal enum SelectionReaderPausePoint
{
    AfterIndexSnapshot,
    AfterContextValidation,
    AfterPresentationStateLoad,
    BeforeCachePublication,
}

/// <summary>Opens an owned reader for one project's chosen Texts and added spellings.</summary>
public sealed record OpenSelectionReaderRequest(
    string ProjectPath, IReadOnlyList<Guid> TextIds, IReadOnlyList<string> AddedWords)
{
    public CurrentEvidenceSnapshot? EvidenceSnapshot { get; init; }
    /// <summary>The exact displayed evidence to retain when opening a different Text scope.</summary>
    public ExpectedContext? DisplayedEvidence { get; init; }
    /// <summary>The Assessment actually shown by the window, before a reader has stamped its context.</summary>
    public SelectionAssessmentEvidence? ShownAssessment { get; init; }
}

/// <summary>Names displayed Assessment components without retaining their word or morphology results.</summary>
public sealed record SelectionAssessmentEvidence(BaselineToken Baseline, string AssessmentId,
    IReadOnlyList<string> ReplacementAssessmentIds, IReadOnlyList<string> MeasurementAssessmentIds);

/// <summary>The chosen Texts and added forms a reader session describes.</summary>
public sealed record SelectionReadRequest(
    string ProjectKey,
    IReadOnlyList<Guid> TextIds,
    IReadOnlyList<string> AddedWords)
{
    public string? ProjectPath { get; init; }
    public CurrentEvidenceSnapshot? EvidenceSnapshot { get; init; }
    /// <summary>The exact displayed evidence to retain when opening a different Text scope.</summary>
    public ExpectedContext? DisplayedEvidence { get; init; }
    public SelectionAssessmentEvidence? ShownAssessment { get; init; }
    internal Action<SelectionReaderPausePoint>? PauseAt { get; init; }
}

/// <summary>The immutable evidence and source identity a reader session was opened against.</summary>
public sealed record SelectionReadContext(
    long Generation,
    string ProjectKey,
    BaselineToken Baseline,
    string SelectionDigest,
    IReadOnlyList<Guid> TextIds,
    IReadOnlyList<string> AddedWords)
{
    public string SelectionOrigin { get; init; } = "explicit";
    public string? AssessmentRootId { get; init; }
    public IReadOnlyList<string> ReplacementAssessmentIds { get; init; } = [];
    public SelectionEvidenceExpectation? Evidence { get; init; }

    /// <summary>Builds the portable expected evidence sent with a Read or pending Draft write.</summary>
    public ExpectedContext ExpectedWriteContext() => new(Baseline)
    {
        TextIds = TextIds,
        AddedWords = AddedWords,
        SelectionEvidence = Evidence,
    };
}

/// <summary>A result stamped with the evidence and presentation generation that produced it.</summary>
public sealed record SelectionRead<T>(
    SelectionReadContext Context,
    long PresentationStamp,
    T Value,
    SelectionReadLease Lease) : IDisposable
{
    public void Dispose() => Lease.Dispose();
}

/// <summary>
/// Captures cumulative model registrations and current reader state for the entire Selection context.
/// Created model counts remain cumulative after registrations are released; live counts do not.
/// </summary>
public sealed record SelectionReaderDiagnostics(
    long Generation,
    long PresentationStamp,
    long QueriesIssued,
    long IndexRowsDeserialized,
    long CompactSemanticIdentities,
    long RepositoryRecordsDeserialized,
    long TextRowsDeserialized,
    long WordformRowsDeserialized,
    long SourceLines,
    long SourceTokens,
    long TextAnalyses,
    long WordformAnalyses,
    long AssessmentRowsDeserialized,
    long AssessmentWordsDeserialized,
    long Morphs,
    long PhysicalOccurrences,
    long Memberships,
    long IndexedWordRows,
    long LiveRowModels,
    long CreatedLineModels,
    long LiveLineModels,
    long CreatedTokenModels,
    long LiveTokenModels,
    long LiveCardModels,
    long LivePinnedModels,
    long CachedEntries,
    long CachedLineRanges,
    long CachedOccurrencePages,
    long CachedWordDetails,
    long CachedTextGraphs,
    long LeasedResults,
    int VisibleResultLeases,
    int PrefetchLeases,
    int PinnedLeases,
    int LeasedLineRanges,
    int LeasedLineTokens,
    int LeasedOccurrencePages,
    int LeasedOccurrenceContexts,
    int LeasedWordKeys,
    int LeasedEvictedLineRanges,
    int LeasedEvictedLineTokens,
    int LeasedEvictedOccurrencePages,
    int PendingReads,
    bool IsObsolete,
    bool IsDisposed)
{
    /// <summary>Cumulative UTF-8 payload bytes decoded from Baseline summary, index and detail JSON.</summary>
    public long BaselineJsonPayloadBytes { get; init; }
    /// <summary>Cumulative physical word tuples decoded from compact indexes and Text detail, including rereads.</summary>
    public long BaselineOccurrenceTuplesRead { get; init; }
    public long OrdinaryRowModels { get; init; }
    public long OrdinaryLineModels { get; init; }
    public long OrdinaryTokenModels { get; init; }
    public long BaselineWordFactCachesOpened { get; init; }
    public long BaselineWordformFactsRead { get; init; }
    public long BaselineWordDetailCachesOpened { get; init; }
}

/// <summary>
/// Holds one Selection's compact index and immutable evidence identity. The database factory borrows its
/// supplied database; the project-path factory owns and releases its database lease with the reader.
/// </summary>
public sealed class SelectionReader : IDisposable, IAsyncDisposable
{
    private static long _nextGeneration;
    private const int MaximumActiveResults = 16;
    private const int MaximumPrefetchResults = 1;
    private const int MaximumPinnedResults = 2;
    private const int MaximumLeasedLineRanges = 3;
    private const int MaximumLeasedLineTokens = 1536;
    private const int MaximumLeasedOccurrencePages = 2;
    private const int MaximumLeasedOccurrenceContexts = 400;
    private const int MaximumLeasedWordKeys = 1536;
    private readonly object _gate = new();
    private readonly object _textReadGate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TaskCompletionSource _disposeCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly long _generation;
    private long _presentationStamp;
    private readonly SqliteConnection _presentationVersionConnection;
    private long _queriesIssued;
    private long _repositoryRecordsDeserialized;
    private long _baselineJsonPayloadBytes;
    private long _baselineOccurrenceTuplesRead;
    private long _indexRowsDeserialized;
    private long _compactSemanticIdentities;
    private long _textRowsDeserialized;
    private long _wordformRowsDeserialized;
    private long _baselineWordFactCachesOpened;
    private long _baselineWordformFactsRead;
    private long _baselineWordDetailCachesOpened;
    private long _sourceLines;
    private long _sourceTokens;
    private long _textAnalyses;
    private long _wordformAnalyses;
    private long _assessmentRowsDeserialized;
    private long _assessmentWordsDeserialized;
    private long _morphs;
    private long _createdLineModels;
    private long _createdTokenModels;
    private SelectionIndexBuilder? _index;
    private readonly MotifDatabase _database;
    private bool _ownsDatabase;
    private readonly string? _projectPath;
    private readonly string _baselineFwDataPath;
    private readonly Action<SelectionReaderPausePoint>? _pauseAt;
    private IReadOnlyList<SIL.Motif.Contract.Responses.WritingSystemDisplay>? _writingSystems;
    private SelectionReadContext? _context;
    private TaskCompletionSource? _activeReadsDrained;
    private int _activeReads;
    private bool _obsolete;
    private bool _disposed;
    private string? _readStateDigest;
    private string? _pendingRevision;
    private string? _warningDigest;
    private IReadOnlyList<AssessmentWordResult> _assessmentResults = [];
    private IReadOnlyList<AssessmentObjectTiming> _objectTimings = [];
    private IReadOnlyList<string> _parseWarningLines = [];
    private readonly HashSet<SelectionReadLease> _leases = [];
    private readonly Dictionary<SelectionReadLease, SelectionLeaseResource> _leaseResources = [];
    private readonly Dictionary<LineCacheKey, TextLineSlice> _lineCache = [];
    private readonly LinkedList<LineCacheKey> _lineLru = [];
    private readonly Dictionary<OccurrenceCacheKey, WordOccurrences> _occurrenceCache = [];
    private readonly LinkedList<OccurrenceCacheKey> _occurrenceLru = [];
    private readonly Dictionary<TextWordKey, TextWordsProjectedWordform> _wordDetailCache = [];
    private readonly LinkedList<TextWordKey> _wordDetailLru = [];
    private Guid? _cachedTextId;
    private TextWordsProjectedText? _cachedText;
    private long _liveLineModels;
    private long _liveTokenModels;
    private long _liveRowModels;
    private long _liveCardModels;
    private long _livePinnedModels;
    private long _ordinaryRowModels;
    private long _ordinaryLineModels;
    private long _ordinaryTokenModels;

    private SelectionReader(
        MotifDatabase database,
        SelectionReadContext context,
        SelectionIndexBuilder index,
        CurrentBaselineSelectionIndexRead source,
        SelectionReadEvidence? evidence,
        RepositoryReadCount openReads,
        Action<SelectionReaderPausePoint>? pauseAt,
        string? projectPath, int baselineWordformFactsRead)
    {
        _database = database;
        _presentationVersionConnection = database.OpenConnection();
        _projectPath = projectPath;
        _baselineFwDataPath = source.Baseline.FwDataPath;
        _pauseAt = pauseAt;
        _context = context;
        _index = index;
        _generation = context.Generation;
        _presentationStamp = context.Generation;
        _queriesIssued = openReads.Queries;
        _repositoryRecordsDeserialized = openReads.RecordsDeserialized;
        _baselineJsonPayloadBytes = openReads.BaselineJsonPayloadBytes;
        _baselineOccurrenceTuplesRead = openReads.BaselineOccurrenceTuples;
        _indexRowsDeserialized = source.Texts.Count;
        _compactSemanticIdentities = index.SemanticIdentityCount;
        _baselineWordformFactsRead = baselineWordformFactsRead;
        _baselineWordFactCachesOpened = baselineWordformFactsRead > 0 ? 1 : 0;
        _sourceLines = source.Texts.Sum(text => text.Lines.Count);
        _sourceTokens = source.Texts.Sum(text => text.Lines.Sum(line => line.Tokens.Count));
        _assessmentRowsDeserialized = evidence?.HeaderRows ?? 0;
        _assessmentWordsDeserialized = evidence?.WordRows ?? 0;
        _assessmentResults = evidence?.Words.Select(AssessmentWordRows.FromRecorded).ToArray() ?? [];
        _objectTimings = evidence?.ObjectTimings ?? [];
        _parseWarningLines = evidence?.ParseWarningLines ?? [];
        _writingSystems = source.Summary.WritingSystems;
    }

    /// <summary>Opens a reader from one finite Baseline read snapshot.</summary>
    public static Task<CommandOutcome<SelectionReader>> OpenAsync(
        MotifDatabase database,
        SelectionReadRequest request,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Open(database, request, cancellationToken));

    /// <summary>Returns a session that owns its database lease; dispose it before releasing the workspace.</summary>
    public static Task<CommandOutcome<SelectionReader>> OpenAsync(OpenSelectionReaderRequest request,
        CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        ArgumentNullException.ThrowIfNull(request);
        if (cancellationToken.IsCancellationRequested)
            return CommandOutcome<SelectionReader>.Refused(new Refusal(
                "selection-reader.cancelled", FailureReason.Cancelled, "Opening the Selection reader was cancelled."));
        return ProjectStoreCommand.OpenSession(request.ProjectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            var result = Open(database, new SelectionReadRequest(ProjectWorkspaceKey.Compute(project),
                request.TextIds, request.AddedWords)
            {
                ProjectPath = request.ProjectPath,
                EvidenceSnapshot = request.EvidenceSnapshot,
                DisplayedEvidence = request.DisplayedEvidence,
                ShownAssessment = request.ShownAssessment,
            }, cancellationToken);
            if (result.Succeeded) result.Value!._ownsDatabase = true;
            return result;
        });
    });

    /// <summary>The evidence identity captured when this reader was opened.</summary>
    public SelectionReadContext Context
    {
        get
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _context!;
            }
        }
    }

    /// <summary>The writing-system display settings captured with the Baseline summary.</summary>
    public IReadOnlyList<SIL.Motif.Contract.Responses.WritingSystemDisplay> WritingSystems
    {
        get
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _writingSystems!;
            }
        }
    }

    /// <summary>Returns row, nested-record, query and active-lifetime counts for this reader.</summary>
    public SelectionReaderDiagnostics Diagnostics
    {
        get
        {
            lock (_gate)
            {
                var leasedLineRanges = GetLeasedLineRanges();
                var leasedOccurrencePages = GetLeasedOccurrencePages();
                return new SelectionReaderDiagnostics(_generation, _presentationStamp,
                    Interlocked.Read(ref _queriesIssued),
                    Interlocked.Read(ref _indexRowsDeserialized),
                    Interlocked.Read(ref _compactSemanticIdentities),
                    Interlocked.Read(ref _repositoryRecordsDeserialized),
                    Interlocked.Read(ref _textRowsDeserialized), Interlocked.Read(ref _wordformRowsDeserialized),
                    Interlocked.Read(ref _sourceLines), Interlocked.Read(ref _sourceTokens),
                    Interlocked.Read(ref _textAnalyses), Interlocked.Read(ref _wordformAnalyses),
                    Interlocked.Read(ref _assessmentRowsDeserialized),
                    Interlocked.Read(ref _assessmentWordsDeserialized),
                    Interlocked.Read(ref _morphs), _index?.PhysicalOccurrenceCount ?? 0,
                    _index?.MembershipCount ?? 0,
                    _index?.WordRowCount ?? 0, Interlocked.Read(ref _liveRowModels),
                    Interlocked.Read(ref _createdLineModels), Interlocked.Read(ref _liveLineModels),
                    Interlocked.Read(ref _createdTokenModels), Interlocked.Read(ref _liveTokenModels),
                    Interlocked.Read(ref _liveCardModels), Interlocked.Read(ref _livePinnedModels),
                    _lineCache.Count + _occurrenceCache.Count + _wordDetailCache.Count +
                        (_cachedText is null ? 0 : 1),
                    _lineCache.Count, _occurrenceCache.Count, _wordDetailCache.Count,
                    _cachedText is null ? 0 : 1, _leases.Count,
                    _leaseResources.Values.Count(resource => resource.Purpose == SelectionReadPurpose.Visible),
                    _leaseResources.Values.Count(resource => resource.Purpose == SelectionReadPurpose.Prefetch),
                    _leaseResources.Values.Count(resource => resource.Purpose == SelectionReadPurpose.Pin),
                    leasedLineRanges.Count, leasedLineRanges.Values.Sum(resource => resource.LineTokens),
                    leasedOccurrencePages.Count,
                    leasedOccurrencePages.Values.Sum(resource => resource.OccurrenceContexts),
                    _leaseResources.Values.SelectMany(resource => resource.WordKeys).Distinct().Count(),
                    leasedLineRanges.Count(pair => !_lineCache.ContainsKey(pair.Key)),
                    leasedLineRanges.Where(pair => !_lineCache.ContainsKey(pair.Key))
                        .Sum(pair => pair.Value.LineTokens),
                    leasedOccurrencePages.Count(pair => !_occurrenceCache.ContainsKey(pair.Key)),
                    _activeReads, _obsolete, _disposed)
                {
                    BaselineJsonPayloadBytes = Interlocked.Read(ref _baselineJsonPayloadBytes),
                    BaselineOccurrenceTuplesRead = Interlocked.Read(ref _baselineOccurrenceTuplesRead),
                    OrdinaryRowModels = _ordinaryRowModels,
                    OrdinaryLineModels = _ordinaryLineModels,
                    OrdinaryTokenModels = _ordinaryTokenModels,
                    BaselineWordFactCachesOpened = Interlocked.Read(ref _baselineWordFactCachesOpened),
                    BaselineWordformFactsRead = Interlocked.Read(ref _baselineWordformFactsRead),
                    BaselineWordDetailCachesOpened = Interlocked.Read(ref _baselineWordDetailCachesOpened),
                };
            }
        }
    }

    private Dictionary<LineCacheKey, SelectionLeaseResource> GetLeasedLineRanges() => _leaseResources.Values
        .Where(resource => resource.LineKey is not null)
        .GroupBy(resource => resource.LineKey!.Value)
        .ToDictionary(group => group.Key, group => new SelectionLeaseResource(
            group.First().Purpose, group.Key, group.Max(resource => resource.LineTokens),
            group.SelectMany(resource => resource.WordKeys).ToHashSet(), null, 0));

    private Dictionary<OccurrenceCacheKey, SelectionLeaseResource> GetLeasedOccurrencePages() =>
        _leaseResources.Values.Where(resource => resource.OccurrenceKey is not null)
            .GroupBy(resource => resource.OccurrenceKey!.Value)
            .ToDictionary(group => group.Key, group => new SelectionLeaseResource(
                group.First().Purpose, null, 0, group.SelectMany(resource => resource.WordKeys).ToHashSet(),
                group.Key, group.Max(resource => resource.OccurrenceContexts)));

    /// <summary>Returns compact summary rows without constructing display line or token models.</summary>
    public Task<CommandOutcome<SelectionRead<SelectionSummary>>> ReadSummaryAsync(
        SelectionViewRequest request,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => ReadSummary(request, cancellationToken));

    /// <summary>Reads current Read marks, Draft fit, and warning attribution for the whole Selection.</summary>
    public Task<CommandOutcome<SelectionRead<SelectionPresentationState>>> ReadPresentationAsync(
        CancellationToken cancellationToken = default) =>
        Task.Run(() => ReadPresentation(cancellationToken));

    private CommandOutcome<SelectionRead<SelectionPresentationState>> ReadPresentation(
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_projectPath))
            return CommandOutcome<SelectionRead<SelectionPresentationState>>.Refused(new Refusal(
                "selection-reader.project-path-required", FailureReason.InvalidArgument,
                "Reading mutable Selection presentation state requires its project path."));
        if (!BeginRead(out var context, out var index, out var busy))
            return busy ? ReaderBusy<SelectionPresentationState>() : ReaderUnavailable<SelectionPresentationState>();

        using var repositoryReads = RepositoryReadCounters.BeginScope();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            if (!IsCurrent(context)) return EvidenceChanged<SelectionPresentationState>();
            var storeGeneration = ReadPresentationStoreGeneration();

            var readState = ReadCurrentReadState(context, linked.Token, RecordPresentationDetailReads);
            if (!readState.Succeeded)
                return CommandOutcome<SelectionRead<SelectionPresentationState>>.Refused(readState.Refusal!);
            var readOccurrences = readState.Value!.Occurrences;

            var project = new ProjectLocator(Path.GetFullPath(_projectPath!),
                Path.GetFileNameWithoutExtension(_projectPath!));
            var pendingRead = ProjectStoreCommand.RunOperation(_projectPath!, () =>
                CommandOutcome<PendingChangesSnapshot>.Success(
                    PendingChanges.ReadForSelection(_database, project)));
            if (!pendingRead.Succeeded)
                return CommandOutcome<SelectionRead<SelectionPresentationState>>.Refused(pendingRead.Refusal!);
            var pending = pendingRead.Value!;
            linked.Token.ThrowIfCancellationRequested();

            var baselineJson = JsonSerializer.Serialize(context.Baseline, MotifJson.CreateOptions());
            var grammarCheck = new GrammarCheckRepository(_database).GetLatest(baselineJson)
                ?? new GrammarCheckResponse([], true);
            var warningEvidence = StoredParseWarnings.Merge(grammarCheck, _parseWarningLines);
            var warnings = WarningWordsQuery.WithYourWords(warningEvidence,
                _assessmentResults, _objectTimings).Findings;
            if (!IsCurrent(context)) return EvidenceChanged<SelectionPresentationState>();

            var readDigest = Digest(context.TextIds.Select(textId => new
                {
                    TextId = textId,
                    Occurrences = readOccurrences[textId].OrderBy(item => item.ParagraphId)
                        .ThenBy(item => item.SegmentId).ThenBy(item => item.Index).ToArray(),
                }));
            var warningDigest = Digest(warnings);
            var words = index.Build().Words;
            var state = new SelectionPresentationState(readOccurrences, pending, warnings)
            {
                PendingByWord = PendingByWord(words, pending),
                WarningsByWord = WarningsByWord(words, warnings),
            };
            _pauseAt?.Invoke(SelectionReaderPausePoint.AfterPresentationStateLoad);
            var presentationRead = CreatePresentationRead(context, state, readDigest, pending.Revision,
                warningDigest, storeGeneration);
            return presentationRead is null
                ? PresentationSuperseded<SelectionPresentationState>()
                : CommandOutcome<SelectionRead<SelectionPresentationState>>.Success(presentationRead);
        }
        catch (OperationCanceledException)
        {
            return Cancelled<SelectionPresentationState>("Reading Selection presentation state was cancelled.");
        }
        catch (InvalidDataException exception)
        {
            return Unreadable<SelectionPresentationState>(exception.Message);
        }
        catch (SelectionReadOwnershipException)
        {
            return OwnershipLimit<SelectionPresentationState>();
        }
        catch (ObjectDisposedException)
        {
            return ReaderUnavailable<SelectionPresentationState>();
        }
        catch (InvalidOperationException) when (IsObsolete())
        {
            return EvidenceChanged<SelectionPresentationState>();
        }
        finally
        {
            RecordRepositoryReads(repositoryReads);
            EndRead();
        }
    }

    private SelectionRead<SelectionPresentationState>? CreatePresentationRead(
        SelectionReadContext context, SelectionPresentationState state, string readDigest,
        string pendingRevision, string warningDigest, long storeGeneration)
    {
        lock (_gate)
        {
            ThrowIfPublicationClosedLocked();
            if (ReadPresentationStoreGenerationLocked() != storeGeneration)
                return null;
            var presentationStamp = _presentationStamp;
            if (_readStateDigest is not null && _readStateDigest != readDigest ||
                _pendingRevision is not null && _pendingRevision != pendingRevision ||
                _warningDigest is not null && _warningDigest != warningDigest)
                presentationStamp++;
            var read = CreateReadLocked(context, state, SelectionReadPurpose.Visible, null, presentationStamp);
            _presentationStamp = presentationStamp;
            _readStateDigest = readDigest;
            _pendingRevision = pendingRevision;
            _warningDigest = warningDigest;
            return read;
        }
    }

    private long ReadPresentationStoreGeneration()
    {
        lock (_gate)
        {
            ThrowIfPublicationClosedLocked();
            return ReadPresentationStoreGenerationLocked();
        }
    }

    private long ReadPresentationStoreGenerationLocked()
    {
        RepositoryReadCounters.QueryExecuted();
        using var command = _presentationVersionConnection.CreateCommand();
        command.CommandText = "PRAGMA data_version;";
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static IReadOnlyDictionary<TextWordKey, IReadOnlyList<SelectionPendingChange>> PendingByWord(
        IReadOnlyList<SelectionWordSummary> words, PendingChangesSnapshot pending)
    {
        var fits = pending.FitSummary.ToDictionary(fit => fit.ChangeId, StringComparer.Ordinal);
        var result = new Dictionary<TextWordKey, List<SelectionPendingChange>>();
        foreach (var change in pending.Changes)
        {
            if (!CanonicalId.TryParse(change.WordformId, out var id)) continue;
            foreach (var word in words.Where(word => word.Key.WordformId == id.ToGuid() &&
                         string.Equals(Normalize(change.Word), word.Key.Form, StringComparison.Ordinal)))
            {
                if (!result.TryGetValue(word.Key, out var matches)) result.Add(word.Key, matches = []);
                matches.Add(new SelectionPendingChange(change, fits.GetValueOrDefault(change.ChangeId)));
            }
        }
        return result.ToDictionary(pair => pair.Key,
            pair => (IReadOnlyList<SelectionPendingChange>)pair.Value.ToArray());
    }

    private static IReadOnlyDictionary<TextWordKey, IReadOnlyList<GrammarWarning>> WarningsByWord(
        IReadOnlyList<SelectionWordSummary> words, IReadOnlyList<GrammarWarning> warnings)
    {
        var result = new Dictionary<TextWordKey, IReadOnlyList<GrammarWarning>>();
        foreach (var word in words)
        {
            var matches = warnings.Where(warning => WarningRows(warning).Any(row =>
                string.Equals(Normalize(row.Word), word.Key.Form, StringComparison.Ordinal))).ToArray();
            if (matches.Length > 0) result.Add(word.Key, matches);
        }
        return result;
    }

    private static IEnumerable<WordRow> WarningRows(GrammarWarning warning)
    {
        if (warning.YourWords is not { } attribution) yield break;
        foreach (var item in attribution.Words.Concat(attribution.MembershipCandidates).Concat(
                     attribution.SpellingCandidates))
            yield return item.Row;
    }

    private static string Digest<T>(T value) => "sha256:" + Convert.ToHexString(
        SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, MotifJson.CreateOptions()))).ToLowerInvariant();

    /// <summary>Reads one bounded range of the captured occurrences for an exact word key.</summary>
    public Task<CommandOutcome<SelectionRead<WordOccurrences>>> ReadOccurrencesAsync(
        TextWordKey word,
        OccurrenceRange? range = null,
        CancellationToken cancellationToken = default,
        SelectionReadPurpose purpose = SelectionReadPurpose.Visible) =>
        Task.Run(() => ReadOccurrences(word, range ?? new OccurrenceRange(), purpose, cancellationToken));

    /// <summary>Reads a bounded line and token range from one captured Text row.</summary>
    public Task<CommandOutcome<SelectionRead<TextLineSlice>>> ReadLinesAsync(
        Guid textId,
        TextLineRange? range = null,
        CancellationToken cancellationToken = default,
        SelectionReadPurpose purpose = SelectionReadPurpose.Visible) =>
        Task.Run(() => ReadLines(textId, range ?? new TextLineRange(), purpose, cancellationToken));

    /// <summary>Reads the independently paged visible lines as one owned range of at most 512 source tokens.</summary>
    public Task<CommandOutcome<SelectionRead<TextLineSlice>>> ReadLinePagesAsync(
        Guid textId, IReadOnlyList<TextLinePage> pages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pages);
        var requested = pages.ToArray();
        return Task.Run(() => ReadLinePages(textId, requested, cancellationToken));
    }

    /// <summary>Reads plain sentence context for an exact occurrence without loading analysis or wordform detail.</summary>
    public Task<CommandOutcome<SelectionRead<SelectionSentenceContext>>> ReadSentenceAsync(
        OccurrenceAnchor occurrence, int? tokenOffset = null, int tokenCount = 20,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => ReadSentence(occurrence, tokenOffset, tokenCount, cancellationToken));

    /// <summary>Reads stored analysis display detail only for the supplied visible word identities.</summary>
    public Task<CommandOutcome<SelectionRead<SelectionWordDetails>>> ReadWordDetailsAsync(
        IReadOnlyList<TextWordKey> words, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(words);
        var requested = words.ToArray();
        return Task.Run(() => ReadWordDetails(requested, cancellationToken));
    }

    /// <summary>Resolves action scope from exact occurrence identities in the compact index.</summary>
    public SelectionActionFacts ResolveActionScope(
        IReadOnlyCollection<TextWordKey> wordKeys,
        IReadOnlySet<Guid>? textIds = null,
        IReadOnlySet<OccurrenceAnchor>? excluded = null,
        IReadOnlySet<OccurrenceAnchor>? included = null)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_obsolete) throw new InvalidOperationException("The Selection reader's evidence is obsolete.");
            return _index!.ResolveActionScope(wordKeys, textIds, excluded, included);
        }
    }

    /// <summary>Invalidates the session after its selected evidence is explicitly replaced.</summary>
    public void Invalidate() => MarkObsolete();

    /// <summary>Returns the adjacent indexed word occurrence without loading its line.</summary>
    public SelectionOccurrenceLocation? Adjacent(OccurrenceAnchor anchor, int offset)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_obsolete) throw new InvalidOperationException("The Selection reader's evidence is obsolete.");
            return _index!.Adjacent(anchor, offset);
        }
    }

    /// <summary>Cancels active reads, waits for them to finish, and drops the compact index.</summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Task drain;
        SelectionReadLease[] leases;
        var start = false;
        lock (_gate)
        {
            if (!_disposed)
            {
                _disposed = true;
                _context = null;
                _index = null;
                _writingSystems = null;
                _assessmentResults = [];
                _objectTimings = [];
                leases = _leases.ToArray();
                ClearDetailCachesLocked();
                drain = _activeReadsDrained?.Task ?? Task.CompletedTask;
                start = true;
            }
            else
            {
                return new ValueTask(_disposeCompletion.Task);
            }
        }

        if (start)
        {
            foreach (var lease in leases) lease.Dispose();
            _lifetime.Cancel();
            _ = FinishDisposeAsync(drain);
        }
        return new ValueTask(_disposeCompletion.Task);
    }

    private static CommandOutcome<SelectionReader> Open(
        MotifDatabase database,
        SelectionReadRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ProjectKey) || request.TextIds is null || request.AddedWords is null ||
            request.TextIds.Any(id => id == Guid.Empty) || request.AddedWords.Any(word => word is null))
            return CommandOutcome<SelectionReader>.Refused(new Refusal(
                "selection-reader.invalid", FailureReason.InvalidArgument,
                "A project key and valid Text and word identities are required."));

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var indexBuilder = new SelectionIndexBuilder();
            var textIds = request.TextIds.Distinct().ToArray();
            var addedWords = request.AddedWords.Select(word => word.Trim()
                .Normalize(System.Text.NormalizationForm.FormD)).Where(word => word.Length > 0)
                .Distinct(StringComparer.Ordinal).ToArray();
            if (!string.IsNullOrWhiteSpace(request.ProjectPath))
            {
                var project = ProjectStoreCommand.Locate(request.ProjectPath);
                if (ProjectWorkspaceKey.Compute(project) != request.ProjectKey)
                    throw new EvidenceContextChangedException();
            }
            using var openReads = RepositoryReadCounters.BeginScope();
            var resolved = new BaselineRepository(database).ReadCurrentSelectionIndexesInSnapshot(
                request.ProjectKey, textIds, (source, connection, transaction) =>
                {
                    if (source.Texts.Count != textIds.Length)
                        throw DamagedSelection("A selected Text row is missing.");
                    foreach (var text in source.Texts)
                    {
                        indexBuilder.AddText(text);
                        foreach (var wordform in text.Wordforms) indexBuilder.AddWordform(wordform);
                    }

                    var addedFactsRead = AddBaselineWordFacts(indexBuilder, source, addedWords, cancellationToken);
                    request.PauseAt?.Invoke(SelectionReaderPausePoint.AfterIndexSnapshot);
                    var evidence = ResolveEvidence(database, connection, transaction, request, source,
                        textIds, addedWords);
                    if (evidence is not null) indexBuilder.AddAssessmentWords(evidence.Words);
                    cancellationToken.ThrowIfCancellationRequested();
                    foreach (var word in addedWords) indexBuilder.AddWord(word);
                    indexBuilder.Complete();
                    var context = new SelectionReadContext(
                        Interlocked.Increment(ref _nextGeneration), request.ProjectKey, source.Baseline.Token,
                        SelectionDigest(request.ProjectKey, source.Baseline.Token, textIds, addedWords, evidence),
                        Array.AsReadOnly(textIds), Array.AsReadOnly(addedWords))
                    {
                        SelectionOrigin = evidence?.Origin ?? "explicit",
                        AssessmentRootId = evidence?.Root?.AssessmentId,
                        ReplacementAssessmentIds = evidence?.ReplacementIds ?? [],
                        Evidence = evidence?.Descriptor,
                    };
                    return new SelectionOpenSnapshot(source, evidence, context, addedFactsRead);
                }, cancellationToken);
            if (resolved is null)
                return CommandOutcome<SelectionReader>.Refused(new Refusal(
                    "selection-reader.no-baseline", FailureReason.Refused,
                    "Capture a Baseline before opening its Selection reader."));
            return CommandOutcome<SelectionReader>.Success(
                new SelectionReader(database, resolved.Context, indexBuilder, resolved.Source, resolved.Evidence,
                    openReads.Snapshot(), request.PauseAt, request.ProjectPath, resolved.BaselineWordformFactsRead));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CommandOutcome<SelectionReader>.Refused(new Refusal(
                "selection-reader.cancelled", FailureReason.Cancelled, "Opening the Selection reader was cancelled."));
        }
        catch (InvalidDataException exception)
        {
            return CommandOutcome<SelectionReader>.Refused(new Refusal(
                "baseline.text-words-unreadable", FailureReason.StoreInconsistent, exception.Message));
        }
        catch (EvidenceContextChangedException)
        {
            return CommandOutcome<SelectionReader>.Refused(new Refusal(
                "texts.evidence-changed", FailureReason.Refused,
                "The saved Selection evidence changed. Reopen the Selection reader."));
        }
        catch (LcmInitializationException exception)
        {
            return CommandOutcome<SelectionReader>.Refused(new Refusal(
                "baseline.text-words-unreadable", FailureReason.StoreInconsistent, exception.Message));
        }
        catch (IOException exception)
        {
            return CommandOutcome<SelectionReader>.Refused(new Refusal(
                "selection-reader.invalid", FailureReason.InvalidArgument, exception.Message));
        }
    }

    private static int AddBaselineWordFacts(SelectionIndexBuilder index,
        CurrentBaselineSelectionIndexRead source, IReadOnlyList<string> addedWords, CancellationToken cancellationToken)
    {
        var requested = addedWords.Intersect(source.Summary.WordWritingSystems.Keys.Select(Normalize), StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        if (requested.Count == 0) return 0;
        if (!File.Exists(source.Baseline.FwDataPath))
            throw DamagedSelection("The exact Baseline file is unavailable for added-word identities.");
        using var baseline = BaselineReadCache.Open(source.Baseline.FwDataPath);
        var count = 0;
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var wordform in baseline.Cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var forms = wordform.Form.AvailableWritingSystemIds.Select(ws => new WritingSystemText(
                Normalize(wordform.Form.get_String(ws)?.Text ?? string.Empty),
                baseline.Cache.WritingSystemFactory.GetStrFromWs(ws)))
                .Where(form => requested.Contains(form.Text)).ToArray();
            if (forms.Length == 0) continue;
            index.AddWordform(TextWordsProjectionBuilder.ReadWordformFacts(baseline.Cache, wordform), forms);
            found.UnionWith(forms.Select(form => form.Text));
            count++;
        }
        if (!found.SetEquals(requested))
            throw DamagedSelection("The captured word inventory disagrees with the exact Baseline.");
        return count;
    }

    private static SelectionReadEvidence? ResolveEvidence(MotifDatabase database, SqliteConnection connection,
        SqliteTransaction transaction, SelectionReadRequest request, CurrentBaselineSelectionIndexRead source,
        IReadOnlyList<Guid> textIds, IReadOnlyList<string> addedWords)
    {
        if (request.DisplayedEvidence is { } displayed)
            return ResolveDisplayedEvidence(database, connection, transaction, displayed, source, addedWords);
        if (request.ShownAssessment is { } shown)
            return ResolveShownAssessment(database, connection, transaction, shown, source, addedWords);
        var saved = new NamedSelectionRepository(database).GetDefault(connection, transaction);
        var matchesDefault = saved is not null && saved.TextIds.SequenceEqual(textIds) &&
            saved.AddedWords.Select(Normalize).Where(word => word.Length > 0)
                .SequenceEqual(addedWords, StringComparer.Ordinal);
        var preliminary = new ExpectedContext(source.Baseline.Token);
        Selection? selection = null;
        AssessmentRecord? root = null;
        IReadOnlyList<AssessmentRecord> replacements = [];
        IReadOnlyList<string> measurementIds = [];
        if (matchesDefault)
        {
            var resolved = CurrentEvidenceQuery.ResolveSelection(source.Summary, saved!);
            if (!resolved.Succeeded) throw new InvalidDataException(resolved.Refusal!.Message);
            selection = resolved.Value!.Selection;
            var assessments = new AssessmentRepository(database);
            root = assessments.FindLatestBaselineAssessment(connection, transaction,
                AssessmentKind.ParseTime.ToStoredKind(), JsonSerializer.Serialize(source.Baseline.Token, MotifJson.CreateOptions()),
                selection.Sha256, selection.Words, saved!.Name);
            if (root is not null)
            {
                var candidates = assessments.ReadReplacementHeaders(connection, transaction,
                    AssessmentKind.ParseTime.ToStoredKind(),
                    JsonSerializer.Serialize(source.Baseline.Token, MotifJson.CreateOptions()),
                    root.AssessmentId);
                replacements = assessments.ReadSelectedWords(connection, transaction,
                    AssessmentWordOverlay.ReplacementsFor(root, candidates), selection.Words);
                measurementIds = ExpectedContextRepository.ReadMeasurementIds(connection, transaction,
                    preliminary, root.Invocation?.InvocationId);
            }
        }

        var warnings = ExpectedContextRepository.ReadWarningIdentities(connection, transaction, preliminary, root);
        var descriptor = new SelectionEvidenceExpectation(
            matchesDefault ? "default" : "explicit",
            matchesDefault ? saved!.Name : null,
            matchesDefault ? saved!.TextIds : [],
            matchesDefault ? saved!.AddedWords : [],
            selection?.Sha256,
            root?.AssessmentId,
            replacements.Select(assessment => assessment.AssessmentId).ToArray(),
            measurementIds,
            warnings);
        ValidateSuppliedSnapshot(request.EvidenceSnapshot, source.Baseline, saved, matchesDefault, root,
            replacements, measurementIds, warnings);
        if (root is null)
            return new SelectionReadEvidence(descriptor.Origin, null, [], [], [], [], 0, 0, descriptor);
        var evidence = AssessmentEvidenceSet.Create(root, replacements);
        var wordRows = evidence.Components.Sum(component => component.Words?.Count ?? 0);
        return new SelectionReadEvidence(descriptor.Origin, root,
            replacements.Select(run => run.AssessmentId).ToArray(), evidence.Words, evidence.ObjectTimings,
            evidence.Components.SelectMany(run => run.Invocation?.GrammarWarningLines ?? [])
                .Distinct(StringComparer.Ordinal).ToArray(), evidence.Components.Count, wordRows, descriptor);
    }

    private static SelectionReadEvidence ResolveShownAssessment(MotifDatabase database, SqliteConnection connection,
        SqliteTransaction transaction, SelectionAssessmentEvidence shown, CurrentBaselineSelectionIndexRead source,
        IReadOnlyList<string> addedWords)
    {
        if (shown.Baseline != source.Baseline.Token) throw new EvidenceContextChangedException();
        var root = new AssessmentRepository(database).GetHeader(connection, transaction, shown.AssessmentId);
        if (root is null) throw new EvidenceContextChangedException();
        var named = new SelectionEvidenceExpectation("explicit", root.Selection.Name, [], [], root.Selection.Sha256,
            root.AssessmentId, Array.AsReadOnly(shown.ReplacementAssessmentIds.ToArray()),
            Array.AsReadOnly(shown.MeasurementAssessmentIds.ToArray()), []);
        return ResolveDisplayedEvidence(database, connection, transaction,
            new ExpectedContext(shown.Baseline) { SelectionEvidence = named }, source, addedWords, root,
            stampWarnings: true);
    }

    private static SelectionReadEvidence ResolveDisplayedEvidence(MotifDatabase database, SqliteConnection connection,
        SqliteTransaction transaction, ExpectedContext displayed, CurrentBaselineSelectionIndexRead source,
        IReadOnlyList<string> addedWords, AssessmentRecord? knownRoot = null, bool stampWarnings = false)
    {
        if (displayed.Baseline != source.Baseline.Token || displayed.SelectionEvidence is not { } named ||
            named.Origin is not ("default" or "explicit"))
            throw new EvidenceContextChangedException();
        var descriptor = named with
        {
            Origin = "explicit",
            DeclarationTextIds = Array.AsReadOnly(named.DeclarationTextIds.ToArray()),
            DeclarationAddedWords = Array.AsReadOnly(named.DeclarationAddedWords.ToArray()),
            ReplacementAssessmentIds = Array.AsReadOnly(named.ReplacementAssessmentIds.ToArray()),
            MeasurementAssessmentIds = Array.AsReadOnly(named.MeasurementAssessmentIds.ToArray()),
            WarningIdentities = Array.AsReadOnly(named.WarningIdentities.ToArray()),
        };
        var expected = displayed with { SelectionEvidence = descriptor };
        var repository = new AssessmentRepository(database);
        var root = knownRoot ?? (descriptor.RootAssessmentId is { } rootId
            ? repository.GetHeader(connection, transaction, rootId) : null);
        if (descriptor.RootAssessmentId is not null && (root is null ||
            !ExpectedContextRepository.MatchesNamedRoot(root, expected)))
            throw new EvidenceContextChangedException();
        if (root is null && descriptor.AssessmentSelectionSha256 is not null)
        {
            if (repository.FindLatestBaselineAssessmentHeader(connection, transaction,
                    AssessmentKind.ParseTime.ToStoredKind(), JsonSerializer.Serialize(displayed.Baseline,
                        MotifJson.CreateOptions()), descriptor.AssessmentSelectionSha256,
                    descriptor.DeclarationName ?? string.Empty) is not null)
                throw new EvidenceContextChangedException();
        }
        var replacements = root is null ? [] : AssessmentWordOverlay.ReplacementsFor(root,
            repository.ReadReplacementHeaders(connection, transaction, AssessmentKind.ParseTime.ToStoredKind(),
                root.BaselineToken, root.AssessmentId));
        var measurements = ExpectedContextRepository.ReadMeasurementIds(connection, transaction, expected,
            root?.Invocation?.InvocationId);
        var warnings = ExpectedContextRepository.ReadWarningIdentities(connection, transaction, expected, root);
        if (!descriptor.ReplacementAssessmentIds.SequenceEqual(replacements.Select(run => run.AssessmentId),
                StringComparer.Ordinal) ||
            !descriptor.MeasurementAssessmentIds.SequenceEqual(measurements, StringComparer.Ordinal) ||
            !stampWarnings && !descriptor.WarningIdentities.SequenceEqual(warnings, StringComparer.Ordinal))
            throw new EvidenceContextChangedException();
        if (stampWarnings) descriptor = descriptor with { WarningIdentities = Array.AsReadOnly(warnings.ToArray()) };
        if (root is null)
            return new SelectionReadEvidence("explicit", null, [], [], [], [], 0, 0, descriptor);
        var forms = source.Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens)
            .SelectMany(token => token.Forms).Select(form => Normalize(form.Text)).Concat(addedWords)
            .Where(form => form.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        var components = repository.ReadSelectedWords(connection, transaction,
            new[] { root }.Concat(replacements).ToArray(), forms);
        var evidence = AssessmentEvidenceSet.Create(components[0], components.Skip(1).ToArray());
        return new SelectionReadEvidence("explicit", components[0],
            replacements.Select(run => run.AssessmentId).ToArray(), evidence.Words, evidence.ObjectTimings,
            evidence.Components.SelectMany(run => run.Invocation?.GrammarWarningLines ?? [])
                .Distinct(StringComparer.Ordinal).ToArray(), evidence.Components.Count,
            evidence.Components.Sum(component => component.Words?.Count ?? 0), descriptor);
    }

    private static void ValidateSuppliedSnapshot(CurrentEvidenceSnapshot? snapshot, BaselineRecord baseline,
        NamedSelectionRecord? saved, bool matchesDefault, AssessmentRecord? root,
        IReadOnlyList<AssessmentRecord> replacements, IReadOnlyList<string> measurementIds,
        IReadOnlyList<string> warnings)
    {
        if (snapshot is null) return;
        if (snapshot.Baseline?.Token != baseline.Token || !SameDeclaration(snapshot.DefaultSelection, saved))
            throw new EvidenceContextChangedException();
        if (!matchesDefault) return;
        var replacementIds = replacements.Select(run => run.AssessmentId).ToArray();
        var snapshotReplacementIds = snapshot.RerunAssessments.Select(run => run.AssessmentId).ToArray();
        var snapshotMeasurements = new[]
            { snapshot.MatchingCorrectnessAssessmentId, snapshot.MatchingObjectTimingAssessmentId }
            .Where(id => id is not null).Select(id => id!).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var expectedMeasurements = measurementIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        if (snapshot.MatchingAssessment?.AssessmentId != root?.AssessmentId ||
            !snapshotReplacementIds.SequenceEqual(replacementIds, StringComparer.Ordinal) ||
            !snapshotMeasurements.SequenceEqual(expectedMeasurements, StringComparer.Ordinal))
            throw new EvidenceContextChangedException();
        var snapshotWarnings = new List<string>();
        if (root?.Invocation?.GrammarWarnings is { Length: > 0 } grammarWarnings)
            snapshotWarnings.Add("grammar:" + ExpectedContextRepository.Digest(grammarWarnings));
        if (snapshot.LastParserRefusal is { } refusal)
            snapshotWarnings.Add("refusal:" + ExpectedContextRepository.Digest(
                JsonSerializer.Serialize(refusal, MotifJson.CreateOptions())));
        snapshotWarnings.Sort(StringComparer.Ordinal);
        if (!warnings.SequenceEqual(snapshotWarnings, StringComparer.Ordinal))
            throw new EvidenceContextChangedException();
    }

    private static bool SameDeclaration(NamedSelectionRecord? left, NamedSelectionRecord? right) =>
        left is null ? right is null : right is not null && left.Name == right.Name &&
        left.TextIds.SequenceEqual(right.TextIds) &&
        left.AddedWords.SequenceEqual(right.AddedWords, StringComparer.Ordinal);

    private CommandOutcome<SelectionRead<SelectionSummary>> ReadSummary(
        SelectionViewRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!BeginRead(out var context, out var index, out var busy))
            return busy ? ReaderBusy<SelectionSummary>() : ReaderUnavailable();
        using var repositoryReads = RepositoryReadCounters.BeginScope();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            if (!IsCurrent(context)) return EvidenceChanged();

            linked.Token.ThrowIfCancellationRequested();
            if (request.ReadState is null)
            {
                var value = index.Build(request);
                return CommandOutcome<SelectionRead<SelectionSummary>>.Success(CreateRead(context, value));
            }

            var storeGeneration = ReadPresentationStoreGeneration();
            var readState = ReadCurrentReadState(context, linked.Token, RecordPresentationDetailReads);
            if (!readState.Succeeded)
                return CommandOutcome<SelectionRead<SelectionSummary>>.Refused(readState.Refusal!);
            var readOccurrences = readState.Value!.Occurrences;
            var readSet = readOccurrences.Values.SelectMany(occurrences => occurrences).ToHashSet();
            var summary = index.Build(request, readSet);
            var readDigest = Digest(context.TextIds.Select(textId => new
            {
                TextId = textId,
                Occurrences = readOccurrences[textId].OrderBy(item => item.ParagraphId)
                    .ThenBy(item => item.SegmentId).ThenBy(item => item.Index).ToArray(),
            }));
            var stamped = CreateReadStateFilteredSummary(context, summary, readDigest, storeGeneration);
            return stamped is null
                ? PresentationSuperseded<SelectionSummary>()
                : CommandOutcome<SelectionRead<SelectionSummary>>.Success(stamped);
        }
        catch (OperationCanceledException)
        {
            return CommandOutcome<SelectionRead<SelectionSummary>>.Refused(new Refusal(
                "selection-reader.cancelled", FailureReason.Cancelled, "Reading the Selection summary was cancelled."));
        }
        catch (InvalidDataException exception)
        {
            return CommandOutcome<SelectionRead<SelectionSummary>>.Refused(new Refusal(
                "baseline.text-words-unreadable", FailureReason.StoreInconsistent, exception.Message));
        }
        catch (SelectionReadOwnershipException)
        {
            return OwnershipLimit<SelectionSummary>();
        }
        catch (ObjectDisposedException)
        {
            return ReaderUnavailable<SelectionSummary>();
        }
        catch (InvalidOperationException) when (IsObsolete())
        {
            return EvidenceChanged<SelectionSummary>();
        }
        finally
        {
            RecordRepositoryReads(repositoryReads);
            EndRead();
        }
    }

    private CommandOutcome<ReadStateSnapshot> ReadCurrentReadState(SelectionReadContext context,
        CancellationToken cancellationToken, Action<TextWordsProjection>? onProjectionRead = null)
    {
        var saved = new ReadStateRepository(_database).GetForTexts(context.TextIds);
        var savedTexts = saved.Select(record => record.Occurrence.TextId).ToHashSet();
        var readOccurrences = new Dictionary<Guid, IReadOnlyList<OccurrenceAnchor>>();
        foreach (var textId in context.TextIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!savedTexts.Contains(textId))
            {
                readOccurrences.Add(textId, []);
                continue;
            }
            var read = ReadStateCommands.ReadValid(new WordReadStateRequest(_projectPath!, textId)
            {
                AssessmentIds = context.AssessmentRootId is { } rootId
                    ? new[] { rootId }.Concat(context.ReplacementAssessmentIds).Distinct(StringComparer.Ordinal)
                        .ToArray()
                    : [],
                ExpectedContext = context.ExpectedWriteContext(),
            }, cancellationToken, onProjectionRead);
            if (!read.Succeeded)
                return CommandOutcome<ReadStateSnapshot>.Refused(read.Refusal!);
            readOccurrences.Add(textId, read.Value!.ReadOccurrences);
        }
        return CommandOutcome<ReadStateSnapshot>.Success(new ReadStateSnapshot(readOccurrences));
    }

    private SelectionRead<SelectionSummary>? CreateReadStateFilteredSummary(
        SelectionReadContext context, SelectionSummary summary, string readDigest, long storeGeneration)
    {
        lock (_gate)
        {
            ThrowIfPublicationClosedLocked();
            if (ReadPresentationStoreGenerationLocked() != storeGeneration) return null;
            var stamp = _presentationStamp;
            if (_readStateDigest is not null && _readStateDigest != readDigest) stamp++;
            var read = CreateReadLocked(context, summary, SelectionReadPurpose.Visible, null, stamp);
            _presentationStamp = stamp;
            _readStateDigest = readDigest;
            return read;
        }
    }

    private CommandOutcome<SelectionRead<WordOccurrences>> ReadOccurrences(
        TextWordKey word, OccurrenceRange range, SelectionReadPurpose purpose, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(range);
        if (!Enum.IsDefined(purpose)) return InvalidRange<WordOccurrences>("A valid result ownership purpose is required.");
        try { range.Validate(); }
        catch (ArgumentOutOfRangeException exception)
        {
            return InvalidRange<WordOccurrences>(exception.Message);
        }
        if (!BeginRead(out var context, out var index, out var busy))
            return busy ? ReaderBusy<WordOccurrences>() : ReaderUnavailable<WordOccurrences>();
        using var repositoryReads = RepositoryReadCounters.BeginScope();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            if (!IsCurrent(context)) return EvidenceChanged<WordOccurrences>();
            var cacheKey = new OccurrenceCacheKey(word, range.Start, range.Count);
            if (TryOccurrenceCache(cacheKey, out var cached))
            {
                if (!IsCurrent(context)) return EvidenceChanged<WordOccurrences>();
                return CommandOutcome<SelectionRead<WordOccurrences>>.Success(CreateRead(context, cached, purpose,
                    OccurrenceResource(cacheKey, cached)));
            }

            var locations = index.Occurrences(word);
            var selected = locations.Skip(range.Start).Take(range.Count).ToArray();
            var candidateWordforms = word.WordformId is null ? index.WordformCandidates(word.Form) : [word];
            if (candidateWordforms.Count > 64)
                throw new SelectionReadOwnershipException("The spelling has too many exact wordforms to retain at once.");
            var wordforms = LoadWordforms(context, index, candidateWordforms, linked.Token);
            if (wordforms is null) return EvidenceChanged<WordOccurrences>();
            foreach (var (key, wordform) in wordforms)
                if (key.WordformId is { } wordformId) index.ValidateWordformDetail(wordformId, wordform);

            WordContextResponse? wordContext = null;
            if (word.WordformId is null && _projectPath is { Length: > 0 } projectPath)
            {
                var contextRead = WordContextQuery.Query(new WordContextRequest(projectPath, word.Form,
                    context.Baseline));
                if (!contextRead.Succeeded)
                    return CommandOutcome<SelectionRead<WordOccurrences>>.Refused(contextRead.Refusal!);
                wordContext = contextRead.Value!;
                if (wordContext?.Analyses.Count > 256)
                    throw new SelectionReadOwnershipException("The spelling has too much Baseline word context to retain at once.");
            }
            var details = new List<WordOccurrenceDetail>(selected.Length);
            foreach (var group in selected.GroupBy(location => location.Anchor.TextId))
            {
                linked.Token.ThrowIfCancellationRequested();
                var current = LoadText(context, group.Key, linked.Token);
                if (current is null) return EvidenceChanged<WordOccurrences>();
                var text = current.Texts.SingleOrDefault(item => item.TextId == group.Key)
                    ?? throw DamagedSelection("A selected Text row is missing.");
                var wordform = wordforms.GetValueOrDefault(word)
                    ?? (word.WordformId is null ? null : throw DamagedSelection("A selected wordform has no stored detail row."));
                foreach (var location in group)
                {
                    var line = text.Lines.ElementAtOrDefault(location.LineIndex);
                    var token = line?.Tokens.ElementAtOrDefault(location.TokenOffset);
                    if (line is null || token is null || line.ParagraphId != location.Anchor.ParagraphId ||
                        line.SegmentId != location.Anchor.SegmentId || token.OccurrenceIndex != location.Anchor.Index ||
                        token.WordformId != word.WordformId || location.Status != token.Status ||
                        location.AnalysisId != token.AnalysisId || !token.Forms.Any(form =>
                            Normalize(form.Text) == word.Form && form.WritingSystem == word.WritingSystem))
                        throw DamagedSelection("A compact occurrence no longer matches its captured Text row.");
                    var analysis = token.AnalysisKey is null ? null : text.Analyses.FirstOrDefault(item =>
                        item.Key == token.AnalysisKey && item.AnalysisId == token.AnalysisId);
                    if (token.AnalysisKey is not null &&
                        (analysis is null || wordform is null || !wordform.Analyses.Any(item =>
                            item.Key == token.AnalysisKey && item.AnalysisId == token.AnalysisId)))
                        throw DamagedSelection("A selected analysis disagrees with its compact wordform identity.");
                    details.Add(new WordOccurrenceDetail(location, line.Sentence, line.SentenceStyle,
                        line.SentenceWritingSystem, token, analysis, wordform));
                }
            }

            var value = new WordOccurrences(word, index.WordSummary(word), range.Start, locations.Count, details);
            value = value with
            {
                Wordforms = wordforms,
                WordContext = wordContext,
                CandidateWordforms = candidateWordforms,
            };
            _pauseAt?.Invoke(SelectionReaderPausePoint.BeforeCachePublication);
            if (!IsCurrent(context)) return EvidenceChanged<WordOccurrences>();
            if (!StoreOccurrenceCache(cacheKey, value))
                return ReaderUnavailable<WordOccurrences>();
            return CommandOutcome<SelectionRead<WordOccurrences>>.Success(CreateRead(context, value, purpose,
                OccurrenceResource(cacheKey, value)));
        }
        catch (SelectionReadOwnershipException)
        {
            return OwnershipLimit<WordOccurrences>();
        }
        catch (OperationCanceledException)
        {
            return Cancelled<WordOccurrences>("Reading word occurrences was cancelled.");
        }
        catch (InvalidDataException exception)
        {
            return Unreadable<WordOccurrences>(exception.Message);
        }
        catch (ObjectDisposedException)
        {
            return ReaderUnavailable<WordOccurrences>();
        }
        catch (InvalidOperationException) when (IsObsolete())
        {
            return EvidenceChanged<WordOccurrences>();
        }
        finally
        {
            RecordRepositoryReads(repositoryReads);
            EndRead();
        }
    }

    private CommandOutcome<SelectionRead<SelectionWordDetails>> ReadWordDetails(
        IReadOnlyList<TextWordKey> words, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(words);
        if (words.Count > 128)
            return InvalidRange<SelectionWordDetails>("At most 128 visible word identities may be read together.");
        if (!BeginRead(out var context, out var index, out var busy))
            return busy ? ReaderBusy<SelectionWordDetails>() : ReaderUnavailable<SelectionWordDetails>();
        using var repositoryReads = RepositoryReadCounters.BeginScope();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            if (!IsCurrent(context)) return EvidenceChanged<SelectionWordDetails>();
            var summaries = words.Distinct().ToDictionary(word => word, index.WordSummary);
            if (summaries.Values.Any(summary => summary is null))
                return InvalidRange<SelectionWordDetails>("A visible word identity is absent from this Selection.");
            var targets = new Dictionary<TextWordKey, TextWordKey>();
            foreach (var (key, summary) in summaries)
            {
                if (key.WordformId is not null) targets.Add(key, key);
                else if (summary!.Actions.CandidateWordformIds is { Count: 1 } candidates)
                {
                    var exact = index.WordformCandidates(key.Form).First(candidate =>
                        candidate.WordformId == candidates[0]);
                    targets.Add(key, exact);
                }
            }
            var keys = targets.Values.Distinct().ToArray();
            var details = LoadWordforms(context, index, keys, linked.Token);
            if (details is null) return EvidenceChanged<SelectionWordDetails>();
            foreach (var (key, wordform) in details)
                index.ValidateWordformDetail(key.WordformId!.Value, wordform);
            _pauseAt?.Invoke(SelectionReaderPausePoint.BeforeCachePublication);
            if (!IsCurrent(context)) return EvidenceChanged<SelectionWordDetails>();
            var value = new SelectionWordDetails(targets.ToDictionary(target => target.Key,
                target => details[target.Value]));
            return CommandOutcome<SelectionRead<SelectionWordDetails>>.Success(CreateRead(context, value,
                SelectionReadPurpose.Visible, new SelectionLeaseResource(SelectionReadPurpose.Visible,
                    null, 0, details.Keys.ToHashSet(), null, 0)));
        }
        catch (SelectionReadOwnershipException)
        {
            return OwnershipLimit<SelectionWordDetails>();
        }
        catch (OperationCanceledException)
        {
            return Cancelled<SelectionWordDetails>("Reading visible word detail was cancelled.");
        }
        catch (InvalidDataException exception)
        {
            return Unreadable<SelectionWordDetails>(exception.Message);
        }
        catch (ObjectDisposedException)
        {
            return ReaderUnavailable<SelectionWordDetails>();
        }
        catch (InvalidOperationException) when (IsObsolete())
        {
            return EvidenceChanged<SelectionWordDetails>();
        }
        finally
        {
            RecordRepositoryReads(repositoryReads);
            EndRead();
        }
    }

    private CommandOutcome<SelectionRead<SelectionSentenceContext>> ReadSentence(
        OccurrenceAnchor occurrence, int? tokenOffset, int tokenCount, CancellationToken cancellationToken)
    {
        if (occurrence.TextId == Guid.Empty || occurrence.ParagraphId == Guid.Empty ||
            occurrence.SegmentId == Guid.Empty || occurrence.Index < 0 || tokenOffset < 0 ||
            tokenCount is < 1 or > 64)
            return InvalidRange<SelectionSentenceContext>("Sentence context requires an exact anchor and at most 64 tokens.");
        if (!BeginRead(out var context, out _, out var busy))
            return busy ? ReaderBusy<SelectionSentenceContext>() : ReaderUnavailable<SelectionSentenceContext>();
        using var repositoryReads = RepositoryReadCounters.BeginScope();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            if (!IsCurrent(context)) return EvidenceChanged<SelectionSentenceContext>();
            var current = LoadText(context, occurrence.TextId, linked.Token, allowMissing: true);
            if (current is null) return EvidenceChanged<SelectionSentenceContext>();
            var text = current.Texts.SingleOrDefault(item => item.TextId == occurrence.TextId);
            var line = text?.Lines.SingleOrDefault(item => item.ParagraphId == occurrence.ParagraphId &&
                item.SegmentId == occurrence.SegmentId);
            var target = line?.Tokens.Select((token, offset) => (token, offset)).FirstOrDefault(item =>
                item.token.OccurrenceIndex == occurrence.Index && item.token.WordformId is not null);
            if (line is null || target?.token is null)
                return CommandOutcome<SelectionRead<SelectionSentenceContext>>.Refused(new Refusal(
                    "texts.occurrence-not-found", FailureReason.Refused,
                    "The exact source occurrence is absent from this Baseline."));
            var start = tokenOffset ?? target.Value.offset / tokenCount * tokenCount;
            var tokens = line.Tokens.Skip(start).Take(tokenCount).Select(token => new SelectionSentenceToken(
                token.Text, token.TextWritingSystem, token.Forms.FirstOrDefault()?.Text is { } form ? Normalize(form) : null,
                token.WordformId is null ? null : new OccurrenceAnchor(occurrence.TextId, line.ParagraphId,
                    line.SegmentId, token.OccurrenceIndex))).ToArray();
            var value = new SelectionSentenceContext(occurrence.TextId, text!.Title, line.Number, line.SentenceStyle,
                start, line.Tokens.Count, target.Value.offset, tokens);
            _pauseAt?.Invoke(SelectionReaderPausePoint.BeforeCachePublication);
            if (!IsCurrent(context)) return EvidenceChanged<SelectionSentenceContext>();
            return CommandOutcome<SelectionRead<SelectionSentenceContext>>.Success(CreateRead(context, value,
                SelectionReadPurpose.Pin, new SelectionLeaseResource(SelectionReadPurpose.Pin,
                    new LineCacheKey(occurrence.TextId, line.Number, 1, start, tokenCount), tokens.Length,
                    new HashSet<TextWordKey>(), null, 0)));
        }
        catch (SelectionReadOwnershipException)
        {
            return OwnershipLimit<SelectionSentenceContext>();
        }
        catch (OperationCanceledException)
        {
            return Cancelled<SelectionSentenceContext>("Reading sentence context was cancelled.");
        }
        catch (InvalidDataException exception)
        {
            return Unreadable<SelectionSentenceContext>(exception.Message);
        }
        catch (ObjectDisposedException)
        {
            return ReaderUnavailable<SelectionSentenceContext>();
        }
        catch (InvalidOperationException) when (IsObsolete())
        {
            return EvidenceChanged<SelectionSentenceContext>();
        }
        finally
        {
            RecordRepositoryReads(repositoryReads);
            EndRead();
        }
    }

    private CommandOutcome<SelectionRead<TextLineSlice>> ReadLinePages(
        Guid textId, IReadOnlyList<TextLinePage> pages, CancellationToken cancellationToken)
    {
        if (pages.Count is < 1 or > 48 || pages.Any(page => page is null || page.LineNumber < 1 || page.TokenOffset < 0 ||
                page.TokenCount is < 0 or > 20) || pages.Sum(page => (long)page.TokenCount) > 512 ||
            pages.Select(page => page.LineNumber).Distinct().Count() != pages.Count)
            return InvalidRange<TextLineSlice>("Visible pages require at most 48 unique lines and 512 source tokens.");
        var ordered = pages.OrderBy(page => page.LineNumber).ToArray();
        return ReadLines(textId, new TextLineRange(ordered.FirstOrDefault()?.LineNumber ?? 1,
            ordered.Length, 0, ordered.Sum(page => page.TokenCount)), SelectionReadPurpose.Visible,
            cancellationToken, ordered);
    }

    private CommandOutcome<SelectionRead<TextLineSlice>> ReadLines(
        Guid textId, TextLineRange range, SelectionReadPurpose purpose, CancellationToken cancellationToken,
        IReadOnlyList<TextLinePage>? pages = null)
    {
        ArgumentNullException.ThrowIfNull(range);
        if (!Enum.IsDefined(purpose)) return InvalidRange<TextLineSlice>("A valid result ownership purpose is required.");
        try { range.Validate(); }
        catch (ArgumentOutOfRangeException exception)
        {
            return InvalidRange<TextLineSlice>(exception.Message);
        }
        if (textId == Guid.Empty) return InvalidRange<TextLineSlice>("A Text identity is required.");
        if (!BeginRead(out var context, out var index, out var busy))
            return busy ? ReaderBusy<TextLineSlice>() : ReaderUnavailable<TextLineSlice>();
        using var repositoryReads = RepositoryReadCounters.BeginScope();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            if (!IsCurrent(context)) return EvidenceChanged<TextLineSlice>();
            var summary = index.Text(textId);
            if (summary is null) return InvalidRange<TextLineSlice>("The Text is not in this Selection.");
            if (pages is not null && pages.Any(page => !summary.Lines.Any(line =>
                    line.Number == page.LineNumber && page.TokenOffset <= line.TokenCount)))
                return InvalidRange<TextLineSlice>("A visible page is outside its captured source line.");
            var cacheKey = new LineCacheKey(textId, range.StartLine, range.LineCount, range.TokenOffset,
                range.TokenLimit, pages is null ? null : Digest(pages));
            if (TryLineCache(cacheKey, out var cached))
            {
                if (!IsCurrent(context)) return EvidenceChanged<TextLineSlice>();
                return CommandOutcome<SelectionRead<TextLineSlice>>.Success(CreateRead(context, cached, purpose,
                    LineResource(cacheKey, cached)));
            }

            var current = LoadText(context, textId, linked.Token);
            if (current is null) return EvidenceChanged<TextLineSlice>();
            var text = current.Texts.SingleOrDefault(item => item.TextId == textId)
                ?? throw DamagedSelection("A selected Text row is missing.");
            var value = pages is null ? SliceLines(text, range, out var wordKeys) :
                SliceLinePages(text, range, pages, out wordKeys);
            var wordforms = LoadWordforms(context, index, wordKeys, linked.Token);
            if (wordforms is null) return EvidenceChanged<TextLineSlice>();
            foreach (var (key, wordform) in wordforms)
                if (key.WordformId is { } wordformId) index.ValidateWordformDetail(wordformId, wordform);
            foreach (var line in value.Lines)
            foreach (var item in line.Tokens)
                ValidateCompactToken(index, textId, line, item.Token);
            value = value with
            {
                Wordforms = wordforms,
                Assessments = index.AssessmentFacts(wordKeys.Select(key => key.Form)),
            };
            _pauseAt?.Invoke(SelectionReaderPausePoint.BeforeCachePublication);
            if (!IsCurrent(context)) return EvidenceChanged<TextLineSlice>();
            if (!StoreLineCache(cacheKey, value))
                return ReaderUnavailable<TextLineSlice>();
            return CommandOutcome<SelectionRead<TextLineSlice>>.Success(CreateRead(context, value, purpose,
                LineResource(cacheKey, value)));
        }
        catch (SelectionReadOwnershipException)
        {
            return OwnershipLimit<TextLineSlice>();
        }
        catch (OperationCanceledException)
        {
            return Cancelled<TextLineSlice>("Reading Text lines was cancelled.");
        }
        catch (InvalidDataException exception)
        {
            return Unreadable<TextLineSlice>(exception.Message);
        }
        catch (ObjectDisposedException)
        {
            return ReaderUnavailable<TextLineSlice>();
        }
        catch (InvalidOperationException) when (IsObsolete())
        {
            return EvidenceChanged<TextLineSlice>();
        }
        finally
        {
            RecordRepositoryReads(repositoryReads);
            EndRead();
        }
    }

    private CurrentBaselineTextRows? LoadText(
        SelectionReadContext context, Guid textId, CancellationToken cancellationToken, bool allowMissing = false)
    {
        lock (_textReadGate)
        {
            TextWordsProjectedText? cachedText;
            lock (_gate) cachedText = _cachedTextId == textId ? _cachedText : null;
            if (cachedText is not null)
            {
                var cachedBaseline = new BaselineRepository(_database).GetCurrent(context.ProjectKey);
                if (cachedBaseline is null || cachedBaseline.Token != context.Baseline)
                {
                    MarkObsolete();
                    return null;
                }
                return new CurrentBaselineTextRows(cachedBaseline, [cachedText], 0);
            }
            var current = new BaselineRepository(_database).GetCurrentTextRows(context.ProjectKey, [textId],
                context.Baseline, cancellationToken);
            if (current is null)
            {
                MarkObsolete();
                return null;
            }
            if (current.Texts.Count == 0)
            {
                if (allowMissing) return current;
                throw DamagedSelection("A selected Text row is missing.");
            }
            lock (_gate)
            {
                ThrowIfPublicationClosedLocked();
                ObjectDisposedException.ThrowIf(_disposed, this);
                _cachedTextId = textId;
                _cachedText = current.Texts[0];
            }
            Interlocked.Add(ref _textRowsDeserialized, current.Texts.Count);
            Interlocked.Add(ref _sourceLines, current.Texts.Sum(text => text.Lines.Count));
            Interlocked.Add(ref _sourceTokens, current.Texts.Sum(text => text.Lines.Sum(line => line.Tokens.Count)));
            Interlocked.Add(ref _textAnalyses, current.Texts.Sum(text => text.Analyses.Count));
            Interlocked.Add(ref _morphs, current.Texts.Sum(text => text.Analyses.Sum(analysis => analysis.Morphs.Count)));
            return current;
        }
    }

    private IReadOnlyDictionary<TextWordKey, TextWordsProjectedWordform>? LoadWordforms(
        SelectionReadContext context, SelectionIndexBuilder index, IReadOnlyCollection<TextWordKey> keys,
        CancellationToken cancellationToken)
    {
        var ids = keys.Select(key => key.WordformId).OfType<Guid>().Distinct().ToArray();
        var byId = new Dictionary<Guid, TextWordsProjectedWordform>();
        var missing = new List<Guid>();
        lock (_gate)
        {
            foreach (var id in ids)
            {
                var cached = _wordDetailCache.FirstOrDefault(pair => pair.Key.WordformId == id).Value
                    ?? _leaseResources.Values.SelectMany(resource => resource.WordDetails.Values)
                        .FirstOrDefault(wordform => wordform.WordformId == id);
                if (cached is null) missing.Add(id);
                else byId.Add(id, cached);
            }
        }
        if (missing.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rows = new BaselineRepository(_database).GetCurrentWordformRows(
                context.ProjectKey, context.Baseline, missing.Where(id => index.HasTextWordform(id)).ToArray(),
                cancellationToken);
            if (rows is null)
            {
                MarkObsolete();
                return null;
            }
            foreach (var wordform in rows.Wordforms) byId.Add(wordform.WordformId, wordform);
            var outsideTexts = missing.Where(id => !byId.ContainsKey(id)).ToHashSet();
            if (outsideTexts.Count > 0)
            {
                using var baseline = BaselineReadCache.Open(_baselineFwDataPath);
                Interlocked.Increment(ref _baselineWordDetailCachesOpened);
                foreach (var wordform in baseline.Cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!outsideTexts.Contains(wordform.Guid)) continue;
                    var detail = TextWordsProjectionBuilder.ReadWordform(baseline.Cache, wordform);
                    byId.Add(wordform.Guid, detail);
                    Interlocked.Increment(ref _wordformRowsDeserialized);
                    Interlocked.Add(ref _wordformAnalyses, detail.Analyses.Count);
                    Interlocked.Add(ref _morphs, detail.Analyses.Sum(analysis => analysis.Morphs.Count));
                }
                if (outsideTexts.Any(id => !byId.ContainsKey(id)))
                    throw DamagedSelection("An added-word identity is missing from the exact Baseline.");
            }
            Interlocked.Add(ref _wordformRowsDeserialized, rows.Wordforms.Count);
            Interlocked.Add(ref _wordformAnalyses, rows.Wordforms.Sum(wordform => wordform.Analyses.Count));
            Interlocked.Add(ref _morphs, rows.Wordforms.Sum(wordform =>
                wordform.Analyses.Sum(analysis => analysis.Morphs.Count)));
        }

        var result = new Dictionary<TextWordKey, TextWordsProjectedWordform>();
        foreach (var key in keys.Distinct())
            if (key.WordformId is { } id && byId.TryGetValue(id, out var wordform)) result.Add(key, wordform);
        CacheWordDetails(result, missing.ToHashSet());
        return result;
    }

    private bool IsCurrent(SelectionReadContext context)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: true);
        var current = ExpectedContextRepository.IsCurrentContext(_database, connection, transaction,
            context.ProjectKey, context.ExpectedWriteContext());
        _pauseAt?.Invoke(SelectionReaderPausePoint.AfterContextValidation);
        if (current)
        {
            transaction.Commit();
            return true;
        }
        transaction.Commit();
        MarkObsolete();
        return false;
    }

    private static TextLineSlice SliceLinePages(TextWordsProjectedText text, TextLineRange range,
        IReadOnlyList<TextLinePage> pages, out HashSet<TextWordKey> selectedKeys)
    {
        selectedKeys = [];
        var fragments = new List<TextLineFragment>(pages.Count);
        foreach (var page in pages)
        {
            var part = SliceLines(text, new TextLineRange(page.LineNumber, 1, page.TokenOffset,
                page.TokenCount), out var keys);
            if (part.Lines.Count != 1 || part.Lines[0].LineNumber != page.LineNumber)
                throw DamagedSelection("A displayed line is missing from the captured Text detail.");
            fragments.AddRange(part.Lines);
            selectedKeys.UnionWith(keys);
        }
        return new TextLineSlice(text.TextId, range.StartLine, fragments.Count, 0, range.TokenLimit,
            fragments.Sum(line => line.SourceTokenCount), fragments.Sum(line => line.Tokens.Count),
            fragments, new Dictionary<TextWordKey, TextWordsProjectedWordform>(),
            new Dictionary<string, SelectionAssessmentFacts>(), null, null);
    }

    private static TextLineSlice SliceLines(
        TextWordsProjectedText text, TextLineRange range, out HashSet<TextWordKey> selectedKeys)
    {
        var sourceLines = text.Lines.Where(line => line.Number >= range.StartLine)
            .Take(range.LineCount).ToArray();
        var remaining = range.TokenLimit;
        var returned = 0;
        var lines = new List<TextLineFragment>(sourceLines.Length);
        var keys = new HashSet<TextWordKey>();
        selectedKeys = keys;
        int? nextLine = null;
        int? nextOffset = null;
        var first = true;
        for (var lineIndex = 0; lineIndex < sourceLines.Length; lineIndex++)
        {
            var line = sourceLines[lineIndex];
            var offset = first ? range.TokenOffset : 0;
            first = false;
            if (offset > line.Tokens.Count)
            {
                nextLine = line.Number;
                nextOffset = line.Tokens.Count;
                break;
            }
            var available = line.Tokens.Count - offset;
            var take = Math.Min(available, remaining);
            var tokens = line.Tokens.Skip(offset).Take(take).Select(token =>
            {
                if (token.WordformId is { } wordformId)
                    foreach (var form in token.Forms)
                        if (!string.IsNullOrWhiteSpace(form.Text))
                            keys.Add(new TextWordKey(wordformId, Normalize(form.Text), form.WritingSystem));
                var analysis = token.AnalysisKey is null ? null : text.Analyses.FirstOrDefault(item =>
                    item.Key == token.AnalysisKey);
                return new TextLineToken(token, analysis);
            }).ToArray();
            lines.Add(new TextLineFragment(line.Number, line.ParagraphId, line.SegmentId, line.ParseIsCurrent,
                line.Sentence, line.SentenceStyle, line.SentenceWritingSystem, offset, line.Tokens.Count, tokens));
            returned += take;
            remaining -= take;
            if (take < available)
            {
                nextLine = line.Number;
                nextOffset = offset + take;
                break;
            }
            if (remaining == 0 && lineIndex + 1 < sourceLines.Length)
            {
                var next = sourceLines.FirstOrDefault(candidate => candidate.Number > line.Number);
                if (next is not null)
                {
                    nextLine = next.Number;
                    nextOffset = 0;
                    break;
                }
            }
        }
        if (nextLine is null && range.LineCount > 0 && lines.Count == range.LineCount &&
            lines[^1].LineNumber < text.Lines.Count)
        {
            nextLine = lines[^1].LineNumber + 1;
            nextOffset = 0;
        }
        return new TextLineSlice(text.TextId, range.StartLine, lines.Count, range.TokenOffset, range.TokenLimit,
            sourceLines.Sum(line => line.Tokens.Count), returned, lines,
            new Dictionary<TextWordKey, TextWordsProjectedWordform>(),
            new Dictionary<string, SelectionAssessmentFacts>(StringComparer.Ordinal), nextLine, nextOffset);
    }

    private static void ValidateCompactToken(SelectionIndexBuilder index, Guid textId,
        TextLineFragment line, TextWordsProjectedToken token)
    {
        if (token.WordformId is null) return;
        var location = index.Occurrence(new OccurrenceAnchor(textId, line.ParagraphId, line.SegmentId,
            token.OccurrenceIndex));
        if (location is null || location.Status != token.Status || location.AnalysisId != token.AnalysisId)
            throw DamagedSelection("A selected token disagrees with its compact occurrence identity.");
    }

    private void CacheWordDetails(IReadOnlyDictionary<TextWordKey, TextWordsProjectedWordform> details,
        IReadOnlySet<Guid> fetched)
    {
        lock (_gate)
        {
            ThrowIfPublicationClosedLocked();
            var admitted = details.Where(pair => _wordDetailCache.ContainsKey(pair.Key) ||
                fetched.Contains(pair.Value.WordformId)).OrderBy(pair => fetched.Contains(pair.Value.WordformId)).ToArray();
            foreach (var (key, wordform) in admitted)
            {
                if (_wordDetailCache.Remove(key)) _wordDetailLru.Remove(key);
                _wordDetailCache.Add(key, wordform);
                _wordDetailLru.AddLast(key);
            }
            while (_wordDetailCache.Count > 32)
            {
                var oldest = _wordDetailLru.First!.Value;
                _wordDetailLru.RemoveFirst();
                _wordDetailCache.Remove(oldest);
            }
        }
    }

    private bool TryLineCache(LineCacheKey key, out TextLineSlice value)
    {
        lock (_gate)
        {
            if (!_lineCache.TryGetValue(key, out value!)) return false;
            _lineLru.Remove(key);
            _lineLru.AddLast(key);
            return true;
        }
    }

    private bool StoreLineCache(LineCacheKey key, TextLineSlice value)
    {
        lock (_gate)
        {
            if (_disposed || _obsolete) return false;
            _lineCache[key] = value;
            _lineLru.Remove(key);
            _lineLru.AddLast(key);
            while (_lineCache.Count > 3 || _lineCache.Values.Sum(slice => slice.ReturnedTokens) > 1536)
            {
                var oldest = _lineLru.First!.Value;
                _lineLru.RemoveFirst();
                _lineCache.Remove(oldest);
            }
            return true;
        }
    }

    private bool TryOccurrenceCache(OccurrenceCacheKey key, out WordOccurrences value)
    {
        lock (_gate)
        {
            if (!_occurrenceCache.TryGetValue(key, out value!)) return false;
            _occurrenceLru.Remove(key);
            _occurrenceLru.AddLast(key);
            return true;
        }
    }

    private bool StoreOccurrenceCache(OccurrenceCacheKey key, WordOccurrences value)
    {
        lock (_gate)
        {
            if (_disposed || _obsolete) return false;
            _occurrenceCache[key] = value;
            _occurrenceLru.Remove(key);
            _occurrenceLru.AddLast(key);
            while (_occurrenceCache.Count > 2)
            {
                var oldest = _occurrenceLru.First!.Value;
                _occurrenceLru.RemoveFirst();
                _occurrenceCache.Remove(oldest);
            }
            return true;
        }
    }

    private static SelectionLeaseResource LineResource(LineCacheKey key, TextLineSlice value) =>
        new(SelectionReadPurpose.Visible, key, value.ReturnedTokens, value.Wordforms.Keys.ToHashSet(), null, 0);

    private static SelectionLeaseResource OccurrenceResource(OccurrenceCacheKey key, WordOccurrences value) =>
        new(SelectionReadPurpose.Visible, null, 0,
            value.Wordforms.Keys.Append(value.Word).ToHashSet(), key,
            value.Occurrences.Count);

    private SelectionRead<T> CreateRead<T>(SelectionReadContext context, T value,
        SelectionReadPurpose purpose = SelectionReadPurpose.Visible, SelectionLeaseResource? resource = null)
    {
        lock (_gate)
            return CreateReadLocked(context, value, purpose, resource, _presentationStamp);
    }

    private SelectionRead<T> CreateReadLocked<T>(SelectionReadContext context, T value,
        SelectionReadPurpose purpose, SelectionLeaseResource? resource, long presentationStamp)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_obsolete) throw new InvalidOperationException("The Selection reader's evidence is obsolete.");
        resource ??= new SelectionLeaseResource(purpose, null, 0, new HashSet<TextWordKey>(), null, 0);
        resource = resource with
        {
            Purpose = purpose,
            WordDetails = value switch
            {
                SelectionWordDetails details => details.Wordforms,
                TextLineSlice lines => lines.Wordforms,
                WordOccurrences occurrences => occurrences.Wordforms,
                _ => new Dictionary<TextWordKey, TextWordsProjectedWordform>(),
            },
        };
        var resources = _leaseResources.Values.Append(resource).ToArray();
        if (_leases.Count >= MaximumActiveResults ||
            purpose == SelectionReadPurpose.Prefetch &&
                resources.Count(item => item.Purpose == SelectionReadPurpose.Prefetch) > MaximumPrefetchResults ||
            purpose == SelectionReadPurpose.Pin &&
                resources.Count(item => item.Purpose == SelectionReadPurpose.Pin) > MaximumPinnedResults)
            throw new SelectionReadOwnershipException("The Selection reader has reached its active-result limit.");

        var lineRanges = resources.Where(item => item.LineKey is not null)
            .GroupBy(item => item.LineKey!.Value)
            .Select(group => group.Max(item => item.LineTokens)).ToArray();
        if (lineRanges.Length > MaximumLeasedLineRanges || lineRanges.Sum() > MaximumLeasedLineTokens)
            throw new SelectionReadOwnershipException("Release an older Text range before requesting another.");
        var occurrencePages = resources.Where(item => item.OccurrenceKey is not null)
            .GroupBy(item => item.OccurrenceKey!.Value)
            .Select(group => group.Max(item => item.OccurrenceContexts)).ToArray();
        if (occurrencePages.Length > MaximumLeasedOccurrencePages ||
            occurrencePages.Sum() > MaximumLeasedOccurrenceContexts)
            throw new SelectionReadOwnershipException("Release an older occurrence page before requesting another.");
        if (resources.SelectMany(item => item.WordKeys).Distinct().Count() > MaximumLeasedWordKeys)
            throw new SelectionReadOwnershipException("The Selection reader has reached its leased word-detail limit.");

        SelectionReadLease? lease = null;
        lease = new SelectionReadLease(RegisterModel, purpose, () => ReleaseLease(lease!));
        _leases.Add(lease);
        _leaseResources.Add(lease, resource);
        return new SelectionRead<T>(context, presentationStamp, value, lease);
    }

    private void RegisterModel(SelectionModelKind kind, SelectionModelUse use, bool add)
    {
        lock (_gate)
        {
            if (add)
            {
                ObjectDisposedException.ThrowIf(_disposed || _obsolete, this);
                if (use == SelectionModelUse.Pinned && _livePinnedModels >= 64)
                    throw new SelectionReadOwnershipException("The Selection reader has reached its 64-model pin limit.");
                if (use == SelectionModelUse.Ordinary && kind == SelectionModelKind.Row && _ordinaryRowModels >= 128 ||
                    use == SelectionModelUse.Ordinary && kind == SelectionModelKind.Line && _ordinaryLineModels >= 48 ||
                    use == SelectionModelUse.Ordinary && kind == SelectionModelKind.Token && _ordinaryTokenModels >= 512 ||
                    kind == SelectionModelKind.Card && _liveCardModels >= 1)
                    throw new SelectionReadOwnershipException("The Selection reader has reached this model kind's limit.");
                if (use == SelectionModelUse.Ordinary)
                {
                    if (kind == SelectionModelKind.Row) _ordinaryRowModels++;
                    if (kind == SelectionModelKind.Line) _ordinaryLineModels++;
                    if (kind == SelectionModelKind.Token) _ordinaryTokenModels++;
                }
                if (kind == SelectionModelKind.Row) _liveRowModels++;
                if (kind == SelectionModelKind.Line)
                {
                    _createdLineModels++;
                    _liveLineModels++;
                }
                if (kind == SelectionModelKind.Token)
                {
                    _createdTokenModels++;
                    _liveTokenModels++;
                }
                if (kind == SelectionModelKind.Card) _liveCardModels++;
                if (use == SelectionModelUse.Pinned) _livePinnedModels++;
                return;
            }

            if (use == SelectionModelUse.Ordinary)
            {
                if (kind == SelectionModelKind.Row) _ordinaryRowModels--;
                if (kind == SelectionModelKind.Line) _ordinaryLineModels--;
                if (kind == SelectionModelKind.Token) _ordinaryTokenModels--;
            }
            if (kind == SelectionModelKind.Row) _liveRowModels--;
            if (kind == SelectionModelKind.Line) _liveLineModels--;
            if (kind == SelectionModelKind.Token) _liveTokenModels--;
            if (kind == SelectionModelKind.Card) _liveCardModels--;
            if (use == SelectionModelUse.Pinned) _livePinnedModels--;
        }
    }

    private sealed class SelectionReadOwnershipException(string message) : InvalidOperationException(message);

    private void ReleaseLease(SelectionReadLease lease)
    {
        lock (_gate)
        {
            _leases.Remove(lease);
            _leaseResources.Remove(lease);
        }
    }

    private void RecordRepositoryReads(RepositoryReadCounters.RepositoryReadScope scope)
    {
        var reads = scope.Snapshot();
        Interlocked.Add(ref _queriesIssued, reads.Queries);
        Interlocked.Add(ref _repositoryRecordsDeserialized, reads.RecordsDeserialized);
        Interlocked.Add(ref _baselineJsonPayloadBytes, reads.BaselineJsonPayloadBytes);
        Interlocked.Add(ref _baselineOccurrenceTuplesRead, reads.BaselineOccurrenceTuples);
    }

    private void RecordPresentationDetailReads(TextWordsProjection projection)
    {
        Interlocked.Add(ref _textRowsDeserialized, projection.Texts.Count);
        Interlocked.Add(ref _wordformRowsDeserialized, projection.Wordforms.Count);
        Interlocked.Add(ref _sourceLines, projection.Texts.Sum(text => text.Lines.Count));
        Interlocked.Add(ref _sourceTokens, projection.Texts.Sum(text => text.Lines.Sum(line => line.Tokens.Count)));
        Interlocked.Add(ref _textAnalyses, projection.Texts.Sum(text => text.Analyses.Count));
        Interlocked.Add(ref _wordformAnalyses, projection.Wordforms.Sum(wordform => wordform.Analyses.Count));
        Interlocked.Add(ref _morphs, projection.Texts.Sum(text => text.Analyses.Sum(analysis => analysis.Morphs.Count)) +
            projection.Wordforms.Sum(wordform => wordform.Analyses.Sum(analysis => analysis.Morphs.Count)));
    }

    private bool BeginRead(out SelectionReadContext context, out SelectionIndexBuilder index, out bool busy)
    {
        lock (_gate)
        {
            if (_disposed || _obsolete)
            {
                context = null!;
                index = null!;
                busy = false;
                return false;
            }
            if (_activeReads >= 2)
            {
                context = null!;
                index = null!;
                busy = true;
                return false;
            }
            busy = false;
            if (_activeReads == 0)
                _activeReadsDrained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _activeReads++;
            context = _context!;
            index = _index!;
            return true;
        }
    }

    private bool IsObsolete()
    {
        lock (_gate) return _obsolete;
    }

    private void EndRead()
    {
        TaskCompletionSource? drained = null;
        lock (_gate)
        {
            _activeReads--;
            if (_activeReads == 0) drained = _activeReadsDrained;
        }
        drained?.TrySetResult();
    }

    private void MarkObsolete()
    {
        SelectionReadLease[] leases;
        lock (_gate)
        {
            if (_disposed || _obsolete) return;
            _obsolete = true;
            _assessmentResults = [];
            _objectTimings = [];
            ClearDetailCachesLocked();
            leases = _leases.ToArray();
        }
        foreach (var lease in leases) lease.Dispose();
        _lifetime.Cancel();
    }

    private async Task FinishDisposeAsync(Task drain)
    {
        try
        {
            await drain.ConfigureAwait(false);
            lock (_gate)
            {
                ClearDetailCachesLocked();
                _leases.Clear();
                _leaseResources.Clear();
                _presentationVersionConnection.Dispose();
            }
            _lifetime.Dispose();
            if (_ownsDatabase) _database.Dispose();
            _disposeCompletion.TrySetResult();
        }
        catch (Exception exception)
        {
            _disposeCompletion.TrySetException(exception);
        }
    }

    private void ClearDetailCachesLocked()
    {
        _lineCache.Clear();
        _lineLru.Clear();
        _occurrenceCache.Clear();
        _occurrenceLru.Clear();
        _wordDetailCache.Clear();
        _wordDetailLru.Clear();
        _cachedTextId = null;
        _cachedText = null;
    }

    private void ThrowIfPublicationClosedLocked()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_obsolete) throw new InvalidOperationException("The Selection reader's evidence is obsolete.");
    }

    private CommandOutcome<SelectionRead<SelectionSummary>> ReaderUnavailable() => ReaderUnavailable<SelectionSummary>();

    private CommandOutcome<SelectionRead<T>> ReaderUnavailable<T>()
    {
        lock (_gate)
        {
            if (_obsolete)
                return CommandOutcome<SelectionRead<T>>.Refused(new Refusal(
                    "texts.evidence-changed", FailureReason.Refused,
                    "The saved Selection evidence changed. Open a new Selection reader."));
        }
        return CommandOutcome<SelectionRead<T>>.Refused(new Refusal(
            "selection-reader.disposed", FailureReason.Refused, "The Selection reader is no longer available."));
    }

    private static CommandOutcome<SelectionRead<T>> ReaderBusy<T>() =>
        CommandOutcome<SelectionRead<T>>.Refused(new Refusal(
            "selection-reader.busy", FailureReason.Busy, "The Selection reader already has two active reads."));

    private static CommandOutcome<SelectionRead<T>> OwnershipLimit<T>() =>
        CommandOutcome<SelectionRead<T>>.Refused(new Refusal(
            "selection-reader.ownership-limit", FailureReason.Busy,
            "Release an existing Selection result or model before requesting more detail."));

    private static CommandOutcome<SelectionRead<T>> PresentationSuperseded<T>() =>
        CommandOutcome<SelectionRead<T>>.Refused(new Refusal(
            "selection-reader.presentation-changed", FailureReason.Refused,
            "The Selection presentation changed while it was being read. Read it again."));

    private static CommandOutcome<SelectionRead<SelectionSummary>> EvidenceChanged() =>
        CommandOutcome<SelectionRead<SelectionSummary>>.Refused(new Refusal(
            "texts.evidence-changed", FailureReason.Refused,
            "The saved Selection evidence changed. Open a new Selection reader."));

    private CommandOutcome<SelectionRead<T>> EvidenceChanged<T>()
    {
        MarkObsolete();
        return CommandOutcome<SelectionRead<T>>.Refused(new Refusal(
            "texts.evidence-changed", FailureReason.Refused,
            "The saved Selection evidence changed. Open a new Selection reader."));
    }

    private static CommandOutcome<SelectionRead<T>> InvalidRange<T>(string message) =>
        CommandOutcome<SelectionRead<T>>.Refused(new Refusal(
            "selection-reader.range-invalid", FailureReason.InvalidArgument, message));

    private static CommandOutcome<SelectionRead<T>> Cancelled<T>(string message) =>
        CommandOutcome<SelectionRead<T>>.Refused(new Refusal(
            "selection-reader.cancelled", FailureReason.Cancelled, message));

    private static CommandOutcome<SelectionRead<T>> Unreadable<T>(string detail) =>
        CommandOutcome<SelectionRead<T>>.Refused(new Refusal(
            "baseline.text-words-unreadable", FailureReason.StoreInconsistent, detail));

    private static InvalidDataException DamagedSelection(string detail) => new(
        "Motif's stored words for this Baseline are missing or damaged. Delete the refused store and let Motif recreate it.",
        new InvalidDataException(detail));

    private static string Normalize(string value) => (value ?? string.Empty).Trim()
        .Normalize(System.Text.NormalizationForm.FormD);

    private static string SelectionDigest(
        string projectKey, BaselineToken baseline, IReadOnlyList<Guid> textIds,
        IReadOnlyList<string> addedWords, SelectionReadEvidence? evidence)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Add(projectKey);
        Add(JsonSerializer.Serialize(baseline, MotifJson.CreateOptions()));
        Add(evidence?.Origin ?? "explicit");
        Add(textIds.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var id in textIds) Add(id.ToString("D"));
        Add(addedWords.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var word in addedWords) Add(word);
        Add(evidence is null ? string.Empty : JsonSerializer.Serialize(evidence.Descriptor,
            MotifJson.CreateOptions()));
        return "sha256:" + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();

        void Add(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            Span<byte> length = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }
    }

    private readonly record struct LineCacheKey(Guid TextId, int StartLine, int LineCount,
        int TokenOffset, int TokenLimit, string? Pages = null);

    private readonly record struct OccurrenceCacheKey(TextWordKey Word, int Start, int Count);

    private sealed record SelectionLeaseResource(SelectionReadPurpose Purpose, LineCacheKey? LineKey,
        int LineTokens, IReadOnlySet<TextWordKey> WordKeys, OccurrenceCacheKey? OccurrenceKey,
        int OccurrenceContexts)
    {
        public IReadOnlyDictionary<TextWordKey, TextWordsProjectedWordform> WordDetails { get; init; } =
            new Dictionary<TextWordKey, TextWordsProjectedWordform>();
    }

    private sealed record SelectionReadEvidence(string Origin, AssessmentRecord? Root,
        IReadOnlyList<string> ReplacementIds, IReadOnlyList<AssessedWord> Words,
        IReadOnlyList<AssessmentObjectTiming> ObjectTimings, IReadOnlyList<string> ParseWarningLines,
        int HeaderRows, int WordRows,
        SelectionEvidenceExpectation Descriptor);

    private sealed record SelectionOpenSnapshot(CurrentBaselineSelectionIndexRead Source,
        SelectionReadEvidence? Evidence, SelectionReadContext Context, int BaselineWordformFactsRead);

    private sealed record ReadStateSnapshot(
        IReadOnlyDictionary<Guid, IReadOnlyList<OccurrenceAnchor>> Occurrences);

    private sealed class EvidenceContextChangedException : Exception { }
}
