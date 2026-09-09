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

        var exit = Run("batch", project, words, outPath, "--word-timeout-ms", "1000", "--threads", "1",
            "--stats", "--cache", cache);

        Assert.Equal(0, exit);
        var rows = File.ReadAllText(outPath).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, rows.Length);
        Assert.Equal(["0", "motifa", "3", "ok", "motifa-sig"], rows[0].TrimEnd('\r').Split('\t'));
        Assert.Equal(["1", "zzz", "3", "none", "-"], rows[1].TrimEnd('\r').Split('\t'));
        Assert.True(File.Exists(cache));
        var argv = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(_root, "_pangloss-argv.json")));
        Assert.Equal("batch", argv![0]);
    }

    [Fact]
    public void Assess_IsNoLongerASubcommandTheFakeAnswers()
    {
        var project = Path.Combine(_root, "p.fwdata");
        File.WriteAllText(project, "never read");

        var exit = Run("assess", project, "--report", Path.Combine(_root, "r.json"));

        Assert.Equal(64, exit);
    }

    private static int Run(params string[] args)
    {
        var start = new ProcessStartInfo(FakeParser.ExecutablePath)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var err = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        process.WaitForExit();
        _ = err.Result;
        _ = output.Result;
        return process.ExitCode;
    }
}
