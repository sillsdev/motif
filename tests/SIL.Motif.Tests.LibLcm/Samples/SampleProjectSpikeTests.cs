using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Samples;

[Collection(LcmCacheTestCollection.Name)]
public sealed class SampleProjectSpikeTests(ITestOutputHelper output)
{
    [Fact]
    public async Task BuilderWritesCurrentFieldWorksModelAndReopenableBackup()
    {
        var root = NewRoot();
        Directory.CreateDirectory(root);

        try
        {
            using var output = await BuildAsync(root);
            var fwDataPath = output.RootElement.GetProperty("projectPath").GetString()!;
            var backupPath = output.RootElement.GetProperty("backupPath").GetString()!;
            var project = XDocument.Load(fwDataPath);
            Assert.Equal("7000072", project.Root?.Attribute("version")?.Value);

            using var backup = ZipFile.OpenRead(backupPath);
            Assert.Contains(backup.Entries, entry => entry.FullName.EndsWith(".fwdata", StringComparison.Ordinal));
            Assert.Contains(backup.Entries, entry =>
                entry.FullName.Equals("WritingSystemStore/tr.ldml", StringComparison.OrdinalIgnoreCase));
            var unpacked = Path.Combine(root, "unpacked");
            backup.ExtractToDirectory(unpacked);
            using var reopened = new FwDataProjectLoader().LoadScratchCache(
                Path.Combine(unpacked, Path.GetFileName(fwDataPath)));
            Assert.Equal("tr", reopened.WritingSystemFactory.GetStrFromWs(reopened.DefaultVernWs));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task TurkishWritingSystemBuildsFromTheLocalProjectData()
    {
        var root = NewRoot();
        Directory.CreateDirectory(root);

        try
        {
            using var output = await BuildAsync(root);
            var fwDataPath = output.RootElement.GetProperty("projectPath").GetString()!;
            Assert.True(File.Exists(fwDataPath));
            var writingSystemFiles = Directory.EnumerateFiles(
                Path.Combine(Path.GetDirectoryName(fwDataPath)!, "WritingSystemStore"), "*.ldml")
                .Select(Path.GetFileName);
            Assert.Contains(writingSystemFiles,
                name => string.Equals(name, "tr.ldml", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task BuilderRejectsAStemCharacterWithoutADeclaredPhoneme()
    {
        var root = NewRoot();
        Directory.CreateDirectory(root);

        try
        {
            var specPath = Path.Combine(root, "invalid-sample.json");
            await File.WriteAllTextAsync(specPath, """
                {
                  "id": "sample-test",
                  "title": "Test sample",
                  "language": { "name": "Test", "tag": "tr" },
                  "phonemes": ["a"],
                  "partsOfSpeech": [{ "id": "noun", "name": "Noun" }],
                  "stems": [{ "id": "car", "form": "ar", "partOfSpeech": "noun", "gloss": "car" }],
                  "texts": [{ "id": "one", "title": "One", "sentences": ["ar"] }]
                }
                """);

            var result = await RunBuilderAsync(root, specPath);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("Stem 'car' form 'ar' uses undeclared character 'r'.", result.StandardError);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task TextWordformGuidsAreDeterministicAcrossBuilds()
    {
        var root = NewRoot();
        var firstRoot = Path.Combine(root, "first");
        var secondRoot = Path.Combine(root, "second");
        Directory.CreateDirectory(firstRoot);
        Directory.CreateDirectory(secondRoot);

        try
        {
            using var first = await BuildAsync(firstRoot);
            using var second = await BuildAsync(secondRoot);

            var firstPath = first.RootElement.GetProperty("projectPath").GetString()!;
            var secondPath = second.RootElement.GetProperty("projectPath").GetString()!;
            Assert.Equal(WordformGuids(firstPath), WordformGuids(secondPath));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [RealParserFact]
    public async Task TenWordSpecBuiltByTheSampleToolParsesThroughMotifAssess()
    {
        var root = NewRoot();
        Directory.CreateDirectory(root);

        try
        {
            using var buildOutput = await BuildAsync(root);
            var projectPath = buildOutput.RootElement.GetProperty("projectPath").GetString()!;
            var textGuid = buildOutput.RootElement.GetProperty("texts")[0].GetProperty("guid").GetString()!;

            var assess = new ProcessStartInfo(BuildOutput.Cli)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            assess.Environment["MOTIF_WORKER_ROOT"] = Path.Combine(root, "worker-root");
            assess.ArgumentList.Add("assess");
            assess.ArgumentList.Add(projectPath);
            assess.ArgumentList.Add("--texts");
            assess.ArgumentList.Add(textGuid);
            assess.ArgumentList.Add("--json");
            var assessment = await RunAsync(assess);

            Assert.True(assessment.ExitCode == 0, assessment.StandardError);
            using var response = JsonDocument.Parse(assessment.StandardOutput);
            var words = response.RootElement.GetProperty("words").EnumerateArray().ToArray();
            Assert.Equal(10, words.Length);
            Assert.All(words, word => Assert.Equal("analysed", word.GetProperty("outcome").GetString()));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [RealParserFact]
    public async Task DuplicateOptionalPluralSlotsMultiplyParserWork()
    {
        var root = NewRoot();
        Directory.CreateDirectory(root);

        try
        {
            var specPath = Path.Combine(root, "slow-spike.json");
            var bugsPath = Path.Combine(root, "slow-spike-bugs.json");
            await File.WriteAllTextAsync(specPath, """
                {
                  "id": "sample-slow-spike",
                  "title": "Slow spike",
                  "language": { "name": "Turkish", "tag": "tr" },
                  "phonemes": ["a", "e", "l", "r", "v"],
                  "naturalClasses": [
                    { "id": "back", "name": "Back vowels", "abbreviation": "Back", "phonemes": ["a"] },
                    { "id": "front", "name": "Front vowels", "abbreviation": "Front", "phonemes": ["e"] },
                    { "id": "consonants", "name": "Consonants", "abbreviation": "C", "phonemes": ["l", "r", "v"] }
                  ],
                  "environments": [
                    { "id": "back-env", "name": "After back vowel", "representation": "/[Back]([C])_" },
                    { "id": "front-env", "name": "After front vowel", "representation": "/[Front]([C])_" }
                  ],
                  "partsOfSpeech": [{ "id": "noun", "name": "Noun" }],
                  "stems": [{ "id": "ev", "form": "ev", "partOfSpeech": "noun", "gloss": "house" }],
                  "affixes": [{
                    "id": "plural", "partOfSpeech": "noun", "slots": ["plural"], "gloss": "plural",
                    "allomorphs": [
                      { "id": "lar", "form": "lar", "environment": "back-env" },
                      { "id": "ler", "form": "ler", "environment": "front-env" }
                    ]
                  }],
                  "affixSlots": [{ "id": "plural", "name": "Plural", "partOfSpeech": "noun", "optional": false }],
                  "affixTemplates": [{ "id": "noun", "name": "Noun suffixes", "partOfSpeech": "noun", "suffixSlots": ["plural"], "final": true }],
                  "phonologicalRules": [],
                  "texts": [{ "id": "houses", "title": "Houses", "sentences": ["evler"] }]
                }
                """);
            await File.WriteAllTextAsync(bugsPath, """
                [{
                  "id": "optional-plural-copies",
                  "patch": [{ "op": "duplicateOptionalSlot", "templateId": "noun", "slotId": "plural", "count": 11 }]
                }]
                """);

            using var fixedBuild = await BuildVariantAsync(root, specPath, bugsPath, null, "fixed");
            using var brokenBuild = await BuildVariantAsync(root, specPath, bugsPath, "optional-plural-copies", "broken");
            var fixedProject = fixedBuild.RootElement.GetProperty("projectPath").GetString()!;
            var fixedText = fixedBuild.RootElement.GetProperty("texts")[0].GetProperty("guid").GetString()!;
            var brokenProject = brokenBuild.RootElement.GetProperty("projectPath").GetString()!;
            var brokenText = brokenBuild.RootElement.GetProperty("texts")[0].GetProperty("guid").GetString()!;

            var fixedWork = await AssessWorkAsync(root, fixedProject, fixedText);
            var brokenWork = await AssessWorkAsync(root, brokenProject, brokenText);
            output.WriteLine($"fixed={fixedWork}; broken={brokenWork}; work ratio={(double)brokenWork.Work / fixedWork.Work:F2}x");
            Assert.True(brokenWork.Work >= fixedWork.Work * 10,
                $"Expected duplicate optional plural slots to multiply work tenfold; fixed={fixedWork}, broken={brokenWork}.");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static string NewRoot() => Path.Combine(
        BuildOutput.ProductDirectory, "SampleSpikes", Guid.NewGuid().ToString("N"));

    private static string[] WordformGuids(string fwDataPath) => XDocument.Load(fwDataPath)
        .Descendants("rt")
        .Where(record => record.Attribute("class")?.Value == "WfiWordform")
        .Select(record => record.Attribute("guid")?.Value ?? string.Empty)
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static async Task<JsonDocument> BuildAsync(string root)
    {
        var result = await RunBuilderAsync(root,
            Path.Combine(RepositoryRoot(), "samples", "sample-turkish", "sample.json"));
        Assert.True(result.ExitCode == 0, result.StandardError);
        return JsonDocument.Parse(result.StandardOutput);
    }

    private static async Task<JsonDocument> BuildVariantAsync(
        string root, string specPath, string bugsPath, string? bugId, string variant)
    {
        var builder = Path.Combine(BuildOutput.ProductDirectory,
            OperatingSystem.IsWindows() ? "SIL.Motif.SampleProjects.exe" : "SIL.Motif.SampleProjects");
        var start = new ProcessStartInfo(builder) { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("build");
        start.ArgumentList.Add(specPath);
        start.ArgumentList.Add(Path.Combine(root, variant));
        start.ArgumentList.Add("--bugs");
        start.ArgumentList.Add(bugsPath);
        if (bugId is not null)
        {
            start.ArgumentList.Add("--bug");
            start.ArgumentList.Add(bugId);
        }
        var result = await RunAsync(start);
        Assert.True(result.ExitCode == 0, result.StandardError);
        return JsonDocument.Parse(result.StandardOutput);
    }

    private static async Task<WorkSnapshot> AssessWorkAsync(string root, string projectPath, string textGuid)
    {
        var assess = new ProcessStartInfo(BuildOutput.Cli)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        assess.Environment["MOTIF_WORKER_ROOT"] = Path.Combine(root, "worker-root");
        assess.Environment["MOTIF_DEVELOPER_COMMANDS"] = "1";
        assess.ArgumentList.Add("assess");
        assess.ArgumentList.Add(projectPath);
        assess.ArgumentList.Add("--texts");
        assess.ArgumentList.Add(textGuid);
        assess.ArgumentList.Add("--json");
        var stopwatch = Stopwatch.StartNew();
        var assessment = await RunAsync(assess);
        stopwatch.Stop();
        Assert.True(assessment.ExitCode == 0, assessment.StandardError);
        using var response = JsonDocument.Parse(assessment.StandardOutput);
        var words = response.RootElement.GetProperty("words").EnumerateArray().ToArray();
        Assert.All(words, word => Assert.Equal("analysed", word.GetProperty("outcome").GetString()));
        var work = words.Sum(word => word.GetProperty("attempts").GetInt32());
        var parserElapsedMs = words.Sum(word => word.GetProperty("elapsedMs").GetInt32());
        return new WorkSnapshot(work, stopwatch.ElapsedMilliseconds, parserElapsedMs);
    }

    private static async Task<ProcessResult> RunBuilderAsync(string root, string specPath)
    {
        var builder = Path.Combine(BuildOutput.ProductDirectory,
            OperatingSystem.IsWindows() ? "SIL.Motif.SampleProjects.exe" : "SIL.Motif.SampleProjects");
        Assert.True(File.Exists(builder), "Build the solution before running the sample spikes.");
        var start = new ProcessStartInfo(builder) { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("build");
        start.ArgumentList.Add(specPath);
        start.ArgumentList.Add(root);
        return await RunAsync(start);
    }

    private static void DeleteDirectory(string root)
    {
        try { Directory.Delete(root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(BuildOutput.ProductDirectory, "..", ".."));

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

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
    private sealed record WorkSnapshot(int Work, long WallClockMs, int ParserElapsedMs);
}
