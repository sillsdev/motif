using System;
using System.IO;
using System.Linq;
using System.Threading;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Pins <see cref="StoredGrammarCheckQuery"/>: it answers with the grammar check stored for the current
/// Baseline, says when there is none, and never runs the parser itself.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class StoredGrammarCheckQueryTests : IDisposable
{
    private readonly PristineProjectFixture _pristine;
    private readonly string _managedRootsParent =
        Path.Combine(Path.GetTempPath(), "SIL.Motif.StoredGrammarCheckQueryTests", Guid.NewGuid().ToString("N"));

    public StoredGrammarCheckQueryTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        Directory.CreateDirectory(_managedRootsParent);
    }

    public void Dispose()
    {
        try { Directory.Delete(_managedRootsParent, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void AStoredCheckOfTheCurrentBaselineIsReturnedWithoutRunningTheParser()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(
                ReportWithOneFinding,
                string.Empty, TimeSpan.Zero),
        };
        var request = new GrammarCheckRequest(fwDataPath);
        var checkedNow = GrammarCheckQuery.Query(request, invoker, CancellationToken.None, parserStamp: "build-1");
        Assert.True(checkedNow.Succeeded, checkedNow.Refusal?.Message);

        var stored = StoredGrammarCheckQuery.Query(request);

        Assert.True(stored.Succeeded, stored.Refusal?.Message);
        Assert.Equal(checkedNow.Value!.Findings.Single().Text, stored.Value!.Check!.Findings.Single().Text);
        Assert.Single(invoker.Requests);
    }

    [Fact]
    public void ASecondCheckOfTheSameBaselineReplacesTheFirstAfterSelectionChanges()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var request = new GrammarCheckRequest(fwDataPath);
        var firstSelection = SelectionCommands.SetDefault(new SetDefaultSelectionRequest(
            fwDataPath, "First", [], ["first"]));
        Assert.True(firstSelection.Succeeded, firstSelection.Refusal?.Message);
        var first = GrammarCheckQuery.Query(request, new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(ReportWithOneFinding, string.Empty, TimeSpan.Zero),
        }, CancellationToken.None, parserStamp: "build-1");
        Assert.True(first.Succeeded, first.Refusal?.Message);
        var firstRow = ReadGrammarCheckRow(fwDataPath);
        Assert.True(firstRow.Succeeded, firstRow.Refusal?.Message);

        var secondSelection = SelectionCommands.SetDefault(new SetDefaultSelectionRequest(
            fwDataPath, "Second", [], ["second"]));
        Assert.True(secondSelection.Succeeded, secondSelection.Refusal?.Message);
        var second = GrammarCheckQuery.Query(request, new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(EmptyReport, string.Empty, TimeSpan.Zero),
        }, CancellationToken.None, parserStamp: "build-2");
        Assert.True(second.Succeeded, second.Refusal?.Message);

        var stored = StoredGrammarCheckQuery.Query(request);
        Assert.True(stored.Succeeded, stored.Refusal?.Message);
        Assert.Empty(stored.Value!.Check!.Findings);
        var rows = ReadGrammarCheckRow(fwDataPath);
        Assert.True(rows.Succeeded, rows.Refusal?.Message);
        Assert.Equal(1, rows.Value!.Count);
        Assert.NotEqual(firstRow.Value!.SelectionSha256, rows.Value.SelectionSha256);
    }

    private static CommandOutcome<GrammarCheckRows> ReadGrammarCheckRow(string fwDataPath) =>
        ProjectStoreCommand.Run(fwDataPath, MotifProductVersion.CurrentText, (database, _) =>
        {
            using var connection = database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*), MAX(SelectionSha256) FROM GrammarChecks;";
            using var reader = command.ExecuteReader();
            reader.Read();
            return CommandOutcome<GrammarCheckRows>.Success(new(reader.GetInt32(0), reader.GetString(1)));
        });

    private sealed record GrammarCheckRows(int Count, string SelectionSha256);

    [Fact]
    public void StoredFindingsSurviveRemovalOfTheBaselineSideCache()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var request = new GrammarCheckRequest(fwDataPath);
        var checkedNow = GrammarCheckQuery.Query(request, new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(ReportWithOneFinding, string.Empty, TimeSpan.Zero),
        }, CancellationToken.None, parserStamp: "build-1");
        Assert.True(checkedNow.Succeeded, checkedNow.Refusal?.Message);

        foreach (var path in Directory.GetFiles(_managedRootsParent, "grammar-check.json", SearchOption.AllDirectories))
            File.Delete(path);

        var stored = StoredGrammarCheckQuery.Query(request);

        Assert.True(stored.Succeeded, stored.Refusal?.Message);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(checkedNow.Value),
            System.Text.Json.JsonSerializer.Serialize(stored.Value!.Check));
    }

    [Fact]
    public void StoredFindingsDoNotRequireTheParserStamp()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var request = new GrammarCheckRequest(fwDataPath);
        GrammarCheckQuery.Query(request, new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(EmptyReport, string.Empty, TimeSpan.Zero),
        }, CancellationToken.None, parserStamp: "build-1");

        Assert.NotNull(StoredGrammarCheckQuery.Query(request).Value!.Check);
    }

    [Fact]
    public void ABaselineNeverCheckedIsNotCheckedYet()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);

        var stored = StoredGrammarCheckQuery.Query(new GrammarCheckRequest(fwDataPath));

        Assert.True(stored.Succeeded, stored.Refusal?.Message);
        Assert.Null(stored.Value!.Check);
    }

    [Fact]
    public void NoBaselineIsAnEmptyCheckThatSaysSo()
    {
        var fwDataPath = _pristine.CopyProjectFile();

        var stored = StoredGrammarCheckQuery.Query(new GrammarCheckRequest(fwDataPath));

        Assert.True(stored.Succeeded, stored.Refusal?.Message);
        Assert.False(stored.Value!.Check!.HasBaseline);
    }

    private const string ReportWithOneFinding = """
        {
          "schema_version": 3,
          "fieldworks_project": { "name": null, "source": null },
          "summary": [
            { "code": "hc-undeclared-segment", "group_name": "Undeclared segment", "level": "info", "count": 1 }
          ],
          "diagnostics": [
            {
              "level": "info",
              "code": "hc-undeclared-segment",
              "group_name": "Undeclared segment",
              "origin": "check",
              "description": "Segment x is undeclared.",
              "guidance": null,
              "subjects": []
            }
          ]
        }
        """;

    private const string EmptyReport = """
        {
          "schema_version": 3,
          "fieldworks_project": { "name": null, "source": null },
          "summary": [],
          "diagnostics": []
        }
        """;

    private void Capture(string fwDataPath)
    {
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), NewManagedRoot());
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
    }

    private string NewManagedRoot()
    {
        var root = Path.Combine(_managedRootsParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
