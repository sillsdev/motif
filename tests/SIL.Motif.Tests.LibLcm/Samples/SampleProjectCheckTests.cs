using System.Diagnostics;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Samples;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group1)]
public sealed class SampleProjectCheckTests
{
    [RealParserFact]
    public async Task CheckPrintsGrammarFindingsAndWordOutcomesForEachVariant()
    {
        var root = Path.Combine(BuildOutput.ProductDirectory, "SampleChecks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var fixture = RuntimeSampleProjectFixture.Create("runtime-check");
            var tokens = fixture.Tokens;
            fixture.Sample.Stems = [fixture.Sample.Stems[0]];
            fixture.Sample.PartsOfSpeech = [fixture.Sample.PartsOfSpeech[0]];
            fixture.Sample.AffixTemplates = [fixture.Sample.AffixTemplates[0]];
            fixture.Sample.Texts = [new(tokens.TextId, tokens.TextTitle, [tokens.NounForm])];
            var samplePath = Path.Combine(root, "sample.json");
            var bugsPath = Path.Combine(root, "bugs.json");
            const string duplicateTemplateVariant = "duplicate-template";
            await RuntimeSampleProjectFixture.WriteSampleAsync(samplePath, fixture.Sample);
            var bug = new RuntimeBugSpec(duplicateTemplateVariant, tokens.Id + " duplicate template",
                fixture.Sample.Disclaimer,
                new("slow", [tokens.NounForm], "A duplicated template adds another parser path."),
                ["Keep the original noun template."],
                [new("duplicateTemplate", TemplateId: tokens.NounTemplateId, Count: 1)]);
            await RuntimeSampleProjectFixture.WriteBugsAsync(bugsPath, [bug]);

            var builder = Path.Combine(BuildOutput.ProductDirectory,
                OperatingSystem.IsWindows() ? "SIL.Motif.SampleProjects.exe" : "SIL.Motif.SampleProjects");
            var start = new ProcessStartInfo(builder)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("build");
            start.ArgumentList.Add(samplePath);
            start.ArgumentList.Add(Path.Combine(root, "output"));
            start.ArgumentList.Add("--bugs");
            start.ArgumentList.Add(bugsPath);
            start.ArgumentList.Add("--check");
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var standardOutput = await stdout;
            var standardError = await stderr;

            Assert.True(process.ExitCode == 0, standardError);
            Assert.Contains("\"projectPath\"", standardOutput);
            Assert.Contains("[fixed] grammar-health errors=", standardError);
            Assert.Contains("[duplicate-template] grammar-health errors=", standardError);
            Assert.DoesNotContain("[all-bugs] grammar-health", standardError);
            Assert.Contains($"[fixed] word {tokens.NounForm} outcome=analysed", standardError);
            Assert.Contains($"[{duplicateTemplateVariant}] grammar-health errors=", standardError);
            Assert.Contains($"[{duplicateTemplateVariant}] word {tokens.NounForm} outcome=analysed", standardError);
            foreach (var line in standardError.Split('\n').Where(line => line.Contains("finding code=", StringComparison.Ordinal)))
                Assert.Matches("^\\[[^]]+\\] finding code=\\S+ level=(error|warning|info) description=.+\\r?$", line);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
