using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// The xUnit framework every Motif test assembly runs under: the standard one, except that when
/// <see cref="ShardVariable"/> names a shard, it runs only the test classes that belong to that shard.
/// </summary>
/// <remarks>
/// <para>
/// Two LibLCM caches opening at once in one process race, so every class that opens one shares a serialized
/// collection and a project's LibLCM tests run one at a time. Separate processes cannot race. The test gate
/// therefore starts one process per shard, with the same value <c>i/N</c> meaning "shard <c>i</c> of
/// <c>N</c>, counting from zero", and the shards together run every test exactly once.
/// </para>
/// <para>
/// A class is the unit, never a single test, so a class fixture and the collection fixture are built once in
/// the one process that runs the class. Membership is a stable hash of the class's full name, so a shard's
/// contents do not depend on discovery order and a failing shard reruns the same classes. Unset, every test
/// runs, which is how a bare <c>dotnet test</c> behaves. Pinned by <c>ShardsPartitionEveryClassExactlyOnce</c>.
/// </para>
/// <para>
/// A hash balances test classes, not seconds, and a few slow classes can land together. When
/// <see cref="WeightsVariable"/> names a file of recorded class durations, the classes are instead dealt out
/// heaviest first, each to the lightest shard so far. Every shard process reads the same file and discovers the
/// same classes, so they agree on the deal. A class the file does not know weighs <see cref="UnknownWeight"/>
/// seconds, so a stale file costs balance, never a test. Pinned by <c>WeightsDealTheHeaviestClassesApart</c>.
/// </para>
/// </remarks>
public sealed class ShardedTestFramework(IMessageSink messageSink) : XunitTestFramework(messageSink)
{
    /// <summary>The environment variable naming the shard to run, as <c>index/count</c>.</summary>
    public const string ShardVariable = "MOTIF_TEST_SHARD";

    /// <summary>
    /// The environment variable naming a JSON file that maps test class full names to recorded seconds.
    /// </summary>
    public const string WeightsVariable = "MOTIF_TEST_SHARD_WEIGHTS";

    /// <summary>The seconds a class missing from the weights file is assumed to take.</summary>
    public const double UnknownWeight = 1.0;

    /// <inheritdoc />
    protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName) =>
        new ShardedExecutor(assemblyName, SourceInformationProvider, DiagnosticMessageSink);

    /// <summary>
    /// Reads <see cref="ShardVariable"/>, returning <see langword="null"/> when it is unset and throwing when it
    /// is set to anything but <c>index/count</c> with <c>0 &lt;= index &lt; count</c>.
    /// </summary>
    public static (int Index, int Count)? CurrentShard() => ParseShard(Environment.GetEnvironmentVariable(ShardVariable));

    /// <summary>Parses a shard value; see <see cref="CurrentShard"/>.</summary>
    public static (int Index, int Count)? ParseShard(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var parts = value.Split('/');
        if (parts.Length == 2 && int.TryParse(parts[0], out var index) && int.TryParse(parts[1], out var count) &&
            count > 0 && index >= 0 && index < count)
            return (index, count);
        throw new InvalidOperationException(
            $"{ShardVariable} must be 'index/count' with 0 <= index < count, not '{value}'.");
    }

    /// <summary>The shard, out of <paramref name="count"/>, that runs the class named <paramref name="className"/>.</summary>
    public static int ShardOf(string className, int count)
    {
        // string.GetHashCode is randomized per process, and every shard process must agree.
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(className));
        return (int)(BitConverter.ToUInt32(digest, 0) % (uint)count);
    }

    /// <summary>
    /// Assigns each of <paramref name="classNames"/> to one of <paramref name="count"/> shards: by
    /// <see cref="ShardOf"/> without <paramref name="weights"/>, and heaviest first to the lightest shard with them.
    /// </summary>
    public static IReadOnlyDictionary<string, int> Assign(
        IEnumerable<string> classNames, int count, IReadOnlyDictionary<string, double>? weights)
    {
        var names = classNames.Distinct(StringComparer.Ordinal).ToArray();
        if (weights is null) return names.ToDictionary(name => name, name => ShardOf(name, count), StringComparer.Ordinal);

        var load = new double[count];
        var assignment = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in names
                     .OrderByDescending(name => weights.TryGetValue(name, out var seconds) ? seconds : UnknownWeight)
                     .ThenBy(name => name, StringComparer.Ordinal))
        {
            var lightest = 0;
            for (var shard = 1; shard < count; shard++)
                if (load[shard] < load[lightest]) lightest = shard;
            load[lightest] += weights.TryGetValue(name, out var weight) ? weight : UnknownWeight;
            assignment[name] = lightest;
        }
        return assignment;
    }

    /// <summary>Reads the file <see cref="WeightsVariable"/> names, or returns <see langword="null"/> when it is unset.</summary>
    public static IReadOnlyDictionary<string, double>? CurrentWeights()
    {
        var path = Environment.GetEnvironmentVariable(WeightsVariable);
        if (string.IsNullOrWhiteSpace(path)) return null;
        return JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(path))
            ?? throw new InvalidOperationException($"{WeightsVariable} names '{path}', which holds no weights.");
    }

    private sealed class ShardedExecutor : XunitTestFrameworkExecutor
    {
        private readonly AssemblyName _assemblyName;

        public ShardedExecutor(
            AssemblyName assemblyName, ISourceInformationProvider sourceInformationProvider, IMessageSink diagnosticMessageSink)
            : base(assemblyName, sourceInformationProvider, diagnosticMessageSink) => _assemblyName = assemblyName;

        protected override void RunTestCases(
            IEnumerable<IXunitTestCase> testCases, IMessageSink executionMessageSink,
            ITestFrameworkExecutionOptions executionOptions)
        {
            var selectedLevels = TestLevelClassifier.ParseSelection(
                Environment.GetEnvironmentVariable(TestLevelClassifier.EnvironmentVariable));
            var cases = testCases.ToArray();
            var defaultLevel = TestLevelClassifier.ReadAssemblyDefault(Assembly.Load(_assemblyName));
            cases = cases.GroupBy(ClassOf)
                .Where(group => TestLevelClassifier.IsSelected(group.First(), defaultLevel, selectedLevels))
                .SelectMany(group => group).ToArray();

            var shard = CurrentShard();
            if (shard is not { } s)
            {
                base.RunTestCases(cases, executionMessageSink, executionOptions);
                return;
            }
            var assignment = Assign(cases.Select(ClassOf), s.Count, CurrentWeights());
            base.RunTestCases(cases.Where(testCase => assignment[ClassOf(testCase)] == s.Index),
                executionMessageSink, executionOptions);
        }

        private static string ClassOf(IXunitTestCase testCase) => testCase.TestMethod.TestClass.Class.Name;
    }
}
