using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SIL.Harmony;
using SIL.Harmony.Core;

namespace SIL.Motif.RecordStore;

internal sealed record RecordHistoryJson(byte[] Commits, string Revision, string SnapshotDigest, string IndexDigest);

public sealed partial class RecordStore
{
    internal Task<RecordHistoryJson> ExportHistoryJsonAsync(int peerModelVersion) => ReadPublishedAsync(async state =>
    {
        if (state.ReplayDigest != await ReplayIntegrity.DigestAsync(database, Workers))
            throw new InvalidDataException("History is corrupt; export was refused.");
        var result = await model.GetChanges(new SyncState([], []));
        var commits = result.MissingFromClient.OrderBy(commit => commit.CompareKey).ToArray();
        foreach (var change in commits.SelectMany(commit => commit.ChangeEntities))
            RequireSupportedModelVersion(change.AuthoredModelVersion, peerModelVersion);
        return new RecordHistoryJson(JsonSerializer.SerializeToUtf8Bytes(commits, HistoryJsonOptions),
            state.Revision, state.SnapshotDigest, state.IndexDigest);
    });

    /// <summary>
    /// Replays complete Harmony JSON history into an empty Home. Resource bytes must already be staged by hash.
    /// The sender's revision and logical projection digests must match before the new Home becomes readable.
    /// </summary>
    internal async Task BootstrapHistoryJsonAsync(RecordHistoryJson history)
    {
        using var writer = AcquireWriter();
        await RequireEmptyImportAsync();
        // The server shape exposes the authored version even when this reader lacks the change discriminator.
        var wire = JsonSerializer.Deserialize<ServerCommit[]>(history.Commits)
            ?? throw new InvalidDataException("Missing Harmony history.");
        foreach (var change in wire.SelectMany(commit => commit.ChangeEntities))
        {
            RequireSupportedModelVersion(change.AuthoredModelVersion, ModelVersion);
            if (change.Change.Type == nameof(CreateImportedRecord) && change.AuthoredModelVersion < CompleteRecordModelVersion)
                throw new InvalidDataException("A complete-record create requires Harmony model version 1.");
        }
        var commits = JsonSerializer.Deserialize<Commit[]>(history.Commits, HistoryJsonOptions)!;
        var path = database.Database.GetDbConnection().DataSource;
        var resources = new ResourceContentStore(path);
        var temporary = Directory.CreateTempSubdirectory("motif-record-bootstrap-");
        try
        {
            var stagedPath = Path.Combine(temporary.FullName, "records.sqlite");
            await CopyDatabaseAsync(path, stagedPath);
            await using (var candidate = new RecordStore(stagedPath))
            {
                // The pinned Harmony sync call owns its transaction, so publication must finish in staging.
                await ((ISyncable)candidate.model).AddRangeFromSync(commits);
                var records = await candidate.ReadRecordsAsync();
                foreach (var resource in records.Where(record => record.Kind == "resource"))
                    resources.Read(resource.Values["hash"].GetString()!);
                await candidate.PublishIndexAsync(records, await candidate.CommitRevisionAsync(), refuseUnprovenNative: true);
                var state = await candidate.PublishedStateAsync();
                if (state.Revision != history.Revision || state.SnapshotDigest != history.SnapshotDigest ||
                    state.IndexDigest != history.IndexDigest)
                    throw new InvalidDataException("Replayed history does not match the sender's published point.");
            }
            try
            {
                await CopyDatabaseAsync(stagedPath, path);
                content.Load(await database.Set<ContentRow>().AsNoTracking().ToArrayAsync());
            }
            catch
            {
                requiresReopen = true;
                throw;
            }
        }
        finally { temporary.Delete(recursive: true); }
    }

    private JsonSerializerOptions HistoryJsonOptions => scope.ServiceProvider.GetRequiredService<JsonSerializerOptions>();
}
