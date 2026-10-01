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
public sealed class SampleBuilderExtensionTests
{
    [Fact]
    public async Task BuilderWritesPrefixAffixesAndClassConditionedFeatures()
    {
        var root = NewRoot();
        Directory.CreateDirectory(root);

        try
        {
            var (samplePath, bugsPath) = await WriteFixtureAsync(root, includeClassFeatures: true);
            using var schema = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(RepositoryRoot(), "samples", "sample.schema.json")));
            using var sample = JsonDocument.Parse(await File.ReadAllTextAsync(samplePath));
            SampleJsonSchemaValidator.AssertValid(sample.RootElement, schema.RootElement);

            using var output = await BuildAsync(root, samplePath, bugsPath, []);
            using var cache = new FwDataProjectLoader().LoadScratchCache(
                output.RootElement.GetProperty("projectPath").GetString()!);

            var stem = cache.LangProject.LexDbOA.Entries.Single(entry =>
                entry.LexemeFormOA is IMoStemAllomorph form &&
                form.Form.get_String(cache.DefaultVernWs).Text == "adam");
            var stemMsa = Assert.IsAssignableFrom<IMoStemMsa>(stem.MorphoSyntaxAnalysesOC.Single());
            Assert.Equal("Noun class one", stemMsa.InflectionClassRA!.Name.get_String(cache.DefaultAnalWs).Text);
            AssertFeature(stemMsa.MsFeaturesOA!, cache, "Noun class", "Class one");

            var prefix = cache.LangProject.LexDbOA.Entries.Single(entry =>
                entry.LexemeFormOA is IMoAffixAllomorph form &&
                form.Form.get_String(cache.DefaultVernWs).Text == "ki");
            var prefixForm = Assert.IsAssignableFrom<IMoAffixAllomorph>(prefix.LexemeFormOA);
            Assert.Equal(MoMorphTypeTags.kguidMorphPrefix, prefixForm.MorphTypeRA!.Guid);
            Assert.Contains(stemMsa.InflectionClassRA, prefixForm.InflectionClassesRC);
            AssertFeature(prefixForm.MsEnvFeaturesOA!, cache, "Noun class", "Class one");
            var prefixMsa = Assert.IsAssignableFrom<IMoInflAffMsa>(prefix.MorphoSyntaxAnalysesOC.Single());
            AssertFeature(prefixMsa.InflFeatsOA!, cache, "Noun class", "Class one");

            var nounTemplate = cache.LangProject.PartsOfSpeechOA.PossibilitiesOS
                .OfType<IPartOfSpeech>().Single(part => part.Name.get_String(cache.DefaultAnalWs).Text == "Noun")
                .AffixTemplatesOS.Single(template => template.Name.get_String(cache.DefaultAnalWs).Text == "Noun suffixes");
            Assert.Single(nounTemplate.PrefixSlotsRS);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task PrefixSlotPatchesSwapDuplicateAndRemoveAllomorph()
    {
        var root = NewRoot();
        Directory.CreateDirectory(root);

        try
        {
            var (samplePath, bugsPath) = await WriteFixtureAsync(root, includeClassFeatures: false);
            using var schema = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(RepositoryRoot(), "samples", "sample.schema.json")));
            using var bugs = JsonDocument.Parse(await File.ReadAllTextAsync(bugsPath));
            SampleJsonSchemaValidator.AssertValid(bugs.RootElement,
                schema.RootElement.GetProperty("$defs").GetProperty("bugList"), schema.RootElement);

            using var output = await BuildAsync(root, samplePath, bugsPath, ["prefix-patches"]);
            using var cache = new FwDataProjectLoader().LoadScratchCache(
                output.RootElement.GetProperty("projectPath").GetString()!);

            var noun = cache.LangProject.PartsOfSpeechOA.PossibilitiesOS
                .OfType<IPartOfSpeech>().Single(part => part.Name.get_String(cache.DefaultAnalWs).Text == "Noun");
            var template = noun.AffixTemplatesOS.Single(candidate =>
                candidate.Name.get_String(cache.DefaultAnalWs).Text == "Noun suffixes");
            Assert.Equal(new[] { "Prefix two", "Prefix one", "Prefix one copy 1" },
                template.PrefixSlotsRS.Select(slot => slot.Name.get_String(cache.DefaultAnalWs).Text));
            Assert.True(template.PrefixSlotsRS[2].Optional);

            var prefix = cache.LangProject.LexDbOA.Entries.Single(entry =>
                entry.LexemeFormOA is IMoAffixAllomorph form &&
                form.Form.get_String(cache.DefaultVernWs).Text == "ki");
            Assert.Empty(prefix.AlternateFormsOS);
            var msa = Assert.IsAssignableFrom<IMoInflAffMsa>(prefix.MorphoSyntaxAnalysesOC.Single());
            Assert.Equal(2, msa.SlotsRC.Count);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task AddAffixAndAddAllomorphPatchesBuildZeroFormAffixes()
    {
        var root = NewRoot();
        Directory.CreateDirectory(root);

        try
        {
            var (samplePath, bugsPath) = await WriteFixtureAsync(root, includeClassFeatures: false);
            var sample = JsonNode.Parse(await File.ReadAllTextAsync(samplePath))!.AsObject();
            sample["affixSlots"]!.AsArray().Add(JsonNode.Parse("""
                { "id": "object", "name": "Object", "partOfSpeech": "verb", "optional": true }
                """));
            var verbTemplate = sample["affixTemplates"]!.AsArray().Single(template =>
                template!["id"]!.GetValue<string>() == "verb")!;
            verbTemplate["prefixSlots"] = JsonNode.Parse("""["object"]""");
            await File.WriteAllTextAsync(samplePath, sample.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            await File.WriteAllTextAsync(bugsPath, """
                [{
                  "id": "zero-object",
                  "title": "Zero object affix",
                  "disclaimer": "Synthetic builder test.",
                  "symptom": { "kind": "slow", "words": ["yapti"], "reason": "The parser explores zero-form object markers." },
                  "fix": ["Remove the zero-form object allomorphs from the Object slot."],
                  "patch": [
                    {
                      "op": "addAffix",
                      "affix": {
                        "id": "zero-object-affix",
                        "partOfSpeech": "verb",
                        "slots": ["object"],
                        "gloss": "object marker"
                      }
                    },
                    {
                      "op": "addAllomorph",
                      "affixId": "zero-object-affix",
                      "allomorph": {
                        "id": "zero-object-conditioned",
                        "form": "",
                        "environment": "after-vowel",
                        "morphType": "prefix"
                      }
                    },
                    {
                      "op": "addAllomorph",
                      "affixId": "zero-object-affix",
                      "allomorph": {
                        "id": "zero-object-unconditioned",
                        "form": "",
                        "morphType": "prefix"
                      }
                    }
                  ]
                }]
                """);
            using var schema = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(RepositoryRoot(), "samples", "sample.schema.json")));
            using var bugs = JsonDocument.Parse(await File.ReadAllTextAsync(bugsPath));
            SampleJsonSchemaValidator.AssertValid(bugs.RootElement,
                schema.RootElement.GetProperty("$defs").GetProperty("bugList"), schema.RootElement);

            using var output = await BuildAsync(root, samplePath, bugsPath, ["zero-object"]);
            using var cache = new FwDataProjectLoader().LoadScratchCache(
                output.RootElement.GetProperty("projectPath").GetString()!);

            var entry = cache.LangProject.LexDbOA.Entries.Single(candidate =>
                candidate.MorphoSyntaxAnalysesOC.OfType<IMoInflAffMsa>().Any(msa =>
                    msa.SlotsRC.Any(slot => slot.Name.get_String(cache.DefaultAnalWs).Text == "Object")));
            var primary = Assert.IsAssignableFrom<IMoAffixAllomorph>(entry.LexemeFormOA);
            Assert.True(string.IsNullOrEmpty(primary.Form.get_String(cache.DefaultVernWs).Text));
            Assert.Equal(MoMorphTypeTags.kguidMorphPrefix, primary.MorphTypeRA!.Guid);
            Assert.Single(primary.PhoneEnvRC);
            Assert.Equal("/[V]_", primary.PhoneEnvRC.Single().StringRepresentation.Text);
            var alternate = Assert.IsAssignableFrom<IMoAffixAllomorph>(Assert.Single(entry.AlternateFormsOS));
            Assert.True(string.IsNullOrEmpty(alternate.Form.get_String(cache.DefaultVernWs).Text));
            Assert.Empty(alternate.PhoneEnvRC);
            var msa = Assert.IsAssignableFrom<IMoInflAffMsa>(entry.MorphoSyntaxAnalysesOC.Single());
            Assert.Equal("Object", msa.SlotsRC.Single().Name.get_String(cache.DefaultAnalWs).Text);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [RealParserFact]
    public async Task PrefixAffixBuiltBySampleBuilderParsesThroughMotifAssess()
    {
        var root = NewRoot();
        Directory.CreateDirectory(root);

        try
        {
            var (samplePath, bugsPath) = await WriteFixtureAsync(root, includeClassFeatures: false);
            var sample = JsonNode.Parse(await File.ReadAllTextAsync(samplePath))!.AsObject();
            while (sample["stems"]!.AsArray().Count > 1)
                sample["stems"]!.AsArray().RemoveAt(sample["stems"]!.AsArray().Count - 1);
            sample["affixes"]!.AsArray().Clear();
            sample["affixSlots"]!.AsArray().Clear();
            sample["affixSlots"]!.AsArray().Add(JsonNode.Parse(
                """{ "id": "prefix-one", "name": "Prefix one", "partOfSpeech": "noun", "optional": false }"""));
            sample["affixTemplates"]!.AsArray().Clear();
            sample["affixTemplates"]!.AsArray().Add(JsonNode.Parse(
                """{ "id": "noun-prefix", "name": "Noun prefix", "partOfSpeech": "noun", "prefixSlots": ["prefix-one"], "final": true }"""));
            sample["phonologicalRules"]!.AsArray().Clear();
            sample["affixes"]!.AsArray().Add(JsonNode.Parse("""
                {
                  "id": "test-prefix",
                  "partOfSpeech": "noun",
                  "slots": ["prefix-one"],
                  "gloss": "test prefix",
                  "allomorphs": [{ "id": "test-prefix-form", "form": "ki", "morphType": "prefix" }]
                }
                """));
            sample["texts"]!.AsArray().Clear();
            sample["texts"]!.AsArray().Add(JsonNode.Parse(
                """{ "id": "prefix-smoke", "title": "Prefix test", "sentences": ["kiadam"] }"""));
            await File.WriteAllTextAsync(samplePath, sample.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            using var output = await BuildAsync(root, samplePath, bugsPath, []);
            var projectPath = output.RootElement.GetProperty("projectPath").GetString()!;
            var text = Assert.Single(output.RootElement.GetProperty("texts").EnumerateArray(), item =>
                item.GetProperty("id").GetString() == "prefix-smoke");
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
            assess.ArgumentList.Add(text.GetProperty("guid").GetString()!);
            assess.ArgumentList.Add("--json");
            var result = await RunAsync(assess);

            Assert.Equal(0, result.ExitCode);
            using var assessment = JsonDocument.Parse(result.StandardOutput);
            var word = Assert.Single(assessment.RootElement.GetProperty("words").EnumerateArray());
            Assert.Equal("kiadam", word.GetProperty("word").GetString());
            Assert.Equal("analysed", word.GetProperty("outcome").GetString());
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static void AssertFeature(IFsFeatStruc structure, LcmCache cache, string featureName, string valueName)
    {
        var specification = Assert.IsAssignableFrom<IFsClosedValue>(Assert.Single(structure.FeatureSpecsOC));
        Assert.Equal(featureName, specification.FeatureRA!.Name.get_String(cache.DefaultAnalWs).Text);
        Assert.Equal(valueName, specification.ValueRA!.Name.get_String(cache.DefaultAnalWs).Text);
    }

    private static async Task<(string SamplePath, string BugsPath)> WriteFixtureAsync(
        string root, bool includeClassFeatures)
    {
        var sample = JsonNode.Parse(await File.ReadAllTextAsync(
            Path.Combine(RepositoryRoot(), "samples", "synthetic-turkic", "sample.json")))!.AsObject();
        var nounTemplate = sample["affixTemplates"]!.AsArray().Single(template =>
            template!["partOfSpeech"]!.GetValue<string>() == "noun" &&
            template["name"]!.GetValue<string>() == "Noun suffixes")!;
        sample["affixSlots"]!.AsArray().Add(JsonNode.Parse("""
            { "id": "prefix-one", "name": "Prefix one", "partOfSpeech": "noun", "optional": false }
            """));
        if (includeClassFeatures)
        {
            sample["inflectionClasses"] = JsonNode.Parse("""
                [{ "id": "class-one", "name": "Noun class one", "partOfSpeech": "noun" }]
                """);
            sample["featureDefinitions"] = JsonNode.Parse("""
                [{
                  "id": "noun-class",
                  "name": "Noun class",
                  "abbreviation": "NC",
                  "values": [{ "id": "class-one", "name": "Class one", "abbreviation": "1" }]
                }]
                """);
            sample["stems"]!.AsArray()[0]!["inflectionClass"] = "class-one";
            sample["stems"]!.AsArray()[0]!["features"] = JsonNode.Parse("""
                [{ "featureId": "noun-class", "valueId": "class-one" }]
                """);
            nounTemplate["prefixSlots"] = JsonNode.Parse("""["prefix-one"]""");
            sample["affixes"]!.AsArray().Add(JsonNode.Parse("""
                {
                  "id": "class-prefix",
                  "partOfSpeech": "noun",
                  "slots": ["prefix-one"],
                  "gloss": "class prefix",
                  "features": [{ "featureId": "noun-class", "valueId": "class-one" }],
                  "allomorphs": [{
                    "id": "class-prefix-form",
                    "form": "ki",
                    "morphType": "prefix",
                    "inflectionClasses": ["class-one"],
                    "requiredFeatures": [{ "featureId": "noun-class", "valueId": "class-one" }]
                  }]
                }
                """));
        }
        else
        {
            sample["affixSlots"]!.AsArray().Add(JsonNode.Parse("""
                { "id": "prefix-two", "name": "Prefix two", "partOfSpeech": "noun", "optional": false }
                """));
            nounTemplate["prefixSlots"] = JsonNode.Parse("""["prefix-one", "prefix-two"]""");
            sample["affixes"]!.AsArray().Add(JsonNode.Parse("""
                {
                  "id": "test-prefix",
                  "partOfSpeech": "noun",
                  "slots": ["prefix-one"],
                  "gloss": "test prefix",
                  "allomorphs": [
                    { "id": "test-prefix-one", "form": "ki", "morphType": "prefix" },
                    { "id": "test-prefix-two", "form": "ko", "morphType": "prefix" }
                  ]
                }
                """));
        }

        var samplePath = Path.Combine(root, "sample.json");
        var bugsPath = Path.Combine(root, "bugs.json");
        await File.WriteAllTextAsync(samplePath, sample.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        await File.WriteAllTextAsync(bugsPath, """
            [{
              "id": "prefix-patches",
              "title": "Prefix slot patch coverage",
              "disclaimer": "Synthetic builder test.",
              "symptom": { "kind": "failure", "words": ["test"], "reason": "Test fixture." },
              "fix": ["Restore the prefix template."],
              "patch": [
                { "op": "swapSlots", "templateId": "noun", "firstSlotId": "prefix-one", "secondSlotId": "prefix-two", "side": "prefix" },
                { "op": "duplicateOptionalSlot", "templateId": "noun", "slotId": "prefix-one", "count": 1, "side": "prefix" },
                { "op": "removeAllomorph", "allomorphId": "test-prefix-two" }
              ]
            }]
            """);
        return (samplePath, bugsPath);
    }

    private static async Task<JsonDocument> BuildAsync(string root, string samplePath, string bugsPath, string[] bugIds)
    {
        var builder = Path.Combine(BuildOutput.ProductDirectory,
            OperatingSystem.IsWindows() ? "SIL.Motif.SampleProjects.exe" : "SIL.Motif.SampleProjects");
        var start = new ProcessStartInfo(builder) { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("build");
        start.ArgumentList.Add(samplePath);
        start.ArgumentList.Add(Path.Combine(root, "output"));
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

    private static string NewRoot() => Path.Combine(BuildOutput.ProductDirectory,
        "SampleBuilderExtensions", Guid.NewGuid().ToString("N"));

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

    private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(BuildOutput.ProductDirectory, "..", ".."));

    private static void DeleteDirectory(string root)
    {
        try { Directory.Delete(root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
