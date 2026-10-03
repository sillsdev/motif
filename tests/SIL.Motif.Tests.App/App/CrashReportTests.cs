using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class CrashReportTests
{
    [Fact]
    public void DefaultCrashReportIncludesOnlyTheAllowlistedFieldsAndSanitizedStack()
    {
        var report = ProblemReport.FromCrash(Crash());

        var text = report.ToText();

        Assert.Contains("Motif version: 0.1.0", text, StringComparison.Ordinal);
        Assert.Contains("PanGloss version:", text, StringComparison.Ordinal);
        Assert.Contains("Operating system: Test OS 1.0", text, StringComparison.Ordinal);
        Assert.Contains("Operation: window action", text, StringComparison.Ordinal);
        Assert.Contains("Refusal code: unhandled-ui-error", text, StringComparison.Ordinal);
        Assert.Contains("Exit status: 1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATEWORD", text, StringComparison.Ordinal);
        Assert.DoesNotContain("/home/private", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret grammar", text, StringComparison.Ordinal);
        Assert.DoesNotContain("raw stderr", text, StringComparison.Ordinal);
        Assert.DoesNotContain(".cs:", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitLocalDetailsChoiceAddsTheCrashAndRefusalDetails()
    {
        var crash = Crash();
        var crashText = crash.ProblemReport.ToText(includeLocalDetails: true);
        var refusal = new Refusal(RefusalCodes.AssessParserUnavailable, FailureReason.Refused,
            "Could not parse PRIVATEWORD from /home/private/project.fwdata: raw stderr secret grammar.",
            new Dictionary<string, string>
            {
                ["projectPath"] = "/home/private/project.fwdata",
                ["parserNotFound"] = "true",
            });
        var refusalReport = ProblemReport.FromRefusal(WindowRefusal.From(refusal));

        Assert.Contains("PRIVATEWORD", crashText, StringComparison.Ordinal);
        Assert.Contains("/home/private/project.fwdata", crashText, StringComparison.Ordinal);
        Assert.Contains("raw stderr: secret grammar text", crashText, StringComparison.Ordinal);
        Assert.Contains("Operation: measure words", refusalReport.ToText(), StringComparison.Ordinal);
        Assert.Contains("Failure fact parserNotFound: true", refusalReport.ToText(), StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATEWORD", refusalReport.ToText(), StringComparison.Ordinal);
        Assert.DoesNotContain("/home/private/project.fwdata", refusalReport.ToText(), StringComparison.Ordinal);
        Assert.Contains("PRIVATEWORD", refusalReport.ToText(includeLocalDetails: true), StringComparison.Ordinal);
        Assert.Contains("/home/private/project.fwdata", refusalReport.ToText(includeLocalDetails: true), StringComparison.Ordinal);
    }

    [Fact]
    public void ARefusalReportCarriesItsCliEquivalentExitStatusAndOnlySafeFacts()
    {
        var refusal = new Refusal("stats.parser-refused", FailureReason.Refused,
            "PanGloss refused the request.",
            new Dictionary<string, string>
            {
                ["exitCode"] = "17",
                ["projectPath"] = "/home/private/project.fwdata",
                ["stderr"] = "PRIVATEWORD secret grammar",
            });

        var text = ProblemReport.FromRefusal(WindowRefusal.From(refusal)).ToText();

        Assert.Contains("Operation: read statistics", text, StringComparison.Ordinal);
        Assert.Contains("Exit status: CLI equivalent 2", text, StringComparison.Ordinal);
        Assert.Contains("Failure fact PanGloss exit status: 17", text, StringComparison.Ordinal);
        Assert.DoesNotContain("project.fwdata", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATEWORD", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret grammar", text, StringComparison.Ordinal);
    }

    private static CrashReport Crash()
    {
        try
        {
            throw new InvalidOperationException("Refreshing PRIVATEWORD at /home/private/project.fwdata failed.",
                new IOException("raw stderr: secret grammar text"));
        }
        catch (InvalidOperationException failure)
        {
            return new CrashReport(failure, DateTimeOffset.UtcNow, "0.1.0", "Test OS 1.0", "Test .NET 10");
        }
    }
}
