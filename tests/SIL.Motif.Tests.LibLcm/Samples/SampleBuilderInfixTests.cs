using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Tests.Parser;
using SIL.LCModel;
using Xunit;

namespace SIL.Motif.Tests.Samples;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group1)]
public sealed class SampleBuilderInfixTests
{
    [Fact]
    public async Task BuilderWritesInfixMorphTypeAndPositionEnvironment()
    {
        var root = NewRoot();
        Directory.CreateDirectory(root);

        try
        {
            var samplePath = await WriteInfixSampleAsync(root);
            using var schema = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(RepositoryRoot(), "samples", "sample.schema.json")));
            using var sample = JsonDocument.Parse(await File.ReadAllTextAsync(samplePath));
            SampleJsonSchemaValidator.AssertValid(sample.RootElement, schema.RootElement);
            using var output = await BuildAsync(root, samplePath);
            using var cache = new FwDataProjectLoader().LoadScratchCache(
                output.RootElement.GetProperty("projectPath").GetString()!);

            var entry = cache.LangProject.LexDbOA.Entries.Single(candidate =>
                candidate.LexemeFormOA is IMoAffixAllomorph form &&
                form.Form.get_String(cache.DefaultVernWs).Text == "um");
            var allomorph = Assert.IsAssignableFrom<IMoAffixAllomorph>(entry.LexemeFormOA);
            Assert.Equal(MoMorphTypeTags.kguidMorphInfix, allomorph.MorphTypeRA!.Guid);
            Assert.Equal("/ # [C] _", allomorph.PositionRS.Single().StringRepresentation.Text);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    internal async Task InfixSampleWordParsesThroughMotifAssess()
    {
        var root = NewRoot();
        Directory.CreateDirectory(root);

        try
        {
            var samplePath = await WriteInfixSampleAsync(root);
            using var output = await BuildAsync(root, samplePath);
            var text = Assert.Single(output.RootElement.GetProperty("texts").EnumerateArray());
            var assess = new ProcessStartInfo(BuildOutput.Cli)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            assess.Environment["MOTIF_WORKER_ROOT"] = Path.Combine(root, "worker-root");
            assess.Environment["MOTIF_DEVELOPER_COMMANDS"] = "1";
            assess.ArgumentList.Add("assess");
            assess.ArgumentList.Add(output.RootElement.GetProperty("projectPath").GetString()!);
            assess.ArgumentList.Add("--texts");
            assess.ArgumentList.Add(text.GetProperty("guid").GetString()!);
            assess.ArgumentList.Add("--json");
            var result = await RunAsync(assess);

            Assert.True(result.ExitCode == 0,
                $"Motif Assess exited {result.ExitCode}.\n{result.StandardOutput}\n{result.StandardError}");
            using var assessment = JsonDocument.Parse(result.StandardOutput);
            var word = Assert.Single(assessment.RootElement.GetProperty("words").EnumerateArray());
            Assert.Equal("sumulat", word.GetProperty("word").GetString());
            Assert.Equal("analysed", word.GetProperty("outcome").GetString());
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static async Task<string> WriteInfixSampleAsync(string root)
    {
        var sample = JsonNode.Parse(await File.ReadAllTextAsync(
            Path.Combine(RepositoryRoot(), "samples", "synthetic-turkic", "sample.json")))!.AsObject();
        sample["stems"]!.AsArray().Add(JsonNode.Parse("""
            { "id": "sulat", "form": "sulat", "partOfSpeech": "verb", "gloss": "write" }
            """));
        sample["affixSlots"]!.AsArray().Add(JsonNode.Parse("""
            { "id": "infix-slot", "name": "Infix", "partOfSpeech": "verb", "optional": true }
            """));
        var verbTemplate = sample["affixTemplates"]!.AsArray().Single(template =>
            template!["id"]!.GetValue<string>() == "verb")!;
        verbTemplate["prefixSlots"] = JsonNode.Parse("""["infix-slot"]""");
        var pastSlot = sample["affixSlots"]!.AsArray().Single(slot => slot!["id"]!.GetValue<string>() == "past")!;
        pastSlot["optional"] = true;
        sample["environments"]!.AsArray().Add(JsonNode.Parse("""
            { "id": "infix-after-initial-c", "name": "After initial consonant", "representation": "/ # [C] _" }
            """));
        sample["affixes"]!.AsArray().Add(JsonNode.Parse("""
            {
              "id": "infix-um",
              "partOfSpeech": "verb",
              "slots": ["infix-slot"],
              "gloss": "infix",
              "allomorphs": [{
                "id": "infix-um-form",
                "form": "um",
                "morphType": "infix",
                "positionEnvironment": "infix-after-initial-c"
              }]
            }
            """));
        sample["texts"]!.AsArray().Clear();
        sample["texts"]!.AsArray().Add(JsonNode.Parse("""
            { "id": "infix-smoke", "title": "Infix test", "sentences": ["sumulat"] }
            """));

        var samplePath = Path.Combine(root, "infix-sample.json");
        await File.WriteAllTextAsync(samplePath, sample.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return samplePath;
    }

    private static async Task<JsonDocument> BuildAsync(string root, string samplePath)
    {
        var builder = Path.Combine(BuildOutput.ProductDirectory,
            OperatingSystem.IsWindows() ? "SIL.Motif.SampleProjects.exe" : "SIL.Motif.SampleProjects");
        var start = new ProcessStartInfo(builder) { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("build");
        start.ArgumentList.Add(samplePath);
        start.ArgumentList.Add(Path.Combine(root, "output"));
        return ParseProcessResult(await RunAsync(start));
    }

    private static JsonDocument ParseProcessResult(ProcessResult result)
    {
        Assert.True(result.ExitCode == 0, result.StandardError);
        return JsonDocument.Parse(result.StandardOutput);
    }

    private static async Task<ProcessResult> RunAsync(ProcessStartInfo start)
    {
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, await output, await error);
    }

    private static string NewRoot() => Path.Combine(BuildOutput.ProductDirectory,
        "SampleBuilderInfix", Guid.NewGuid().ToString("N"));

    private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(BuildOutput.ProductDirectory, "..", ".."));

    private static void DeleteDirectory(string root)
    {
        try { Directory.Delete(root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group1)]
[Trait("MotifTestLevel", "System")]
public sealed class RealParserSampleBuilderInfixTests
{
    [RealParserFact]
    public Task InfixSampleWordParsesThroughMotifAssess() =>
        new SampleBuilderInfixTests().InfixSampleWordParsesThroughMotifAssess();
}
