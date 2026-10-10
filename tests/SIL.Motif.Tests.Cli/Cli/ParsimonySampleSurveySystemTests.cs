using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.Motif.Commands.Preferences;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Cli;

[Trait("MotifTestLevel", "System")]
public sealed class ParsimonySampleSurveySystemTests
{
    private readonly ITestOutputHelper _output;

    public ParsimonySampleSurveySystemTests(ITestOutputHelper output) => _output = output;

    private static readonly SampleGrammar[] Samples =
    [
        new("Amharic", "amharic.fwdata"),
        new("Awetí", "aweti.fwdata"),
        new("Indonesian", "indonesian.fwdata"),
        new("Mbugwe", "mbugwe.fwdata"),
        new("Sena", "sena.fwdata"),
    ];

    [ParsimonySurveyFact]
    public Task FivePanGlossSampleGrammarsCompleteParsimonySurvey() => RunSampleSurveyAsync(null);

    [ParsimonySurveyFact]
    public Task FivePanGlossSampleGrammarsCompleteAlternationFamilySurvey() =>
        RunSampleSurveyAsync(["P-allo-alternation-family"]);

    private async Task RunSampleSurveyAsync(IReadOnlyCollection<string>? selectedMeasureIds)
    {
        var samplesPath = RequiredPath("MOTIF_PANGLOSS_SAMPLES");
        var parserPath = RequiredPath(PanGlossExecutable.PathVariable);
        var scratch = Path.Combine(Path.GetTempPath(), "motif-parsimony-survey-" + Guid.NewGuid().ToString("N"));
        var outputPath = Path.Combine(scratch, "survey");
        Directory.CreateDirectory(scratch);

        try
        {
            var results = new List<GrammarSurvey>();
            foreach (var sample in Samples)
            {
                var sampleRoot = Path.Combine(scratch, sample.Slug);
                Directory.CreateDirectory(sampleRoot);
                CopySharedDirectory(samplesPath, sampleRoot, "SharedSettings");
                CopySharedDirectory(samplesPath, sampleRoot, "WritingSystemStore");
                var sourceProject = Path.Combine(samplesPath, sample.FileName);
                var projectPath = Path.Combine(sampleRoot, sample.FileName);
                File.Copy(sourceProject, projectPath);
                var projectSha256 = Sha256File(projectPath);

                var projectRoot = Path.Combine(sampleRoot, "motif");
                var workerRoot = Path.Combine(projectRoot, "worker");
                var preferencePath = Path.Combine(projectRoot, "advanced-ai-mode.json");
                Directory.CreateDirectory(projectRoot);
                new FileAdvancedAiModePreferenceStore(preferencePath).SetEnabled(true);

                var capture = await RunMotifAsync(workerRoot, parserPath, preferencePath,
                    "baseline", "capture", projectPath, "--json");
                Assert.True(capture.ExitCode == 0, capture.FailureDetails);

                var measures = MeasureCatalog.All.Where(measure => MeasureRunner.Supports(measure.Id) &&
                    (selectedMeasureIds is null || selectedMeasureIds.Contains(measure.Id, StringComparer.Ordinal)))
                    .ToArray();
                var measureResults = new List<MeasureSurvey>();
                foreach (var measure in measures)
                {
                    var report = await RunMotifAsync(workerRoot, parserPath, preferencePath,
                        "parsimony", "--project", projectPath, "--measure", measure.Id,
                        "--evidence-scope", "project-approved", "--wait", "--wait-timeout-ms", "900000", "--json");
                    Assert.True(report.ExitCode == 0, report.FailureDetails);
                    var response = JsonSerializer.Deserialize<ParsimonyReportResponse>(report.Output,
                        new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    Assert.NotNull(response);
                    AssertDenominatorsArePresent(report.Output, response!.JoinQuality);
                    var run = Assert.Single(response.MeasureRuns, item => item.MeasureId == measure.Id);
                    measureResults.Add(new MeasureSurvey(measure.Id, run.Status.ToString(), run.EligibleItems,
                        run.FindingItems, response.Findings.Select(finding =>
                            $"{finding.FindingId} → {finding.AttachesTo.Identity} " +
                            $"({finding.Number.Numerator}/{finding.Number.Denominator} {finding.Number.Unit})").ToArray(),
                        response.JoinQuality!, measure.Id == "P-allo-alternation-family" ? run.Detail :
                            measure.Id is "B-adhoc-is-slot-order" or "R-env-broad" or "R-nc-excess"
                                ? response.Text
                                : null));
                }

                Assert.Equal(projectSha256, Sha256File(projectPath));
                _output.WriteLine($"{sample.Name} copied project unchanged: sha256={projectSha256}");

                results.Add(new GrammarSurvey(sample.Name, measureResults));
            }

            Directory.CreateDirectory(outputPath);
            var artifact = new SurveyArtifact(results);
            File.WriteAllText(Path.Combine(outputPath, "parsimony-survey.json"),
                JsonSerializer.Serialize(artifact, new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    WriteIndented = true,
                }));
            var markdown = RenderMarkdown(results);
            File.WriteAllText(Path.Combine(outputPath, "parsimony-survey.md"), markdown);
            _output.WriteLine(markdown);
            Assert.True(new FileInfo(Path.Combine(outputPath, "parsimony-survey.json")).Length > 0);
            Assert.True(new FileInfo(Path.Combine(outputPath, "parsimony-survey.md")).Length > 0);
        }
        finally
        {
            try { Directory.Delete(scratch, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task<MotifProcessResult> RunMotifAsync(
        string workerRoot,
        string parserPath,
        string preferencePath,
        params string[] arguments)
    {
        var start = CliProcess.CreateStartInfoWithAdvancedAiModePath(
            workerRoot, parserPath, developerCommands: true, preferencePath, arguments);
        start.Environment[RunnerOptions.NamespaceVariable] =
            "motif-survey-" + Guid.NewGuid().ToString("N");
        start.Environment[RunnerOptions.IdleVariable] = "1";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The Motif CLI did not start.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(20));
        }
        catch (TimeoutException exception)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }

            throw new TimeoutException("The Motif CLI did not finish the sample survey command.", exception);
        }

        return new MotifProcessResult(process.ExitCode, await standardOutput, await standardError);
    }

    private static void AssertDenominatorsArePresent(string json, ParsimonyJoinQuality? joinQuality)
    {
        var quality = Assert.IsType<ParsimonyJoinQuality>(joinQuality);
        using var document = JsonDocument.Parse(json);
        var join = document.RootElement.GetProperty("joinQuality");
        var denominators = new (string Name, long? Value)[]
        {
            ("projectWordforms", quality.ProjectWordforms),
            ("projectJudgedWordforms", quality.ProjectJudgedWordforms),
            ("projectApprovedReadings", quality.ProjectApprovedReadings),
            ("projectDisapprovedReadings", quality.ProjectDisapprovedReadings),
            ("projectCandidateAnalyses", quality.ProjectCandidateAnalyses),
            ("projectLexemes", quality.ProjectLexemes),
            ("projectTextOccurrences", quality.ProjectTextOccurrences),
            ("scopeForms", quality.ScopeForms),
            ("scopeWordforms", quality.ScopeWordforms),
            ("scopeApprovedReadings", quality.ScopeApprovedReadings),
            ("scopeLexemes", quality.ScopeLexemes),
            ("scopeTextOccurrences", quality.ScopeTextOccurrences),
        };
        foreach (var (name, expected) in denominators)
        {
            if (expected is null)
            {
                if (join.TryGetProperty(name, out var unavailable))
                    Assert.Equal(JsonValueKind.Null, unavailable.ValueKind);
                continue;
            }

            Assert.True(join.TryGetProperty(name, out var denominator),
                $"Join quality is missing the '{name}' denominator.");
            Assert.Equal(JsonValueKind.Number, denominator.ValueKind);
            Assert.Equal(expected.Value, denominator.GetInt64());
        }
    }

    private static string RenderMarkdown(IReadOnlyList<GrammarSurvey> grammars)
    {
        var text = new StringBuilder("# Parsimony sample survey\n\n");
        foreach (var grammar in grammars)
        {
            text.Append("## ").Append(grammar.Name).AppendLine().AppendLine();
            text.AppendLine("| Measure | Status | Eligible | Finding items | Finding refs | Project denominators (wordforms / judged / approved / disapproved / candidates / lexemes / text occurrences) | Scope denominators (forms / wordforms / approved / lexemes / text occurrences) |");
            text.AppendLine("|---|---|---:|---:|---|---|---|");
            foreach (var measure in grammar.Measures)
            {
                var join = measure.JoinQuality;
                text.Append("| ").Append(measure.MeasureId).Append(" | ").Append(measure.Status).Append(" | ")
                    .Append(Format(measure.EligibleItems)).Append(" | ").Append(Format(measure.FindingItems)).Append(" | ")
                    .Append(measure.Findings.Count == 0 ? "—" : string.Join("<br>", measure.Findings)).Append(" | ")
                    .Append(join.ProjectWordforms).Append(" / ").Append(join.ProjectJudgedWordforms).Append(" / ")
                    .Append(join.ProjectApprovedReadings).Append(" / ").Append(join.ProjectDisapprovedReadings).Append(" / ")
                    .Append(join.ProjectCandidateAnalyses).Append(" / ").Append(join.ProjectLexemes).Append(" / ")
                    .Append(join.ProjectTextOccurrences).Append(" | ")
                    .Append(Format(join.ScopeForms)).Append(" / ").Append(Format(join.ScopeWordforms)).Append(" / ")
                    .Append(Format(join.ScopeApprovedReadings)).Append(" / ").Append(Format(join.ScopeLexemes)).Append(" / ")
                    .Append(Format(join.ScopeTextOccurrences)).AppendLine(" |");
            }

            text.AppendLine();
            foreach (var measure in grammar.Measures.Where(item => item.ReportText is not null))
            {
                text.Append("### ").Append(measure.MeasureId).AppendLine().AppendLine();
                text.AppendLine("```text");
                text.AppendLine(measure.ReportText);
                text.AppendLine("```").AppendLine();
            }
        }

        return text.ToString();
    }

    private static string Format(long? value) => value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "n/a";

    private static string Sha256File(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
        .ToLowerInvariant();

    private static string RequiredPath(string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{variable} is required to write a Parsimony sample survey.");
        return Path.GetFullPath(value);
    }

    private static void CopySharedDirectory(string sourceRoot, string destinationRoot, string name)
    {
        var source = Path.Combine(sourceRoot, name);
        if (!Directory.Exists(source)) return;
        CopyDirectory(source, Path.Combine(destinationRoot, name));
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private sealed record SampleGrammar(string Name, string FileName)
    {
        public string Slug => Path.GetFileNameWithoutExtension(FileName);
    }

    private sealed record MotifProcessResult(int ExitCode, string Output, string Error)
    {
        public string FailureDetails =>
            $"Motif CLI exited {ExitCode}.{Environment.NewLine}Standard error:{Environment.NewLine}{Error}" +
            $"{Environment.NewLine}Standard output:{Environment.NewLine}{Output}";
    }

    private sealed record MeasureSurvey(string MeasureId, string Status, long? EligibleItems, long? FindingItems,
        IReadOnlyList<string> Findings, ParsimonyJoinQuality JoinQuality, string? ReportText);

    private sealed record GrammarSurvey(string Name, IReadOnlyList<MeasureSurvey> Measures);

    private sealed record SurveyArtifact(IReadOnlyList<GrammarSurvey> Grammars);
}

public sealed class ParsimonySurveyFactAttribute : FactAttribute
{
    public ParsimonySurveyFactAttribute()
    {
        var missing = new[] { "MOTIF_PANGLOSS_SAMPLES", PanGlossExecutable.PathVariable }
            .Where(variable => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)))
            .ToArray();
        if (missing.Length > 0)
            Skip = $"Parsimony sample survey requires {string.Join(" and ", missing)}.";
    }
}
