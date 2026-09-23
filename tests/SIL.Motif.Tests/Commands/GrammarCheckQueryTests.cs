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
/// Pins <see cref="GrammarCheckQuery"/> against a fake <see cref="IPanGlossInvoker"/>: no Baseline is a
/// successful, empty answer; a Baseline runs <c>grammar-health</c> and turns its stderr load warnings and
/// its findings file into <see cref="SIL.Motif.Contract.Responses.GrammarWarning"/>s; a declined parser is
/// a typed Refusal, never an exception.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class GrammarCheckQueryTests : IDisposable
{
    private readonly PristineProjectFixture _pristine;
    private readonly string _managedRootsParent =
        Path.Combine(Path.GetTempPath(), "SIL.Motif.GrammarCheckQueryTests", Guid.NewGuid().ToString("N"));

    public GrammarCheckQueryTests(PristineProjectFixture pristine)
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
    public void NoBaselineIsASuccessfulEmptyAnswer_AndNeverReachesTheParser()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var invoker = new FakeInvoker
        {
            Respond = _ => throw new InvalidOperationException("Must not reach the parser without a Baseline."),
        };

        var outcome = GrammarCheckQuery.Query(new GrammarCheckRequest(fwDataPath), invoker, CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.False(outcome.Value!.HasBaseline);
        Assert.Empty(outcome.Value.Findings);
        Assert.Empty(invoker.Requests);
    }

    [Fact]
    public void ABaselineRunsGrammarHealth_AndMergesLoadWarningsWithFindings()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var invoker = new FakeInvoker
        {
            Respond = request => request is PanGlossRequest.GrammarHealth
                ? new PanGlossOutcome.Completed(
                    """[{"severity":"warning","code":"hc-partial-morpheme","message":"Entry 'foo' is partial.","subjects":[{"kind":"lex_entry","entry":1,"name":"foo"}]}]""",
                    "warning: dropped an allomorph\n", TimeSpan.Zero)
                : throw new InvalidOperationException("Only grammar-health should run."),
        };

        var outcome = GrammarCheckQuery.Query(new GrammarCheckRequest(fwDataPath), invoker, CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var response = outcome.Value!;
        Assert.True(response.HasBaseline);
        Assert.Equal(2, response.Findings.Count);
        Assert.Contains(response.Findings, finding => finding.Text.Contains("dropped an allomorph", StringComparison.Ordinal));
        var health = Assert.Single(response.Findings, finding => finding.Severity == "warning" && finding.Kind == "Partial morpheme");
        Assert.Contains(health.Subject, part => part.Text == "foo");
        Assert.Contains("Entry 'foo' is partial.", health.Problem.Single().Text, StringComparison.Ordinal);

        var request = Assert.IsType<PanGlossRequest.GrammarHealth>(Assert.Single(invoker.Requests).Request);
        Assert.EndsWith(".fwdata", request.GrammarPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheWrappedFindingsShapeCarriesTheParsersGroupNameAndLinksSubjectsByGuid()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var entryGuid = _pristine.Seed.FirstEntryId.ToString("D");
        Capture(fwDataPath);
        // The shape a newer parser writes: an envelope, "problem" for the sentence, and titled subjects.
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(
                "{\"schema_version\":1,\"findings\":[{\"severity\":\"warning\",\"code\":\"hc-partial-morpheme\"," +
                "\"group_name\":\"Partial morpheme analysis\",\"problem\":\"Lexical entry 'mbo - ADD' is partially analyzed.\"," +
                "\"description\":\"The entry has no category or slot.\",\"guidance\":\"Give it one in FieldWorks.\"," +
                "\"subjects\":[{\"kind\":\"lex_entry\",\"title\":\"mbo - ADD\",\"subtitle\":null,\"internal_id\":\"lex_entry#34\"," +
                "\"fieldworks\":{\"guid\":\"" + entryGuid + "\",\"tool\":\"lexiconEdit\",\"url\":null," +
                "\"url_unavailable\":\"no FieldWorks project name supplied\"}}]}]}",
                string.Empty, TimeSpan.Zero),
        };

        var outcome = GrammarCheckQuery.Query(new GrammarCheckRequest(fwDataPath), invoker, CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var finding = Assert.Single(outcome.Value!.Findings);
        Assert.Equal("Partial morpheme analysis", finding.Group);
        Assert.Equal("hc-partial-morpheme", finding.Code);
        Assert.Equal("The entry has no category or slot.", finding.Description);
        Assert.Equal("Give it one in FieldWorks.", finding.Guidance);
        Assert.Contains("partially analyzed", finding.Problem.Single().Text, StringComparison.Ordinal);
        var subject = Assert.Single(finding.Subject);
        Assert.Equal("mbo - ADD", subject.Text);
        Assert.Equal("object", subject.Role);
        Assert.StartsWith("silfw://", subject.FieldWorksLink, StringComparison.Ordinal);
    }

    [Fact]
    public void ASecondCheckOfTheSameBaselineByTheSameParserAnswersFromTheCacheWithoutRunningIt()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(
                """[{"severity":"error","code":"hc-undeclared-segment","message":"Segment x is undeclared.","subjects":[]}]""",
                string.Empty, TimeSpan.Zero),
        };
        var request = new GrammarCheckRequest(fwDataPath);

        var first = GrammarCheckQuery.Query(request, invoker, CancellationToken.None, parserStamp: "build-1");
        var second = GrammarCheckQuery.Query(request, invoker, CancellationToken.None, parserStamp: "build-1");
        var otherParser = GrammarCheckQuery.Query(request, invoker, CancellationToken.None, parserStamp: "build-2");

        Assert.True(second.Succeeded, second.Refusal?.Message);
        Assert.Equal(first.Value!.Findings.Single().Text, second.Value!.Findings.Single().Text);
        Assert.True(otherParser.Succeeded, otherParser.Refusal?.Message);
        Assert.Equal(2, invoker.Requests.Count);
    }

    [Fact]
    public void AParserThatDeclinesIsATypedRefusal_NotAnException()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Unavailable("Could not find the pangloss executable."),
        };

        var outcome = GrammarCheckQuery.Query(new GrammarCheckRequest(fwDataPath), invoker, CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("grammarcheck.parser-unavailable", outcome.Refusal!.Code);
    }

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
