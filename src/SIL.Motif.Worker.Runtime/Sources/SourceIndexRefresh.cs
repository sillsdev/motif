using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Sources;
using SIL.Motif.Host.Store;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Scripture;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Worker.Sources;

/// <summary>Queues derived Source work and publishes complete generations away from Apply and Read commands.</summary>
public static class SourceIndexRefresh
{
    public const string JobKind = "source-index";
    internal static readonly AsyncLocal<Action<string>?> PassageTokenized = new();
    private const int MaxAutomaticRequeues = 2;
    private enum RefreshRequest { Explicit, Retry, Reconcile }
    private enum SourceIndexFailureReason { Busy, Timeout, PermanentIo }

    /// <summary>Marks old generations pending and queues a durable refresh without reading or tokenizing Source text.</summary>
    public static void Enqueue(MotifDatabase database, IReadOnlyList<SourceListEntry>? sources = null) =>
        Enqueue(database, RefreshRequest.Explicit, sources);

    private static bool Enqueue(MotifDatabase database, RefreshRequest request,
        IReadOnlyList<SourceListEntry>? sources = null)
    {
        var projectKey = ProjectWorkspaceKey.Compute(database.Project);
        using (var connection = database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            var input = "{}";
            if (request == RefreshRequest.Retry)
            {
                if (NextRetry(connection, transaction, projectKey) is not { } retry) return false;
                input = JsonSerializer.Serialize(new { sourceIndexRetry = retry });
            }
            if (request == RefreshRequest.Reconcile && !CanReconcile(connection, projectKey, transaction)) return false;
            MarkPendingCore(connection, transaction, sources, deferred: false);
            var now = JobTimestamp.FormatUtc(DateTimeOffset.UtcNow);
            new JobRepository(database).CreateInTransaction(connection, transaction,
                new JobRecord(CanonicalId.FromGuid(Guid.NewGuid()).Value, projectKey, JobKind, JobStatus.Queued, 1,
                    input, null, now, now, null, null));
            transaction.Commit();
        }
        return true;
    }

    /// <summary>
    /// Marks every Source index generation pending without queuing a job, for a foreground-only command: the index
    /// reads as stale (pending-foreground) until a later default-mode capture or Apply refreshes it; a runner start never does.
    /// </summary>
    public static void MarkPending(MotifDatabase database, IReadOnlyList<SourceListEntry>? sources = null,
        bool deferred = true)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        MarkPendingCore(connection, transaction, sources, deferred);
        transaction.Commit();
    }

    private static void MarkPendingCore(SqliteConnection connection, SqliteTransaction transaction,
        IReadOnlyList<SourceListEntry>? sources, bool deferred)
    {
        SourceIndexRepository.MarkAllPending(connection, transaction);
        foreach (var source in sources ?? [])
        {
            using var pending = connection.CreateCommand();
            pending.Transaction = transaction;
            pending.CommandText = "INSERT OR IGNORE INTO SourceIndexState VALUES " +
                "($source,$kind,$generation,'','{}','pending',$tokenizer,$anchor);";
            pending.Parameters.AddWithValue("$source", source.Id.ToUpperInvariant());
            pending.Parameters.AddWithValue("$kind", source.Kind.ToString());
            pending.Parameters.AddWithValue("$generation", Guid.NewGuid().ToString("N"));
            pending.Parameters.AddWithValue("$tokenizer", ParatextPassages.TokenizerVersion);
            pending.Parameters.AddWithValue("$anchor", ParatextPassages.AnchorVersion);
            pending.ExecuteNonQuery();
        }
        using (var texts = connection.CreateCommand())
        {
            texts.Transaction = transaction;
            texts.CommandText = "INSERT OR IGNORE INTO SourceIndexState VALUES " +
                "($source,$kind,$generation,'','{}','pending',$tokenizer,$anchor);";
            texts.Parameters.AddWithValue("$source", FieldWorksTextsIndex.SourceId);
            texts.Parameters.AddWithValue("$kind", FieldWorksTextsIndex.Kind);
            texts.Parameters.AddWithValue("$generation", Guid.NewGuid().ToString("N"));
            texts.Parameters.AddWithValue("$tokenizer", FieldWorksTextsIndex.TokenizerVersion);
            texts.Parameters.AddWithValue("$anchor", FieldWorksTextsIndex.AnchorVersion);
            texts.ExecuteNonQuery();
        }
        if (deferred) SourceIndexRepository.MarkAllDeferred(connection, transaction);
    }

    private static long? NextRetry(SqliteConnection connection, SqliteTransaction transaction, string projectKey)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // The running job carries the budget forward even if earlier attempts have been purged.
        command.CommandText = """
            SELECT 1 + COALESCE((
                SELECT MAX(CAST(json_extract(InputJson, '$.sourceIndexRetry') AS INTEGER)) FROM Jobs
                WHERE ProjectKey=$project AND Kind=$kind AND Status='running'
            ), 0) WHERE NOT EXISTS (
                SELECT 1 FROM Jobs WHERE ProjectKey=$project AND Kind=$kind AND Status='queued'
            );
            """;
        command.Parameters.AddWithValue("$project", projectKey);
        command.Parameters.AddWithValue("$kind", JobKind);
        return command.ExecuteScalar() is long retry && retry <= MaxAutomaticRequeues ? retry : null;
    }

    private static bool CanReconcile(SqliteConnection connection, string projectKey, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT NOT EXISTS (
                SELECT 1 FROM Jobs WHERE ProjectKey=$project AND Kind=$kind AND Status IN ('queued','running')
            );
            """;
        command.Parameters.AddWithValue("$project", projectKey);
        command.Parameters.AddWithValue("$kind", JobKind);
        return Equals(command.ExecuteScalar(), 1L);
    }

    private static void MarkRefreshFailed(MotifDatabase database, SourceIndexFailureReason reason)
    {
        var inputs = SourceIndexFailureInputs.Observe(database, checkProjectAccess: reason == SourceIndexFailureReason.PermanentIo);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        // Keep the observed inputs through job archival so only a fresh request can spend another retry budget.
        command.CommandText = """
            UPDATE SourceIndexState SET Status='failed', Generation=lower(hex(randomblob(16))), IdentityJson=$inputs
            WHERE SourceId=$texts AND NOT EXISTS (
                SELECT 1 FROM Jobs WHERE ProjectKey=$project AND Kind=$kind AND Status='queued'
            );
            """;
        command.Parameters.AddWithValue("$project", ProjectWorkspaceKey.Compute(database.Project));
        command.Parameters.AddWithValue("$kind", JobKind);
        command.Parameters.AddWithValue("$texts", FieldWorksTextsIndex.SourceId);
        command.Parameters.AddWithValue("$inputs", JsonSerializer.Serialize(inputs));
        command.ExecuteNonQuery();
    }

    /// <summary>Builds from a private copy of saved project files and validates their identity before publication.</summary>
    public static JobOutcome Run(MotifDatabase database, CancellationToken cancellationToken = default)
    {
        try { return RunCore(database, cancellationToken); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or TimeoutException or
                                           LcmFileLockedException or LcmInitializationException)
        {
            return Fail(database, ClassifyFailure(exception), exception.Message, cancellationToken);
        }
    }

    private static SourceIndexFailureReason ClassifyFailure(Exception exception) => exception switch
    {
        TimeoutException => SourceIndexFailureReason.Timeout,
        ProjectStartupBusyException or LcmFileLockedException => SourceIndexFailureReason.Busy,
        _ when ProjectFileHeldException.IsSharingViolation(exception) => SourceIndexFailureReason.Busy,
        _ => SourceIndexFailureReason.PermanentIo,
    };

    private static JobOutcome Fail(MotifDatabase database, SourceIndexFailureReason reason, string message,
        CancellationToken cancellationToken, bool projectWide = true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var retryQueued = (reason is SourceIndexFailureReason.Busy or SourceIndexFailureReason.Timeout) &&
            Enqueue(database, RefreshRequest.Retry);
        if (!retryQueued && projectWide) MarkRefreshFailed(database, reason);
        return new(JobStatus.Failed, JobFailureCategory.Infrastructure,
            JsonSerializer.Serialize(new { reason = reason.ToString(), message, retryQueued }));
    }

    private static JobOutcome RunCore(MotifDatabase database, CancellationToken cancellationToken)
    {
        var project = database.Project;
        var key = ProjectWorkspaceKey.Compute(project);
        ProgressSourceIdentity? projectIdentity;
        using (ProjectStartupGate.Enter(project.FullFwDataPath, cancellationToken, sourceIndex: true))
            projectIdentity = ProgressSourceIdentity.Capture(project.FullFwDataPath, ReadObserverForTest(cancellationToken));
        if (projectIdentity is null)
            return Fail(database, SourceIndexFailureReason.Busy, "The project changed while its Source index was being prepared.",
                cancellationToken);
        using var reader = BaselineReadCache.OpenWithCoordinatedCopy(project.FullFwDataPath, cancellationToken);
        if (ProgressSourceIdentity.Capture(reader.Cache.ProjectId.Path)?.Digest != projectIdentity.Digest ||
            !projectIdentity.MatchesMetadata(project.FullFwDataPath))
            return Fail(database, SourceIndexFailureReason.Busy, "The project changed while its Source index was being prepared.",
                cancellationToken);
        IReadOnlyList<SourceListEntry> sources;
        try { sources = SourceList.Read(reader.Cache); }
        catch (SourceListAmbiguousException) { sources = []; }
        var ws = reader.Cache.WritingSystemFactory.GetStrFromWs(reader.Cache.DefaultVernWs);
        var tokeniser = SourceApprovals.Tokeniser(reader.Cache);
        var local = ScriptureLocalStore.ForInstallation().Read(key);
        var retry = false;
        (SourceIndexFailureReason Reason, string Message)? sourceFailure = null;
        foreach (var source in sources.Where(source =>
                     !string.Equals(source.Id, FieldWorksTextsIndex.SourceId, StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folder = local.SourcePaths.GetValueOrDefault(source.Id);
            SourceFileStamp[]? before = null;
            try
            {
                before = folder is null ? null : MetadataIfPresent(folder);
                if (source.Kind != SourceKind.Paratext || source.Error is not null || source.Status == SourceStatus.Removed ||
                    folder is null || before is null)
                    retry |= !PublishUnavailable(database, source, folder, ws, projectIdentity, source.Error, before ?? []);
                else
                    retry |= !Build(database, source, folder, ws, projectIdentity, tokeniser, cancellationToken, reader.Cache);
            }
            catch (Exception exception) when (SourceIoFailure(exception) is { } io)
            {
                var reason = ClassifyFailure(io);
                if (sourceFailure is null || reason == SourceIndexFailureReason.PermanentIo)
                    sourceFailure = (reason, $"{source.Id}: {exception.Message}");
                retry |= !PublishUnavailable(database, source, folder, ws, projectIdentity, exception.Message, null,
                    SourceIndexIdentity.ObserveFolder(folder));
            }
            catch (Exception exception) when (exception is InvalidDataException or ArgumentException or System.Xml.XmlException)
            {
                retry |= !PublishUnavailable(database, source, folder, ws, projectIdentity, exception.Message, before);
            }
        }
        retry |= !BuildTexts(database, reader.Cache, ws, projectIdentity, cancellationToken);
        if (sourceFailure is { } failure)
            return Fail(database, failure.Reason, failure.Message, cancellationToken, projectWide: false);
        return retry
            ? Fail(database, SourceIndexFailureReason.Busy, "Source inputs changed during indexing.", cancellationToken)
            : JobOutcome.Completed;
    }

    private static SourceFileStamp[]? MetadataIfPresent(string folder)
    {
        try { return SourceIndexIdentity.Metadata(folder); }
        catch (DirectoryNotFoundException) { return null; }
    }

    private static Exception? SourceIoFailure(Exception exception)
    {
        if (exception is InvalidDataException { InnerException: { } cause }) exception = cause;
        return exception is IOException or UnauthorizedAccessException or TimeoutException ? exception : null;
    }

    // Keeps the source handle open until the CLI regression has started its LibLCM reopen.
    private static Action? ReadObserverForTest(CancellationToken cancellationToken)
    {
        var marker = Environment.GetEnvironmentVariable("MOTIF_TEST_SOURCE_INDEX_READ_MARKER");
        return string.IsNullOrEmpty(marker) ? null : () =>
        {
            using var watcher = new FileSystemWatcher(Path.GetDirectoryName(marker)!, Path.GetFileName(marker) + ".release");
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            watcher.Created += (_, _) => release.TrySetResult();
            watcher.EnableRaisingEvents = true;
            File.WriteAllText(marker, "");
            if (!File.Exists(marker + ".release"))
                release.Task.WaitAsync(TimeSpan.FromMinutes(5), cancellationToken).GetAwaiter().GetResult();
        };
    }

    private static bool BuildTexts(MotifDatabase database, LcmCache cache, string writingSystem,
        ProgressSourceIdentity projectIdentity, CancellationToken cancellationToken)
    {
        try { return FieldWorksTextsIndex.Build(database, cache, writingSystem, projectIdentity, cancellationToken); }
        catch (InvalidDataException exception)
        {
            return PublishUnavailable(database, FieldWorksTextsIndex.SourceId, FieldWorksTextsIndex.Kind, "", "", "Active",
                null, writingSystem, projectIdentity, exception.Message, null);
        }
    }

    private static bool PublishUnavailable(MotifDatabase database, SourceListEntry source, string? folder,
        string writingSystem, ProgressSourceIdentity projectIdentity, string? error, SourceFileStamp[]? before,
        SourceFolderInputs? unavailableInputs = null) =>
        PublishUnavailable(database, source.Id, source.Kind.ToString(), source.ContextText, source.VersificationName,
            source.Status.ToString(), folder, writingSystem, projectIdentity, error, before, unavailableInputs);

    private static bool PublishUnavailable(MotifDatabase database, string sourceId, string kind, string contextText,
        string versification, string status, string? folder, string writingSystem, ProgressSourceIdentity projectIdentity,
        string? error, SourceFileStamp[]? before, SourceFolderInputs? unavailableInputs = null)
    {
        var repository = new SourceIndexRepository(database);
        var previous = repository.ReadState(sourceId);
        var files = before ?? [];
        var identity = new SourceIndexIdentity(folder is null ? "" : Path.GetFullPath(folder), contextText,
            versification, status, writingSystem, projectIdentity.Serialize(), files, error)
            { UnavailableInputs = unavailableInputs };
        var currentFolder = SourceProviders.Versions(kind).Folder(ProjectWorkspaceKey.Compute(database.Project), sourceId);
        if (!projectIdentity.MatchesMetadata(database.Project.FullFwDataPath) ||
            (currentFolder is null ? "" : Path.GetFullPath(currentFolder)) != identity.Folder) return false;
        if (before is not null && !identity.FolderCurrent(currentFolder)) return false;
        var versions = SourceProviders.Versions(kind);
        return repository.Publish(new(sourceId, kind, Guid.NewGuid().ToString("N"), "",
                identity.Serialize(), "stale", versions.TokenizerVersion, versions.AnchorVersion),
            [], [], [], previous?.Generation ?? "");
    }

    /// <summary>Queues changed inputs from worker maintenance; reads neither build nor call this method.</summary>
    public static bool Reconcile(MotifDatabase database)
    {
        using var connection = database.OpenConnection();
        var workspace = ProjectWorkspaceKey.Compute(database.Project);
        if (!CanReconcile(connection, workspace)) return false;
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT SourceId, Kind, IdentityJson, TokenizerVersion, AnchorVersion, Status FROM SourceIndexState;";
        var states = new List<(string Id, string Kind, string Identity, string Tokenizer, string Anchor, string Status)>();
        using (var rows = command.ExecuteReader())
            while (rows.Read()) states.Add((rows.GetString(0), rows.GetString(1), rows.GetString(2), rows.GetString(3), rows.GetString(4), rows.GetString(5)));
        // A foreground-only command left the index deferred: only a later default-mode command may queue its refresh.
        if (states.Any(state => state.Status == SourceIndexRepository.DeferredStatus)) return false;
        var failedRefresh = states.FirstOrDefault(state => state.Id == FieldWorksTextsIndex.SourceId && state.Status == "failed");
        if (failedRefresh.Identity is { } failedInputs)
            return !SourceIndexFailureInputs.StillCurrent(failedInputs, database) && Enqueue(database, RefreshRequest.Reconcile);
        foreach (var state in states)
        {
            var identity = SourceIndexIdentity.Parse(state.Identity);
            var provider = SourceProviders.Versions(state.Kind);
            try
            {
                var folder = provider.Folder(workspace, state.Id);
                var projectPath = database.Project.FullFwDataPath;
                var current = identity?.UnavailableInputs is { } unavailable
                    ? identity.ProjectCurrent(projectPath) && unavailable.Matches(SourceIndexIdentity.ObserveFolder(folder))
                    : identity?.Current(projectPath, folder) == true;
                if (state.Status != "pending" && current &&
                    state.Tokenizer == provider.TokenizerVersion && state.Anchor == provider.AnchorVersion)
                    continue;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            return Enqueue(database, RefreshRequest.Reconcile);
        }
        return false;
    }

    internal static bool Build(MotifDatabase database, SourceListEntry source, string folder, string writingSystem,
        ProgressSourceIdentity projectIdentity, IWordTokeniser tokeniser, CancellationToken cancellationToken = default, LcmCache? cache = null)
    {
        folder = Path.GetFullPath(folder);
        var repository = new SourceIndexRepository(database);
        var previous = repository.ReadState(source.Id);
        var files = SourceIndexIdentity.Metadata(folder);
        var identity = new SourceIndexIdentity(folder, source.ContextText, source.VersificationName,
            source.Status.ToString(), writingSystem, projectIdentity.Serialize(), files);
        var oldIdentity = previous is null ? null : SourceIndexIdentity.Parse(previous.IdentityJson);
        var reuse = previous?.TokenizerVersion == ParatextPassages.TokenizerVersion &&
            previous.AnchorVersion == ParatextPassages.AnchorVersion && oldIdentity?.WritingSystem == writingSystem;
        var chapters = ParatextPassages.Read(folder, cancellationToken);
        var passages = new List<SourcePassage>();
        var wordforms = new Dictionary<string, SourceWordform>(StringComparer.Ordinal);
        var places = new List<SourceWordPlace>();
        foreach (var chapter in chapters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var old = reuse ? repository.ReadPassage(source.Id, chapter.Locator) : null;
            var start = places.Count;
            if (old?.ContentHash == chapter.Hash)
            {
                var after = 0;
                while (true)
                {
                    var page = repository.ReadPage(source.Id, chapter.Locator, after, 200);
                    if (page.Count == 0) break;
                    foreach (var row in page)
                    {
                        places.Add(row.Place);
                        wordforms.TryAdd(row.Wordform.WordformKey, row.Wordform);
                    }
                    after = page[^1].Place.Ordinal;
                }
            }
            else
            {
                PassageTokenized.Value?.Invoke(chapter.Locator);
                var ordinal = 0;
                foreach (var word in ParatextPassages.Tokenize(chapter, tokeniser))
                {
                    var normalized = word.Surface.Normalize(NormalizationForm.FormC).ToLowerInvariant();
                    var wordKey = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(writingSystem + "\0" + normalized)));
                    wordforms.TryAdd(wordKey, new(wordKey, writingSystem, normalized, word.Surface));
                    places.Add(new(chapter.Locator, ++ordinal, word.Anchor, word.Verse, word.Surface, wordKey));
                }
            }
            var count = places.Count - start;
            passages.Add(new(chapter.Locator, chapter.Locator, chapter.Hash, count,
                places.Skip(start).Count(place => place.Surface.EnumerateRunes().Any(Rune.IsLetter))));
        }
        (places, var unreadable, var orphaned, var conflicts) = OverlayApprovals(cache, source.Id, chapters, places);
        identity = identity with { UnreadableApprovals = unreadable, OrphanedApprovals = orphaned, ConflictedApprovals = conflicts };
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('\n', passages.Select(p => p.PassageId + ":" + p.ContentHash)))));
        var state = new SourceIndexState(source.Id, source.Kind.ToString(), Guid.NewGuid().ToString("N"), digest,
            identity.Serialize(), "ready", ParatextPassages.TokenizerVersion, ParatextPassages.AnchorVersion);
        using var stage = SourceIndexStage.Create(state, passages, wordforms.Values, places);
        cancellationToken.ThrowIfCancellationRequested();
        var currentFolder = ScriptureLocalStore.ForInstallation().Read(ProjectWorkspaceKey.Compute(database.Project))
            .SourcePaths.GetValueOrDefault(source.Id);
        if (!projectIdentity.MatchesMetadata(database.Project.FullFwDataPath) || !identity.FolderCurrent(currentFolder))
            return false;
        return repository.Publish(stage, previous?.Generation ?? "");
    }

    // Marks approved and stale places; unreadable and orphaned approvals are returned, never dropped.
    private static (List<SourceWordPlace> Places, string[] Unreadable, string[] Orphaned,
        SIL.Motif.Contract.Responses.SourceApprovalConflict[] Conflicts) OverlayApprovals(
        LcmCache? cache, string sourceId, IReadOnlyList<ParatextPassage> chapters, List<SourceWordPlace> places)
    {
        var marked = new Dictionary<string, OccurrenceApproval>(StringComparer.Ordinal);
        var conflicted = new HashSet<string>(StringComparer.Ordinal);
        var unreadable = Array.Empty<string>();
        var orphaned = new SortedSet<string>(StringComparer.Ordinal);
        var conflicts = new List<SIL.Motif.Contract.Responses.SourceApprovalConflict>();
        if (cache is not null)
        {
            SourceApprovalSet set;
            try { set = SourceApprovals.ReadReporting(cache, sourceId); }
            catch (SourceListAmbiguousException ambiguous) { set = new(new Dictionary<string, ScriptureApprovalDocument>(), new Dictionary<string, IReadOnlyList<Guid>>(), [ambiguous.Message]); }
            unreadable = [.. set.Unreadable];
            var byLocator = chapters.ToDictionary(chapter => chapter.Locator, StringComparer.Ordinal);
            foreach (var (locator, document) in set.Documents)
                if (byLocator.TryGetValue(locator, out var chapter))
                    foreach (var approval in SourceApprovals.Evaluate(cache, document, chapter)) marked[approval.Anchor] = approval;
                else if (document.Approvals.Count > 0) orphaned.Add(locator);
            foreach (var (anchor, analyses) in set.Conflicts)
            {
                conflicts.Add(new(anchor, analyses.Select(guid => guid.ToString("D")).ToArray()));
                if (byLocator.ContainsKey(ScriptureApprovalDocument.PassageOf(anchor))) conflicted.Add(anchor);
                else orphaned.Add(ScriptureApprovalDocument.PassageOf(anchor));
            }
        }
        return (places.Select(place => marked.TryGetValue(place.Anchor, out var approval)
            ? place with
            {
                ReadingKey = approval.Analysis.ToString("D"),
                State = approval.State == ApprovalState.Approved ? "approved" : "stale",
            }
            : conflicted.Contains(place.Anchor)
                ? place with { ReadingKey = null, Signature = null, State = "conflict" }
                : place with { ReadingKey = null, Signature = null, State = "unmeasured" }).ToList(),
            unreadable, [.. orphaned], [.. conflicts]);
    }
}
