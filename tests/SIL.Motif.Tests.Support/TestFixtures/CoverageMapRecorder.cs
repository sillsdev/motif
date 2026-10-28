using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// For a coverage-map build only: records which source files each test class's code executes, by reading and
/// clearing the hit arrays coverlet injects into every instrumented module whenever a class finishes.
/// </summary>
/// <remarks>
/// Classes run one at a time in this mode, so the hits between a class's start and finish are its own. Hits in a
/// collection fixture (before its first class starts) are shared by every class of the collection, and hits
/// outside any collection (assembly fixtures) by every class of the process. The result is one JSON file per test
/// process in <see cref="ShardedTestFramework.CoverageMapVariable"/>; <c>tools/MotifChangedTests.psm1</c> merges them. Off unless the
/// variable is set, so an ordinary run pays nothing.
/// </remarks>
internal sealed class CoverageMapRecorder
{
    private readonly string _directory;
    private readonly string _root;
    private readonly object _gate = new();
    private readonly Dictionary<Assembly, (FieldInfo? Hits, FieldInfo? Path)> _trackers = [];
    private List<(string Dll, string HitsFile)>? _childModules;
    private readonly Dictionary<string, HashSet<int>> _ambient = [];
    private readonly Dictionary<string, HashSet<int>> _collectionBase = [];
    private readonly Dictionary<string, Dictionary<string, HashSet<int>>> _collectionBases = [];
    private readonly List<ClassRecord> _classes = [];
    private string _collection = "";
    private string? _assemblyName;

    private CoverageMapRecorder(string directory, string root) => (_directory, _root) = (directory, root);

    /// <summary>The recorder for this process, or <see langword="null"/> when no map build is under way.</summary>
    internal static CoverageMapRecorder? FromEnvironment(ITestFrameworkExecutionOptions executionOptions)
    {
        var directory = Environment.GetEnvironmentVariable(ShardedTestFramework.CoverageMapVariable);
        if (string.IsNullOrWhiteSpace(directory)) return null;
        executionOptions.SetValue("xunit.execution.DisableParallelization", true);
        // bin/<Configuration>/tests/ sits three levels under the repository root.
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        return new CoverageMapRecorder(directory, root);
    }

    /// <summary>Returns a sink that forwards every message to <paramref name="inner"/> after recording it.</summary>
    internal IMessageSink Wrap(IMessageSink inner) => new RecordingSink(this, inner);

    private void OnMessage(IMessageSinkMessage message)
    {
        lock (_gate)
        {
            switch (message)
            {
                case ITestCollectionStarting starting:
                    Merge(_ambient, Take());
                    _collection = starting.TestCollection.DisplayName;
                    _collectionBase.Clear();
                    break;
                case ITestClassStarting:
                    Merge(_collectionBase, Take());
                    break;
                case ITestClassFinished finished:
                    _classes.Add(new ClassRecord(finished.TestClass.Class.Name, _collection, Take(),
                        (double)finished.ExecutionTime, finished.TestsRun, finished.TestsSkipped));
                    break;
                case ITestCollectionFinished:
                    Merge(_collectionBase, Take());
                    _collectionBases[_collection] = Copy(_collectionBase);
                    break;
                case ITestAssemblyStarting starting:
                    _assemblyName = starting.TestAssembly.Assembly.Name;
                    break;
                case ITestAssemblyFinished:
                    Merge(_ambient, Take());
                    Write();
                    break;
            }
        }
    }

    private Dictionary<string, HashSet<int>> Take()
    {
        var taken = new Dictionary<string, HashSet<int>>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic || TrackerOf(assembly).Hits is not { } field || field.GetValue(null) is not int[] hits) continue;
            HashSet<int>? set = null;
            for (var index = 0; index < hits.Length; index++)
            {
                if (hits[index] == 0) continue;
                (set ??= []).Add(index);
                hits[index] = 0;
            }
            if (set is not null) taken[assembly.Location] = set;
        }
        // Child processes load the same instrumented files and add their hits to a shared file when they exit.
        foreach (var (dll, hitsFile) in ChildModules())
        {
            if (TakeChildHits(hitsFile) is not { } childHits) continue;
            if (!taken.TryGetValue(dll, out var existing)) taken[dll] = existing = [];
            existing.UnionWith(childHits);
        }
        return taken;
    }

    private (FieldInfo? Hits, FieldInfo? Path) TrackerOf(Assembly assembly)
    {
        if (_trackers.TryGetValue(assembly, out var known)) return known;
        Type? tracker;
        try { tracker = assembly.GetTypes().FirstOrDefault(type => type.Namespace == CoverageSourceResolver.TrackerNamespace); }
        catch (ReflectionTypeLoadException exception) { tracker = exception.Types.FirstOrDefault(type => type?.Namespace == CoverageSourceResolver.TrackerNamespace); }
        var flags = BindingFlags.Public | BindingFlags.Static;
        return _trackers[assembly] = (tracker?.GetField("HitsArray", flags), tracker?.GetField("HitsFilePath", flags));
    }

    // The instrumented Motif files beside the test assembly, each with the file its tracker merges hits into.
    private List<(string Dll, string HitsFile)> ChildModules()
    {
        if (_childModules is not null) return _childModules;
        var modules = new List<(string, string)>();
        foreach (var dll in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll"))
        {
            var name = Path.GetFileName(dll);
            if (!name.StartsWith("SIL.Motif.", StringComparison.Ordinal) && name != "motif.dll") continue;
            try
            {
                using var stream = File.OpenRead(dll);
                using var pe = new PEReader(stream);
                var metadata = pe.GetMetadataReader();
                foreach (var handle in metadata.TypeDefinitions)
                {
                    var type = metadata.GetTypeDefinition(handle);
                    if (metadata.GetString(type.Namespace) != CoverageSourceResolver.TrackerNamespace) continue;
                    modules.Add((dll, Path.Combine(Path.GetTempPath(), metadata.GetString(type.Name))));
                    break;
                }
            }
            catch (BadImageFormatException) { }
        }
        return _childModules = modules;
    }

    // The file holds a slot count, then one int per slot; reading clears it, so each class gets its own children.
    private static HashSet<int>? TakeChildHits(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                if (stream.Length < sizeof(int)) return null;
                using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
                var length = reader.ReadInt32();
                HashSet<int>? set = null;
                var zero = new byte[sizeof(int)];
                for (var index = 0; index < length && stream.Position + sizeof(int) <= stream.Length; index++)
                {
                    if (reader.ReadInt32() == 0) continue;
                    (set ??= []).Add(index);
                    stream.Seek(-sizeof(int), SeekOrigin.Current);
                    stream.Write(zero);
                }
                return set;
            }
            catch (IOException) { Thread.Sleep(20); }
        }
        return null;
    }

    private static void Merge(Dictionary<string, HashSet<int>> into, Dictionary<string, HashSet<int>> from)
    {
        foreach (var (assembly, set) in from)
        {
            if (!into.TryGetValue(assembly, out var existing)) into[assembly] = existing = [];
            existing.UnionWith(set);
        }
    }

    private static Dictionary<string, HashSet<int>> Copy(Dictionary<string, HashSet<int>> source) =>
        source.ToDictionary(pair => pair.Key, pair => new HashSet<int>(pair.Value));

    private void Write()
    {
        var resolver = new CoverageSourceResolver(_root);
        var files = new Dictionary<string, Dictionary<int, string[]>>();
        var needed = new Dictionary<string, HashSet<int>>();
        Merge(needed, _ambient);
        foreach (var record in _classes) Merge(needed, record.Hits);
        foreach (var bases in _collectionBases.Values) Merge(needed, bases);
        foreach (var (dll, indices) in needed) files[dll] = resolver.Resolve(dll, indices);

        string[] FilesOf(params Dictionary<string, HashSet<int>>[] hits) => hits
            .SelectMany(set => set)
            .SelectMany(pair => pair.Value.SelectMany(index => files[pair.Key].GetValueOrDefault(index) ?? []))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        var ambient = FilesOf(_ambient);
        var classes = _classes.GroupBy(record => record.Name).ToDictionary(group => group.Key, group => new
        {
            files = FilesOf(group.Select(record => record.Hits)
                .Concat(group.Select(record => _collectionBases.GetValueOrDefault(record.Collection) ?? []))
                .ToArray()).Union(ambient, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            seconds = Math.Round(group.Sum(record => record.Seconds), 3),
            tests = group.Sum(record => record.Tests),
            skipped = group.Sum(record => record.Skipped),
        });
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, $"{_assemblyName?.Split(',')[0] ?? "assembly"}.{Environment.ProcessId}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { assembly = _assemblyName, classes }));
    }

    private sealed record ClassRecord(
        string Name, string Collection, Dictionary<string, HashSet<int>> Hits, double Seconds, int Tests, int Skipped);

    private sealed class RecordingSink(CoverageMapRecorder recorder, IMessageSink inner) : Xunit.LongLivedMarshalByRefObject, IMessageSink
    {
        public bool OnMessage(IMessageSinkMessage message)
        {
            recorder.OnMessage(message);
            return inner.OnMessage(message);
        }
    }
}
