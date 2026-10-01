using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// Pins <c>motif trace</c>: a live trace of a seeded word prints the record Try a Word reads, whole, and a saved trace
/// read with <c>--load</c> prints the same reading the window builds from that file.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class TraceArgvTests(PristineProjectFixture pristine) : IDisposable
{
    private const string GoldenFile = "trace-motifa.golden.json";
    private readonly string _managedRoot = Path.Combine(
        Path.GetTempPath(), "motif-trace-argv-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task TraceJsonOnTheSeededProjectMatchesTheGolden()
    {
        var (project, baseline) = TracedProject();

        var result = await CliProcess.RunAsync(_managedRoot, null, true,
            "trace", "--project", project, "--word", SeededProject.FirstForm, "--json");

        Assert.True(result.ExitCode == 0, result.Error);
        var actual = Normalize(result.Output, baseline);
        var golden = Path.Combine(AppContext.BaseDirectory, "TestFixtures", GoldenFile);
        var expected = File.Exists(golden) ? File.ReadAllText(golden) : string.Empty;
        if (actual != expected.ReplaceLineEndings("\n"))
        {
            var written = Path.Combine(Path.GetTempPath(), GoldenFile);
            File.WriteAllText(written, actual);
            Assert.Fail($"motif trace --json no longer matches {GoldenFile}; the new output is at {written}.");
        }
    }

    [Fact]
    public async Task TraceTextNamesTheParseAndWhereEachNameOpens()
    {
        var (project, _) = TracedProject();

        var result = await CliProcess.RunAsync(_managedRoot, null, true,
            "trace", "--project", project, "--word", SeededProject.FirstForm);

        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Contains("Trace of motifa: parsed, 1 analysis", result.Output, StringComparison.Ordinal);
        Assert.Contains($"Analysis: motifa ({SeededProject.FirstGloss})", result.Output, StringComparison.Ordinal);
        Assert.Contains("opens in Lexicon Edit: silfw:", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASavedTraceLoadsIntoTheReadingTheWindowBuildsFromIt()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestFixtures", "trace-details-v2-kumata.json");

        var result = await CliProcess.RunAsync(_managedRoot, null, true, "trace", "--load", path, "--json");

        Assert.True(result.ExitCode == 0, result.Error);
        var loaded = ProjectionJson.Deserialize<WordTraceResponse>(result.Output)!;
        var window = WordTraceQuery.LoadDiagnostic(File.ReadAllText(path)).Value!;
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(JsonValueKind.Object, json.RootElement.GetProperty("root").ValueKind);
        Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("candidates").ValueKind);
        Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("analyses").ValueKind);
        Assert.Equal(ProjectionJson.Serialize(window.Root), ProjectionJson.Serialize(loaded.Root));
        Assert.Equal(ProjectionJson.Serialize(window.Candidates), ProjectionJson.Serialize(loaded.Candidates));
        Assert.Equal(ProjectionJson.Serialize(window.Analyses), ProjectionJson.Serialize(loaded.Analyses));
        Assert.Equal(ProjectionJson.Serialize(window.Reading!), ProjectionJson.Serialize(loaded.Reading!));
        Assert.NotEmpty(loaded.Reading!.Refs);
    }

    [Fact]
    public async Task AMissingSavedTraceIsNotFound()
    {
        var result = await CliProcess.RunAsync(_managedRoot, null, true,
            "trace", "--load", Path.Combine(_managedRoot, "absent.json"), "--json");

        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.NotFound), result.ExitCode);
        var envelope = ProjectionJson.Deserialize<FailureEnvelope>(result.Error)!;
        Assert.Equal("wordtrace.diagnostic-unreadable", envelope.Code);
    }

    [Theory]
    [InlineData("trace", "--project", "x.fwdata")]
    [InlineData("trace", "--word", "motifa")]
    [InlineData("trace", "--load", "x.json", "--word", "motifa")]
    public async Task InvalidTraceArgumentsPrintTheUsageLine(params string[] arguments)
    {
        var result = await CliProcess.RunAsync(_managedRoot, null, true, arguments);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Usage: motif trace", result.Error, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try { Directory.Delete(_managedRoot, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private (string Project, BaselineCaptureResponse Baseline) TracedProject()
    {
        var project = pristine.CopyProjectFile();
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project), _managedRoot);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var seed = pristine.Seed;
        var stem = new JsonObject
        {
            ["identity"] = new JsonObject
            {
                ["entryId"] = seed.FirstEntryId.ToString("D"),
                ["formId"] = seed.FirstLexemeFormId.ToString("D"),
                ["quality"] = "authored",
            },
            ["form"] = SeededProject.FirstForm,
            ["gloss"] = SeededProject.FirstGloss,
        };
        var lookup = new JsonObject
        {
            ["type"] = "LexicalLookup", ["source"] = "Morphology", ["inputShape"] = SeededProject.FirstForm,
            ["sourceIdentity"] = new JsonObject { ["kind"] = "stratum", ["id"] = "0", ["quality"] = "grammar-local" },
            ["attemptedMorphs"] = new JsonArray(stem.DeepClone()),
            ["children"] = new JsonArray(new JsonObject
            {
                ["type"] = "Successful", ["outputShape"] = SeededProject.FirstForm, ["children"] = new JsonArray(),
            }),
        };
        var tree = new JsonObject
        {
            ["type"] = "WordAnalysis", ["inputShape"] = SeededProject.FirstForm, ["children"] = new JsonArray(lookup),
        };
        var analysis = new JsonObject
        {
            ["analysisId"] = "analysis-0", ["index"] = 0, ["surface"] = SeededProject.FirstForm,
            ["morphs"] = new JsonArray(stem),
        };
        FakeParser.Behave(Path.GetDirectoryName(captured.Value!.FwDataPath)!, new
        {
            traceSignature = "motifa|motifa",
            traceJson = tree.ToJsonString(),
            traceAnalyses = new[] { JsonDocument.Parse(analysis.ToJsonString()).RootElement },
        });
        return (project, captured.Value);
    }

    // Identities the seed makes afresh each run become their names; times and digests become placeholders.
    private string Normalize(string output, BaselineCaptureResponse baseline)
    {
        var seed = pristine.Seed;
        var text = output
            .Replace(seed.FirstEntryId.ToString("D"), "{FirstEntryId}", StringComparison.OrdinalIgnoreCase)
            .Replace(seed.FirstLexemeFormId.ToString("D"), "{FirstLexemeFormId}", StringComparison.OrdinalIgnoreCase)
            .Replace(baseline.Token.ProjectIdentity, "{ProjectIdentity}", StringComparison.OrdinalIgnoreCase);
        var root = JsonNode.Parse(text)!;
        Blank(root);
        return root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }).ReplaceLineEndings("\n") + "\n";

        static void Blank(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject value:
                    foreach (var name in value.Select(property => property.Key).ToArray())
                    {
                        if (name is "elapsedMs" or "capturedUtc" or "wallElapsedMs" or "bundleDigest" or "diagnosticJson")
                            value[name] = "<varies>";
                        else
                            Blank(value[name]);
                    }
                    break;
                case JsonArray items:
                    foreach (var item in items) Blank(item);
                    break;
            }
        }
    }
}
