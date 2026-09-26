using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Samples;

[Collection(LcmCacheTestCollection.Name)]
public sealed class SampleProjectSpikeTests
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
}
