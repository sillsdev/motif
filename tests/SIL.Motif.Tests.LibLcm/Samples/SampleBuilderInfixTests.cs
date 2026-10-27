using System.Diagnostics;
using System.Text.Json;
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
            var fixture = await WriteInfixSampleAsync(root);
            var tokens = fixture.Tokens;
            using var schema = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(RepositoryRoot(), "samples", "sample.schema.json")));
            using var sample = JsonDocument.Parse(await File.ReadAllTextAsync(fixture.SamplePath));
            SampleJsonSchemaValidator.AssertValid(sample.RootElement, schema.RootElement);
            using var output = await BuildAsync(root, fixture.SamplePath);
            using var cache = new FwDataProjectLoader().LoadScratchCache(
                output.RootElement.GetProperty("projectPath").GetString()!);

            var entry = cache.LangProject.LexDbOA.Entries.Single(candidate =>
                candidate.LexemeFormOA is IMoAffixAllomorph form &&
                form.Form.get_String(cache.DefaultVernWs).Text == tokens.InfixForm);
            var allomorph = Assert.IsAssignableFrom<IMoAffixAllomorph>(entry.LexemeFormOA);
            Assert.Equal(MoMorphTypeTags.kguidMorphInfix, allomorph.MorphTypeRA!.Guid);
            Assert.Equal(tokens.InfixEnvironmentRepresentation, allomorph.PositionRS.Single().StringRepresentation.Text);
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
            var fixture = await WriteInfixSampleAsync(root);
            var tokens = fixture.Tokens;
            using var output = await BuildAsync(root, fixture.SamplePath);
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
            Assert.Equal(tokens.InfixResult, word.GetProperty("word").GetString());
            Assert.Equal("analysed", word.GetProperty("outcome").GetString());
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static async Task<WrittenFixture> WriteInfixSampleAsync(string root)
    {
        var runtime = RuntimeSampleProjectFixture.Create("runtime-infix");
        var sample = runtime.Sample;
        var tokens = runtime.Tokens;
        sample.AffixSlots.Add(new(tokens.InfixSlotId, tokens.InfixSlotName, tokens.VerbId, Optional: true));
        var templateIndex = sample.AffixTemplates.FindIndex(template => template.Id == tokens.VerbTemplateId);
        sample.AffixTemplates[templateIndex] = sample.AffixTemplates[templateIndex] with
            { PrefixSlots = [tokens.InfixSlotId] };
        sample.Environments.Add(new(tokens.InfixEnvironmentId, tokens.Id + " initial position",
            tokens.InfixEnvironmentRepresentation));
        sample.Affixes.Add(new(tokens.Id + "-infix-affix", tokens.VerbId, [tokens.InfixSlotId],
            tokens.Id + " infix", [new(tokens.Id + "-infix-form", tokens.InfixForm,
                MorphType: "infix", PositionEnvironment: tokens.InfixEnvironmentId)]));
        sample.Texts = [new(tokens.TextId, tokens.TextTitle, [tokens.InfixResult])];
        var samplePath = Path.Combine(root, "infix-sample.json");
        await RuntimeSampleProjectFixture.WriteSampleAsync(samplePath, sample);
        return new WrittenFixture(samplePath, tokens);
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
    private sealed record WrittenFixture(string SamplePath, RuntimeSampleTokens Tokens);
}

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group1)]
[Trait("MotifTestLevel", "System")]
public sealed class RealParserSampleBuilderInfixTests
{
    [RealParserFact]
    public Task InfixSampleWordParsesThroughMotifAssess() =>
        new SampleBuilderInfixTests().InfixSampleWordParsesThroughMotifAssess();
}
