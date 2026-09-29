using System.Diagnostics;
using System.Text.Json;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Parser;

/// <summary>
/// Pins the fake parser's <c>batch</c> arm to the row shape <c>BatchTsvParser</c> reads, so a test that
/// drives Motif through the fake exercises the same TSV contract the real binary honours.
/// </summary>
public sealed class FakePanGlossBatchTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "motif-fake-batch-" + Guid.NewGuid().ToString("N"));

    public FakePanGlossBatchTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
    }

    [Fact]
    public void Batch_WritesOneTsvRowPerWord_AndACacheWhenAsked()
    {
        var project = Path.Combine(_root, "p.fwdata");
        File.WriteAllText(project, "never read");
        var words = Path.Combine(_root, "words.txt");
        File.WriteAllLines(words, ["motifa", "zzz"]);
        var outPath = Path.Combine(_root, "out.tsv");
        var cache = Path.Combine(_root, "cache.bin");

        var result = Run("batch", project, words, outPath, "--word-timeout-ms", "1000", "--threads", "1",
            "--stats", "--cache", cache);

        Assert.True(result.ExitCode == 0, result.FailureDetails);
        var rows = File.ReadAllText(outPath).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, rows.Length);
        Assert.Equal(["0", "motifa", "3", "ok", "motifa-sig"], rows[0].TrimEnd('\r').Split('\t'));
        Assert.Equal(["1", "zzz", "3", "ok", "-"], rows[1].TrimEnd('\r').Split('\t'));
        Assert.True(File.Exists(cache));
        var argv = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(_root, "_pangloss-argv.json")));
        Assert.Equal("batch", argv![0]);
    }

    [Fact]
    public void Assess_IsNoLongerASubcommandTheFakeAnswers()
    {
        var project = Path.Combine(_root, "p.fwdata");
        File.WriteAllText(project, "never read");

        var result = Run("assess", project, "--report", Path.Combine(_root, "r.json"));

        Assert.True(result.ExitCode == 64, result.FailureDetails);
    }

    [Fact]
    public void GrammarHealth_DefaultReportMatchesPanGlossV2Shape()
    {
        var grammar = Path.Combine(_root, "grammar.fwdata");
        var reportPath = Path.Combine(_root, "report.json");
        File.WriteAllText(grammar, "never read");

        var result = Run("grammar-health", grammar, reportPath);

        Assert.True(result.ExitCode == 0, result.FailureDetails);
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = report.RootElement;
        Assert.Equal(2, root.GetProperty("schema_version").GetInt32());
        var project = root.GetProperty("fieldworks_project");
        Assert.Equal("grammar", project.GetProperty("name").GetString());
        Assert.Equal("fwdata_path", project.GetProperty("source").GetString());

        var summary = root.GetProperty("summary");
        var diagnostics = root.GetProperty("diagnostics");
        Assert.Equal(3, summary.GetArrayLength());
        Assert.Equal(3, diagnostics.GetArrayLength());

        var importedWarning = diagnostics[0];
        Assert.Equal("warning", importedWarning.GetProperty("level").GetString());
        Assert.Equal("fwdata.empty-representation", importedWarning.GetProperty("code").GetString());
        Assert.Equal("import", importedWarning.GetProperty("origin").GetString());
        var subject = Assert.Single(importedWarning.GetProperty("subjects").EnumerateArray());
        Assert.Equal("PhBdryMarker", subject.GetProperty("kind").GetString());
        Assert.Null(subject.GetProperty("internal_id").GetString());
        Assert.True(subject.TryGetProperty("opens_in", out var opensIn));
        Assert.Equal("phonemeEdit", opensIn.GetProperty("tool").GetString());
        Assert.Equal("available", subject.GetProperty("fieldworks").GetProperty("status").GetString());

        var importedInfo = diagnostics[1];
        Assert.Equal("info", importedInfo.GetProperty("level").GetString());
        Assert.Equal("fwdata.only-first-used", importedInfo.GetProperty("code").GetString());
        Assert.Equal("import", importedInfo.GetProperty("origin").GetString());
        Assert.Empty(importedInfo.GetProperty("subjects").EnumerateArray());

        var check = diagnostics[2];
        Assert.Equal("warning", check.GetProperty("level").GetString());
        Assert.Equal("check", check.GetProperty("origin").GetString());
        Assert.Equal("guid_not_recorded", Assert.Single(check.GetProperty("subjects").EnumerateArray())
            .GetProperty("fieldworks").GetProperty("reason").GetString());
        Assert.All(diagnostics.EnumerateArray(), diagnostic =>
            Assert.False(diagnostic.TryGetProperty("audience", out _)));
    }

    [Theory]
    [InlineData("batch")]
    [InlineData("grammar-health")]
    public void AHeldInvocationTimesOutWithItsOwnExitCode(string subcommand)
    {
        var project = Path.Combine(_root, "p.fwdata");
        var startedPath = Path.Combine(_root, "started");
        var releasePath = Path.Combine(_root, "release");
        File.WriteAllText(project, "never read");
        FakeParser.Behave(_root, new
        {
            subcommands = new Dictionary<string, object>
            {
                [subcommand] = new
                {
                    startedPath,
                    holdUntilPath = releasePath,
                    holdTimeoutMs = 25,
                },
            },
        });

        var result = subcommand switch
        {
            "batch" => RunBounded("batch", project, Path.Combine(_root, "words.txt"),
                Path.Combine(_root, "out.tsv"), "--word-timeout-ms", "1000", "--threads", "1"),
            "grammar-health" => RunBounded("grammar-health", project, Path.Combine(_root, "report.json")),
            _ => throw new ArgumentOutOfRangeException(nameof(subcommand)),
        };

        Assert.True(File.Exists(startedPath));
        Assert.False(result.TimedOut, "The fake parser did not enforce its hold timeout.");
        Assert.Equal(86, result.ExitCode);
        Assert.Contains("fake parser hold timed out", result.Error, StringComparison.Ordinal);
    }

    private static CliRun Run(params string[] args)
    {
        var start = new ProcessStartInfo(FakeParser.ExecutablePath)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        start.Environment.Remove("ICU_DATA");
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var err = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        process.WaitForExit();
        var standardError = err.GetAwaiter().GetResult();
        var standardOutput = output.GetAwaiter().GetResult();
        return new CliRun(process.ExitCode, standardOutput, standardError);
    }

    private sealed record CliRun(int ExitCode, string Output, string Error)
    {
        public string FailureDetails =>
            $"Fake parser exited {ExitCode}.{Environment.NewLine}Standard error:{Environment.NewLine}{Error}" +
            $"{Environment.NewLine}Standard output:{Environment.NewLine}{Output}";
    }

    private static (int ExitCode, bool TimedOut, string Error) RunBounded(params string[] args)
    {
        var start = new ProcessStartInfo(FakeParser.ExecutablePath)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        var exited = process.WaitForExit(5000);
        if (!exited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
        _ = output.GetAwaiter().GetResult();
        return (exited ? process.ExitCode : -1, !exited, error.GetAwaiter().GetResult());
    }
}
