using System.Text.Json;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.Parser;
using Xunit;

namespace SIL.Motif.Tests.TestFixtures;

internal static class TestGrammars
{
    internal static string? SetsRoot { get; } = ResolveSetsRoot();

    internal static string MissingReason => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MOTIF_TEST_GRAMMARS"))
        ? "Test grammars are absent. Run evals/Get-TestGrammars.ps1 or set MOTIF_TEST_GRAMMARS to a pinned checkout."
        : $"MOTIF_TEST_GRAMMARS does not contain evals/sets: {Environment.GetEnvironmentVariable("MOTIF_TEST_GRAMMARS")}";

    private static string? ResolveSetsRoot()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("MOTIF_TEST_GRAMMARS");
        var repositoryRoot = configuredRoot;
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            var lockPath = Path.Combine(AppContext.BaseDirectory, "test-grammars.lock.json");
            if (!File.Exists(lockPath)) return null;
            using var lockFile = JsonDocument.Parse(File.ReadAllText(lockPath));
            var commit = lockFile.RootElement.GetProperty("commit").GetString();
            if (string.IsNullOrWhiteSpace(commit)) return null;
            repositoryRoot = Path.GetFullPath(Path.Combine(BuildOutput.ProductDirectory, "..", ".cache",
                "test-grammars", commit));
        }

        var setsRoot = Path.Combine(repositoryRoot, "evals", "sets");
        return Directory.Exists(setsRoot) ? Path.GetFullPath(setsRoot) : null;
    }
}

/// <summary>Runs an evaluation-set check when its pinned data and PanGloss are available.</summary>
public sealed class RealEvalSetParserFactAttribute : RealParserFactAttribute
{
    /// <summary>Skips with the missing dependency when evaluation data or PanGloss is unavailable.</summary>
    public RealEvalSetParserFactAttribute()
    {
        if (Skip is null && TestGrammars.SetsRoot is null)
            Skip = TestGrammars.MissingReason;
    }
}

/// <summary>Runs a check that reads the pinned evaluation sets but needs no parser.</summary>
public sealed class EvalSetFactAttribute : FactAttribute
{
    /// <summary>Skips with the missing dependency when the pinned evaluation data is unavailable.</summary>
    public EvalSetFactAttribute()
    {
        if (TestGrammars.SetsRoot is null)
            Skip = TestGrammars.MissingReason;
    }
}
