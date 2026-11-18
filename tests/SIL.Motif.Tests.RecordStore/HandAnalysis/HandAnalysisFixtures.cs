using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HandAnalysis;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.RecordStore;
using SIL.Motif.RecordStore.HandAnalysis;
using SIL.Motif.RecordStore.Lcm;
using Xunit;

namespace SIL.Motif.Tests.RecordStore.HandAnalyses;

/// <summary>An imported private copy plus the LCM cache it came from, the independent reader for comparisons.</summary>
public class ImportedProject : IAsyncLifetime
{
    private readonly string variable;
    private readonly string? keptVariable;
    private string root = "";


    public ImportedProject() : this("MOTIF_RECORD_STORE_INDONESIAN") { }

    protected ImportedProject(string variable, string? keptVariable = null) => (this.variable, this.keptVariable) = (variable, keptVariable);

    public LcmCache? Cache { get; private set; }
    public string Source { get; private set; } = "";
    public string Store => Path.Combine(root, "records.sqlite");

    public async Task InitializeAsync()
    {
        var original = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(original)) return;
        root = Directory.CreateTempSubdirectory("motif-hand-analysis-").FullName;
        var retained = keptVariable is null ? null : Environment.GetEnvironmentVariable(keptVariable);
        var directory = Path.GetDirectoryName(Path.GetFullPath(original))!;
        var retainedProject = retained is null ? null : Path.Combine(retained, "project");
        var retainedSource = retainedProject is null ? null : Path.Combine(retainedProject, Path.GetFileName(original));
        var source = Path.GetFullPath(original);
        var copySource = source;
        if (retainedSource is not null && File.Exists(retainedSource))
        {
            using var expected = File.OpenRead(source);
            using var candidate = File.OpenRead(retainedSource);
            if (System.Security.Cryptography.SHA256.HashData(expected).SequenceEqual(System.Security.Cryptography.SHA256.HashData(candidate)))
                copySource = retainedSource;
        }
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                     .Where(f => !Path.GetFileName(f).Contains(".motif.", StringComparison.OrdinalIgnoreCase)))
        {
            var target = Path.Combine(root, "project", Path.GetRelativePath(directory, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file == source ? copySource : file, target);
        }
        Source = Path.Combine(root, "project", Path.GetFileName(original));
        Cache = new FwDataProjectLoader().LoadScratchCache(Source, Path.Combine(root, "templates"));
        await LcmRecordBridge.ImportAsync(Cache, Source, Store);
    }

    /// <summary>A private copy of the imported store for tests that write.</summary>
    public string CopyStore()
    {
        var copy = Path.Combine(root, "copy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(copy);
        foreach (var file in Directory.EnumerateFileSystemEntries(root, "records.sqlite*"))
            if (File.Exists(file)) File.Copy(file, Path.Combine(copy, Path.GetFileName(file)));
        var resources = Store + ".resources";
        if (Directory.Exists(resources))
            foreach (var file in Directory.EnumerateFiles(resources, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(copy, "records.sqlite.resources", Path.GetRelativePath(resources, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
        return Path.Combine(copy, "records.sqlite");
    }

    public void ReleaseCache()
    {
        Cache?.Dispose();
        Cache = null;
    }

    public Task DisposeAsync()
    {
        ReleaseCache();
        if (root.Length > 0 && Directory.Exists(root)) Directory.Delete(root, true);
        return Task.CompletedTask;
    }
}

public sealed class ImportedCebuano : ImportedProject
{
    public ImportedCebuano() : base("MOTIF_RECORD_STORE_CEBUANO", "MOTIF_RECORD_STORE_HAND_DIR") { }
}

internal static class TestWord
{
    public static WordIdentity.CaseForm Key(string ws, string text) => new(new(ws, WfiProjection.Nfd(text)));

    public static WordQuery.Reading Reading(Guid id) => new(new ReadingKey.Analysis(new AnalysisRef.Native(id)));

    public static Guid AnalysisId(WordFact.Reading reading) => Assert.IsType<AnalysisRef.Native>(reading.Analysis).Id;
}

internal static class Pages
{
    /// <summary>Reads every page of a query with the given page size and returns the concatenated items.</summary>
    public static async Task<List<T>> All<T>(Func<PageRequest, ValueTask<Page<T>>> read, int size)
    {
        var items = new List<T>();
        string? cursor = null;
        do
        {
            var page = await read(new PageRequest(size, cursor));
            Assert.InRange(page.Items.Length, 0, size);
            items.AddRange(page.Items);
            cursor = page.NextCursor;
            Assert.True(page.Complete);
            Assert.NotNull(page.Stamp);
        } while (cursor is not null);
        return items;
    }

    public static T Value<T>(this CommandOutcome<T> outcome) where T : class
    {
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        return Assert.IsAssignableFrom<T>(outcome.Value);
    }

    public static async ValueTask<Page<WordFact>> Words(this IHandAnalysisSnapshot snapshot, WordQuery query,
        PageRequest page, CancellationToken ct = default) => (await snapshot.WordsAsync(query, page, ct)).Value();

    public static async ValueTask<ReferencePage> References(this IHandAnalysisSnapshot snapshot, ReferenceQuery query,
        PageRequest page) => (await snapshot.ReferencesAsync(query, page, default)).Value();

    public static async ValueTask<Page<WordHeader>> Inventory(this StoreSnapshot snapshot, PageRequest page) =>
        (await snapshot.InventoryAsync(page)).Value();

    public static string Json(IEnumerable<WordHeader> facts) => JsonSerializer.Serialize(facts);
}
