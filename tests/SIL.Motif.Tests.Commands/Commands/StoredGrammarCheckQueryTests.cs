using System;
using System.IO;
using System.Linq;
using System.Threading;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Pins <see cref="StoredGrammarCheckQuery"/>: it answers with the grammar check stored for the current
/// Baseline by the same parser, says when there is none, and never runs the parser itself.
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

        var stored = StoredGrammarCheckQuery.Query(request, parserStamp: "build-1");

        Assert.True(stored.Succeeded, stored.Refusal?.Message);
        Assert.Equal(checkedNow.Value!.Findings.Single().Text, stored.Value!.Check!.Findings.Single().Text);
        Assert.Single(invoker.Requests);
    }

    [Fact]
    public void NothingStoredForThisParserMeansNotCheckedYet()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var request = new GrammarCheckRequest(fwDataPath);
        GrammarCheckQuery.Query(request, new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(EmptyReport, string.Empty, TimeSpan.Zero),
        }, CancellationToken.None, parserStamp: "build-1");

        Assert.Null(StoredGrammarCheckQuery.Query(request, parserStamp: "build-2").Value!.Check);
        Assert.Null(StoredGrammarCheckQuery.Query(request, parserStamp: null).Value!.Check);
    }

    [Fact]
    public void ABaselineNeverCheckedIsNotCheckedYet()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);

        var stored = StoredGrammarCheckQuery.Query(new GrammarCheckRequest(fwDataPath), parserStamp: "build-1");

        Assert.True(stored.Succeeded, stored.Refusal?.Message);
        Assert.Null(stored.Value!.Check);
    }

    [Fact]
    public void TheStoredReadStampsTheParserExactlyAsTheCheckDoes()
    {
        static string? StampOf(Type query) => (string?)query
            .GetMethod("ParserStamp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, null);

        Assert.Equal(StampOf(typeof(GrammarCheckQuery)), StampOf(typeof(StoredGrammarCheckQuery)));
    }

    [Fact]
    public void NoBaselineIsAnEmptyCheckThatSaysSo()
    {
        var fwDataPath = _pristine.CopyProjectFile();

        var stored = StoredGrammarCheckQuery.Query(new GrammarCheckRequest(fwDataPath), parserStamp: "build-1");

        Assert.True(stored.Succeeded, stored.Refusal?.Message);
        Assert.False(stored.Value!.Check!.HasBaseline);
    }

    private const string ReportWithOneFinding = """
        {
          "schema_version": 2,
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
          "schema_version": 2,
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
