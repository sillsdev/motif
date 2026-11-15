using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Host.Sources;
using SIL.Motif.Host.Store;
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

    /// <summary>Marks old generations pending and queues a durable refresh without reading or tokenizing Source text.</summary>
    public static void Enqueue(MotifDatabase database, IReadOnlyList<SourceListEntry>? sources = null)
    {
        using (var connection = database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
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
            transaction.Commit();
        }
        var now = JobTimestamp.FormatUtc(DateTimeOffset.UtcNow);
        new JobRepository(database).Create(CanonicalId.FromGuid(Guid.NewGuid()).Value, ProjectWorkspaceKey.Compute(database.Project),
            JobKind, "{}", now);
    }

    /// <summary>Builds from a private copy of saved project files and validates their identity before publication.</summary>
    public static JobOutcome Run(MotifDatabase database, CancellationToken cancellationToken = default)
    {
        try { return RunCore(database, cancellationToken); }
        catch (IOException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Enqueue(database);
            return new(JobStatus.Failed, JobFailureCategory.Infrastructure,
                JsonSerializer.Serialize(new { message = exception.Message, retryQueued = true }));
        }
    }

    private static JobOutcome RunCore(MotifDatabase database, CancellationToken cancellationToken)
    {
        var project = database.Project;
        var key = ProjectWorkspaceKey.Compute(project);
        var projectIdentity = ProgressSourceIdentity.Capture(project.FullFwDataPath);
        if (projectIdentity is null) throw new IOException("The project changed while its Source index was being prepared.");
        using var reader = BaselineReadCache.Open(project.FullFwDataPath);
        if (ProgressSourceIdentity.Capture(reader.Cache.ProjectId.Path)?.Digest != projectIdentity.Digest ||
            !projectIdentity.Current(project.FullFwDataPath))
            throw new IOException("The saved project changed while its Source index was being prepared.");
        var sources = SourceList.Read(reader.Cache);
        var ws = reader.Cache.WritingSystemFactory.GetStrFromWs(reader.Cache.DefaultVernWs);
        var local = ScriptureLocalStore.ForInstallation().Read(key);
        var retry = false;
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folder = local.SourcePaths.GetValueOrDefault(source.Id);
            SourceFileStamp[]? before = null;
            try
            {
                before = folder is not null && Directory.Exists(folder) ? SourceIndexIdentity.Metadata(folder) : [];
                if (source.Kind != SourceKind.Paratext || source.Error is not null || source.Status == SourceStatus.Removed ||
                    folder is null || !Directory.Exists(folder))
                    retry |= !PublishUnavailable(database, source, folder, ws, projectIdentity, source.Error, before);
                else
                    retry |= !Build(database, source, folder, ws, projectIdentity, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or
                                               UnauthorizedAccessException or ArgumentException or System.Xml.XmlException)
            {
                retry |= !PublishUnavailable(database, source, folder, ws, projectIdentity, exception.Message, before);
            }
        }
        if (!retry) return JobOutcome.Completed;
        Enqueue(database);
        return new(JobStatus.Failed, JobFailureCategory.Infrastructure,
            JsonSerializer.Serialize(new { message = "Source inputs changed during indexing; a fresh job was queued." }));
    }

    private static bool PublishUnavailable(MotifDatabase database, SourceListEntry source, string? folder,
        string writingSystem, ProgressSourceIdentity projectIdentity, string? error, SourceFileStamp[]? before)
    {
        var repository = new SourceIndexRepository(database);
        var previous = repository.ReadState(source.Id);
        var files = before ?? [];
        var identity = new SourceIndexIdentity(folder is null ? "" : Path.GetFullPath(folder), source.ContextText,
            source.VersificationName, source.Status.ToString(), writingSystem, projectIdentity.Serialize(), files, error);
        var currentFolder = ScriptureLocalStore.ForInstallation().ReadExisting(ProjectWorkspaceKey.Compute(database.Project))
            .SourcePaths.GetValueOrDefault(source.Id);
        if (!identity.ProjectCurrent(database.Project.FullFwDataPath) ||
            (currentFolder is null ? "" : Path.GetFullPath(currentFolder)) != identity.Folder) return false;
        if (before is not null && !identity.FolderCurrent(currentFolder)) return false;
        return repository.Publish(new(source.Id, source.Kind.ToString(), Guid.NewGuid().ToString("N"), "",
                identity.Serialize(), "stale", ParatextPassages.TokenizerVersion, ParatextPassages.AnchorVersion),
            [], [], [], previous?.Generation ?? "");
    }

    /// <summary>Queues changed inputs from worker maintenance; reads neither build nor call this method.</summary>
    public static bool Reconcile(MotifDatabase database)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM Jobs WHERE Kind=$kind AND Status IN ('queued','running') LIMIT 1;";
        command.Parameters.AddWithValue("$kind", JobKind);
        if (command.ExecuteScalar() is not null) return false;
        command.CommandText = "SELECT SourceId, Kind, IdentityJson, TokenizerVersion, AnchorVersion, Status FROM SourceIndexState;";
        var states = new List<(string Id, string Kind, string Identity, string Tokenizer, string Anchor, string Status)>();
        using (var rows = command.ExecuteReader())
            while (rows.Read()) states.Add((rows.GetString(0), rows.GetString(1), rows.GetString(2), rows.GetString(3), rows.GetString(4), rows.GetString(5)));
        var local = ScriptureLocalStore.ForInstallation().ReadExisting(ProjectWorkspaceKey.Compute(database.Project));
        foreach (var state in states)
        {
            var identity = SourceIndexIdentity.Parse(state.Identity);
            try
            {
                if (state.Status != "pending" && identity is not null && identity.Current(database.Project.FullFwDataPath, local.SourcePaths.GetValueOrDefault(state.Id)) &&
                    state.Tokenizer == ParatextPassages.TokenizerVersion && state.Anchor == ParatextPassages.AnchorVersion)
                    continue;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { continue; }
            Enqueue(database);
            return true;
        }
        return false;
    }

    internal static bool Build(MotifDatabase database, SourceListEntry source, string folder, string writingSystem,
        ProgressSourceIdentity projectIdentity, CancellationToken cancellationToken = default)
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
                foreach (var word in ParatextPassages.Tokenize(chapter))
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
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('\n', passages.Select(p => p.PassageId + ":" + p.ContentHash)))));
        var state = new SourceIndexState(source.Id, source.Kind.ToString(), Guid.NewGuid().ToString("N"), digest,
            identity.Serialize(), "ready", ParatextPassages.TokenizerVersion, ParatextPassages.AnchorVersion);
        using var stage = SourceIndexStage.Create(state, passages, wordforms.Values, places);
        cancellationToken.ThrowIfCancellationRequested();
        var currentFolder = ScriptureLocalStore.ForInstallation().Read(ProjectWorkspaceKey.Compute(database.Project))
            .SourcePaths.GetValueOrDefault(source.Id);
        if (!identity.Current(database.Project.FullFwDataPath, currentFolder)) return false;
        return repository.Publish(stage, previous?.Generation ?? "");
    }
}
