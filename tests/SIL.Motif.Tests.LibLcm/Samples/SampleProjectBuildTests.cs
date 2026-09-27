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

            using var bugs = JsonDocument.Parse(await File.ReadAllTextAsync(bugsPath));
            SampleJsonSchemaValidator.AssertValid(bugs.RootElement, schemaRoot.GetProperty("$defs").GetProperty("bugList"), schemaRoot);
            var disclaimer = spec.RootElement.GetProperty("disclaimer").GetString()!;
            foreach (var bug in bugs.RootElement.EnumerateArray())
                Assert.Equal(disclaimer, bug.GetProperty("disclaimer").GetString());

            var root = Path.Combine(BuildOutput.ProductDirectory, "SampleBuilds", Guid.NewGuid().ToString("N"), sampleId);
            Directory.CreateDirectory(root);
            try
            {
                using var fixedResult = await BuildAsync(root, specPath, bugsPath, [], "fixed");
                Reopen(fixedResult.RootElement.GetProperty("projectPath").GetString()!, disclaimer);
                AssertBackup(fixedResult.RootElement.GetProperty("backupPath").GetString()!);

                foreach (var bug in bugs.RootElement.EnumerateArray())
                {
                    var bugId = bug.GetProperty("id").GetString()!;
                    using var brokenResult = await BuildAsync(root, specPath, bugsPath, [bugId], bugId);
                    Reopen(brokenResult.RootElement.GetProperty("projectPath").GetString()!, disclaimer);
                    AssertBackup(brokenResult.RootElement.GetProperty("backupPath").GetString()!);
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

    private static async Task<JsonDocument> BuildAsync(
        string root, string specPath, string bugsPath, string[] bugIds, string variant)
    {
        var builder = Path.Combine(BuildOutput.ProductDirectory,
            OperatingSystem.IsWindows() ? "SIL.Motif.SampleProjects.exe" : "SIL.Motif.SampleProjects");
        var start = new ProcessStartInfo(builder) { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("build");
        start.ArgumentList.Add(specPath);
        start.ArgumentList.Add(Path.Combine(root, variant));
        start.ArgumentList.Add("--bugs");
        start.ArgumentList.Add(bugsPath);
        foreach (var bugId in bugIds)
        {
            start.ArgumentList.Add("--bug");
            start.ArgumentList.Add(bugId);
        }
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

    private static void AssertBackup(string backupPath)
    {
        using var archive = ZipFile.OpenRead(backupPath);
        Assert.Contains(archive.Entries, entry => entry.FullName.EndsWith(".fwdata", StringComparison.Ordinal));
        Assert.Contains(archive.Entries, entry => entry.FullName.Equals(
            "WritingSystemStore/tr.ldml", StringComparison.OrdinalIgnoreCase));
    }
}
