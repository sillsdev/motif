using System.Reflection;
using System.Security.Cryptography;
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
/// </remarks>
public sealed class ShardedTestFramework(IMessageSink messageSink) : XunitTestFramework(messageSink)
{
    /// <summary>The environment variable naming the shard to run, as <c>index/count</c>.</summary>
    public const string ShardVariable = "MOTIF_TEST_SHARD";

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

    private sealed class ShardedExecutor(
        AssemblyName assemblyName, ISourceInformationProvider sourceInformationProvider, IMessageSink diagnosticMessageSink)
        : XunitTestFrameworkExecutor(assemblyName, sourceInformationProvider, diagnosticMessageSink)
    {
        protected override void RunTestCases(
            IEnumerable<IXunitTestCase> testCases, IMessageSink executionMessageSink,
            ITestFrameworkExecutionOptions executionOptions)
        {
            var shard = CurrentShard();
            var selected = shard is { } s
                ? testCases.Where(testCase => ShardOf(testCase.TestMethod.TestClass.Class.Name, s.Count) == s.Index)
                : testCases;
            base.RunTestCases(selected, executionMessageSink, executionOptions);
        }
    }
}
