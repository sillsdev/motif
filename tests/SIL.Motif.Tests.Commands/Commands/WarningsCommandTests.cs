using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class WarningsCommandTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.WarningsCommandTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void KindAndLeftOutFilterTheStoredFindings()
    {
        var project = CheckedProject();

        var all = WarningsCommand.Warnings(new WarningsRequest(project));
        var kind = WarningsCommand.Warnings(new WarningsRequest(project, Kind: "hc-unused-rule"));
        var leftOut = WarningsCommand.Warnings(new WarningsRequest(project, LeftOut: true));

        Assert.True(all.Succeeded, all.Refusal?.Message);
        Assert.Equal(4, all.Value!.TotalCount);
        Assert.Equal(2, all.Value.ErrorCount);
        Assert.Equal(1, all.Value.WarningCount);
        Assert.Equal(1, all.Value.InformationCount);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(all.Value));
        Assert.Equal(2, json.RootElement.GetProperty("ErrorCount").GetInt32());
        Assert.Equal("error", json.RootElement.GetProperty("Findings")[0].GetProperty("Severity").GetString());
        Assert.Equal("hc-unused-rule", Assert.Single(kind.Value!.Findings).Code);
        Assert.All(leftOut.Value!.Findings, finding => Assert.Equal("warning", finding.Severity.ToWireValue()));
        Assert.Single(leftOut.Value.Findings);
    }

    [Fact]
    public void OverviewCountsMatchTheUnfilteredWarningsCommand()
    {
        var project = CheckedProject();

        var warnings = WarningsCommand.Warnings(new WarningsRequest(project));
        var overview = OverviewCommand.Overview(new OverviewRequest(project));

        Assert.True(overview.Succeeded, overview.Refusal?.Message);
        Assert.Equal(warnings.Value!.TotalCount, overview.Value!.Warnings!.Count);
        Assert.Equal(warnings.Value.WarningCount, overview.Value.Warnings.WarningCount);
        Assert.Equal(warnings.Value.InformationCount, overview.Value.Warnings.InformationCount);
        Assert.Equal(warnings.Value.ErrorCount, overview.Value.Warnings.ErrorCount);
        Assert.Equal(ProjectionJson.Serialize(warnings.Value.ByKind), ProjectionJson.Serialize(overview.Value.Warnings.ByKind));
    }

    [Fact]
    public void StoredAnalysisNoLongerParsesFindingIsShownAsInformation()
    {
        Directory.CreateDirectory(_root);
        var project = pristine.CopyProjectFile();
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project), _root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var reportPath = Path.Combine(AppContext.BaseDirectory, "TestFixtures", "GrammarHealth",
            "schema-v4-stored-analysis-no-longer-parses.json");
        var report = File.ReadAllText(reportPath);

        var checkedNow = GrammarCheckQuery.Query(new GrammarCheckRequest(project), new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(report, string.Empty, TimeSpan.Zero),
        }, CancellationToken.None, parserStamp: "build-1");

        Assert.True(checkedNow.Succeeded, checkedNow.Refusal?.Message);
        var finding = Assert.Single(checkedNow.Value!.Findings);
        Assert.Equal("grammar.stored-analysis.no-longer-parses", finding.Code);
        Assert.Equal("Stored analysis changed", finding.Title);
        Assert.Equal(SIL.Motif.Contract.Responses.GrammarDiagnosticLevel.Information, finding.Severity);
        Assert.Contains("forward-synthesis trace", finding.Description, StringComparison.Ordinal);
        Assert.Contains("Check whether the named rule is meant to apply", finding.Guidance, StringComparison.Ordinal);
        Assert.Contains(finding.Subject, subject => subject.Title == "muma");
        Assert.Contains(finding.Subject, subject => subject.Title == "mu");
        Assert.Contains(finding.Subject, subject => subject.Title == "probe-rewrite");

        var warnings = WarningsCommand.Warnings(new WarningsRequest(project));

        Assert.True(warnings.Succeeded, warnings.Refusal?.Message);
        Assert.Equal(1, warnings.Value!.TotalCount);
        Assert.Equal(1, warnings.Value.InformationCount);
        Assert.Equal(0, warnings.Value.WarningCount);
        Assert.Equal(0, warnings.Value.ErrorCount);
        Assert.Equal("grammar.stored-analysis.no-longer-parses", Assert.Single(warnings.Value.Findings).Code);
    }

    private string CheckedProject()
    {
        Directory.CreateDirectory(_root);
        var project = pristine.CopyProjectFile();
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project), _root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var checkedNow = GrammarCheckQuery.Query(new GrammarCheckRequest(project), new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(Report, string.Empty, TimeSpan.Zero),
        }, CancellationToken.None, parserStamp: "build-1");
        Assert.True(checkedNow.Succeeded, checkedNow.Refusal?.Message);
        return project;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private const string Report = """
        {
          "schema_version": 4,
          "fieldworks_project": {
            "name": null,
            "source": null
          },
          "summary": [
            {
              "code": "hc-invalid-feature-system",
              "group_name": "Invalid feature system",
              "level": "error",
              "count": 1
            },
            {
              "code": "hc-unused-rule",
              "group_name": "Unused rule",
              "level": "warning",
              "count": 1
            },
            {
              "code": "hc-undeclared-segment",
              "group_name": "Undeclared segment",
              "level": "info",
              "count": 1
            },
            {
              "code": "fwdata.no-usable-allomorphs",
              "group_name": "No usable entry allomorphs",
              "level": "error",
              "count": 1
            }
          ],
          "diagnostics": [
            {
              "level": "error",
              "code": "hc-invalid-feature-system",
              "group_name": "Invalid feature system",
              "origin": "check",
              "description": "A feature system could not be loaded.",
              "guidance": null,
              "subjects": [],
              "title": "Invalid feature system",
              "explanation": "A feature system could not be loaded.",
              "help_path": null,
              "help_body": null,
              "fieldworks_places": [],
              "scope": "project_settings"
            },
            {
              "level": "warning",
              "code": "hc-unused-rule",
              "group_name": "Unused rule",
              "origin": "check",
              "description": "Rule x is unused.",
              "guidance": null,
              "subjects": [],
              "title": "Unused rule",
              "explanation": "Rule x is unused.",
              "help_path": null,
              "help_body": null,
              "fieldworks_places": [],
              "scope": "project_settings"
            },
            {
              "level": "info",
              "code": "hc-undeclared-segment",
              "group_name": "Undeclared segment",
              "origin": "check",
              "description": "Segment x is undeclared.",
              "guidance": null,
              "subjects": [],
              "title": "Undeclared segment",
              "explanation": "Segment x is undeclared.",
              "help_path": null,
              "help_body": null,
              "fieldworks_places": [],
              "scope": "project_settings"
            },
            {
              "level": "error",
              "code": "fwdata.no-usable-allomorphs",
              "group_name": "No usable entry allomorphs",
              "origin": "import",
              "description": "The entry has no usable allomorph.",
              "guidance": null,
              "subjects": [],
              "title": "No usable entry allomorphs",
              "explanation": "The entry has no usable allomorph.",
              "help_path": null,
              "help_body": null,
              "fieldworks_places": [],
              "scope": "project_settings"
            }
          ],
          "locale": "en"
        }
        """;
}
