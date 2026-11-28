using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Records;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Project;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Opt-in measurement of a 5000-entry folder; set MOTIF_RECORD_SCALE=1 to run it. The peak working set it logs is the whole
/// test host's, which includes generating the folder; <c>allocatedBytes</c> is what the import call itself allocated.
/// </summary>
[Collection(LcmCacheParallelCollections.Group2)]
public sealed partial class RecordImportScaleTests(PristineProjectFixture pristine, ITestOutputHelper output) : IDisposable
{
    private static readonly int Entries = int.Parse(Environment.GetEnvironmentVariable("MOTIF_RECORD_ENTRIES") ?? "5000");
    private static readonly int Variants = Entries / 10;
    private const int FilesPerFolder = 1000;
    private static readonly Guid Namespace = new("5f1d8c1e-3b8a-4f0e-9c52-0d6a7b2e41aa");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-record-scale-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidToken();

    /// <summary>A name-based version 5 GUID, so the same folder is written again byte for byte.</summary>
    internal static Guid V5(string name)
    {
        var bytes = new byte[16];
        Namespace.TryWriteBytes(bytes, bigEndian: true, out _);
        var hash = SHA1.HashData([.. bytes, .. Encoding.UTF8.GetBytes(name)]);
        var guid = hash.AsSpan(0, 16).ToArray();
        guid[6] = (byte)((guid[6] & 0x0F) | 0x50);
        guid[8] = (byte)((guid[8] & 0x3F) | 0x80);
        return new Guid(guid, bigEndian: true);
    }

    /// <summary>One template's two renamings, whose differing GUIDs are the objects the record owns.</summary>
    private sealed record Template(string Text, HashSet<int> Owned);

    private static Template TemplateOf(string project, Guid source)
    {
        string Written(Guid root)
        {
            using var cache = new FwDataProjectLoader().LoadCache(project);
            var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
            var exporter = new ProjectRecordExporter(cache, schema);
            var read = RecordFileReader.Read(exporter.Export(source).Text!, schema.Schema, exporter.CreateReadOptions());
            Assert.False(read.HasErrors);
            var document = read.Document!;
            document.Base = null;
            document.Data["head-word"] = null;
            document.Data["homograph-number"] = null;
            return RecordCreatePlanCommandTests.WriteRenamed(document, schema, root);
        }
        var first = Written(Guid.NewGuid());
        var second = Written(Guid.NewGuid());
        var left = GuidToken().Matches(first);
        var right = GuidToken().Matches(second);
        Assert.Equal(left.Count, right.Count);
        var owned = Enumerable.Range(0, left.Count).Where(index => left[index].Value != right[index].Value).ToHashSet();
        return new(first, owned);
    }

    private static string Instance(Template template, string key, Func<string, string> text)
    {
        var index = -1;
        var renamed = GuidToken().Replace(template.Text, match =>
            template.Owned.Contains(++index) ? V5(key + "/" + match.Value.ToLowerInvariant()).ToString("D") : match.Value);
        return text(renamed);
    }

    private static Guid RootOf(string key, Template template)
    {
        var index = -1;
        foreach (Match match in GuidToken().Matches(template.Text))
            if (template.Owned.Contains(++index)) return V5(key + "/" + match.Value.ToLowerInvariant());
        throw new InvalidOperationException("The template owns no object.");
    }

    private (string Project, string Folder, int Files) Generate()
    {
        var folder = Path.Combine(_root, "records");
        var seed = RecordBatchImportTests.Seeded(pristine, folder);
        var main = TemplateOf(seed.Project, seed.Main);
        var variant = TemplateOf(seed.Project, seed.Variant);
        for (var index = 0; index < Entries; index++)
        {
            var key = "main/" + index.ToString(CultureInfo.InvariantCulture);
            var text = Instance(main, key, value => Regex.Replace(value, @"\bzzword\b", "zzw" + index.ToString("D5", CultureInfo.InvariantCulture)));
            Write(folder, "main", index, text);
        }
        for (var index = 0; index < Variants; index++)
        {
            var key = "variant/" + index.ToString(CultureInfo.InvariantCulture);
            var target = RootOf("main/" + (index * (Entries / Variants)).ToString(CultureInfo.InvariantCulture), main);
            var text = Instance(variant, key, value => Regex.Replace(
                value.Replace(seed.Main.ToString("D"), target.ToString("D"), StringComparison.OrdinalIgnoreCase),
                @"\bzzvar\b", "zzv" + index.ToString("D5", CultureInfo.InvariantCulture)));
            Write(folder, "variant", index, text);
        }
        return (seed.Project, folder, Entries + Variants);
    }

    /// <summary>Files go in sub-folders of at most <see cref="FilesPerFolder"/>, as the help advises.</summary>
    private static void Write(string folder, string kind, int index, string text)
    {
        var directory = Path.Combine(folder, kind + (index / FilesPerFolder).ToString("D2", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, kind + index.ToString("D5", CultureInfo.InvariantCulture) + ".fw.md"), text);
    }

    private void Log(string line)
    {
        output.WriteLine(line);
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "record-import-scale.log"), line + Environment.NewLine);
    }

    [RecordScaleFact]
    [Trait("Category", "RecordImportScale")]
    public void FiveThousandNewEntriesAndFiveHundredVariantsAreDryRunAndStagedAsOneProposal()
    {
        var generated = Stopwatch.StartNew();
        var (project, folder, files) = Generate();
        Log($"RECORD_SCALE generated files={files} ms={generated.ElapsedMilliseconds}");
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        Log($"RECORD_SCALE workingBeforeBytes={process.WorkingSet64}");

        var dryAllocated = GC.GetTotalAllocatedBytes(precise: true);
        using var listener = new AllocListener();
        var dry = Stopwatch.StartNew();
        var preview = RecordImportCommand.Run(new(project, null, DirPath: folder));
        dry.Stop();
        Assert.True(preview.Succeeded, preview.Refusal?.Message);
        process.Refresh();
        Log($"RECORD_SCALE dryRun ms={dry.ElapsedMilliseconds} operations={preview.Value!.OperationCount} " +
            $"created={preview.Value.Dir!.Created} refused={preview.Value.Dir.Refused} hostPeakWorkingBytes={process.PeakWorkingSet64} allocatedBytes={GC.GetTotalAllocatedBytes(precise: true) - dryAllocated}");
        Assert.Equal(files, preview.Value.Dir.Files);
        Assert.Equal(files, preview.Value.Dir.Created);
        Assert.True(SIL.Motif.Contract.Responses.ProjectionJson.ToUtf8Bytes(preview.Value).Length <= RecordImportPaging.SummaryBytes);

        var stageAllocated = GC.GetTotalAllocatedBytes(precise: true);
        Thread.Sleep(3000);
        File.WriteAllText("/tmp/claude-1000/types-dry.txt", listener.Top()); listener.Types.Clear();
        var stage = Stopwatch.StartNew();
        var staged = RecordImportCommand.Run(new(project, null, "scale", DirPath: folder));
        stage.Stop();
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        process.Refresh();
        Log($"RECORD_SCALE stage ms={stage.ElapsedMilliseconds} operations={staged.Value!.OperationCount} hostPeakWorkingBytes={process.PeakWorkingSet64} allocatedBytes={GC.GetTotalAllocatedBytes(precise: true) - stageAllocated}");
        Thread.Sleep(3000);
        File.WriteAllText("/tmp/claude-1000/types-stage.txt", listener.Top());
        using var database = ProjectMotifDatabase.Open(project);
        var draftJson = new ProposalRepository(database).GetDraft("scale").ProposalJson!;
        var proposalJson = ProposalCommands.BuildProposalJson(ProposalCommands.DeserializeDraft(draftJson));
        Log($"RECORD_SCALE draftBytes={Encoding.UTF8.GetByteCount(draftJson)} proposalBytes={Encoding.UTF8.GetByteCount(proposalJson)}");
        Assert.Equal(preview.Value.OperationCount, staged.Value.OperationCount);
    }
}

/// <summary>Keeps the 5500-file measurement out of ordinary runs unless explicitly requested.</summary>
public sealed class RecordScaleFactAttribute : FactAttribute
{
    public RecordScaleFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("MOTIF_RECORD_SCALE") != "1") Skip = "Set MOTIF_RECORD_SCALE=1 to measure a 5500-file record import.";
    }
}

public sealed class AllocListener : System.Diagnostics.Tracing.EventListener
{
    public readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> Types = new();
    protected override void OnEventSourceCreated(System.Diagnostics.Tracing.EventSource source)
    {
        if (source.Name == "Microsoft-Windows-DotNETRuntime") EnableEvents(source, System.Diagnostics.Tracing.EventLevel.Verbose, (System.Diagnostics.Tracing.EventKeywords)1);
    }
    protected override void OnEventWritten(System.Diagnostics.Tracing.EventWrittenEventArgs e)
    {
        if (e.EventName?.StartsWith("GCAllocationTick") != true) return;
        var name = (string)e.Payload![e.PayloadNames!.IndexOf("TypeName")]!;
        var amount = Convert.ToInt64(e.Payload[e.PayloadNames.IndexOf("AllocationAmount64")]);
        Types.AddOrUpdate(name, amount, (_, old) => old + amount);
    }
    public string Top() => string.Join("\n", Types.OrderByDescending(p => p.Value).Take(25).Select(p => "TYPE " + p.Value / 1048576 + " MB " + p.Key));
}
