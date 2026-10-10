using System.Text;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands.PanGloss;

public sealed class CompileDiagnosticTests
{
    [Fact]
    public void ConfiguredParserIdentityNamesItsFileAndReportedVersion()
    {
        var identity = ParserExecutableIdentity.Read(FakeParser.ExecutablePath);
        Assert.Equal(Path.GetFileName(FakeParser.ExecutablePath) + " 0.8.1", identity);
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
