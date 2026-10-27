using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Samples;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group1)]
public sealed class SampleProjectSpikeTests(ITestOutputHelper output)
{
    [Fact]
    public async Task BuilderWritesCurrentFieldWorksModelAndReopenableBackup()
    {
        var root = NewRoot();
        Directory.CreateDirectory(root);

        try
        {
            using var output = await BuildCachedTurkicAsync(root);
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
    public async Task SyntheticTurkicWritingSystemBuildsFromTheLocalProjectData()
    {
        var root = NewRoot();
        Directory.CreateDirectory(root);

        try
        {
            using var output = await BuildCachedTurkicAsync(root);
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
    public async Task CachedTurkicBuildsUseIsolatedProjectPaths()
    {
        var root = NewRoot();
        var firstRoot = Path.Combine(root, "first");
        var secondRoot = Path.Combine(root, "second");
        Directory.CreateDirectory(firstRoot);
        Directory.CreateDirectory(secondRoot);

        try
        {
            using var first = await BuildCachedTurkicAsync(firstRoot);
            using var second = await BuildCachedTurkicAsync(secondRoot);

            Assert.NotEqual(
                first.RootElement.GetProperty("projectPath").GetString(),
                second.RootElement.GetProperty("projectPath").GetString());
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
            var fixture = RuntimeSampleProjectFixture.Create("runtime-undeclared");
            var tokens = fixture.Tokens;
            var undeclaredForm = tokens.Phonemes[0] + tokens.Phonemes[1];
            fixture.Sample.Phonemes = [tokens.Phonemes[0]];
            fixture.Sample.NaturalClasses.Clear();
            fixture.Sample.Environments.Clear();
            fixture.Sample.PartsOfSpeech = [fixture.Sample.PartsOfSpeech[0]];
            fixture.Sample.Stems = [new(tokens.NounId, undeclaredForm, tokens.NounId, tokens.Id + " stem")];
            fixture.Sample.AffixTemplates.Clear();
            fixture.Sample.Texts = [new(tokens.TextId, tokens.TextTitle, [undeclaredForm])];
            var specPath = Path.Combine(root, "invalid-sample.json");
            await RuntimeSampleProjectFixture.WriteSampleAsync(specPath, fixture.Sample);

            var result = await RunBuilderAsync(root, specPath);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains($"Stem '{tokens.NounId}' form '{undeclaredForm}' uses undeclared character '{tokens.Phonemes[1]}'.",
                result.StandardError);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task BuilderDoesNotTreatTheBaseOfADiacriticPhonemeAsDeclared()
    {
        var root = NewRoot();
        Directory.CreateDirectory(root);

        try
        {
            var fixture = RuntimeSampleProjectFixture.Create("runtime-diacritic");
            var tokens = fixture.Tokens;
            var diacritic = char.ConvertFromUtf32(0x00E7);
            var decomposedBase = char.ConvertFromUtf32(0x0063);
            fixture.Sample.Phonemes = [diacritic];
            fixture.Sample.NaturalClasses.Clear();
            fixture.Sample.Environments.Clear();
            fixture.Sample.PartsOfSpeech = [fixture.Sample.PartsOfSpeech[0]];
            fixture.Sample.Stems = [new(tokens.NounId, decomposedBase, tokens.NounId, tokens.Id + " stem")];
            fixture.Sample.AffixTemplates.Clear();
            fixture.Sample.Texts = [new(tokens.TextId, tokens.TextTitle, [decomposedBase])];
            var specPath = Path.Combine(root, "invalid-sample.json");
            await RuntimeSampleProjectFixture.WriteSampleAsync(specPath, fixture.Sample);

            var result = await RunBuilderAsync(root, specPath);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains($"Stem '{tokens.NounId}' form '{decomposedBase}' uses undeclared character '{decomposedBase}'.",
                result.StandardError);
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

    internal async Task SyntheticSampleWordsParseThroughMotifAssess()
    {
        var root = NewRoot();
        Directory.CreateDirectory(root);

        try
        {
            using var buildOutput = await BuildCachedTurkicAsync(root);
            var samplePath = Path.Combine(SyntheticSampleInputs.SampleDirectory(RepositoryRoot(), "synthetic-turkic"), "sample.json");
            using var sample = JsonDocument.Parse(await File.ReadAllTextAsync(samplePath));
            var wordOccurrences = WordOccurrencesByText(sample.RootElement);
            var assessment = await AssessTextsAsync(root, buildOutput.RootElement, wordOccurrences);
            var expectedWords = wordOccurrences.Values.Sum();
            Assert.Equal(expectedWords, assessment.Words);
            Assert.Equal(expectedWords, assessment.Parsed);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    internal async Task SyntheticTurkicBugVariantsMatchDeclaredSymptomsAndPinParserWork()
    {
        var root = NewRoot();
        Directory.CreateDirectory(root);

        try
        {
            var sampleFolder = SyntheticSampleInputs.SampleDirectory(RepositoryRoot(), "synthetic-turkic");
            var specPath = Path.Combine(sampleFolder, "sample.json");
            var bugsPath = Path.Combine(sampleFolder, "bugs.json");
            var expectedPath = Path.Combine(sampleFolder, "expected.json");
            using var spec = JsonDocument.Parse(await File.ReadAllTextAsync(specPath));
            using var bugs = JsonDocument.Parse(await File.ReadAllTextAsync(bugsPath));
            using var schema = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(RepositoryRoot(), "samples", "sample.schema.json")));
            var definitions = bugs.RootElement.EnumerateArray().ToArray();
            var bugIds = definitions.Select(bug => bug.GetProperty("id").GetString()!).ToArray();
            var wordOccurrences = WordOccurrencesByText(spec.RootElement);
            var textCount = wordOccurrences.Values.Sum();
            var parserProbeWords = definitions
                .Where(bug => bug.GetProperty("symptom").GetProperty("kind").GetString() == "slow")
                .SelectMany(bug => bug.GetProperty("symptom").GetProperty("words").EnumerateArray())
                .Select(word => word.GetString()!)
                .ToHashSet(StringComparer.Ordinal);

            using var fixedBuild = await BuildCachedTurkicAsync(root);
            var fixedGrammar = SampleGrammarHealth.AssertFixedProjectHasNoErrors(
                fixedBuild.RootElement.GetProperty("projectPath").GetString()!, Path.Combine(root, "grammar-health", "fixed"));
            AssertFixedWarningsDocumented(spec.RootElement, fixedGrammar);
            var fixedResult = await AssessTextsAsync(root, fixedBuild.RootElement, wordOccurrences,
                traceWords: parserProbeWords);
            Assert.Equal(textCount, fixedResult.Words);
            Assert.True(fixedResult.Words == fixedResult.Parsed,
                $"Fixed sample has unparsed words: {string.Join("; ", fixedResult.Outcomes.Where(pair => pair.Value != "analysed").Select(pair => pair.Key))}");

            var variantResults = new Dictionary<string, AssessmentSnapshot>(StringComparer.Ordinal)
            {
                ["fixed"] = fixedResult,
            };
            var variantFailures = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal)
            {
                ["fixed"] = new(StringComparer.Ordinal),
            };
            var declaredFailures = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var bug in definitions)
            {
                var bugId = bug.GetProperty("id").GetString()!;
                var symptom = bug.GetProperty("symptom");
                var expectedWords = symptom.GetProperty("words").EnumerateArray()
                    .Select(word => word.GetString()!).Order(StringComparer.Ordinal).ToArray();
                using var variantBuild = await BuildVariantAsync(root, specPath, bugsPath, [bugId], bugId);
                var assessmentWords = expectedWords.ToHashSet(StringComparer.Ordinal);
                var traceWords = symptom.GetProperty("kind").GetString() == "slow"
                    ? assessmentWords
                    : null;
                var result = await AssessTextsAsync(root, variantBuild.RootElement, wordOccurrences,
                    assessmentWords, traceWords);
                Assert.Equal(expectedWords, result.Outcomes.Keys.Order(StringComparer.Ordinal));
                variantResults.Add(bugId, result);
                var actualFailures = result.Outcomes.Where(pair => pair.Value != "analysed")
                    .Select(pair => pair.Key).Order(StringComparer.Ordinal).ToArray();
                if (symptom.GetProperty("kind").GetString() == "slow")
                {
                    Assert.Empty(actualFailures);
                    variantFailures.Add(bugId, new(StringComparer.Ordinal));
                    var slowestWords = SlowestWords(result);
                    var brokenWork = result.Work.GetValueOrDefault();
                    var fixedWork = fixedResult.Work.GetValueOrDefault();
                    Assert.True(result.Work.HasValue && fixedResult.Work.HasValue,
                        "Both slow-variant measurements must trace their declared words.");
                    output.WriteLine($"slow variant work={brokenWork}; fixed work={fixedWork}; ratio={(double)brokenWork / fixedWork:F2}x; steps={result.Steps}/{fixedResult.Steps}; slowest={string.Join(", ", slowestWords)}");
                    Assert.Equal(expectedWords, slowestWords);
                    Assert.True(brokenWork >= fixedWork * 10,
                        $"Slow bug '{bugId}' needs 10x parser work; fixed={fixedResult}, broken={result}.");
                }
                else
                {
                    Assert.True(expectedWords.SequenceEqual(actualFailures),
                        $"Bug '{bugId}' expected failures [{string.Join(", ", expectedWords)}], " +
                        $"got [{string.Join(", ", actualFailures)}]; outcomes: " +
                        string.Join(", ", result.Outcomes.Select(pair => $"{pair.Key}={pair.Value}")));
                    foreach (var word in expectedWords)
                        Assert.True(declaredFailures.TryAdd(word, bugId), $"Word '{word}' is assigned to two bugs.");
                    variantFailures.Add(bugId,
                        expectedWords.ToDictionary(word => word, _ => bugId, StringComparer.Ordinal));
                }
            }

            using var brokenBuild = await BuildVariantAsync(root, specPath, bugsPath, bugIds, "all-bugs");
            var brokenGrammar = SampleGrammarHealth.Read(
                brokenBuild.RootElement.GetProperty("projectPath").GetString()!, Path.Combine(root, "grammar-health", "broken"));
            var brokenResult = await AssessTextsAsync(root, brokenBuild.RootElement, wordOccurrences);
            variantResults.Add("all-bugs", brokenResult);
            variantFailures.Add("all-bugs", declaredFailures);
            Assert.Equal(declaredFailures.Keys.Order(StringComparer.Ordinal), brokenResult.Outcomes
                .Where(pair => pair.Value != "analysed").Select(pair => pair.Key).Order(StringComparer.Ordinal));

            var expected = new SampleExpected(
                spec.RootElement.GetProperty("disclaimer").GetString()!,
                ToExpected(fixedResult),
                ToExpected(brokenResult, declaredFailures),
                variantResults.ToDictionary(pair => pair.Key,
                    pair => ToExpected(pair.Value, variantFailures[pair.Key]), StringComparer.Ordinal),
                new SampleGrammarHealthPins(fixedGrammar, brokenGrammar));
            var json = JsonSerializer.Serialize(expected, ExpectedJsonOptions);
            using var actualDocument = JsonDocument.Parse(json);
            SampleJsonSchemaValidator.AssertValid(actualDocument.RootElement,
                schema.RootElement.GetProperty("$defs").GetProperty("sampleExpected"), schema.RootElement);
            if (Environment.GetEnvironmentVariable("MOTIF_SAMPLES_UPDATE_EXPECTED") == "1")
                await File.WriteAllTextAsync(expectedPath, json + Environment.NewLine);
            else
            {
                var actual = JsonNode.Parse(json)!;
                var pinned = JsonNode.Parse(await File.ReadAllTextAsync(expectedPath))!;
                RemoveWallClock(actual);
                RemoveWallClock(pinned);
                Assert.True(JsonNode.DeepEquals(pinned, actual), "Pinned parser results changed; update expected.json intentionally.");
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    internal async Task DuplicateOptionalPluralSlotsMultiplyParserWork()
    {
        var root = NewRoot();
        Directory.CreateDirectory(root);

        try
        {
            var fixture = RuntimeSampleProjectFixture.Create("runtime-optional-slots");
            var tokens = fixture.Tokens;
            var backVowel = tokens.Phonemes[1];
            var frontVowel = tokens.Phonemes[4];
            var consonant = tokens.Phonemes[2];
            var backClass = tokens.Id + "-back-vowels";
            var frontClass = tokens.Id + "-front-vowels";
            var consonantClass = tokens.ConsonantClassId;
            var slotId = tokens.Id + "-plural-slot";
            var stem = frontVowel + consonant;
            var backSuffix = backVowel + consonant;
            var frontSuffix = frontVowel + consonant;
            var backEnvironmentId = tokens.Id + "-back-environment";
            var frontEnvironmentId = tokens.Id + "-front-environment";
            var backEnvironment = $"/[{tokens.BackVowelClassAbbreviation}]([{tokens.ConsonantClassAbbreviation}])_";
            var frontEnvironment = $"/[{tokens.FrontVowelClassAbbreviation}]([{tokens.ConsonantClassAbbreviation}])_";
            fixture.Sample.NaturalClasses =
            [
                new(backClass, tokens.Id + " back vowels", tokens.BackVowelClassAbbreviation, [backVowel]),
                new(frontClass, tokens.Id + " front vowels", tokens.FrontVowelClassAbbreviation, [frontVowel]),
                new(consonantClass, tokens.Id + " consonants", tokens.ConsonantClassAbbreviation,
                    [.. tokens.Phonemes.Where(value => value != backVowel && value != frontVowel)]),
            ];
            fixture.Sample.Environments =
            [
                new(backEnvironmentId, tokens.Id + " back-vowel context", backEnvironment),
                new(frontEnvironmentId, tokens.Id + " front-vowel context", frontEnvironment),
            ];
            fixture.Sample.PartsOfSpeech = [fixture.Sample.PartsOfSpeech[0]];
            fixture.Sample.Stems = [new(tokens.NounId, stem, tokens.NounId, tokens.Id + " stem gloss")];
            fixture.Sample.AffixSlots = [new(slotId, tokens.Id + " plural slot", tokens.NounId, Optional: false)];
            fixture.Sample.AffixTemplates =
            [
                new(tokens.NounTemplateId, tokens.NounTemplateName, tokens.NounId, Final: true,
                    SuffixSlots: [slotId]),
            ];
            fixture.Sample.Affixes =
            [
                new(tokens.Id + "-plural-affix", tokens.NounId, [slotId], tokens.Id + " plural gloss",
                [
                    new(tokens.Id + "-back-allomorph", backSuffix, backEnvironmentId),
                    new(tokens.Id + "-front-allomorph", frontSuffix, frontEnvironmentId),
                ]),
            ];
            fixture.Sample.Texts = [new(tokens.TextId, tokens.TextTitle, [stem + frontSuffix])];
            var specPath = Path.Combine(root, "slow-spike.json");
            var bugsPath = Path.Combine(root, "slow-spike-bugs.json");
            await RuntimeSampleProjectFixture.WriteSampleAsync(specPath, fixture.Sample);
            var bug = new RuntimeBugSpec(tokens.BugId, tokens.Id + " repeated optional slots",
                fixture.Sample.Disclaimer,
                new("slow", [stem + frontSuffix], "Repeated optional slots create redundant parser paths."),
                ["Keep one plural slot in the noun template."],
                [new("duplicateOptionalSlot", TemplateId: tokens.NounTemplateId, SlotId: slotId, Count: 64)]);
            await RuntimeSampleProjectFixture.WriteBugsAsync(bugsPath, [bug]);

            using var fixedBuild = await BuildVariantAsync(root, specPath, bugsPath, [], "fixed");
            using var brokenBuild = await BuildVariantAsync(root, specPath, bugsPath, [tokens.BugId], "broken");
            var fixedProject = fixedBuild.RootElement.GetProperty("projectPath").GetString()!;
            var fixedText = fixedBuild.RootElement.GetProperty("texts")[0].GetProperty("guid").GetString()!;
            var brokenProject = brokenBuild.RootElement.GetProperty("projectPath").GetString()!;
            var brokenText = brokenBuild.RootElement.GetProperty("texts")[0].GetProperty("guid").GetString()!;

            var fixedWork = await AssessWorkAsync(root, fixedProject, fixedText);
            var brokenWork = await AssessWorkAsync(root, brokenProject, brokenText);
            output.WriteLine($"fixed={fixedWork}; broken={brokenWork}; step ratio={(double)brokenWork.Steps / fixedWork.Steps:F2}x; work ratio={(double)brokenWork.Work / fixedWork.Work:F2}x");
            Assert.True(brokenWork.Steps >= fixedWork.Steps * 10,
                $"Expected duplicate optional plural slots to multiply parser steps tenfold; fixed={fixedWork}, broken={brokenWork}.");
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

    private static async Task<JsonDocument> BuildCachedTurkicAsync(string destinationRoot)
    {
        var sampleFolder = SyntheticSampleInputs.SampleDirectory(RepositoryRoot(), "synthetic-turkic");
        var specPath = Path.Combine(sampleFolder, "sample.json");
        var bugsPath = Path.Combine(sampleFolder, "bugs.json");
        var schemaPath = Path.Combine(RepositoryRoot(), "samples", "sample.schema.json");
        var builderPath = Path.Combine(BuildOutput.ProductDirectory, "SIL.Motif.SampleProjects.dll");
        var fingerprintFiles = new[] { specPath, bugsPath, schemaPath, builderPath };
        var fingerprintText = string.Join("\n", fingerprintFiles.Select(path =>
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            BuildOutput.ProductDirectory + "\nproject-copy-v1\n" + fingerprintText)));
        var cacheParent = Path.Combine(BuildOutput.ProductDirectory, "SampleSpikes", "shared-turkic");
        Directory.CreateDirectory(cacheParent);
        var cacheRoot = Path.Combine(cacheParent, fingerprint);
        await using var cacheLock = await AcquireCacheLockAsync(cacheRoot + ".lock");
        var resultPath = Path.Combine(cacheRoot, "build.json");
        if (File.Exists(resultPath))
        {
            var cachedJson = await File.ReadAllTextAsync(resultPath);
            try
            {
                using var cached = JsonDocument.Parse(cachedJson);
                var projectPath = cached.RootElement.GetProperty("projectPath").GetString();
                var backupPath = cached.RootElement.GetProperty("backupPath").GetString();
                if (projectPath is not null && File.Exists(projectPath) &&
                    backupPath is not null && File.Exists(backupPath))
                    return CopyCachedTurkicBuild(cachedJson, cacheRoot, destinationRoot);
            }
            catch (JsonException)
            {
            }
        }

        if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
        Directory.CreateDirectory(cacheRoot);
        using var built = await BuildVariantAsync(cacheRoot, specPath, bugsPath, [], "fixed");
        var json = built.RootElement.GetRawText();
        await File.WriteAllTextAsync(resultPath, json);
        return CopyCachedTurkicBuild(json, cacheRoot, destinationRoot);
    }

    private static JsonDocument CopyCachedTurkicBuild(string json, string cacheRoot, string destinationRoot)
    {
        var output = JsonNode.Parse(json)!.AsObject();
        var cachedVariantRoot = Path.Combine(cacheRoot, "fixed");
        var copiedVariantRoot = Path.Combine(destinationRoot, "cached-fixed");
        CopyDirectory(cachedVariantRoot, copiedVariantRoot);
        output["projectPath"] = Path.Combine(copiedVariantRoot,
            Path.GetRelativePath(cachedVariantRoot, output["projectPath"]!.GetValue<string>()));
        output["backupPath"] = Path.Combine(copiedVariantRoot,
            Path.GetRelativePath(cachedVariantRoot, output["backupPath"]!.GetValue<string>()));
        return JsonDocument.Parse(output.ToJsonString());
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relativePath));
        }

        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file).Contains(".motif.db", StringComparison.OrdinalIgnoreCase)) continue;
            var copyPath = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copyPath)!);
            File.Copy(file, copyPath);
        }
    }

    private static async Task<FileStream> AcquireCacheLockAsync(string path)
    {
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25));
            }
        }
    }

    private static async Task<JsonDocument> BuildAsync(string root)
    {
        var result = await RunBuilderAsync(root,
            Path.Combine(SyntheticSampleInputs.SampleDirectory(RepositoryRoot(), "synthetic-turkic"), "sample.json"));
        Assert.True(result.ExitCode == 0, result.StandardError);
        return JsonDocument.Parse(result.StandardOutput);
    }

    private static async Task<JsonDocument> BuildVariantAsync(
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
        var work = 0L;
        var steps = 0L;
        var parserElapsedMs = 0d;
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        foreach (var word in words)
        {
            var trace = await tracer.TraceAsync(projectPath, word.GetProperty("word").GetString()!,
                CancellationToken.None, TimeSpan.FromSeconds(30));
            var completed = Assert.IsType<PanGlossTraceOutcome.Completed>(trace);
            var details = Assert.IsType<PanGlossTraceDetails>(completed.Details);
            steps += details.Steps;
            work += details.Categories.Sum(category => category.Work);
            parserElapsedMs += details.ElapsedNs / 1_000_000d;
        }
        return new WorkSnapshot(work, steps, stopwatch.ElapsedMilliseconds, parserElapsedMs);
    }

    private static async Task<AssessmentSnapshot> AssessTextsAsync(
        string root, JsonElement buildResult, IReadOnlyDictionary<string, int> wordOccurrences,
        IReadOnlySet<string>? assessmentWords = null, IReadOnlySet<string>? traceWords = null)
    {
        var outcomes = new Dictionary<string, string>(StringComparer.Ordinal);
        var wordWork = new Dictionary<string, long>(StringComparer.Ordinal);
        var tracedWords = traceWords is null ? null : new HashSet<string>(StringComparer.Ordinal);
        var words = 0;
        var parsed = 0;
        var work = 0L;
        var steps = 0L;
        var assessmentId = Guid.NewGuid().ToString("N")[..8];
        var workerRoot = Path.Combine(root, "worker-root");
        var assess = new ProcessStartInfo(BuildOutput.Cli)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        assess.Environment["MOTIF_WORKER_ROOT"] = Path.Combine(workerRoot, assessmentId);
        assess.Environment["MOTIF_DEVELOPER_COMMANDS"] = "1";
        assess.ArgumentList.Add("assess");
        assess.ArgumentList.Add(buildResult.GetProperty("projectPath").GetString()!);
        if (assessmentWords is null)
        {
            assess.ArgumentList.Add("--texts");
            assess.ArgumentList.Add(string.Join(',', buildResult.GetProperty("texts").EnumerateArray()
                .Select(text => text.GetProperty("guid").GetString())));
        }
        else
        {
            Directory.CreateDirectory(workerRoot);
            var wordsPath = Path.Combine(workerRoot, assessmentId + ".words");
            await File.WriteAllLinesAsync(wordsPath, assessmentWords.Order(StringComparer.Ordinal));
            assess.ArgumentList.Add("--words");
            assess.ArgumentList.Add(wordsPath);
        }
        assess.ArgumentList.Add("--json");
        var stopwatch = Stopwatch.StartNew();
        var assessment = await RunAsync(assess);
        stopwatch.Stop();
        Assert.True(assessment.ExitCode == 0, assessment.StandardError);

        using var invoker = traceWords is null ? null : new PanGlossInvoker();
        var tracer = invoker is null ? null : new PanGlossTracer(invoker);
        using var response = JsonDocument.Parse(assessment.StandardOutput);
        foreach (var word in response.RootElement.GetProperty("words").EnumerateArray())
        {
            var form = word.GetProperty("word").GetString()!;
            var normalized = form.Normalize(NormalizationForm.FormC);
            var outcome = word.GetProperty("outcome").GetString()!;
            var occurrenceCount = wordOccurrences.GetValueOrDefault(normalized);
            Assert.True(occurrenceCount > 0, $"Assessment returned undeclared sample word '{form}'.");
            words += occurrenceCount;
            if (outcomes.TryGetValue(normalized, out var previousOutcome))
                Assert.Equal(previousOutcome, outcome);
            else
                outcomes.Add(normalized, outcome);
            if (outcome == "analysed") parsed += occurrenceCount;

            if (tracer is null || !traceWords!.Contains(form) || !tracedWords!.Add(form)) continue;
            var trace = await tracer.TraceAsync(buildResult.GetProperty("projectPath").GetString()!, form,
                CancellationToken.None, TimeSpan.FromSeconds(30));
            var completed = Assert.IsType<PanGlossTraceOutcome.Completed>(trace);
            var details = Assert.IsType<PanGlossTraceDetails>(completed.Details);
            steps += details.Steps;
            var wordUnits = details.Categories.Sum(category => category.Work);
            work += wordUnits;
            wordWork[normalized] = wordUnits;
        }

        if (traceWords is not null)
            Assert.Equal(traceWords.Order(StringComparer.Ordinal), tracedWords!.Order(StringComparer.Ordinal));

        return new AssessmentSnapshot(outcomes, words, parsed,
            traceWords is null ? null : work, traceWords is null ? null : steps, stopwatch.ElapsedMilliseconds,
            tracedWords?.Order(StringComparer.Ordinal).ToArray(), assessmentWords?.Order(StringComparer.Ordinal).ToArray(),
            wordWork);
    }

    private static Dictionary<string, int> WordOccurrencesByText(JsonElement sample)
    {
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var text in sample.GetProperty("texts").EnumerateArray())
        foreach (var form in text.GetProperty("sentences").EnumerateArray()
                     .SelectMany(sentence => sentence.GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                     .Distinct(StringComparer.Ordinal))
        {
            var normalized = form.Normalize(NormalizationForm.FormC);
            occurrences[normalized] = occurrences.GetValueOrDefault(normalized) + 1;
        }
        return occurrences;
    }

    private static SampleVariantExpected ToExpected(
        AssessmentSnapshot result, Dictionary<string, string>? failing = null) =>
        new(result.Words, result.Parsed, result.Words == 0 ? 0 : (double)result.Parsed / result.Words,
            result.Work, result.Steps, result.WallClockMs, result.SelectedWords,
            result.TracedWords is null ? null : SlowestWords(result), result.TracedWords,
            failing ?? new Dictionary<string, string>(StringComparer.Ordinal));

    private static string[] SlowestWords(AssessmentSnapshot result) => result.WordWork
        .OrderByDescending(pair => pair.Value)
        .ThenBy(pair => pair.Key, StringComparer.Ordinal)
        .Take(4)
        .Select(pair => pair.Key)
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static void AssertFixedWarningsDocumented(JsonElement spec, SampleGrammarHealthSnapshot grammar)
    {
        var warnings = grammar.Findings.Where(finding => finding.Level == "warning")
            .Select(finding => finding.Code).ToHashSet(StringComparer.Ordinal);
        var documented = spec.TryGetProperty("knownGrammarWarnings", out var entries)
            ? entries.EnumerateArray().ToArray()
            : [];
        Assert.Equal(warnings.Order(StringComparer.Ordinal), documented
            .Select(entry => entry.GetProperty("code").GetString()!).Order(StringComparer.Ordinal));
        foreach (var entry in documented)
        {
            var reason = entry.GetProperty("reason").GetString()!;
            Assert.DoesNotContain('\n', reason);
            Assert.DoesNotContain('\r', reason);
        }
    }

    private static void RemoveWallClock(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            obj.Remove("wallClockMs");
            foreach (var value in obj.Select(pair => pair.Value).Where(value => value is not null))
                RemoveWallClock(value!);
        }
        else if (node is JsonArray array)
        {
            foreach (var value in array.Where(value => value is not null))
                RemoveWallClock(value!);
        }
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
    private sealed record WorkSnapshot(long Work, long Steps, long WallClockMs, double ParserElapsedMs);
    private sealed record AssessmentSnapshot(
        Dictionary<string, string> Outcomes, int Words, int Parsed, long? Work, long? Steps, long WallClockMs,
        string[]? TracedWords, string[]? SelectedWords, Dictionary<string, long> WordWork);
    private sealed record SampleExpected(
        string Disclaimer, SampleVariantExpected Fixed, SampleVariantExpected Broken,
        Dictionary<string, SampleVariantExpected> Variants, SampleGrammarHealthPins GrammarHealth);
    private sealed record SampleGrammarHealthPins(
        SampleGrammarHealthSnapshot Fixed, SampleGrammarHealthSnapshot Broken);
    private sealed record SampleVariantExpected(
        int Words, int Parsed, double TextCoverage, long? Work, long? Steps, long WallClockMs,
        string[]? SelectedWords, string[]? SlowestWords, string[]? TracedWords,
        Dictionary<string, string> Failing);

    private static readonly JsonSerializerOptions ExpectedJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };
}

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group1)]
public sealed class SyntheticSampleWordsTests(ITestOutputHelper output)
{
    [RealParserFact]
    public Task SyntheticSampleWordsParseThroughMotifAssess() =>
        new SampleProjectSpikeTests(output).SyntheticSampleWordsParseThroughMotifAssess();
}

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group1)]
public sealed class SyntheticTurkicBugVariantsTests(ITestOutputHelper output)
{
    [RealParserFact]
    public Task SyntheticTurkicBugVariantsMatchDeclaredSymptomsAndPinParserWork() =>
        new SampleProjectSpikeTests(output).SyntheticTurkicBugVariantsMatchDeclaredSymptomsAndPinParserWork();
}

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group1)]
public sealed class DuplicateOptionalPluralSlotsTests(ITestOutputHelper output)
{
    [RealParserFact]
    public Task DuplicateOptionalPluralSlotsMultiplyParserWork() =>
        new SampleProjectSpikeTests(output).DuplicateOptionalPluralSlotsMultiplyParserWork();
}
