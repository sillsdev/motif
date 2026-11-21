using SIL.Motif.Cli.Rendering;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>What a capped read prints names how much it left out and the flag that reads the next page.</summary>
public sealed class OutputBoundsTextTests
{
    private const int TwoKilobytes = 2048;

    private static GrammarWarning Finding(int index, GrammarDiagnosticLevel level = GrammarDiagnosticLevel.Warning) =>
        new(level, "label", [], [], "finding " + index) { Code = "code-" + index };

    [Fact]
    public void WarningsTextShowsOnePageWithTheTotalAndTheNextOffset()
    {
        var findings = Enumerable.Range(0, 5000).Select(i => Finding(i)).ToArray();
        var response = new WarningsResponse(true, true, findings, [], 5000, 0).Window(0, 10);

        var text = CommandTextRenderer.Render(CommandOutcome<WarningsResponse>.Success(response), asJson: false).Output;

        Assert.Contains("Grammar findings: 5,000", text, StringComparison.Ordinal);
        Assert.Contains("Showing findings 1-10 of 5,000.", text, StringComparison.Ordinal);
        Assert.Contains("Next: --offset 10 --limit 10, or --all for every finding.", text, StringComparison.Ordinal);
        Assert.Contains("finding 9", text, StringComparison.Ordinal);
        Assert.DoesNotContain("finding 10" + Environment.NewLine, text, StringComparison.Ordinal);
        Assert.True(text.Length < TwoKilobytes, text.Length.ToString());
    }

    [Fact]
    public void WarningsTextOnTheLastPageHasNoNextStepAndAPastEndPageNamesTheTotal()
    {
        var findings = Enumerable.Range(0, 12).Select(i => Finding(i)).ToArray();
        var response = new WarningsResponse(true, true, findings, [], 12, 0);

        var last = CommandTextRenderer.Render(
            CommandOutcome<WarningsResponse>.Success(response.Window(10, 10)), asJson: false).Output;
        var pastEnd = CommandTextRenderer.Render(
            CommandOutcome<WarningsResponse>.Success(response.Window(40, 10)), asJson: false).Output;

        Assert.Contains("Showing findings 11-12 of 12.", last, StringComparison.Ordinal);
        Assert.DoesNotContain("Next:", last, StringComparison.Ordinal);
        Assert.Contains("No findings at offset 40 of 12.", pastEnd, StringComparison.Ordinal);
    }

    [Fact]
    public void WarningsTextWithoutAWindowPrintsNoPagingLine()
    {
        var response = new WarningsResponse(true, true, [Finding(1)], [], 1, 0);

        var text = CommandTextRenderer.Render(CommandOutcome<WarningsResponse>.Success(response), asJson: false).Output;

        Assert.DoesNotContain("Showing findings", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WarningsTextListsTheTwentyLargestCodesAndSaysHowManyMoreThereAre()
    {
        var kinds = Enumerable.Range(0, 70)
            .Select(i => new GrammarWarningSummary("code-" + i, null, GrammarDiagnosticLevel.Warning, 100 - i)).ToArray();
        var response = new WarningsResponse(true, true, [], kinds, 0, 0);

        var text = CommandTextRenderer.Render(CommandOutcome<WarningsResponse>.Success(response), asJson: false).Output;

        Assert.Contains("code-19:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("code-20:", text, StringComparison.Ordinal);
        Assert.Contains("and 50 more finding codes; --json lists every one.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void GrammarCheckTextCountsEveryFindingWhileListingOnePage()
    {
        var findings = Enumerable.Range(0, 900).Select(i => Finding(i, (GrammarDiagnosticLevel)(i % 3))).ToArray();
        var response = new GrammarCheckResponse(findings, true).Window(20, 10);

        var text = CommandTextRenderer.Render(CommandOutcome<GrammarCheckResponse>.Success(response), asJson: false).Output;

        Assert.Contains("Grammar findings: 900 (300 errors, 300 warnings, 300 information)", text, StringComparison.Ordinal);
        Assert.Contains("Showing findings 21-30 of 900.", text, StringComparison.Ordinal);
        Assert.Contains("Next: --offset 30 --limit 10", text, StringComparison.Ordinal);
        Assert.True(text.Length < TwoKilobytes, text.Length.ToString());
    }

    [Fact]
    public void ABatchReportIsBoundedInTextAndJsonAndNamesTheNextStep()
    {
        var batch = new ComposedBatchResponse("d",
            Enumerable.Range(0, 2093).Select(i => new ComposedIntentResult(i, "compose-author-phoneme", "composer",
                [new OperationSummary("op" + i, "lexical/create", "entity" + i)])).ToArray(), 2093);

        var text = ProposalCommandRenderer.Render(CommandOutcome<ComposedBatchResponse>.Success(batch), asJson: false).Output;
        var json = ProposalCommandRenderer.Render(CommandOutcome<ComposedBatchResponse>.Success(batch), asJson: true).Output;

        Assert.Contains("Composed 2093 intent(s) against draft 'd': 2093 operation(s) added.", text, StringComparison.Ordinal);
        Assert.Contains("motif show d --project <fwdata> --offset 0", text, StringComparison.Ordinal);
        Assert.Contains("Draft now has 2093 operation(s).", text, StringComparison.Ordinal);
        Assert.True(text.Length < 2 * TwoKilobytes, text.Length.ToString());
        Assert.True(json.Length < 8 * TwoKilobytes, json.Length.ToString());
        Assert.Contains("\"intentTotal\":2093", json.Replace(" ", ""), StringComparison.Ordinal);
    }

    [Fact]
    public void AnOverlongRefusalPrintsItsStartAndNamesTheJsonFlag()
    {
        var refusal = new Refusal("draft.invalid", FailureReason.InvalidArgument, new string('m', 20_000));

        var text = ProposalCommandRenderer.Render(CommandOutcome<DraftCreatedResponse>.Refused(refusal), asJson: false).Output;
        var json = ProposalCommandRenderer.Render(CommandOutcome<DraftCreatedResponse>.Refused(refusal), asJson: true).Output;

        Assert.True(text.Length < 2300, text.Length.ToString());
        Assert.Contains("add --json for the whole message", text, StringComparison.Ordinal);
        Assert.Contains("more characters not shown", json, StringComparison.Ordinal);
    }
}
