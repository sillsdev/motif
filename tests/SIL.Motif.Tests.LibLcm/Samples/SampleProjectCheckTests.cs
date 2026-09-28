using System.Diagnostics;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Samples;

[Collection(LcmCacheTestCollection.Name)]
public sealed class SampleProjectCheckTests
{
    [RealParserFact]
    public async Task CheckPrintsGrammarFindingsAndWordOutcomesForEachVariant()
    {
        var root = Path.Combine(BuildOutput.ProductDirectory, "SampleChecks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var samplePath = Path.Combine(root, "sample.json");
            var bugsPath = Path.Combine(root, "bugs.json");
            await File.WriteAllTextAsync(samplePath, """
                {
                  "id": "sample-check",
                  "title": "Synthetic check sample",
                  "disclaimer": "SYNTHETIC EXAMPLE. This language data was generated to demonstrate Motif. It is modelled loosely on Turkish, but it is not real Turkish, has not been checked by speakers, and must not be used as a description of any language.",
                  "description": "SYNTHETIC EXAMPLE. This language data was generated to demonstrate Motif. It is modelled loosely on Turkish, but it is not real Turkish, has not been checked by speakers, and must not be used as a description of any language.",
                  "teaches": ["sample checking"],
                  "summary": "A tiny sample for checking the builder.",
                  "language": { "name": "Synthetic check", "tag": "tr" },
                  "phonemes": ["a", "r"],
                  "partsOfSpeech": [{ "id": "noun", "name": "Noun" }],
                  "stems": [{ "id": "word", "form": "ar", "partOfSpeech": "noun", "gloss": "word" }],
                  "affixTemplates": [{ "id": "noun", "name": "Noun", "partOfSpeech": "noun", "final": true }],
                  "texts": [{ "id": "example", "title": "Example", "sentences": ["ar"] }]
                }
                """);
            await File.WriteAllTextAsync(bugsPath, """
                [{
                  "id": "duplicate-template",
                  "title": "Duplicate template",
                  "disclaimer": "SYNTHETIC EXAMPLE. This language data was generated to demonstrate Motif. It is modelled loosely on Turkish, but it is not real Turkish, has not been checked by speakers, and must not be used as a description of any language.",
                  "symptom": { "kind": "slow", "words": ["ar"], "reason": "A duplicated template adds another path." },
                  "fix": ["Keep one noun template."],
                  "patch": [{ "op": "duplicateTemplate", "templateId": "noun", "count": 1 }]
                }]
                """);

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
            Assert.Contains("[fixed] word ar outcome=analysed", standardError);
            Assert.Contains("[duplicate-template] word ar outcome=analysed", standardError);
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
