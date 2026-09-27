using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
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
    public async Task SyntheticTurkicWritingSystemBuildsFromTheLocalProjectData()
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
                  "disclaimer": "SYNTHETIC EXAMPLE. This language data was generated to demonstrate Motif. It is modelled loosely on Turkish, but it is not real Turkish, has not been checked by speakers, and must not be used as a description of any language.",
                  "description": "SYNTHETIC EXAMPLE. This language data was generated to demonstrate Motif. It is modelled loosely on Turkish, but it is not real Turkish, has not been checked by speakers, and must not be used as a description of any language.",
                  "language": { "name": "Synthetic test", "tag": "tr" },
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
    public async Task SyntheticSampleWordsParseThroughMotifAssess()
    {
        var root = NewRoot();
        Directory.CreateDirectory(root);

        try
        {
            using var buildOutput = await BuildAsync(root);
            var projectPath = buildOutput.RootElement.GetProperty("projectPath").GetString()!;
            var parsedWords = 0;
            foreach (var text in buildOutput.RootElement.GetProperty("texts").EnumerateArray())
            {
                var assess = new ProcessStartInfo(BuildOutput.Cli)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                assess.Environment["MOTIF_WORKER_ROOT"] = Path.Combine(root, "worker-root", text.GetProperty("id").GetString()!);
                assess.Environment["MOTIF_DEVELOPER_COMMANDS"] = "1";
                assess.ArgumentList.Add("assess");
                assess.ArgumentList.Add(projectPath);
                assess.ArgumentList.Add("--texts");
                assess.ArgumentList.Add(text.GetProperty("guid").GetString()!);
                assess.ArgumentList.Add("--json");
                var assessment = await RunAsync(assess);

                Assert.True(assessment.ExitCode == 0, assessment.StandardError);
                using var response = JsonDocument.Parse(assessment.StandardOutput);
                var words = response.RootElement.GetProperty("words").EnumerateArray().ToArray();
                Assert.All(words, word => Assert.Equal("analysed", word.GetProperty("outcome").GetString()));
                parsedWords += words.Length;
            }
            Assert.Equal(16, parsedWords);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [RealParserFact]
    public async Task SyntheticTurkicBugVariantsMatchDeclaredSymptomsAndPinParserWork()
    {
        var root = NewRoot();
        Directory.CreateDirectory(root);

        try
        {
            var sampleFolder = Path.Combine(RepositoryRoot(), "samples", "synthetic-turkic");
            var specPath = Path.Combine(sampleFolder, "sample.json");
            var bugsPath = Path.Combine(sampleFolder, "bugs.json");
            var expectedPath = Path.Combine(sampleFolder, "expected.json");
            using var spec = JsonDocument.Parse(await File.ReadAllTextAsync(specPath));
            using var bugs = JsonDocument.Parse(await File.ReadAllTextAsync(bugsPath));
            var definitions = bugs.RootElement.EnumerateArray().ToArray();
            var bugIds = definitions.Select(bug => bug.GetProperty("id").GetString()!).ToArray();
            var textCount = spec.RootElement.GetProperty("texts").EnumerateArray()
                .Sum(text => text.GetProperty("sentences").EnumerateArray()
                    .Sum(sentence => sentence.GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length));

            using var fixedBuild = await BuildVariantAsync(root, specPath, bugsPath, [], "fixed");
            var fixedResult = await AssessTextsAsync(root, fixedBuild.RootElement, trace: true);
            Assert.Equal(textCount, fixedResult.Words);
            Assert.Equal(fixedResult.Words, fixedResult.Parsed);

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
                using var variantBuild = await BuildVariantAsync(root, specPath, bugsPath, [bugId], bugId);
                var result = await AssessTextsAsync(root, variantBuild.RootElement, trace: true);
                variantResults.Add(bugId, result);
                var actualFailures = result.Outcomes.Where(pair => pair.Value != "analysed")
                    .Select(pair => pair.Key).Order(StringComparer.Ordinal).ToArray();
                var symptom = bug.GetProperty("symptom");
                var expectedWords = symptom.GetProperty("words").EnumerateArray()
                    .Select(word => word.GetString()!).Order(StringComparer.Ordinal).ToArray();
                if (symptom.GetProperty("kind").GetString() == "slow")
                {
                    Assert.Empty(actualFailures);
                    variantFailures.Add(bugId, new(StringComparer.Ordinal));
                    var slowestWords = SlowestWords(result);
                    output.WriteLine($"slow variant steps={result.Steps}; fixed steps={fixedResult.Steps}; ratio={(double)result.Steps / fixedResult.Steps:F2}x; slowest={string.Join(", ", slowestWords)}");
                    Assert.Equal(expectedWords, slowestWords);
                    Assert.True(result.Steps >= fixedResult.Steps * 10,
                        $"Slow bug '{bugId}' needs 10x parser steps; fixed={fixedResult}, broken={result}.");
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
            var brokenResult = await AssessTextsAsync(root, brokenBuild.RootElement, trace: true);
            variantResults.Add("all-bugs", brokenResult);
            variantFailures.Add("all-bugs", declaredFailures);
            Assert.Equal(declaredFailures.Keys.Order(StringComparer.Ordinal), brokenResult.Outcomes
                .Where(pair => pair.Value != "analysed").Select(pair => pair.Key).Order(StringComparer.Ordinal));

            var expected = new SampleExpected(
                spec.RootElement.GetProperty("disclaimer").GetString()!,
                ToExpected(fixedResult),
                ToExpected(brokenResult, declaredFailures),
                variantResults.ToDictionary(pair => pair.Key,
                    pair => ToExpected(pair.Value, variantFailures[pair.Key]), StringComparer.Ordinal));
            var json = JsonSerializer.Serialize(expected, ExpectedJsonOptions);
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
                  "title": "Synthetic slow spike",
                  "disclaimer": "SYNTHETIC EXAMPLE. This language data was generated to demonstrate Motif. It is modelled loosely on Turkish, but it is not real Turkish, has not been checked by speakers, and must not be used as a description of any language.",
                  "description": "SYNTHETIC EXAMPLE. This language data was generated to demonstrate Motif. It is modelled loosely on Turkish, but it is not real Turkish, has not been checked by speakers, and must not be used as a description of any language.",
                  "language": { "name": "Synthetic Turkic-style", "tag": "tr" },
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
                  "title": "Repeated optional plural slots",
                  "disclaimer": "SYNTHETIC EXAMPLE. This language data was generated to demonstrate Motif. It is modelled loosely on Turkish, but it is not real Turkish, has not been checked by speakers, and must not be used as a description of any language.",
                  "fix": ["Keep one Plural slot in the noun template."],
                  "symptom": { "kind": "slow", "words": ["evler"], "reason": "Repeated optional slots create redundant parse paths." },
                  "patch": [{ "op": "duplicateOptionalSlot", "templateId": "noun", "slotId": "plural", "count": 64 }]
                }]
                """);

            using var fixedBuild = await BuildVariantAsync(root, specPath, bugsPath, [], "fixed");
            using var brokenBuild = await BuildVariantAsync(root, specPath, bugsPath, ["optional-plural-copies"], "broken");
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

    private static async Task<JsonDocument> BuildAsync(string root)
    {
        var result = await RunBuilderAsync(root,
            Path.Combine(RepositoryRoot(), "samples", "synthetic-turkic", "sample.json"));
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
        string root, JsonElement buildResult, bool trace)
    {
        var outcomes = new Dictionary<string, string>(StringComparer.Ordinal);
        var wordCount = 0;
        var parsedCount = 0;
        var wallClockMs = 0L;
        var work = 0L;
        var steps = 0L;
        var wordSteps = new Dictionary<string, long>(StringComparer.Ordinal);
        var invoker = trace ? new PanGlossInvoker() : null;
        var tracer = invoker is null ? null : new PanGlossTracer(invoker);
        try
        {
            foreach (var text in buildResult.GetProperty("texts").EnumerateArray())
            {
                var textId = text.GetProperty("id").GetString()!;
                var assess = new ProcessStartInfo(BuildOutput.Cli)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                assess.Environment["MOTIF_WORKER_ROOT"] = Path.Combine(root, "worker-root", textId);
                assess.Environment["MOTIF_DEVELOPER_COMMANDS"] = "1";
                assess.ArgumentList.Add("assess");
                assess.ArgumentList.Add(buildResult.GetProperty("projectPath").GetString()!);
                assess.ArgumentList.Add("--texts");
                assess.ArgumentList.Add(text.GetProperty("guid").GetString()!);
                assess.ArgumentList.Add("--json");
                var stopwatch = Stopwatch.StartNew();
                var assessment = await RunAsync(assess);
                stopwatch.Stop();
                wallClockMs += stopwatch.ElapsedMilliseconds;
                Assert.True(assessment.ExitCode == 0, assessment.StandardError);
                using var response = JsonDocument.Parse(assessment.StandardOutput);
                foreach (var word in response.RootElement.GetProperty("words").EnumerateArray())
                {
                    var form = word.GetProperty("word").GetString()!;
                    var outcome = word.GetProperty("outcome").GetString()!;
                    wordCount++;
                    var normalizedForm = form.Normalize(NormalizationForm.FormC);
                    if (outcomes.TryGetValue(normalizedForm, out var previousOutcome))
                        Assert.Equal(previousOutcome, outcome);
                    else
                        outcomes.Add(normalizedForm, outcome);
                    if (outcome == "analysed") parsedCount++;
                    if (tracer is null || outcome != "analysed") continue;
                    var traceResult = await tracer.TraceAsync(buildResult.GetProperty("projectPath").GetString()!,
                        form, CancellationToken.None, TimeSpan.FromSeconds(30));
                    var completed = Assert.IsType<PanGlossTraceOutcome.Completed>(traceResult);
                    var details = Assert.IsType<PanGlossTraceDetails>(completed.Details);
                    steps += details.Steps;
                    work += details.Categories.Sum(category => category.Work);
                    wordSteps[normalizedForm] = wordSteps.GetValueOrDefault(normalizedForm) + details.Steps;
                }
            }
            return new AssessmentSnapshot(outcomes, wordCount, parsedCount, work, steps, wallClockMs, wordSteps);
        }
        finally
        {
            invoker?.Dispose();
        }
    }

    private static SampleVariantExpected ToExpected(
        AssessmentSnapshot result, Dictionary<string, string>? failing = null) =>
        new(result.Words, result.Parsed, result.Words == 0 ? 0 : (double)result.Parsed / result.Words,
            result.Work, result.Steps, result.WallClockMs,
            SlowestWords(result), failing ?? new Dictionary<string, string>(StringComparer.Ordinal));

    private static string[] SlowestWords(AssessmentSnapshot result) => result.WordSteps
        .OrderByDescending(pair => pair.Value)
        .ThenBy(pair => pair.Key, StringComparer.Ordinal)
        .Take(4)
        .Select(pair => pair.Key)
        .Order(StringComparer.Ordinal)
        .ToArray();

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
        Dictionary<string, string> Outcomes, int Words, int Parsed, long Work, long Steps, long WallClockMs,
        Dictionary<string, long> WordSteps);
    private sealed record SampleExpected(
        string Disclaimer, SampleVariantExpected Fixed, SampleVariantExpected Broken,
        Dictionary<string, SampleVariantExpected> Variants);
    private sealed record SampleVariantExpected(
        int Words, int Parsed, double TextCoverage, long Work, long Steps, long WallClockMs,
        string[] SlowestWords,
        Dictionary<string, string> Failing);

    private static readonly JsonSerializerOptions ExpectedJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
}
