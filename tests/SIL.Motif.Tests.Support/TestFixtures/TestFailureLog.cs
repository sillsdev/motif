using System.Globalization;
using System.Text;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Appends one line per failed test to a file that outlives the run, so how often a test fails is a count to read
/// rather than something remembered. The results folder is wiped on every run and cannot hold it.
/// </summary>
/// <remarks>
/// Off unless <see cref="PathVariable"/> names a file; <c>test.ps1</c> names one in the machine-wide folder that holds
/// the test slots. A line is tab separated: the UTC time, the test assembly, the shard (or <c>-</c>), the test's
/// display name and the first line of its failure. Shard processes append to one file, so each line is written with
/// one append.
/// </remarks>
public sealed class TestFailureLog
{
    /// <summary>The environment variable naming the file failures are appended to.</summary>
    public const string PathVariable = "MOTIF_TEST_FAILURE_LOG";

    private const int MessageLimit = 300;
    private static readonly object Gate = new();
    private readonly string _path;
    private string _assembly = "";

    private TestFailureLog(string path) => _path = path;

    /// <summary>The log for this process, or <see langword="null"/> when no file is named.</summary>
    internal static TestFailureLog? FromEnvironment() => At(Environment.GetEnvironmentVariable(PathVariable));

    /// <summary>The log that appends to <paramref name="path"/>, or <see langword="null"/> when there is no path.</summary>
    public static TestFailureLog? At(string? path) => string.IsNullOrWhiteSpace(path) ? null : new TestFailureLog(path);

    /// <summary>Returns a sink that forwards every message to <paramref name="inner"/> after noting any failure.</summary>
    internal IMessageSink Wrap(IMessageSink inner) => new RecordingSink(this, inner);

    /// <summary>The line a failed test adds, without its line ending.</summary>
    public static string Line(DateTime utc, string assembly, string? shard, string testName, string? message) =>
        string.Join('\t', utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture), assembly,
            string.IsNullOrEmpty(shard) ? "-" : shard, Flatten(testName, 400), Flatten(message ?? "", MessageLimit));

    private static string Flatten(string text, int limit)
    {
        var line = text.Split('\n', 2)[0].Replace('\t', ' ').Replace('\r', ' ').Trim();
        return line.Length <= limit ? line : line[..limit];
    }

    private void OnMessage(IMessageSinkMessage message)
    {
        switch (message)
        {
            case ITestAssemblyStarting starting:
                _assembly = starting.TestAssembly.Assembly.Name.Split(',')[0];
                break;
            case ITestFailed failed:
                Record(_assembly, Environment.GetEnvironmentVariable(ShardedTestFramework.ShardVariable),
                    failed.Test.DisplayName, failed.Messages.FirstOrDefault());
                break;
        }
    }

    /// <summary>Appends the line for one failed test.</summary>
    public void Record(string assembly, string? shard, string testName, string? message)
    {
        var line = Line(DateTime.UtcNow, assembly, shard, testName, message);
        try
        {
            lock (Gate)
            {
                if (Path.GetDirectoryName(Path.GetFullPath(_path)) is { Length: > 0 } directory)
                    Directory.CreateDirectory(directory);
                using var file = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                file.Write(Encoding.UTF8.GetBytes(line + "\n"));
            }
        }
        // A log that cannot be written must not fail the run it describes.
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class RecordingSink(TestFailureLog log, IMessageSink inner) : Xunit.LongLivedMarshalByRefObject, IMessageSink
    {
        public bool OnMessage(IMessageSinkMessage message)
        {
            log.OnMessage(message);
            return inner.OnMessage(message);
        }
    }
}
