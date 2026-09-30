using System.Text.Json;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Points a test at the fake <c>pangloss</c> executable and writes the behaviour it should take.
/// </summary>
/// <remarks>
/// The fake honours the real command contract, so a test using it exercises the genuine process boundary
/// — argument building, stream draining, exit codes, cancellation and report parsing — without a Rust
/// build or a grammar a parser would accept. Behaviour travels in a file beside the grammar source rather
/// than in the environment, so parallel test classes cannot see each other's settings.
/// </remarks>
internal static class FakeParser
{
    private const string BehaviourFileName = "_fake-pangloss.json";
    private const string WrongDescriptionFileName = "_fake-pangloss-wrong-description";
    private const string RecordInvocationsSentinel = "_fake-pangloss-record-invocations";
    private const string InvocationsFileName = "_pangloss-invocations.log";

    internal static string ExecutableFileName => OperatingSystem.IsWindows() ? "pangloss.exe" : "pangloss";

    /// <summary>
    /// The fake parser's path: a directory below the test binaries, never beside them, because parser
    /// discovery prefers an executable sitting next to the running application.
    /// </summary>
    internal static string ExecutablePath
    {
        get
        {
            var path = Path.Combine(AppContext.BaseDirectory, "fake-pangloss", ExecutableFileName);
            if (!File.Exists(path))
                throw new FileNotFoundException("The fake parser was not built beside the tests.", path);
            return path;
        }
    }

    /// <summary>Tells the fake how to behave for candidates exported into this directory.</summary>
    internal static void Behave(string candidateDirectory, object behaviour) =>
        File.WriteAllText(Path.Combine(candidateDirectory, BehaviourFileName),
            JsonSerializer.Serialize(behaviour));

    /// <summary>
    /// Tells one copy of the fake (from <see cref="Copy"/>) how to behave wherever its candidate is exported,
    /// for work such as a Trial whose candidate lands in a directory the test cannot know in advance.
    /// </summary>
    internal static void BehaveBesideExecutable(string copiedExecutable, object behaviour) =>
        Behave(Path.GetDirectoryName(copiedExecutable)!, behaviour);

    /// <summary>
    /// Copies the fake into <paramref name="directory"/> and has that copy log each command it runs, so a
    /// test can tell that work reached this copy rather than some other parser.
    /// </summary>
    internal static string CopyRecordingInvocations(string directory) =>
        CopyWithSentinel(directory, RecordInvocationsSentinel);

    /// <summary>The commands a copy from <see cref="CopyRecordingInvocations"/> has run, in order.</summary>
    internal static IReadOnlyList<string> Invocations(string copiedExecutable)
    {
        var log = Path.Combine(Path.GetDirectoryName(copiedExecutable)!, InvocationsFileName);
        return File.Exists(log) ? File.ReadAllLines(log) : [];
    }

    internal static string CopyWithWrongDescription(string candidateDirectory)
    {
        return CopyWithSentinel(candidateDirectory, WrongDescriptionFileName);
    }

    internal static string CopyWithSentinel(string candidateDirectory, string sentinel)
    {
        var executable = Copy(candidateDirectory);
        File.WriteAllText(Path.Combine(candidateDirectory, sentinel), string.Empty);
        return executable;
    }

    internal static string Copy(string candidateDirectory)
    {
        Directory.CreateDirectory(candidateDirectory);
        var sourceDirectory = Path.GetDirectoryName(ExecutablePath)!;
        // The whole build, not only pangloss.*: the SQLite statistics cache needs its own libraries beside it.
        foreach (var source in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(source).StartsWith('_')) continue;
            var target = Path.Combine(candidateDirectory, Path.GetRelativePath(sourceDirectory, source));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            // A second copy into one directory must not rewrite an executable a parser it started still runs.
            if (IsSameFile(source, target)) continue;
            File.Copy(source, target, overwrite: true);
        }
        return Path.Combine(candidateDirectory, ExecutableFileName);
    }

    private static bool IsSameFile(string source, string target)
    {
        var existing = new FileInfo(target);
        if (!existing.Exists) return false;
        var original = new FileInfo(source);
        return existing.Length == original.Length && existing.LastWriteTimeUtc == original.LastWriteTimeUtc;
    }
}
