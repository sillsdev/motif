using System.Diagnostics;
using Xunit;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>Locates a standard-library-only Python 3 interpreter for tests that run one as a real process.</summary>
public static class PythonExecutable
{
    private static readonly Lazy<string?> Located = new(Locate);

    public static string? Path => Located.Value;

    private static string? Locate()
    {
        foreach (var candidate in new[] { "python3", "python" })
        {
            if (CanRun(candidate)) return candidate;
        }
        return null;
    }

    private static bool CanRun(string candidate)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(candidate, "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            process!.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException)
        {
            return false;
        }
    }
}

/// <summary>
/// Skips at discovery when no Python 3 interpreter is on <c>PATH</c>, rather than let a test fail on a
/// machine that simply never installed one — the same shape as <c>RealParserFactAttribute</c> for pangloss.
/// </summary>
public sealed class RequiresPythonFactAttribute : FactAttribute
{
    public RequiresPythonFactAttribute()
    {
        if (PythonExecutable.Path is null)
            Skip = "No Python 3 interpreter found on PATH (tried 'python3' and 'python').";
    }
}
