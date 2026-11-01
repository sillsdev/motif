using SIL.Motif.Cli;
using SIL.Motif.Cli.Rendering;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Commands.Preferences;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Trait("MotifTestLevel", "Unit")]
public sealed class DifferenceShowFlagTests
{
    [Theory]
    [InlineData("--limitation", "nonsense", "difference.invalid-limitation")]
    [InlineData("--occurrence-limit", "0", "difference.invalid-page")]
    [InlineData("--occurrence-limit", "201", "difference.invalid-page")]
    public void TheNewFlagsReachTheSharedCommandAndAreCheckedThereLikeTheMcpArguments(string flag, string value, string code)
    {
        var (exitCode, text) = RunShow("--word", "sita", flag, value);

        Assert.NotEqual(0, exitCode);
        Assert.Contains(code, text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--reference", "nonsense", "difference.invalid-reference")]
    [InlineData("--text", "not-a-guid", "difference.invalid-text")]
    public void TheTextAndReferenceFlagsReachTheSharedCommandAndAreCheckedThere(string flag, string value, string code)
    {
        var (exitCode, text) = RunShow(flag, value);

        Assert.NotEqual(0, exitCode);
        Assert.Contains(code, text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--occurrence-offset", "1")]
    [InlineData("--occurrence-limit", "10")]
    public void PlaceFlagsWithoutAWordAreRefusedInsteadOfIgnored(string flag, string value)
    {
        var (exitCode, text) = RunShow(flag, value);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("difference.occurrence-needs-word", text, StringComparison.Ordinal);
    }

    private static (int ExitCode, string Text) RunShow(params string[] flagsAndValues)
    {
        var workerRoot = Path.Combine(Path.GetTempPath(), "motif-difference-flags-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workerRoot);
        try
        {
            var preferencePath = Path.Combine(workerRoot, "advanced-ai-mode.json");
            new FileAdvancedAiModePreferenceStore(preferencePath).SetEnabled(true);
            var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [RunnerOptions.RootVariable] = workerRoot,
                [FileAdvancedAiModePreferenceStore.PathEnvironmentVariable] = preferencePath,
            };
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = global::SIL.Motif.Cli.Program.Run(
                ["difference", "show", "--project", Path.Combine(workerRoot, "p.fwdata"), "--proposal", "proposal/x",
                    .. flagsAndValues, "--json"], output, error, environment, static (_, _) => { });

            return (exitCode, output.ToString() + error);
        }
        finally
        {
            if (Directory.Exists(workerRoot)) Directory.Delete(workerRoot, recursive: true);
        }
    }

    [Fact]
    public void TheUsageLineNamesTheLimitationAndWordFlags()
    {
        var usage = CliVerbCatalog.All.Single(verb => verb.CommandName == "difference show").UsageLines.Single();

        Assert.Contains("--limitation <code>", usage);
        Assert.Contains("--word <word>", usage);
        Assert.Contains("--occurrence-offset <n>", usage);
        Assert.Contains("--occurrence-limit <n>", usage);
    }

    [Fact]
    public void TheRenderingListsOnePageOfAWordsPlacesAndHowToAskForMore()
    {
        var pin = new ProposalDifferenceSidePin("1", "s", "g", "sel", "s", "tok", "1");
        var summary = new ProposalDifferenceSummary("difference/1", "proposal/1", "job/1", "a/b", "a/a", "text/1", "Title",
            "baseline/1", new ProposalDifferencePolicy(), 1, 3, 0, 1, 0, 0, [], [], "pangloss", null, pin, pin, [], 0, [],
            "2026-10-31T00:00:00Z");
        var places = new ProposalDifferenceOccurrencePage("sita",
            [new("text/1", "seg/1", 0, 4), new("text/1", "seg/2", 2, 9)], 3, 2);

        var rendered = CommandTextRenderer.Render(CommandOutcome<ProposalDifferenceResponse>.Success(
            new ProposalDifferenceResponse(summary, [], 0, null, true, places)), asJson: false);

        Assert.Contains("Places of sita: showing 2 of 3", rendered.Output);
        Assert.Contains("line 4, word 1", rendered.Output);
        Assert.Contains("line 9, word 3", rendered.Output);
        Assert.Contains("--occurrence-offset 2", rendered.Output);
    }

    private static ProposalDifferenceSummary SummaryOf(string id, string reference, string title)
    {
        var pin = new ProposalDifferenceSidePin("1", "s", "g", "sel", "s", "tok", "1");
        return new ProposalDifferenceSummary(id, "proposal/1", "job/1", "a/b", "a/a", "text/" + id, title,
            "baseline/1", new ProposalDifferencePolicy(), 1, 3, 0, 1, 0, 0, [], [], "pangloss", null, pin, pin, [], 0,
            [], "2026-10-31T00:00:00Z", reference);
    }

    [Fact]
    public void TheRenderingListsEverySavedDifferenceWithItsTextAndReferenceAndMarksTheShownOne()
    {
        var shown = SummaryOf("difference/1", "human", "Genesis 1");
        var response = new ProposalDifferenceResponse(shown, [], 0, null, true, null, null,
            [new("difference/1", "text/difference/1", "Genesis 1", "human", "2026-10-31T00:00:00Z", true),
             new("difference/2", "text/difference/2", "Exodus 1", "source-validated", "2026-10-30T00:00:00Z", false)], 2);

        var rendered = CommandTextRenderer.Render(CommandOutcome<ProposalDifferenceResponse>.Success(response), asJson: false);

        Assert.Contains("Saved Differences of this Proposal", rendered.Output);
        Assert.Contains("* Genesis 1", rendered.Output);
        Assert.Contains("Exodus 1", rendered.Output);
        Assert.Contains("source-validated", rendered.Output);
        Assert.Contains("difference/2", rendered.Output);
        Assert.Contains("summary only", rendered.Output);
        Assert.Contains("made 2026-10-31T00:00:00Z", rendered.Output);
        Assert.Contains("made 2026-10-30T00:00:00Z", rendered.Output);
        Assert.DoesNotContain("could not be read", rendered.Output);
    }

    [Fact]
    public void TheRenderingSaysHowManySavedDifferencesCouldNotBeReadAndStillListsTheRest()
    {
        var response = new ProposalDifferenceResponse(SummaryOf("difference/1", "human", "Genesis 1"), [], 0, null, true, null,
            null, [new("difference/1", "text/difference/1", "Genesis 1", "human", "2026-10-31T00:00:00Z", true)], 3, 2);

        var rendered = CommandTextRenderer.Render(CommandOutcome<ProposalDifferenceResponse>.Success(response), asJson: false);

        Assert.Contains("* Genesis 1", rendered.Output);
        Assert.Contains("2 saved Differences of this Proposal could not be read and are not listed", rendered.Output);
    }

    [Fact]
    public void ASummaryOnlyDifferenceSaysDecidedOrDeferredAndOffersToMakeItAgain()
    {
        var response = new ProposalDifferenceResponse(SummaryOf("difference/1", "human", "Genesis 1"), [], 0, null, false);

        var rendered = CommandTextRenderer.Render(CommandOutcome<ProposalDifferenceResponse>.Success(response), asJson: false);

        Assert.Contains("decided or deferred", rendered.Output);
        Assert.Contains("deferred", rendered.Output);
        Assert.Contains("again", rendered.Output, StringComparison.OrdinalIgnoreCase);
    }
}
