using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Samples;

[Collection(LcmCacheTestCollection.Name)]
public sealed class SampleProjectBuildTests
{
    [Fact]
    public async Task SampleSchemaRejectsMissingRequiredFields()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(BuildOutput.ProductDirectory, "..", ".."));
        var samplesRoot = Path.Combine(repositoryRoot, "samples");
        using var schema = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(samplesRoot, "sample.schema.json")));
        using var invalid = JsonDocument.Parse("{\"id\":\"synthetic-turkic\"}");

        var errors = SampleJsonSchemaValidator.Validate(invalid.RootElement, schema.RootElement);

        Assert.Contains(errors, error => error.Contains("title", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SampleExpectedSchemaPinsGrammarHealthFindings()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(BuildOutput.ProductDirectory, "..", ".."));
        var samplesRoot = Path.Combine(repositoryRoot, "samples");
        using var schema = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(samplesRoot, "sample.schema.json")));
        using var expected = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(samplesRoot, "synthetic-turkic", "expected.json")));

        SampleJsonSchemaValidator.AssertValid(
            expected.RootElement, schema.RootElement.GetProperty("$defs").GetProperty("sampleExpected"), schema.RootElement);
    }

    [Fact]
    public async Task EverySampleAndPatchValidatesBuildsAndReopens()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(BuildOutput.ProductDirectory, "..", ".."));
        var samplesRoot = Path.Combine(repositoryRoot, "samples");
        using var schema = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(samplesRoot, "sample.schema.json")));
        var schemaRoot = schema.RootElement;
        var sampleFolders = Directory.GetDirectories(samplesRoot)
            .Where(folder => File.Exists(Path.Combine(folder, "sample.json")))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(sampleFolders);

        foreach (var sampleFolder in sampleFolders)
        {
            var sampleId = Path.GetFileName(sampleFolder);
            var specPath = Path.Combine(sampleFolder, "sample.json");
            var bugsPath = Path.Combine(sampleFolder, "bugs.json");
            using var spec = JsonDocument.Parse(await File.ReadAllTextAsync(specPath));
            SampleJsonSchemaValidator.AssertValid(spec.RootElement, schemaRoot);
            Assert.Equal(sampleId, spec.RootElement.GetProperty("id").GetString());
            var languageTag = spec.RootElement.GetProperty("language").GetProperty("tag").GetString()!;

            using var bugs = JsonDocument.Parse(await File.ReadAllTextAsync(bugsPath));
            SampleJsonSchemaValidator.AssertValid(bugs.RootElement, schemaRoot.GetProperty("$defs").GetProperty("bugList"), schemaRoot);
            var disclaimer = spec.RootElement.GetProperty("disclaimer").GetString()!;
            foreach (var bug in bugs.RootElement.EnumerateArray())
                Assert.Equal(disclaimer, bug.GetProperty("disclaimer").GetString());

            var root = Path.Combine(BuildOutput.ProductDirectory, "SampleBuilds", Guid.NewGuid().ToString("N"), sampleId);
            Directory.CreateDirectory(root);
            try
            {
                using var buildMatrix = await BuildMatrixAsync(root, specPath, bugsPath);
                var variants = buildMatrix.RootElement.GetProperty("variants").EnumerateArray()
                    .ToDictionary(variant => variant.GetProperty("name").GetString()!,
                        variant => variant.GetProperty("build"), StringComparer.Ordinal);
                Assert.Equal(bugs.RootElement.GetArrayLength() + 1, variants.Count);

                var fixedResult = variants["fixed"];
                Reopen(fixedResult.GetProperty("projectPath").GetString()!, disclaimer);
                AssertBackup(fixedResult.GetProperty("backupPath").GetString()!, languageTag);

                foreach (var bug in bugs.RootElement.EnumerateArray())
                {
                    var bugId = bug.GetProperty("id").GetString()!;
                    var brokenResult = variants[bugId];
                    Assert.Equal([bugId], brokenResult.GetProperty("appliedBugs").EnumerateArray()
                        .Select(appliedBug => appliedBug.GetString()!).ToArray());
                    Reopen(brokenResult.GetProperty("projectPath").GetString()!, disclaimer);
                    AssertBackup(brokenResult.GetProperty("backupPath").GetString()!, languageTag);
                }
            }
            finally
            {
                try { Directory.Delete(root, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static async Task<JsonDocument> BuildMatrixAsync(string root, string specPath, string bugsPath)
    {
        var builder = Path.Combine(BuildOutput.ProductDirectory,
            OperatingSystem.IsWindows() ? "SIL.Motif.SampleProjects.exe" : "SIL.Motif.SampleProjects");
        var start = new ProcessStartInfo(builder) { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("build-matrix");
        start.ArgumentList.Add(specPath);
        start.ArgumentList.Add(root);
        start.ArgumentList.Add("--bugs");
        start.ArgumentList.Add(bugsPath);
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await error);
        return JsonDocument.Parse(await output);
    }

    private static void Reopen(string projectPath, string disclaimer)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(projectPath);
        var description = cache.LangProject.Description.get_String(cache.DefaultAnalWs).Text;
        Assert.StartsWith(disclaimer, description, StringComparison.Ordinal);
    }

    private static void AssertBackup(string backupPath, string languageTag)
    {
        using var archive = ZipFile.OpenRead(backupPath);
        Assert.Contains(archive.Entries, entry => entry.FullName.EndsWith(".fwdata", StringComparison.Ordinal));
        Assert.Contains(archive.Entries, entry => entry.FullName.Equals(
            $"WritingSystemStore/{languageTag}.ldml", StringComparison.OrdinalIgnoreCase));
    }
}
