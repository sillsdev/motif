using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>Pins how grammar-health report diagnostics become Motif findings.</summary>
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
    public void ABaselineReadsReportDiagnosticsAndIgnoresStderr()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var entryGuid = _pristine.Seed.FirstEntryId.ToString("D");
        var openGuid = "5c9e433d-cc9b-4d12-b8cb-b5840f46dbd2";
        var report = Report(entryGuid, openGuid);
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(
                report,
                "warning: stderr-only warning\ncapability: stderr-only capability\n", TimeSpan.Zero),
        };

        var outcome = GrammarCheckQuery.Query(new GrammarCheckRequest(fwDataPath), invoker, CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var response = outcome.Value!;
        Assert.True(response.HasBaseline);
        Assert.Equal(2, response.Findings.Count);
        Assert.Equal(2, response.Summary.Count);
        Assert.DoesNotContain(response.Findings, finding => finding.Text.Contains("stderr-only", StringComparison.Ordinal));

        var warning = response.Findings[0];
        Assert.Equal("warning", warning.Severity);
        Assert.Equal("import", warning.Origin);
        Assert.Equal("Partial morpheme analysis", warning.Group);
        Assert.Equal("Lexical entry 'mbo' has no grammatical category.", warning.Description);
        Assert.Equal("In Lexicon > Lexicon Edit, set Grammatical Info. > Category.", warning.Guidance);
        Assert.Equal(2, warning.Subject.Count);

        var entry = warning.Subject[0];
        Assert.Equal("mbo (ADD)", entry.Text);
        Assert.Equal("mbo", entry.Title);
        Assert.Equal("ADD", entry.Subtitle);
        Assert.Equal("LexEntry", entry.Kind);
        Assert.Equal(entryGuid, entry.ObjectId);
        Assert.Equal(entryGuid, entry.SubjectGuid);
        Assert.Null(entry.InternalId);
        Assert.Equal("available", entry.LinkStatus);
        Assert.Equal("lexiconEdit", entry.FieldWorksTool);
        Assert.Equal(openGuid, entry.FieldWorksGuid);
        Assert.Contains(openGuid, entry.FieldWorksLink, StringComparison.Ordinal);

        var phoneme = warning.Subject[1];
        Assert.Equal("ng", phoneme.Text);
        Assert.Equal("PhPhoneme", phoneme.Kind);
        Assert.Equal("unsupported_kind", phoneme.LinkReason);
        Assert.Null(phoneme.FieldWorksLink);

        var info = response.Findings[1];
        Assert.Equal("info", info.Severity);
        Assert.Equal("check", info.Origin);
        Assert.Equal("Duplicate segment features", info.Group);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response));
        Assert.False(json.RootElement.TryGetProperty("FieldWorksProject", out _));
        var reportFinding = json.RootElement.GetProperty("Findings")[0];
        Assert.Equal("import", reportFinding.GetProperty("Origin").GetString());
        Assert.False(reportFinding.TryGetProperty("Audience", out _));
        Assert.Equal(entryGuid, reportFinding.GetProperty("Subject")[0].GetProperty("SubjectGuid").GetString());
        Assert.Null(reportFinding.GetProperty("Subject")[0].GetProperty("InternalId").GetString());
        Assert.False(reportFinding.GetProperty("Subject")[0].TryGetProperty("OpenTargetTool", out _));
        Assert.Equal("available", reportFinding.GetProperty("Subject")[0].GetProperty("LinkStatus").GetString());

        var request = Assert.IsType<PanGlossRequest.GrammarHealth>(Assert.Single(invoker.Requests).Request);
        Assert.EndsWith(".fwdata", request.GrammarPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AReportMustHaveTheCurrentV2Envelope_AndOldFindingsShapeIsRejected()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(
                "{\"schema_version\":2,\"summary\":[],\"findings\":[]}",
                string.Empty, TimeSpan.Zero),
        };

        var outcome = GrammarCheckQuery.Query(new GrammarCheckRequest(fwDataPath), invoker, CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("grammarcheck.malformed-findings", outcome.Refusal!.Code);
    }

    [Fact]
    public void ASecondCheckOfTheSameBaselineByTheSameParserAnswersFromTheCacheWithoutRunningIt()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(
                "{\"schema_version\":2,\"fieldworks_project\":{\"name\":null,\"source\":null}," +
                "\"summary\":[],\"diagnostics\":[{\"level\":\"info\",\"code\":\"hc-undeclared-segment\"," +
                "\"group_name\":\"Undeclared segment\",\"origin\":\"check\",\"description\":\"Segment x is undeclared.\"," +
                "\"guidance\":null,\"subjects\":[]}]}",
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

    private static string Report(string entryGuid, string openGuid) => JsonSerializer.Serialize(new
    {
        schema_version = 2,
        fieldworks_project = new { name = "Sena 3", source = "argument" },
        summary = new[]
        {
            new { code = "hc-partial-morpheme", group_name = "Partial morpheme analysis", level = "warning", count = 1 },
            new { code = "hc-duplicate-feature-bundle", group_name = "Duplicate segment features", level = "info", count = 1 },
        },
        diagnostics = new object[]
        {
            new
            {
                level = "warning",
                code = "hc-partial-morpheme",
                group_name = "Partial morpheme analysis",
                origin = "import",
                description = "Lexical entry 'mbo' has no grammatical category.",
                guidance = "In Lexicon > Lexicon Edit, set Grammatical Info. > Category.",
                subjects = new object[]
                {
                    new
                    {
                        kind = "LexEntry", title = "mbo", subtitle = "ADD", guid = entryGuid,
                        internal_id = (string?)null,
                        opens_in = new { tool = "lexiconEdit", guid = openGuid },
                        fieldworks = new
                        {
                            status = "available", guid = openGuid, tool = "lexiconEdit",
                            url = $"silfw://localhost/link?database=Sena%203&tool=lexiconEdit&guid={openGuid}&tag=",
                        },
                    },
                    new
                    {
                        kind = "PhPhoneme", title = "ng", subtitle = (string?)null, guid = (string?)null,
                        internal_id = (string?)null,
                        fieldworks = new { status = "unavailable", reason = "unsupported_kind", guid = (string?)null },
                    },
                },
            },
            new
            {
                level = "info", code = "hc-duplicate-feature-bundle", group_name = "Duplicate segment features",
                origin = "check", description = "Two phonemes share the same feature values.", guidance = (string?)null,
                subjects = Array.Empty<object>(),
            },
        },
    });

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
