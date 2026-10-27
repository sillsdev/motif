using System.Diagnostics;
using System.Text.Json;
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
            var fixture = await WriteFixtureAsync(root, includeClassFeatures: true);
            var tokens = fixture.Tokens;
            using var schema = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(RepositoryRoot(), "samples", "sample.schema.json")));
            using var sample = JsonDocument.Parse(await File.ReadAllTextAsync(fixture.SamplePath));
            SampleJsonSchemaValidator.AssertValid(sample.RootElement, schema.RootElement);

            using var output = await BuildAsync(root, fixture.SamplePath, fixture.BugsPath, []);
            using var cache = new FwDataProjectLoader().LoadScratchCache(
                output.RootElement.GetProperty("projectPath").GetString()!);

            var stem = cache.LangProject.LexDbOA.Entries.Single(entry =>
                entry.LexemeFormOA is IMoStemAllomorph form &&
                form.Form.get_String(cache.DefaultVernWs).Text == tokens.NounForm);
            var stemMsa = Assert.IsAssignableFrom<IMoStemMsa>(stem.MorphoSyntaxAnalysesOC.Single());
            Assert.Equal(tokens.ClassName, stemMsa.InflectionClassRA!.Name.get_String(cache.DefaultAnalWs).Text);
            AssertFeature(stemMsa.MsFeaturesOA!, cache, tokens.FeatureName, tokens.FeatureValueName);

            var prefix = cache.LangProject.LexDbOA.Entries.Single(entry =>
                entry.LexemeFormOA is IMoAffixAllomorph form &&
                form.Form.get_String(cache.DefaultVernWs).Text == tokens.PrefixForm);
            var prefixForm = Assert.IsAssignableFrom<IMoAffixAllomorph>(prefix.LexemeFormOA);
            Assert.Equal(MoMorphTypeTags.kguidMorphPrefix, prefixForm.MorphTypeRA!.Guid);
            Assert.Contains(stemMsa.InflectionClassRA, prefixForm.InflectionClassesRC);
            AssertFeature(prefixForm.MsEnvFeaturesOA!, cache, tokens.FeatureName, tokens.FeatureValueName);
            var prefixMsa = Assert.IsAssignableFrom<IMoInflAffMsa>(prefix.MorphoSyntaxAnalysesOC.Single());
            AssertFeature(prefixMsa.InflFeatsOA!, cache, tokens.FeatureName, tokens.FeatureValueName);

            var nounTemplate = cache.LangProject.PartsOfSpeechOA.PossibilitiesOS
                .OfType<IPartOfSpeech>().Single(part => part.Name.get_String(cache.DefaultAnalWs).Text == tokens.NounName)
                .AffixTemplatesOS.Single(template => template.Name.get_String(cache.DefaultAnalWs).Text == tokens.NounTemplateName);
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
            var fixture = await WriteFixtureAsync(root, includeClassFeatures: false);
            var tokens = fixture.Tokens;
            using var schema = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(RepositoryRoot(), "samples", "sample.schema.json")));
            using var bugs = JsonDocument.Parse(await File.ReadAllTextAsync(fixture.BugsPath));
            SampleJsonSchemaValidator.AssertValid(bugs.RootElement,
                schema.RootElement.GetProperty("$defs").GetProperty("bugList"), schema.RootElement);

            using var output = await BuildAsync(root, fixture.SamplePath, fixture.BugsPath, [fixture.BugId]);
            using var cache = new FwDataProjectLoader().LoadScratchCache(
                output.RootElement.GetProperty("projectPath").GetString()!);

            var noun = cache.LangProject.PartsOfSpeechOA.PossibilitiesOS
                .OfType<IPartOfSpeech>().Single(part => part.Name.get_String(cache.DefaultAnalWs).Text == tokens.NounName);
            var template = noun.AffixTemplatesOS.Single(candidate =>
                candidate.Name.get_String(cache.DefaultAnalWs).Text == tokens.NounTemplateName);
            Assert.Equal(new[] { tokens.PrefixSlotTwoName, tokens.PrefixSlotOneName,
                    tokens.PrefixSlotOneName + " copy 1" },
                template.PrefixSlotsRS.Select(slot => slot.Name.get_String(cache.DefaultAnalWs).Text));
            Assert.True(template.PrefixSlotsRS[2].Optional);

            var prefix = cache.LangProject.LexDbOA.Entries.Single(entry =>
                entry.LexemeFormOA is IMoAffixAllomorph form &&
                form.Form.get_String(cache.DefaultVernWs).Text == tokens.PrefixForm);
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
            var fixture = await WriteFixtureAsync(root, includeClassFeatures: false);
            var tokens = fixture.Tokens;
            var sample = fixture.Sample;
            sample.AffixSlots.Add(new(tokens.ObjectSlotId, tokens.ObjectSlotName, tokens.VerbId, Optional: true));
            var verbTemplateIndex = sample.AffixTemplates.FindIndex(template => template.Id == tokens.VerbTemplateId);
            sample.AffixTemplates[verbTemplateIndex] = sample.AffixTemplates[verbTemplateIndex] with
                { PrefixSlots = [tokens.ObjectSlotId] };
            var zeroAffix = new RuntimeAffixPatchSpec(tokens.ZeroAffixId, tokens.VerbId,
                [tokens.ObjectSlotId], tokens.Id + " object marker");
            var zeroBug = new RuntimeBugSpec(tokens.BugId, tokens.Id + " zero-form affix", sample.Disclaimer,
                new("slow", [tokens.VerbForm], "The parser explores zero-form object markers."),
                ["Remove the zero-form object allomorphs from the object slot."],
                [
                    new("addAffix", Affix: zeroAffix),
                    new("addAllomorph", AffixId: tokens.ZeroAffixId,
                        Allomorph: new(tokens.ZeroConditionedAllomorphId, string.Empty, tokens.EnvironmentId, "prefix")),
                    new("addAllomorph", AffixId: tokens.ZeroAffixId,
                        Allomorph: new(tokens.ZeroUnconditionedAllomorphId, string.Empty, MorphType: "prefix")),
                ]);
            await RuntimeSampleProjectFixture.WriteSampleAsync(fixture.SamplePath, sample);
            await RuntimeSampleProjectFixture.WriteBugsAsync(fixture.BugsPath, [zeroBug]);
            using var schema = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(RepositoryRoot(), "samples", "sample.schema.json")));
            using var bugs = JsonDocument.Parse(await File.ReadAllTextAsync(fixture.BugsPath));
            SampleJsonSchemaValidator.AssertValid(bugs.RootElement,
                schema.RootElement.GetProperty("$defs").GetProperty("bugList"), schema.RootElement);

            using var output = await BuildAsync(root, fixture.SamplePath, fixture.BugsPath, [tokens.BugId]);
            using var cache = new FwDataProjectLoader().LoadScratchCache(
                output.RootElement.GetProperty("projectPath").GetString()!);

            var entry = cache.LangProject.LexDbOA.Entries.Single(candidate =>
                candidate.MorphoSyntaxAnalysesOC.OfType<IMoInflAffMsa>().Any(msa =>
                    msa.SlotsRC.Any(slot => slot.Name.get_String(cache.DefaultAnalWs).Text == tokens.ObjectSlotName)));
            var primary = Assert.IsAssignableFrom<IMoAffixAllomorph>(entry.LexemeFormOA);
            Assert.True(string.IsNullOrEmpty(primary.Form.get_String(cache.DefaultVernWs).Text));
            Assert.Equal(MoMorphTypeTags.kguidMorphPrefix, primary.MorphTypeRA!.Guid);
            Assert.Single(primary.PhoneEnvRC);
            Assert.Equal(tokens.EnvironmentRepresentation, primary.PhoneEnvRC.Single().StringRepresentation.Text);
            var alternate = Assert.IsAssignableFrom<IMoAffixAllomorph>(Assert.Single(entry.AlternateFormsOS));
            Assert.True(string.IsNullOrEmpty(alternate.Form.get_String(cache.DefaultVernWs).Text));
            Assert.Empty(alternate.PhoneEnvRC);
            var msa = Assert.IsAssignableFrom<IMoInflAffMsa>(entry.MorphoSyntaxAnalysesOC.Single());
            Assert.Equal(tokens.ObjectSlotName, msa.SlotsRC.Single().Name.get_String(cache.DefaultAnalWs).Text);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    internal async Task PrefixAffixBuiltBySampleBuilderParsesThroughMotifAssess()
    {
        var root = NewRoot();
        Directory.CreateDirectory(root);

        try
        {
            var fixture = await WriteFixtureAsync(root, includeClassFeatures: false);
            var tokens = fixture.Tokens;
            fixture.Sample.Stems.RemoveRange(1, fixture.Sample.Stems.Count - 1);
            fixture.Sample.Affixes.Clear();
            fixture.Sample.AffixSlots.Clear();
            fixture.Sample.AffixSlots.Add(new(tokens.PrefixSlotOneId, tokens.PrefixSlotOneName,
                tokens.NounId, Optional: false));
            fixture.Sample.AffixTemplates.Clear();
            fixture.Sample.AffixTemplates.Add(new(tokens.NounTemplateId + "-prefix", tokens.Id + " noun prefix",
                tokens.NounId, Final: true, PrefixSlots: [tokens.PrefixSlotOneId]));
            fixture.Sample.PhonologicalRules.Clear();
            fixture.Sample.Affixes.Add(new(tokens.PrefixAffixId, tokens.NounId, [tokens.PrefixSlotOneId],
                tokens.Id + " prefix", [new(tokens.PrefixAllomorphOneId, tokens.PrefixForm, MorphType: "prefix")]));
            fixture.Sample.Texts = [new(tokens.TextId, tokens.TextTitle, [tokens.PrefixForm + tokens.NounForm])];
            await RuntimeSampleProjectFixture.WriteSampleAsync(fixture.SamplePath, fixture.Sample);
            using var output = await BuildAsync(root, fixture.SamplePath, fixture.BugsPath, []);
            var projectPath = output.RootElement.GetProperty("projectPath").GetString()!;
            var text = Assert.Single(output.RootElement.GetProperty("texts").EnumerateArray(), item =>
                item.GetProperty("id").GetString() == tokens.TextId);
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
            Assert.Equal(tokens.PrefixForm + tokens.NounForm, word.GetProperty("word").GetString());
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

    private static async Task<WrittenFixture> WriteFixtureAsync(
        string root, bool includeClassFeatures)
    {
        var runtime = RuntimeSampleProjectFixture.Create("prefix-extension");
        var sample = runtime.Sample;
        var tokens = runtime.Tokens;
        sample.AffixSlots.Add(new(tokens.PrefixSlotOneId, tokens.PrefixSlotOneName,
            tokens.NounId, Optional: false));
        if (includeClassFeatures)
        {
            var assignment = new RuntimeFeatureAssignmentSpec(tokens.FeatureId, tokens.FeatureValueId);
            sample.InflectionClasses.Add(new(tokens.ClassId, tokens.ClassName, tokens.NounId));
            sample.FeatureDefinitions.Add(new(tokens.FeatureId, tokens.FeatureName, "NC",
                [new(tokens.FeatureValueId, tokens.FeatureValueName, "1")]));
            sample.Stems[0] = sample.Stems[0] with
                { InflectionClass = tokens.ClassId, Features = [assignment] };
            sample.AffixTemplates[0] = sample.AffixTemplates[0] with
                { PrefixSlots = [tokens.PrefixSlotOneId] };
            sample.Affixes.Add(new(tokens.PrefixAffixId, tokens.NounId, [tokens.PrefixSlotOneId],
                tokens.Id + " class prefix",
                [new(tokens.PrefixAllomorphOneId, tokens.PrefixForm, MorphType: "prefix",
                    InflectionClasses: [tokens.ClassId], RequiredFeatures: [assignment])],
                [assignment]));
        }
        else
        {
            sample.AffixSlots.Add(new(tokens.PrefixSlotTwoId, tokens.PrefixSlotTwoName,
                tokens.NounId, Optional: false));
            sample.AffixTemplates[0] = sample.AffixTemplates[0] with
                { PrefixSlots = [tokens.PrefixSlotOneId, tokens.PrefixSlotTwoId] };
            sample.Affixes.Add(new(tokens.PrefixAffixId, tokens.NounId, [tokens.PrefixSlotOneId],
                tokens.Id + " test prefix",
                [
                    new(tokens.PrefixAllomorphOneId, tokens.PrefixForm, MorphType: "prefix"),
                    new(tokens.PrefixAllomorphTwoId, tokens.SuffixForm, MorphType: "prefix"),
                ]));
        }

        var samplePath = Path.Combine(root, "sample.json");
        var bugsPath = Path.Combine(root, "bugs.json");
        await RuntimeSampleProjectFixture.WriteSampleAsync(samplePath, sample);
        var bugs = new RuntimeBugSpec(tokens.BugId, tokens.Id + " prefix slot patch", sample.Disclaimer,
            new("failure", [tokens.NounForm], "A reordered prefix template changes the build."),
            ["Restore the generated prefix slot order."],
            [
                new("swapSlots", TemplateId: tokens.NounTemplateId, FirstSlotId: tokens.PrefixSlotOneId,
                    SecondSlotId: tokens.PrefixSlotTwoId, Side: "prefix"),
                new("duplicateOptionalSlot", TemplateId: tokens.NounTemplateId, SlotId: tokens.PrefixSlotOneId,
                    Count: 1, Side: "prefix"),
                new("removeAllomorph", AllomorphId: tokens.PrefixAllomorphTwoId),
            ]);
        await RuntimeSampleProjectFixture.WriteBugsAsync(bugsPath, [bugs]);
        return new WrittenFixture(samplePath, bugsPath, tokens.BugId, sample, tokens);
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
    private sealed record WrittenFixture(string SamplePath, string BugsPath, string BugId,
        RuntimeSampleSpec Sample, RuntimeSampleTokens Tokens);
}

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group1)]
[Trait("MotifTestLevel", "System")]
public sealed class RealParserSampleBuilderExtensionTests
{
    [RealParserFact]
    public Task PrefixAffixBuiltBySampleBuilderParsesThroughMotifAssess() =>
        new SampleBuilderExtensionTests().PrefixAffixBuiltBySampleBuilderParsesThroughMotifAssess();
}
