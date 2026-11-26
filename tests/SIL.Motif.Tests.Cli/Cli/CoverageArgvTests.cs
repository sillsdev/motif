using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary><c>coverage</c> through the released executable: JSON and text goldens, usage and refusals.</summary>
public sealed class CoverageArgvTests : IDisposable
{
    private const string Pin = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string Header = "word\ttokens\tstate\texpected_readings\tclass\tevidence\tprovenance\tgrammar_revision";
    private const string Alpha = "11111111-0000-0000-0000-000000000001";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-coverage-argv-" + Guid.NewGuid().ToString("N"));
    private readonly string _workerRoot = Path.Combine(Path.GetTempPath(), "motif-coverage-worker-" + Guid.NewGuid().ToString("N"));
    private readonly string _project;
    private readonly string _ledger;
    private readonly string _sidecar;
    private readonly string _frequency;

    public CoverageArgvTests()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_workerRoot);
        _project = Path.Combine(_root, "p.fwdata");
        File.WriteAllText(_project, "<languageproject>\n<rt class=\"LangProject\" guid=\"" + Alpha + "\">\n</rt>\n</languageproject>\n");
        _ledger = Path.Combine(_root, "ledger.tsv");
        _sidecar = Path.Combine(_root, "ledger.meta.json");
        _frequency = Path.Combine(_root, "freq.tsv");
        File.WriteAllText(_ledger, Header + "\n" + string.Concat(new[]
        {
            Row("alpha", 3, "Unverified", "", ""), Row("beta", 2, "Open", "", "compounding"),
            Row("gamma", 4, "Disposition", "a place", "name"), Row("delta", 1, "Open", "", "clitic"),
        }.Select(row => row + "\n")));
        File.WriteAllText(_sidecar,
            "{\"tokenizerId\":\"t@1\",\"tokenizerSha256\":\"" + Pin + "\",\"fold\":\"none\",\"normalization\":\"NFC\"," +
            "\"sourceIds\":[\"s1\"],\"scopeFilter\":\"all\",\"frequencySource\":\"freq.tsv\",\"grammarRevision\":\"rev-a\"," +
            "\"rowCount\":4,\"tokenSum\":10}");
        File.WriteAllText(_frequency, string.Concat(new[] { ("alpha", 3), ("beta", 2), ("gamma", 4), ("delta", 1) }
            .Select(item => $"{item.Item1}\t{item.Item2}\n")));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        try { Directory.Delete(_workerRoot, recursive: true); } catch (IOException) { }
    }

    private static string Row(string word, int tokens, string state, string evidence, string cls) =>
        string.Join('\t', word, tokens, state, "", cls, evidence, "p", "rev-a");

    private string[] Files => ["--project", _project, "--ledger", _ledger, "--sidecar", _sidecar, "--frequency", _frequency];

    private Task<CliProcessResult> Run(params string[] arguments) =>
        CliProcess.RunAsync(_workerRoot, null, false, ["coverage", .. arguments]);

    private static void Matches(string golden, string actual)
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        var path = Path.Combine(repositoryRoot, "tests", "SIL.Motif.Tests.Cli", "Golden", golden);
        if (Environment.GetEnvironmentVariable("MOTIF_UPDATE_GOLDEN") == "1") File.WriteAllText(path, actual);
        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal), actual);
    }

    [Fact]
    public async Task JsonOutputMatchesItsGolden()
    {
        var result = await Run([.. Files, "--json"]);

        Assert.True(result.ExitCode == 0, result.Error);
        Matches("coverage.json", result.Output.Replace("\r\n", "\n", StringComparison.Ordinal));
        var root = JsonDocument.Parse(result.Output).RootElement;
        Assert.Equal("unmeasured", root.GetProperty("done").GetProperty("verdict").GetString());
        Assert.Equal(1, root.GetProperty("open").GetProperty("byMechanism").GetProperty("compounding").GetProperty("types").GetInt32());
        Assert.True(!root.TryGetProperty("tokenShare", out var share) || share.ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task TextOutputMatchesItsGoldenAndPrintsNoTokenPercentage()
    {
        var result = await Run([.. Files, "--tokens"]);

        Assert.True(result.ExitCode == 0, result.Error);
        Matches("coverage.txt", result.Output.Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.DoesNotContain("%", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TokensAddsTheTokenShareToJson()
    {
        var result = await Run([.. Files, "--json", "--tokens", "--limit", "1"]);

        Assert.True(result.ExitCode == 0, result.Error);
        var root = JsonDocument.Parse(result.Output).RootElement;
        Assert.Equal(0.1, root.GetProperty("tokenShare").GetDouble(), 6);
        Assert.Equal(1, root.GetProperty("open").GetProperty("ranked").GetArrayLength());
    }

    [Theory]
    [InlineData("--ledger")]
    [InlineData("--sidecar")]
    [InlineData("--frequency")]
    public async Task AMissingInputFlagPrintsTheUsage(string omitted)
    {
        var arguments = Files.ToList();
        var at = arguments.IndexOf(omitted);
        arguments.RemoveRange(at, 2);

        var result = await Run([.. arguments]);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("coverage --project <fwdata> --ledger <tsv>", result.Error + result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownFlagAndABadLimitPrintUsage()
    {
        Assert.NotEqual(0, (await Run([.. Files, "--bogus", "x"])).ExitCode);
        var limit = await Run([.. Files, "--limit", "many"]);
        Assert.NotEqual(0, limit.ExitCode);
        Assert.Contains("--limit must be a positive integer", limit.Error + limit.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMalformedLedgerIsRefusedWithACoverageCode()
    {
        File.WriteAllText(_ledger, Header.Replace("tokens", "count") + "\n");

        var result = await Run([.. Files, "--json"]);

        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.Refused), result.ExitCode);
        Assert.Contains("coverage.bad-header", result.Error + result.Output, StringComparison.Ordinal);
    }
}
