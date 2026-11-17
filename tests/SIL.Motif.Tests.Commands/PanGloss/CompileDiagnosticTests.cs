using System.Text;
using System.Text.Json;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Generator;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands.PanGloss;

public sealed class CompileDiagnosticTests
{
    [Fact]
    public void ConfiguredParserIdentityNamesItsFileAndReportedVersion()
    {
        var identity = ParserExecutableIdentity.Read(FakeParser.ExecutablePath);
        using var pin = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.FindRepoRoot(), "pangloss-release.json")));
        var pinnedVersion = pin.RootElement.GetProperty("version").GetString();
        Assert.Equal(Path.GetFileName(FakeParser.ExecutablePath) + " " + pinnedVersion, identity);
        Assert.DoesNotContain(Path.GetDirectoryName(FakeParser.ExecutablePath)!, identity);
    }

    [Fact]
    public void CurrentDebugFixtureShowsFiveEnvironmentsAndKeepsRawText()
    {
        using var stream = typeof(CompileDiagnosticTests).Assembly.GetManifestResourceStream(
            "SIL.Motif.Tests.Commands.Fixtures.compile-refusal.txt")!;
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
        var text = reader.ReadToEnd();
        Assert.DoesNotContain("\uFFFD", text);
        Assert.Contains("Allomorph 'á(k)'", text);
        Assert.Contains("Allomorph 'íé(k)'", text);
        var result = Assert.IsType<SIL.Motif.Contract.Commands.ParserCompileDiagnostic>(
            ParserCompileDiagnosticReader.Read(text));

        Assert.Equal("PanGloss can't use this grammar: 5 environments it can't read. Fix them in FieldWorks, then Refresh.",
            result.Summary);
        Assert.Equal(5, result.Issues.Count);
        Assert.Equal(text, result.RawText);
        Assert.All(result.Issues, issue =>
        {
            Assert.Equal("EnvironmentInvalid", issue.Code);
            Assert.Equal("PhEnvironment", issue.Kind);
            Assert.True(Guid.TryParse(issue.ObjectGuid, out _));
            Assert.Contains("FieldWorks", issue.Advice);
        });
        Assert.Contains(result.Issues, issue => issue.Text.Contains("\"_#\"", StringComparison.Ordinal));
        Assert.Contains(result.Issues, issue => issue.Text.Contains("unknown natural class \"+ATR\"", StringComparison.Ordinal));
    }

    /// <summary>
    /// PanGloss's advice for a phoneme table that cannot segment "+" says to report an unsupported construct; the usual
    /// cause is a project with no phonology yet, so Motif names the fix instead. The text is PanGloss 0.8.3's own.
    /// </summary>
    [Fact]
    public void UnsegmentableBoundaryAdviceNamesTheFirstPhonemeProposal()
    {
        const string text = """
            {"schema_version":1,"status":"compile_error","path":"p.fwdata","message":"cannot compile compounding","issues":[
              {"code":"grammar.compile.failed","kind":"unsegmentable_boundary","object_guid":null,"object_kind":null,"field":null,
               "text":"cannot compile compounding: table \"main\" cannot segment the morph boundary \"+\"",
               "advice":"Use the error description to locate and correct malformed grammar input. If FieldWorks accepts the input, report the unsupported construct with this diagnostic.",
               "fatal":true}]}
            """;
        var issue = Assert.Single(ParserCompileDiagnosticReader.Read(text)!.Issues);
        Assert.Equal(ParserCompileDiagnosticReader.NoBoundaryMarkersAdvice, issue.Advice);
        Assert.Contains("phoneme Proposal", issue.Advice, StringComparison.Ordinal);
        Assert.DoesNotContain("report the unsupported construct", issue.Advice, StringComparison.Ordinal);
    }

    [Fact]
    public void StructuredIssuesKeepAuthoredTextIdentityAndAdvice()
    {
        const string text = """
            {"schema_version":1,"status":"compile_error","issues":[
              {"code":"EnvironmentInvalid","kind":"invalidSource","object_kind":"PhEnvironment","object_guid":"first","field":"StringRepresentation",
               "text":"/","advice":"Fix in Grammar › Environments","fatal":true},
              {"code":"WarningOnly","kind":"MoForm","text":"skipped","fatal":false},
              {"code":"NoFatalFlag","kind":"MoForm","text":"not a fatal issue"}]}
            """;
        var result = ParserCompileDiagnosticReader.Read(text)!;
        var issue = Assert.Single(result.Issues);
        Assert.Equal("invalidSource", issue.Kind);
        Assert.Equal("PhEnvironment", issue.ObjectKind);
        Assert.Contains("1 environment", result.Summary);
        Assert.Equal("first", issue.ObjectGuid);
        Assert.Equal("StringRepresentation", issue.Field);
        Assert.Equal("/", issue.Text);
        Assert.Equal("Fix in Grammar › Environments", issue.Advice);
    }

    [Fact]
    public void CompileErrorOnStderrTakesPrecedenceOverDebugOutputOnStdout()
    {
        const string stderr = """
            importing project
            {"schema_version":1,"status":"compile_error","path":"sample.fwdata","message":"Cannot compile","issues":[{"code":"grammar.environment.unresolved","kind":"invalidSource","object_guid":"00000000-0000-0000-0000-000000000042","object_kind":"MoForm","field":"PhoneEnv","text":"environment does not resolve","advice":"Repair the attachment in FieldWorks.","fatal":true}]}
            """;
        const string stdout = "Conversion(ConversionError { issues: [] })";
        var refusal = ParserExecutionRefusal.From("wordtrace.parser-refused", "sample.fwdata",
            new PanGlossOutcome.Refused(1, stderr, stdout, "parser exited 1"));

        var issue = Assert.Single(refusal.ParserDiagnostic!.Issues);
        Assert.Equal("grammar.environment.unresolved", issue.Code);
        Assert.Equal("PhoneEnv", issue.Field);
        Assert.Equal("environment does not resolve", issue.Text);
        Assert.Equal("Repair the attachment in FieldWorks.", issue.Advice);
        Assert.Equal(stderr, refusal.Facts["standardError"]);
        Assert.Contains(stdout, refusal.ParserDiagnostic.RawText);
    }

    [Theory]
    [InlineData("/tmp/out of memory/project.fwdata", "environment does not resolve")]
    [InlineData("/tmp/Out of memory./project.fwdata", "environment does not resolve")]
    [InlineData("/tmp/project.fwdata", "Out of memory.")]
    [InlineData("/tmp/System.OutOfMemoryException/project.fwdata", "environment does not resolve")]
    [InlineData("/tmp/project.fwdata", "environment named out of memory does not resolve")]
    [InlineData("/tmp/project.fwdata", "memory allocation of 4096 bytes failed")]
    public void CompileDiagnosticPathsAndMessagesDoNotProveMemoryExhaustion(string path, string text)
    {
        var stderr = JsonSerializer.Serialize(new
        {
            schema_version = 1, status = "compile_error", path, message = "Cannot compile",
            issues = new[] { new { code = "grammar.environment.unresolved", text, fatal = true } },
        });
        var failure = new PanGlossOutcome.Refused(1, stderr, "", "parser exited 1");

        var refusal = ParserExecutionRefusal.From("assess.parser-unavailable", path, failure);

        Assert.Equal("assess.parser-unavailable", refusal.Code);
        Assert.Equal(SIL.Motif.Contract.Jobs.JobFailureCategory.ParserRefusal, failure.FailureCategory);
        Assert.Equal(text, Assert.Single(refusal.ParserDiagnostic!.Issues).Text);
    }

    [Theory]
    [InlineData("warning: could not open /tmp/out of memory/project.fwdata")]
    [InlineData("cannot read /tmp/memory allocation of 4096 bytes failed/project.fwdata")]
    [InlineData("read /tmp/System.OutOfMemoryException/project.fwdata failed")]
    [InlineData("warning: Out of memory.")]
    [InlineData("cannot read /tmp/Out of memory./project.fwdata")]
    [InlineData("Out of memory. while opening project")]
    public void DiagnosticProseDoesNotProveMemoryExhaustion(string stderr)
    {
        var failure = new PanGlossOutcome.Refused(1, stderr, "", "parser exited 1");

        var refusal = ParserExecutionRefusal.From("assess.parser-unavailable", "project.fwdata", failure);

        Assert.Equal("assess.parser-unavailable", refusal.Code);
    }

    [Theory]
    [InlineData("memory allocation of 4096 bytes failed")]
    [InlineData("importing project\nmemory allocation of 1048576 bytes failed\n")]
    [InlineData("Unhandled exception. System.OutOfMemoryException: Exception of type 'System.OutOfMemoryException' was thrown.")]
    [InlineData("System.OutOfMemoryException: Insufficient memory.")]
    public void KnownAllocatorAndExceptionRecordsProveMemoryExhaustion(string stderr)
    {
        var failure = new PanGlossOutcome.Refused(1, stderr, "", "parser exited 1");

        var refusal = ParserExecutionRefusal.From("assess.parser-unavailable", "project.fwdata", failure);

        Assert.Equal("assess.out-of-memory", refusal.Code);
        Assert.Null(refusal.ParserDiagnostic);
    }

    [Theory]
    [InlineData("Out of memory.\n", false)]
    [InlineData("Out of memory.\r\n", false)]
    [InlineData("loading project\nOut of memory.\n", false)]
    [InlineData("Out of memory.\n", true)]
    [InlineData("Out of memory.\r\n", true)]
    [InlineData("loading project\r\nOut of memory.\r\n", true)]
    public void CoreClrMemoryFailureRecordsTakePrecedenceOverTermination(string stderr, bool signalled)
    {
        var failure = new PanGlossOutcome.Refused(signalled ? 134 : 1, stderr, "", "parser stopped")
        { TerminationSignal = signalled ? 6 : null };

        var refusal = ParserExecutionRefusal.From("assess.parser-unavailable", "project.fwdata", failure);

        Assert.Equal(SIL.Motif.Contract.Jobs.JobFailureCategory.OutOfMemory, failure.FailureCategory);
        Assert.Equal("assess.out-of-memory", refusal.Code);
        Assert.Null(refusal.ParserDiagnostic);
    }

    [Fact]
    public void StructuredIssuesKeepDifferentAttachmentsOnTheSameObject()
    {
        const string text = """
            {"schema_version":1,"status":"compile_error","issues":[
              {"code":"grammar.environment.unresolved","kind":"invalidSource","object_kind":"MoForm","object_guid":"same","field":"PhoneEnv","text":"first attachment","advice":"Repair it.","fatal":true},
              {"code":"grammar.environment.unresolved","kind":"invalidSource","object_kind":"MoForm","object_guid":"same","field":"Position","text":"second attachment","advice":"Repair it.","fatal":true}]}
            """;

        Assert.Equal(2, ParserCompileDiagnosticReader.Read(text)!.Issues.Count);
    }

    [Theory]
    [InlineData("{\"issues\":[]}")]
    [InlineData("{\"status\":\"progress\",\"schema_version\":1,\"issues\":[]}")]
    public void ObjectsWithoutCompileErrorStatusAreNotCompileDiagnostics(string text) =>
        Assert.Null(ParserCompileDiagnosticReader.Read(text));

    [Theory]
    [InlineData("{\"status\":\"compile_error\",\"schema_version\":2,\"issues\":[{\"code\":\"future\",\"text\":\"unknown shape\",\"fatal\":true}]}")]
    [InlineData("{\"status\":\"compile_error\",\"issues\":[{\"code\":\"future\",\"text\":\"unknown shape\",\"fatal\":true}]}")]
    public void UnsupportedCompileErrorSchemasKeepOnlyThePlainFailure(string text)
    {
        var diagnostic = ParserCompileDiagnosticReader.Read(text)!;
        Assert.Empty(diagnostic.Issues);
        Assert.Contains("can't use this grammar", diagnostic.Summary);
    }

    [Theory]
    [InlineData("access denied")]
    [InlineData("{broken issues}")]
    [InlineData("{\"issues\":false}")]
    public void UnrecognisedOutputIsNotInventedIntoCompileIssues(string text) =>
        Assert.Null(ParserCompileDiagnosticReader.Read(text));

    [Fact]
    public void JsonDiagnosticLineWinsAmongProgressObjectsAndDebugText()
    {
        const string text = """
            {"progress":"importing"}
            Conversion(ConversionError { issues: [] })
            {"schema_version":1,"status":"compile_error","issues":[{"code":"grammar.compile.failed","kind":"semantic","text":"missing source","advice":"Repair it.","fatal":true}]}
            """;
        var result = ParserCompileDiagnosticReader.Read(text)!;
        Assert.Equal("missing source", Assert.Single(result.Issues).Text);
        Assert.Equal(text, result.RawText);
    }

    [Fact]
    public void TruncatedDebugOutputStillSaysGrammarAndPreservesTheOutput()
    {
        const string text = "Conversion(ConversionError { issues: [ConversionIssue { code:";
        var result = ParserCompileDiagnosticReader.Read(text)!;
        Assert.Empty(result.Issues);
        Assert.Contains("can't use this grammar", result.Summary);
        Assert.Equal(text, result.RawText);
    }
}
