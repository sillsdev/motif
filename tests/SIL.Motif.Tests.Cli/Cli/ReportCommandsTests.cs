using Microsoft.Data.Sqlite;
using SIL.Motif.Host.Assess;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using SIL.Motif.Cli.Rendering;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>Checks Report handlers over a paired store, including refusal and persisted rendering.</summary>
public sealed class ReportCommandsTests : IDisposable
{
    private const string ProductVersion = "1.0";
    private const string GrammarSha = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "motif-report-cmd-" + Guid.NewGuid().ToString("N"));
    private readonly string _project;

    public ReportCommandsTests()
    {
        Directory.CreateDirectory(_root);
        _project = Path.Combine(_root, "sample.fwdata");
        File.WriteAllText(_project, "<languageproject/>");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void ListKinds_ListsCoverageAndApprovedMorphologyCorrectness()
    {
        var result = ReportCommands.ListKinds(new ListReportKindsRequest());

        Assert.True(result.Succeeded);
        Assert.Contains(result.Value!.Kinds, kind => kind.Kind == "coverage");
        Assert.Contains(result.Value.Kinds, kind => kind.Kind == "correctness");
        Assert.Contains("coverage  ", CommandTextRenderer.Render(result, asJson: false).Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AskingForAKindOutsideTheRegistry_Refuses()
    {
        var assessmentId = RecordAssessment("ParseTime", ("a", true), ("b", false));

        var result = ReportCommands.Produce(
            new ProduceReportRequest(_project, ProductVersion, assessmentId, "nonsense-kind", null, null));

        Assert.False(result.Succeeded);
        Assert.Equal("report.invalid-kind", result.Refusal!.Code);
        Assert.Equal("nonsense-kind", result.Refusal.Facts["kind"]);
    }

    [Fact]
    public void ACorrectnessReportOverAParseTimeAssessment_RefusesNamingTheReason()
    {
        var assessmentId = RecordAssessment("ParseTime", ("a", true), ("b", false));

        var result = ReportCommands.Produce(
            new ProduceReportRequest(_project, ProductVersion, assessmentId, "correctness", null, null));

        Assert.False(result.Succeeded);
        Assert.Equal("report.refused", result.Refusal!.Code);
        Assert.Equal(FailureReason.Refused, result.Refusal.Reason);
        Assert.Contains("requires 'Correctness'", result.Refusal.Message, StringComparison.Ordinal);
        Assert.Contains("ParseTime", result.Refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACorrectnessReportWithoutMaterial_RefusesNamingTheMissingAssessmentCapability()
    {
        var result = ReportCommands.Produce(
            new ProduceReportRequest(_project, ProductVersion, null, "correctness", null, null));

        Assert.False(result.Succeeded);
        Assert.Equal("report.refused", result.Refusal!.Code);
        Assert.Contains("Assessment", result.Refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GenericReportCommandCannotProduceParsimonyWithoutFrozenInputs()
    {
        var result = ReportCommands.Produce(
            new ProduceReportRequest(_project, ProductVersion, null, "parsimony", null, null));

        Assert.False(result.Succeeded);
        Assert.Equal("report.refused", result.Refusal!.Code);
        Assert.Contains("frozen Baseline evidence bundle", result.Refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACoverageReport_IsComputedStoredAndReadableWithNoAssessorAnywhereInTheProcess()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var assessmentId = RecordAssessment("ParseTime", ("motifa", true), ("motifb", false));

        var jsonResult = ReportCommands.Produce(
            new ProduceReportRequest(_project, ProductVersion, assessmentId, "coverage", null, null));
        Assert.True(jsonResult.Succeeded);
        var response = jsonResult.Value!;
        Assert.Equal("coverage", response.Kind);
        Assert.Contains("50.0%", response.Text, StringComparison.Ordinal);

        // Read the stored row back through a fresh handle, bypassing ReportCommands and any Assessor.
        var stored = ReadStoredReport(response.ReportId);
        Assert.NotNull(stored);
        Assert.Equal("coverage", stored!.Kind);
        Assert.Contains("50.0%", stored.RenderedText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("older")]
    [InlineData("newer")]
    public void ReportingOnAnAssessmentLeavesAMissingOlderOrNewerStoreUnchanged(string state)
    {
        var storePath = ProjectDatabaseCatalog.DatabasePathFor(
            new ProjectLocator(_project, Path.GetFileNameWithoutExtension(_project)));
        if (state != "missing")
        {
            Assert.True(ProjectStoreCommand.Run(_project, ProductVersion,
                (_, _) => CommandOutcome<string>.Success("created")).Succeeded);
            using var connection = new SqliteConnection($"Data Source={storePath};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA user_version = {MotifSchema.CurrentSchema + (state == "older" ? -1 : 1)};";
            command.ExecuteNonQuery();
        }
        string[] StoreFiles() => Directory.GetFiles(_root).Where(path =>
            !path.EndsWith("-wal", StringComparison.Ordinal) && !path.EndsWith("-shm", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToArray();
        var before = StoreFiles().ToDictionary(path => path, File.ReadAllBytes);

        var result = ReportCommands.Produce(new ProduceReportRequest(
            _project, ProductVersion, CanonicalId.Mint("assessment/").Value, "coverage", null, null));

        Assert.False(result.Succeeded);
        Assert.Equal(state == "missing" ? "report.assessment-not-found" : "store.other-version", result.Refusal!.Code);
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), StoreFiles());
        foreach (var file in before) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
        Assert.Equal(state != "missing", File.Exists(storePath));
    }

    private string RecordAssessment(string kind, params (string Word, bool Analysed)[] words)
    {
        var selection = Selection.Create("test", words.Select(w => w.Word));
        var assessmentId = CanonicalId.Mint("assessment/").Value;
        var assessedWords = words
            .Select(w => new AssessedWord(
                w.Word, w.Analysed ? "analysed" : "no-analysis",
                w.Analysed
                    ? new[] { new ParsedAnalysis(null, Array.Empty<string>(), 0, "digest") }
                    : Array.Empty<ParsedAnalysis>()))
            .ToArray();

        var result = ProjectStoreCommand.Run<string>(_project, ProductVersion, (database, _) =>
        {
            new AssessmentRepository(database).Record(new NewAssessmentRecord(
                AssessmentId: assessmentId,
                ProposalId: null,
                ProposalIntentDigest: null,
                Assessor: "pangloss",
                Kind: kind,
                ScopeJson: """{"words":[],"collect":[],"perWordLimitMs":1000,"perWordParserWorkLimit":{"steps":200000,"isUnbounded":false}}""",
                ScopeDigest: "sha256:" + new string('a', 64),
                Subject: new AssessmentSubject.Words(selection, "none", "1"),
                BaselineToken: "{}",
                OutcomeDigest: "sha256:" + new string('b', 64),
                SemanticDigest: "sha256:" + new string('c', 64),
                GrammarSourceSha256: GrammarSha,
                ModelFingerprint: "model",
                Pipeline: "pipeline",
                DiagnosticCount: 0,
                Material: new AssessmentRecordMaterial.Words(assessedWords, null, null, [])));
            return CommandOutcome<string>.Success(string.Empty);
        });
        Assert.True(result.Succeeded);
        return assessmentId;
    }

    private ReportRecord? ReadStoredReport(string reportId)
    {
        ReportRecord? stored = null;
        var result = ProjectStoreCommand.Run<string>(_project, ProductVersion, (database, _) =>
        {
            stored = new ReportRepository(database).Get(reportId);
            return CommandOutcome<string>.Success(string.Empty);
        });
        Assert.True(result.Succeeded);
        return stored;
    }
}
