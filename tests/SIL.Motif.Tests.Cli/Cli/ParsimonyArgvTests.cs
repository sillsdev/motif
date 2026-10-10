using System.Diagnostics;
using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Preferences;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class ParsimonyArgvTests : IDisposable
{
    private readonly PristineProjectFixture _pristine;
    private readonly string _root = Path.Combine(Path.GetTempPath(), nameof(ParsimonyArgvTests),
        Guid.NewGuid().ToString("N"));
    private readonly string _workerRoot;
    private readonly string _project;
    private readonly string _preferencePath;

    public ParsimonyArgvTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        Directory.CreateDirectory(_root);
        _workerRoot = Path.Combine(_root, "worker");
        _project = Path.Combine(_root, "sample.fwdata");
        _preferencePath = Path.Combine(_root, "advanced-ai-mode.json");
        File.WriteAllText(_project, "<languageproject/>");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task WaitStoresAndReturnsTheBaselineReportAsJson()
    {
        new FileAdvancedAiModePreferenceStore(_preferencePath).SetEnabled(true);
        var project = _pristine.CopyProjectFile();
        var captureStart = StartWithParser(_preferencePath, null,
            "baseline", "capture", project, "--json");
        var captured = await CliProcess.RunAsync(captureStart);
        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.Error);

        var parserPath = FactsParser();
        var start = StartWithParser(_preferencePath, parserPath,
            "parsimony", "--project", project, "--measure", "P-adhoc-duplicate", "--evidence-scope",
            "project-approved", "--wait", "--json");
        var result = await CliProcess.RunAsync(start);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        Assert.Empty(result.Error);
        var response = JsonSerializer.Deserialize<ParsimonyReportResponse>(result.Output,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(response);
        Assert.Equal("baseline", response!.Inputs.InputKind);
        Assert.Equal(ParsimonyEvidenceScopeKind.ProjectApproved, response.Inputs.EvidenceScope);
        Assert.NotNull(response.JoinQuality);
        Assert.Equal("project-approved", response.JoinQuality.Scope);
        Assert.Empty(response.AssessmentIds);
        Assert.Empty(response.Inputs.AssessmentIds);
        Assert.Equal(2, response.Findings.Count);
        Assert.All(response.Findings, finding =>
        {
            Assert.Equal(2, finding.Number.Numerator);
            Assert.Equal(3, finding.Number.Denominator);
        });
        Assert.DoesNotContain(response.Findings,
            finding => finding.AttachesTo.Identity == "00000000-0000-0000-0000-000000000003");

        var latest = await CliProcess.RunAsync(StartWithParser(_preferencePath, null,
            "parsimony", "latest", "--project", project, "--json"));
        Assert.True(latest.ExitCode == 0, latest.Error + latest.Output);
        var named = JsonSerializer.Deserialize<ParsimonyLatestReportResponse>(latest.Output,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(response.ReportId, named!.ReportId);
        Assert.Equal(response.Inputs.BundleId, named.BundleId);
        Assert.Equal(new[] { "describe", "import", "facts" }, FakeParser.Invocations(parserPath));

        var activeView = StartWithParser(_preferencePath, Path.Combine(_root, "parser-not-installed"),
            "parsimony", "view", "parsimony-active-findings", "--project", project, "--bundle",
            response.Inputs.BundleId, "--report", response.ReportId, "--limit", "1", "--json");
        var viewResult = await CliProcess.RunAsync(activeView);

        Assert.True(viewResult.ExitCode == 0, viewResult.Error + viewResult.Output);
        Assert.Empty(viewResult.Error);
        var view = JsonSerializer.Deserialize<ParsimonyNamedViewResponse>(viewResult.Output,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(view);
        Assert.Equal(response.ReportId, Assert.IsType<ParsimonyFindingDispositionViewRow>(
            Assert.Single(view!.Rows)).ReportId);
        Assert.Equal(1, view.Returned);
        Assert.True(view.Truncated);
        Assert.NotNull(view.NextCursor);
        Assert.Equal(new[] { "describe", "import", "facts" }, FakeParser.Invocations(parserPath));

        var show = StartWithParser(_preferencePath, Path.Combine(_root, "parser-now-absent"),
            "parsimony", "show", "--project", project, response.ReportId, "--json");
        var shown = await CliProcess.RunAsync(show);

        Assert.Equal(0, shown.ExitCode);
        Assert.Empty(shown.Error);
        var stored = JsonSerializer.Deserialize<ParsimonyReportResponse>(shown.Output,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(stored);
        Assert.Equal(response.ReportId, stored!.ReportId);
        Assert.Equal(response.Text, stored.Text);
        Assert.Equal(response.Findings.Select(finding => finding.FindingId),
            stored.Findings.Select(finding => finding.FindingId));
        Assert.Equal(new[] { "describe", "import", "facts" }, FakeParser.Invocations(parserPath));

        var parserCaseView = StartWithParser(_preferencePath, Path.Combine(_root, "parser-now-absent"),
            "parsimony", "view", "parser-cases", "--project", project, "--bundle",
            response.Inputs.BundleId, "--report", response.ReportId, "--json");
        var viewed = await CliProcess.RunAsync(parserCaseView);

        Assert.True(viewed.ExitCode == 0, $"{viewed.Error}{viewed.Output}");
        Assert.Empty(viewed.Error);
        var parserCases = JsonSerializer.Deserialize<ParsimonyNamedViewResponse>(viewed.Output,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(parserCases);
        Assert.Equal("parser-cases", parserCases!.View);
        Assert.Equal(ParsimonyMeasureStatus.NotAvailable, parserCases.Status);
        Assert.Empty(parserCases.Rows);
        Assert.Equal(new[] { "describe", "import", "facts" }, FakeParser.Invocations(parserPath));
    }

    [Fact]
    public async Task ParsimonyCommandRefusesWhenAdvancedAiModeIsOff()
    {
        new FileAdvancedAiModePreferenceStore(_preferencePath).SetEnabled(false);
        var start = StartWithParser(_preferencePath, "missing-pangloss",
            "parsimony", "--project", Path.Combine(_root, "missing.fwdata"), "--json");

        var result = await CliProcess.RunAsync(start);

        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Output);
        using var refusal = JsonDocument.Parse(result.Error);
        Assert.Equal("command.advanced-ai-mode-required", refusal.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ShowReadsTheStoredReportWhenNoParserIsAvailable()
    {
        new FileAdvancedAiModePreferenceStore(_preferencePath).SetEnabled(true);
        var reportId = CanonicalId.Mint("report/").Value;
        var token = new BaselineToken("project", "sha256:" + new string('a', 64), "1",
            "2026-10-05T00:00:00Z", "sha256:" + new string('b', 64));
        var inputs = new ParsimonyReportInputs("bundle-1", token, "baseline", null, "fingerprint-1",
            new ParsimonyArtifactDigest(4, new string('c', 64)),
            new ParsimonyArtifactDigest(1, new string('d', 64)), null, null, []);
        var response = new ParsimonyReportResponse(reportId, inputs, [], "Stored Parsimony report text.")
        {
            Notes = [],
            JoinQuality = new ParsimonyJoinQuality("default-selection", false, 0, 0, 0, 0, 0, 0, 0,
                null, null, null, null, null),
        };
        var reportJson = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var saved = ProjectStoreCommand.Run<string>(_project, "1.0", (database, _) =>
        {
            new ReportRepository(database).Save(new ReportRecord(reportId, null, null, reportJson,
                "{}", "parsimony", response.Text));
            return CommandOutcome<string>.Success("saved");
        });
        Assert.True(saved.Succeeded);

        var start = StartWithParser(_preferencePath, Path.Combine(_root, "parser-not-installed"),
            "parsimony", "show", "--project", _project, reportId, "--json");
        var result = await CliProcess.RunAsync(start);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        var shown = JsonSerializer.Deserialize<ParsimonyReportResponse>(result.Output,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(shown);
        Assert.Equal(reportId, shown!.ReportId);
        Assert.Equal("Stored Parsimony report text.", shown.Text);
        Assert.Empty(shown.AssessmentIds);
    }

    private string FactsParser()
    {
        var parserPath = FakeParser.CopyRecordingInvocations(Path.Combine(_root, "parser-for-wait"));
        var primary = _pristine.Seed.FirstLexemeFormId.ToString("D").ToLowerInvariant();
        var other = _pristine.Seed.SecondLexemeFormId.ToString("D").ToLowerInvariant();
        FakeParser.WriteFactsFixture(parserPath, new
        {
            prohibitions = new[]
            {
                Prohibition("00000000-0000-0000-0000-000000000001", primary, other, "somewhereToLeft"),
                Prohibition("00000000-0000-0000-0000-000000000002", primary, other, "somewhereToLeft"),
                Prohibition("00000000-0000-0000-0000-000000000004", primary, other, "somewhereToLeft"),
                Prohibition("00000000-0000-0000-0000-000000000003", primary, other, "adjacentToLeft"),
                Prohibition("00000000-0000-0000-0000-000000000005", primary, other, "somewhereToLeft",
                    disabled: true),
                Prohibition("00000000-0000-0000-0000-000000000006", primary, other, "somewhereToLeft",
                    loaded: null),
            },
        });
        return parserPath;
    }

    private static object Prohibition(string id, string primary, string other, string adjacency,
        bool disabled = false, bool? loaded = true) => new
    {
        prohibitionGuid = id,
        kind = "allomorph",
        disabled,
        adjacency,
        primaryGuid = primary,
        targetKind = "allomorph",
        others = new[] { new { targetGuid = other, targetKind = "allomorph" } },
        loaded,
    };

    [Fact]
    public async Task LatestRefusesWithNoReportYetOnAProjectWithoutOne()
    {
        new FileAdvancedAiModePreferenceStore(_preferencePath).SetEnabled(true);
        var project = _pristine.CopyProjectFile();

        var result = await CliProcess.RunAsync(StartWithParser(_preferencePath, null,
            "parsimony", "latest", "--project", project));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("No Parsimony Report yet.", result.Error + result.Output, StringComparison.Ordinal);
    }

    private ProcessStartInfo StartWithParser(string preferencePath, string? parserPath, params string[] args) =>
        CliProcess.CreateStartInfoWithAdvancedAiModePath(_workerRoot, parserPath, false,
            preferencePath, args);
}
