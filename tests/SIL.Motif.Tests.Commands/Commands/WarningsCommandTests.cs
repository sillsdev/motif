using System;
using System.IO;
using System.Linq;
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

[Collection(LcmCacheTestCollection.Name)]
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
        Assert.Equal(2, all.Value!.TotalCount);
        Assert.Equal(1, all.Value.WarningCount);
        Assert.Equal(1, all.Value.InformationCount);
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
        Assert.Equal(warnings.Value.ByKind, overview.Value.Warnings.ByKind);
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
          "schema_version": 2,
          "fieldworks_project": { "name": null, "source": null },
          "summary": [
            { "code": "hc-unused-rule", "group_name": "Unused rule", "level": "warning", "count": 1 },
            { "code": "hc-undeclared-segment", "group_name": "Undeclared segment", "level": "info", "count": 1 }
          ],
          "diagnostics": [
            { "level": "warning", "code": "hc-unused-rule", "group_name": "Unused rule",
              "origin": "check", "description": "Rule x is unused.", "guidance": null, "subjects": [] },
            { "level": "info", "code": "hc-undeclared-segment", "group_name": "Undeclared segment",
              "origin": "check", "description": "Segment x is undeclared.", "guidance": null, "subjects": [] }
          ]
        }
        """;
}
