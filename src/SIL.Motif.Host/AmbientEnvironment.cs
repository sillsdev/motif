namespace SIL.Motif.Host;

/// <summary>
/// The environment variables Motif settings read. A process reads its own; an in-process caller (the CLI entry
/// point under test, an MCP server given its own environment) pushes a dictionary that stands in for the child
/// process's environment for the calling flow only, so concurrent callers do not share variables.
/// </summary>
public static class AmbientEnvironment
{
    private static readonly AsyncLocal<IReadOnlyDictionary<string, string?>?> Current = new();

    /// <summary>The variable's value in the pushed environment, or the process's when none is pushed.</summary>
    public static string? Get(string name) => Current.Value is { } pushed
        ? pushed.GetValueOrDefault(name)
        : Environment.GetEnvironmentVariable(name);

    /// <summary>Makes <paramref name="environment"/> the environment of this flow until the result is disposed.</summary>
    public static IDisposable Push(IReadOnlyDictionary<string, string?> environment)
    {
        var previous = Current.Value;
        Current.Value = new Dictionary<string, string?>(environment, StringComparer.OrdinalIgnoreCase);
        return new Restore(previous);
    }

    private sealed class Restore(IReadOnlyDictionary<string, string?>? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
