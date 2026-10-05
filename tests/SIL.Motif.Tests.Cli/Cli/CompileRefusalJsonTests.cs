using System.Text.Json;
using SIL.Motif.Cli.Rendering;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host.PanGloss;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class CompileRefusalJsonTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void JsonKeepsTheParserMessageStreamsExitStatusAndStructuredIssue(bool diagnosticOnStderr)
    {
        const string output = """
            {"schema_version":1,"status":"compile_error","issues":[
              {"code":"grammar.environment.unresolved","kind":"invalidSource","object_kind":"PhEnvironment",
               "object_guid":"item","field":"StringRepresentation","text":"/","advice":"Fix in FieldWorks.","fatal":true}]}
            """;
        var stderr = diagnosticOnStderr ? output : "parser stderr";
        var stdout = diagnosticOnStderr ? "parser stdout" : output;
        var refusal = ParserExecutionRefusal.From(RefusalCodes.AssessParserUnavailable, "one.fwdata",
            new PanGlossOutcome.Refused(1, stderr, stdout, "original parser message"));
        var rendered = CommandTextRenderer.Render(CommandOutcome<object>.Refused(refusal), asJson: true);
        using var document = JsonDocument.Parse(rendered.Output);
        var root = document.RootElement;
        Assert.Equal(2, rendered.ExitCode);
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Contains("can't use this grammar", root.GetProperty("message").GetString());
        var facts = root.GetProperty("detail");
        Assert.Equal("1", facts.GetProperty("exitCode").GetString());
        Assert.Equal("original parser message", facts.GetProperty("parserMessage").GetString());
        Assert.Equal(stderr, facts.GetProperty("standardError").GetString());
        Assert.Equal(stdout, facts.GetProperty("standardOutput").GetString());
        var issue = root.GetProperty("parserDiagnostic").GetProperty("issues")[0];
        Assert.Equal("/", issue.GetProperty("text").GetString());
        Assert.Equal("item", issue.GetProperty("objectGuid").GetString());
        Assert.Equal("StringRepresentation", issue.GetProperty("field").GetString());
        Assert.Equal("Fix in FieldWorks.", issue.GetProperty("advice").GetString());
    }
}
