using SIL.Motif.Host.PanGloss;
using Xunit;

namespace SIL.Motif.Tests.PanGloss;

/// <summary>
/// PanGloss's compile-error report, as the pinned release writes it on standard error when a grammar cannot
/// compile, read into a refusal a person can act on.
/// </summary>
public sealed class PanGlossCompileErrorTests
{
    private const string XmlFailure =
        """{"schema_version":1,"status":"compile_error","path":"bad.xml","message":"XML parse error: no active <Language> element","issues":[{"code":"grammar.compile.failed","kind":"xml","object_guid":null,"object_kind":null,"field":null,"text":"XML parse error: no active <Language> element","advice":"Use the error description to locate and correct malformed grammar input.","fatal":true}]}""";

    [Fact]
    public void TheReportIsFoundAmongProgressLinesAndDescribedWithItsAdvice()
    {
        var error = PanGlossCompileError.TryRead("loading bad.xml\n{not json\n" + XmlFailure + "\n");

        Assert.NotNull(error);
        Assert.Equal("grammar.compile.failed", Assert.Single(error!.Issues).Code);
        Assert.Equal(
            "pangloss parse exited 1: it could not load the grammar. XML parse error: no active <Language> element" +
            Environment.NewLine + "- Use the error description to locate and correct malformed grammar input.",
            error.Describe("parse", 1));
    }

    [Fact]
    public void AFatalIssueWithItsOwnTextIsListedAndANonFatalOneIsLeftOut()
    {
        var error = PanGlossCompileError.TryRead(
            """{"schema_version":1,"status":"compile_error","message":"cannot convert","issues":[{"code":"grammar.environment.unresolved","text":"environment does not resolve","advice":"Inspect Allomorphs > Environments.","fatal":true},{"code":"grammar.environment.invalid","text":"/_#","advice":"Fix it.","fatal":false}]}""");

        Assert.Equal(
            "pangloss batch exited 1: it could not load the grammar. cannot convert" + Environment.NewLine +
            "- environment does not resolve Inspect Allomorphs > Environments.",
            error!.Describe("batch", 1));
    }

    [Theory]
    [InlineData("pangloss parse: load bad.xml: Xml(\"no active <Language> element\")")]
    [InlineData("""{"schema_version":2,"status":"compile_error","message":"later shape","issues":[]}""")]
    [InlineData("""{"schema_version":1,"status":"io_error","message":"not a compile refusal"}""")]
    public void AnythingElseIsNotReadAsTheReport(string standardError) =>
        Assert.Null(PanGlossCompileError.TryRead(standardError));
}
