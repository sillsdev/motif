using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Samples;

[Collection(LcmCacheTestCollection.Name)]
public sealed class SyntheticBantuSampleTests(ITestOutputHelper output)
{
    private const string SampleFolder = "synthetic-bantu";

    [Fact]
    public async Task SyntheticBantuVariantsValidateBuildAndReopen()
    {
        var root = NewRoot();
        Directory.CreateDirectory(root);

        try
        {
            var sampleFolder = Path.Combine(RepositoryRoot(), "samples", SampleFolder);
            var specPath = Path.Combine(sampleFolder, "sample.json");
            var bugsPath = Path.Combine(sampleFolder, "bugs.json");
            using var schema = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(RepositoryRoot(), "samples", "sample.schema.json")));
            using var spec = JsonDocument.Parse(await File.ReadAllTextAsync(specPath));
            using var bugs = JsonDocument.Parse(await File.ReadAllTextAsync(bugsPath));
            SampleJsonSchemaValidator.AssertValid(spec.RootElement, schema.RootElement);
            SampleJsonSchemaValidator.AssertValid(bugs.RootElement,
                schema.RootElement.GetProperty("$defs").GetProperty("bugList"), schema.RootElement);

            var disclaimer = spec.RootElement.GetProperty("disclaimer").GetString()!;
            foreach (var bug in bugs.RootElement.EnumerateArray())
                Assert.Equal(disclaimer, bug.GetProperty("disclaimer").GetString());

            using var fixedBuild = await BuildVariantAsync(root, specPath, bugsPath, [], "fixed");
            Reopen(fixedBuild.RootElement.GetProperty("projectPath").GetString()!, disclaimer);

            foreach (var bug in bugs.RootElement.EnumerateArray())
            {
                var bugId = bug.GetProperty("id").GetString()!;
                using var variant = await BuildVariantAsync(root, specPath, bugsPath, [bugId], bugId);
                Reopen(variant.RootElement.GetProperty("projectPath").GetString()!, disclaimer);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [RealParserFact]
    public async Task SyntheticBantuVariantsMatchDeclaredSymptomsAndPinParserWork()
    {
        var root = NewRoot();
        Directory.CreateDirectory(root);

        try
        {
            var sampleFolder = Path.Combine(RepositoryRoot(), "samples", SampleFolder);
            var specPath = Path.Combine(sampleFolder, "sample.json");
            var bugsPath = Path.Combine(sampleFolder, "bugs.json");
            var expectedPath = Path.Combine(sampleFolder, "expected.json");
            using var spec = JsonDocument.Parse(await File.ReadAllTextAsync(specPath));
            using var bugs = JsonDocument.Parse(await File.ReadAllTextAsync(bugsPath));
            var definitions = bugs.RootElement.EnumerateArray().ToArray();
            var bugIds = definitions.Select(bug => bug.GetProperty("id").GetString()!).ToArray();
            var textCount = spec.RootElement.GetProperty("texts").EnumerateArray()
                .Sum(text => text.GetProperty("sentences").EnumerateArray()
                    .SelectMany(sentence => sentence.GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    .Distinct(StringComparer.Ordinal).Count());

            using var fixedBuild = await BuildVariantAsync(root, specPath, bugsPath, [], "fixed");
            var fixedResult = await AssessTextsAsync(root, fixedBuild.RootElement);
            Assert.Equal(textCount, fixedResult.Words);
            Assert.True(fixedResult.Words == fixedResult.Parsed,
                $"Fixed sample has unparsed words: {string.Join(", ", fixedResult.Outcomes.Where(pair => pair.Value != "analysed").Select(pair => pair.Key))}");
            var fixedGrammar = ReadGrammarFindings(fixedBuild.RootElement);
            Assert.DoesNotContain(fixedGrammar, finding => finding.Level == "error");
            WriteGrammarFindings(output, "fixed", fixedGrammar);

            var variantResults = new Dictionary<string, AssessmentSnapshot>(StringComparer.Ordinal)
            {
                ["fixed"] = fixedResult,
            };
            var variantFailures = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal)
            {
                ["fixed"] = new(StringComparer.Ordinal),
            };
            var grammarFindings = new Dictionary<string, GrammarFindingExpected[]>(StringComparer.Ordinal)
            {
                ["fixed"] = fixedGrammar,
            };
            var declaredFailures = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var bug in definitions)
            {
                var bugId = bug.GetProperty("id").GetString()!;
                using var variantBuild = await BuildVariantAsync(root, specPath, bugsPath, [bugId], bugId);
                var result = await AssessTextsAsync(root, variantBuild.RootElement);
                variantResults.Add(bugId, result);
                var findings = ReadGrammarFindings(variantBuild.RootElement);
                grammarFindings.Add(bugId, findings);
                WriteGrammarFindings(output, bugId, findings);
                var actualFailures = result.Outcomes.Where(pair => pair.Value != "analysed")
                    .Select(pair => pair.Key).Order(StringComparer.Ordinal).ToArray();
                var symptom = bug.GetProperty("symptom");
                var expectedWords = symptom.GetProperty("words").EnumerateArray()
                    .Select(word => word.GetString()!).Order(StringComparer.Ordinal).ToArray();

                if (symptom.GetProperty("kind").GetString() == "slow")
                {
                    Assert.Empty(actualFailures);
                    variantFailures.Add(bugId, new(StringComparer.Ordinal));
                    Assert.All(expectedWords, word => Assert.Contains(word, result.Outcomes.Keys));
                    output.WriteLine(
                        $"slow variant work={result.Work}; fixed work={fixedResult.Work}; ratio={(double)result.Work / fixedResult.Work:F2}x; steps={result.Steps}/{fixedResult.Steps}");
                    Assert.True(result.Work >= fixedResult.Work * 10,
                        $"Slow bug '{bugId}' needs 10x parser work; fixed={fixedResult}, broken={result}.");
                }
                else
                {
                    Assert.True(expectedWords.SequenceEqual(actualFailures),
                        $"Bug '{bugId}' expected failures [{string.Join(", ", expectedWords)}], " +
                        $"got [{string.Join(", ", actualFailures)}].");
                    foreach (var word in expectedWords)
                        Assert.True(declaredFailures.TryAdd(word, bugId), $"Word '{word}' is assigned to two bugs.");
                    variantFailures.Add(bugId,
                        expectedWords.ToDictionary(word => word, _ => bugId, StringComparer.Ordinal));
                }
            }

            using var brokenBuild = await BuildVariantAsync(root, specPath, bugsPath, bugIds, "all-bugs");
            var brokenResult = await AssessTextsAsync(root, brokenBuild.RootElement);
            variantResults.Add("all-bugs", brokenResult);
            var combinedGrammar = ReadGrammarFindings(brokenBuild.RootElement);
            grammarFindings.Add("all-bugs", combinedGrammar);
            WriteGrammarFindings(output, "all-bugs", combinedGrammar);
            var combinedFailures = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var word in brokenResult.Outcomes.Where(pair => pair.Value != "analysed")
                         .Select(pair => pair.Key).Order(StringComparer.Ordinal))
            {
                Assert.True(declaredFailures.TryGetValue(word, out var bugId),
                    $"Combined variant failed undeclared word '{word}'.");
                combinedFailures.Add(word, bugId!);
            }
            variantFailures.Add("all-bugs", combinedFailures);

            var expected = new SampleExpected(
                spec.RootElement.GetProperty("disclaimer").GetString()!,
                ToExpected(fixedResult, grammarFindings: fixedGrammar),
                ToExpected(brokenResult, combinedFailures, combinedGrammar),
                variantResults.ToDictionary(pair => pair.Key,
                    pair => ToExpected(pair.Value, variantFailures[pair.Key], grammarFindings[pair.Key]),
                    StringComparer.Ordinal));
            var json = JsonSerializer.Serialize(expected, ExpectedJsonOptions);
            if (Environment.GetEnvironmentVariable("MOTIF_SAMPLES_UPDATE_EXPECTED") == "1")
                await File.WriteAllTextAsync(expectedPath, json + Environment.NewLine, new UTF8Encoding(false));
            else
            {
                var actual = JsonNode.Parse(json)!;
                var pinned = JsonNode.Parse(await File.ReadAllTextAsync(expectedPath))!;
                RemoveWallClock(actual);
                RemoveWallClock(pinned);
                Assert.True(JsonNode.DeepEquals(pinned, actual),
                    "Pinned parser results changed; update expected.json intentionally.");
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
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

    private static async Task<AssessmentSnapshot> AssessTextsAsync(string root, JsonElement buildResult)
    {
        var outcomes = new Dictionary<string, string>(StringComparer.Ordinal);
        var wordWork = new Dictionary<string, long>(StringComparer.Ordinal);
        var words = 0;
        var parsed = 0;
        var work = 0L;
        var steps = 0L;
        var wallClockMs = 0L;
        var assessmentId = Guid.NewGuid().ToString("N")[..8];
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);

        foreach (var text in buildResult.GetProperty("texts").EnumerateArray())
        {
            var textId = text.GetProperty("id").GetString()!;
            var assess = new ProcessStartInfo(BuildOutput.Cli)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            assess.Environment["MOTIF_WORKER_ROOT"] = Path.Combine(root, "w", assessmentId, textId);
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
                var normalized = form.Normalize(NormalizationForm.FormC);
                var outcome = word.GetProperty("outcome").GetString()!;
                words++;
                if (outcomes.TryGetValue(normalized, out var previous))
                    Assert.Equal(previous, outcome);
                else
                    outcomes.Add(normalized, outcome);
                if (outcome == "analysed") parsed++;

                var trace = await tracer.TraceAsync(buildResult.GetProperty("projectPath").GetString()!, form,
                    CancellationToken.None, TimeSpan.FromSeconds(30));
                var completed = Assert.IsType<PanGlossTraceOutcome.Completed>(trace);
                var details = Assert.IsType<PanGlossTraceDetails>(completed.Details);
                steps += details.Steps;
                var wordUnits = details.Categories.Sum(category => category.Work);
                work += wordUnits;
                wordWork[normalized] = wordWork.GetValueOrDefault(normalized) + wordUnits;
            }
        }

        return new AssessmentSnapshot(outcomes, words, parsed, work, steps, wallClockMs, wordWork);
    }

    private static GrammarFindingExpected[] ReadGrammarFindings(JsonElement buildResult)
    {
        var projectPath = buildResult.GetProperty("projectPath").GetString()!;
        var outcome = GrammarCheckQuery.Query(new GrammarCheckRequest(projectPath));
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.True(outcome.Value!.HasBaseline, "Grammar check needs the Baseline captured by Assessment.");

        return outcome.Value.Findings
            .Select(finding =>
            {
                string? warningReason = null;
                if (finding.Severity == GrammarDiagnosticLevel.Warning)
                {
                    Assert.True(GrammarWarningReasons.TryGetValue(finding.Code!, out var reason),
                        $"Warning '{finding.Code}' needs a one-line reason.");
                    Assert.False(string.IsNullOrWhiteSpace(reason),
                        $"Warning '{finding.Code}' needs a one-line reason.");
                    Assert.DoesNotContain("\r", reason!);
                    Assert.DoesNotContain("\n", reason!);
                    warningReason = reason;
                }

                return new GrammarFindingExpected(
                    finding.Severity.ToWireValue(),
                    finding.Code ?? string.Empty,
                    finding.Group ?? string.Empty,
                    finding.Description,
                    finding.Guidance,
                    finding.Subject.Select(subject => subject.Text).Order(StringComparer.Ordinal).ToArray(),
                    warningReason);
            })
            .OrderBy(finding => finding.Code, StringComparer.Ordinal)
            .ThenBy(finding => finding.Level, StringComparer.Ordinal)
            .ThenBy(finding => finding.Description, StringComparer.Ordinal)
            .ToArray();
    }

    private static void WriteGrammarFindings(
        ITestOutputHelper output, string variant, IReadOnlyList<GrammarFindingExpected> findings)
    {
        output.WriteLine($"grammar-health {variant}: {findings.Count} finding(s)");
        foreach (var finding in findings)
            output.WriteLine($"{finding.Level} {finding.Code}: {finding.Description}");
    }

    private static void Reopen(string projectPath, string disclaimer)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(projectPath);
        var description = cache.LangProject.Description.get_String(cache.DefaultAnalWs).Text;
        Assert.StartsWith(disclaimer, description, StringComparison.Ordinal);
    }

    private static SampleVariantExpected ToExpected(
        AssessmentSnapshot result, Dictionary<string, string>? failing = null,
        GrammarFindingExpected[]? grammarFindings = null) =>
        new(result.Words, result.Parsed, result.Words == 0 ? 0 : (double)result.Parsed / result.Words,
            result.Work, result.Steps, result.WallClockMs, SlowestWords(result),
            failing ?? new Dictionary<string, string>(StringComparer.Ordinal), grammarFindings ?? []);

    private static string[] SlowestWords(AssessmentSnapshot result) => result.WordWork
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

    private static string NewRoot() => Path.Combine(
        BuildOutput.ProductDirectory, "SB", Guid.NewGuid().ToString("N"));

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(BuildOutput.ProductDirectory, "..", ".."));

    private static void DeleteDirectory(string root)
    {
        try { Directory.Delete(root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
    private sealed record AssessmentSnapshot(
        Dictionary<string, string> Outcomes, int Words, int Parsed, long Work, long Steps, long WallClockMs,
        Dictionary<string, long> WordWork);
    private sealed record SampleExpected(
        string Disclaimer, SampleVariantExpected Fixed, SampleVariantExpected Broken,
        Dictionary<string, SampleVariantExpected> Variants);
    private sealed record SampleVariantExpected(
        int Words, int Parsed, double TextCoverage, long Work, long Steps, long WallClockMs,
        string[] SlowestWords, Dictionary<string, string> Failing,
        GrammarFindingExpected[] GrammarFindings);
    private sealed record GrammarFindingExpected(
        string Level, string Code, string Group, string Description, string? Guidance,
        string[] Subjects, string? WarningReason);

    private static readonly JsonSerializerOptions ExpectedJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static readonly Dictionary<string, string> GrammarWarningReasons = new(StringComparer.Ordinal);
}
