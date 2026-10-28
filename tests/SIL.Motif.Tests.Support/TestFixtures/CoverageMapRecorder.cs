using System.Reflection;
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
    private readonly Dictionary<Assembly, FieldInfo?> _trackers = [];
    private readonly Dictionary<Assembly, HashSet<int>> _ambient = [];
    private readonly Dictionary<Assembly, HashSet<int>> _collectionBase = [];
    private readonly Dictionary<string, Dictionary<Assembly, HashSet<int>>> _collectionBases = [];
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

    private Dictionary<Assembly, HashSet<int>> Take()
    {
        var taken = new Dictionary<Assembly, HashSet<int>>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic || HitsArrayOf(assembly) is not { } field || field.GetValue(null) is not int[] hits) continue;
            HashSet<int>? set = null;
            for (var index = 0; index < hits.Length; index++)
            {
                if (hits[index] == 0) continue;
                (set ??= []).Add(index);
                hits[index] = 0;
            }
            if (set is not null) taken[assembly] = set;
        }
        return taken;
    }

    private FieldInfo? HitsArrayOf(Assembly assembly)
    {
        if (_trackers.TryGetValue(assembly, out var known)) return known;
        FieldInfo? field = null;
        try
        {
            field = assembly.GetTypes()
                .FirstOrDefault(type => type.Namespace == CoverageSourceResolver.TrackerNamespace)
                ?.GetField("HitsArray", BindingFlags.Public | BindingFlags.Static);
        }
        catch (ReflectionTypeLoadException exception)
        {
            field = exception.Types
                .FirstOrDefault(type => type?.Namespace == CoverageSourceResolver.TrackerNamespace)
                ?.GetField("HitsArray", BindingFlags.Public | BindingFlags.Static);
        }
        return _trackers[assembly] = field;
    }

    private static void Merge(Dictionary<Assembly, HashSet<int>> into, Dictionary<Assembly, HashSet<int>> from)
    {
        foreach (var (assembly, set) in from)
        {
            if (!into.TryGetValue(assembly, out var existing)) into[assembly] = existing = [];
            existing.UnionWith(set);
        }
    }

    private static Dictionary<Assembly, HashSet<int>> Copy(Dictionary<Assembly, HashSet<int>> source) =>
        source.ToDictionary(pair => pair.Key, pair => new HashSet<int>(pair.Value));

    private void Write()
    {
        var resolver = new CoverageSourceResolver(_root);
        var files = new Dictionary<Assembly, Dictionary<int, string[]>>();
        var needed = new Dictionary<Assembly, HashSet<int>>();
        Merge(needed, _ambient);
        foreach (var record in _classes) Merge(needed, record.Hits);
        foreach (var bases in _collectionBases.Values) Merge(needed, bases);
        foreach (var (assembly, indices) in needed) files[assembly] = resolver.Resolve(assembly, indices);

        string[] FilesOf(params Dictionary<Assembly, HashSet<int>>[] hits) => hits
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
        string Name, string Collection, Dictionary<Assembly, HashSet<int>> Hits, double Seconds, int Tests, int Skipped);

    private sealed class RecordingSink(CoverageMapRecorder recorder, IMessageSink inner) : Xunit.LongLivedMarshalByRefObject, IMessageSink
    {
        public bool OnMessage(IMessageSinkMessage message)
        {
            recorder.OnMessage(message);
            return inner.OnMessage(message);
        }
    }
}
