namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// The test classes a changed-files run keeps: the file <see cref="Variable"/> names holds one entry per line,
/// either a class full name or, with a trailing <c>*</c>, a prefix, and every other class is skipped.
/// </summary>
public sealed class TestClassSelection
{
    /// <summary>The environment variable naming the selection file; unset, every class runs.</summary>
    public const string Variable = "MOTIF_TEST_CLASS_SELECTION";

    private readonly HashSet<string> _names;
    private readonly string[] _prefixes;

    /// <summary>Builds a selection from entry lines; blank lines and lines starting with <c>#</c> are ignored.</summary>
    public TestClassSelection(IEnumerable<string> entries)
    {
        var lines = entries.Select(line => line.Trim()).Where(line => line.Length > 0 && line[0] != '#').ToArray();
        _names = lines.Where(line => !line.EndsWith('*')).ToHashSet(StringComparer.Ordinal);
        _prefixes = lines.Where(line => line.EndsWith('*')).Select(line => line[..^1]).ToArray();
    }

    /// <summary>Reads the file <see cref="Variable"/> names, or returns <see langword="null"/> when it is unset.</summary>
    public static TestClassSelection? Current()
    {
        var path = Environment.GetEnvironmentVariable(Variable);
        return string.IsNullOrWhiteSpace(path) ? null : new TestClassSelection(File.ReadAllLines(path));
    }

    /// <summary>Whether the class named <paramref name="className"/> is kept.</summary>
    public bool Includes(string className) =>
        _names.Contains(className) || _prefixes.Any(prefix => className.StartsWith(prefix, StringComparison.Ordinal));
}
