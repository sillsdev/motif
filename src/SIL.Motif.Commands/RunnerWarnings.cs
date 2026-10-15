namespace SIL.Motif.Commands;

/// <summary>Wraps runner-start warning output so a wait that wakes the runner repeatedly says each thing once.</summary>
public static class RunnerWarnings
{
    /// <summary>
    /// A report callback that forwards each distinct message once. Create one per wait: a wait that wakes the
    /// runner every few seconds would otherwise repeat the same warning for as long as it lasts.
    /// </summary>
    public static Action<string>? Once(Action<string>? report)
    {
        if (report is null) return null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return message =>
        {
            lock (seen)
                if (!seen.Add(message)) return;
            report(message);
        };
    }
}
