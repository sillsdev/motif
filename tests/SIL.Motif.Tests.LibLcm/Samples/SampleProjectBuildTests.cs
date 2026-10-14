using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Samples;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group1)]
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
    public async Task GenesisBuildReopensWithFieldWorksBoundariesAndOneFirstRule()
    {
        var root = Path.Combine(BuildOutput.ProductDirectory, "SampleGenesis", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var specPath = Path.Combine(root, "genesis.json");
            await File.WriteAllTextAsync(specPath, """
                {
                  "id": "t17-boundary-genesis",
                  "title": "Boundary genesis fixture",
                  "teaches": ["FieldWorks boundaries"],
                  "summary": "A minimal generated project for boundary authoring.",
                  "disclaimer": "Synthetic test data.",
                  "description": "Boundary genesis fixture.",
                  "language": { "name": "French", "tag": "fr" },
                  "phonemes": ["a", "b"],
                  "naturalClasses": [
                    { "id": "vowels", "name": "Vowels", "abbreviation": "V", "phonemes": ["a"] }
                  ],
                  "partsOfSpeech": [{ "id": "noun", "name": "Noun" }],
                  "stems": [{ "id": "stem", "form": "a", "partOfSpeech": "noun", "gloss": "a" }],
                  "texts": [{ "id": "text", "title": "Fixture text", "sentences": ["a"] }]
                }
                """);

            var builder = Path.Combine(BuildOutput.ProductDirectory,
                OperatingSystem.IsWindows() ? "SIL.Motif.SampleProjects.exe" : "SIL.Motif.SampleProjects");
            var start = new ProcessStartInfo(builder);
            start.ArgumentList.Add("build");
            start.ArgumentList.Add(specPath);
            start.ArgumentList.Add(Path.Combine(root, "output"));
            var result = ToolProcess.Run(start);
            Assert.True(result.ExitCode == 0, result.Error);
            using var build = JsonDocument.Parse(result.Output);
            var projectPath = build.RootElement.GetProperty("projectPath").GetString()!;

            var loader = new FwDataProjectLoader();
            using (var live = loader.LoadCache(projectPath))
            {
                var set = Assert.Single(live.LangProject.PhonologicalDataOA.PhonemeSetsOS);
                var a = Assert.Single(set.PhonemesOC, phoneme =>
                    phoneme.Name.get_String(live.DefaultVernWs)?.Text == "a");
                var b = Assert.Single(set.PhonemesOC, phoneme =>
                    phoneme.Name.get_String(live.DefaultVernWs)?.Text == "b");
                var ruleOperations = AuthorPhonologicalRuleComposer.Build(live,
                    new AuthorPhonologicalRuleIntent("First rule", PhonologicalRuleDirection.Simultaneous,
                        [new(Phoneme: CanonicalId.FromGuid(a.Guid))], [new(Phoneme: CanonicalId.FromGuid(b.Guid))],
                        [new(Boundary: "word"), new(Boundary: "morpheme")],
                        [new(Boundary: "morpheme"), new(Boundary: "word")]));
                Assert.DoesNotContain(ruleOperations,
                    operation => operation.Kind == PhPhonemeSetBoundaryMarkersOperationKinds.Create);
                var proposal = new Proposal(new Dictionary<string, string> { ["grammar"] = "1.0" },
                    CanonicalId.Mint(), null, ruleOperations);
                using var dryScratch = DryRunScratch.Adopt(loader.LoadScratchCache(projectPath), "genesis rule preview");
                var dryRun = ProposalDryRunner.Run(dryScratch, proposal);
                Assert.NotEmpty(dryRun.ExpectedEffects);
                Assert.Equal(2, dryScratch.PeekCache().LangProject.PhonologicalDataOA.PhonemeSetsOS.Single()
                    .BoundaryMarkersOC.Count);
                var receipt = ProposalApplier.Apply(live, proposal, dryRun.Anchor, "genesis tests", "Add first rule");
                Assert.False(receipt.AlreadyApplied);
                loader.Save(live);
            }

            using var cache = loader.LoadCache(projectPath);
            var reopenedSet = Assert.Single(cache.LangProject.PhonologicalDataOA.PhonemeSetsOS);
            Assert.Equal(2, reopenedSet.BoundaryMarkersOC.Count);
            Assert.Single(cache.LangProject.PhonologicalDataOA.PhonRulesOS);
            foreach (var (id, representation) in new[]
                     {
                         (LangProjectTags.kguidPhRuleWordBdry, "#"),
                         (LangProjectTags.kguidPhRuleMorphBdry, "+"),
                     })
            {
                var marker = Assert.Single(reopenedSet.BoundaryMarkersOC, item => item.Guid == id);
                Assert.Equal(reopenedSet.Guid, marker.Owner?.Guid);
                Assert.Equal(representation, marker.Name.get_String(cache.DefaultVernWs)?.Text);
                Assert.Equal(representation, Assert.Single(marker.CodesOS).Representation
                    .get_String(cache.DefaultVernWs)?.Text);
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
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
