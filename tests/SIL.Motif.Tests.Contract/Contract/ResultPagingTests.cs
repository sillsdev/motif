using System.Text.Json.Nodes;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Contract;

/// <summary>A capped read keeps its total, names the next offset, and treats a window past the end as an empty page.</summary>
public sealed class ResultPagingTests
{
    private static GrammarWarning Finding(int index, GrammarDiagnosticLevel level) =>
        new(level, "label", [], [], "finding " + index) { Code = "code-" + index % 3 };

    [Theory]
    [InlineData(0, 1, true)]
    [InlineData(0, 200, true)]
    [InlineData(-1, 10, false)]
    [InlineData(0, 0, false)]
    [InlineData(0, 201, false)]
    public void TheWindowRangeIsOneRuleForEveryCappedRead(int offset, int limit, bool valid) =>
        Assert.Equal(valid, ResultPaging.IsValid(offset, limit));

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    [InlineData(1000000)]
    public void ACursorNamesTheOffsetItWasMadeFrom(int offset)
    {
        Assert.True(ResultPaging.TryOffset(ResultPaging.Cursor(offset), out var read));
        Assert.Equal(offset, read);
    }

    [Theory]
    [InlineData("25")]
    [InlineData("!!")]
    [InlineData("cA")]
    [InlineData("cC0x")]
    public void ACursorTheApiDidNotIssueIsNotAnOffset(string cursor)
    {
        Assert.False(ResultPaging.TryOffset(cursor, out _));
        Assert.Equal(-1, ResultPaging.OffsetOrInvalid(cursor));
    }

    [Fact]
    public void NoCursorIsTheFirstPage()
    {
        Assert.True(ResultPaging.TryOffset(null, out var offset));
        Assert.Equal(0, offset);
    }

    [Fact]
    public void ThePageAfterTheLastRowHasNoNextCursor()
    {
        Assert.Equal(ResultPaging.Cursor(25), ResultPaging.NextCursor(0, 25, 60));
        Assert.Equal(ResultPaging.Cursor(60), ResultPaging.NextCursor(35, 25, 100));
        Assert.Null(ResultPaging.NextCursor(50, 10, 60));
        Assert.Null(ResultPaging.NextCursor(99, 0, 60));
    }

    [Fact]
    public void ProposalListPagesConcatenateToTheFullListAndNameTheTotal()
    {
        var all = Enumerable.Range(0, 60).Select(i => new ProposalListItem("proposal/" + i, "draft", "label " + i)).ToArray();
        var list = new ProposalListProjection(all);
        var seen = new List<string>();
        for (var offset = 0; ; )
        {
            var page = list.Window(offset, 25);
            Assert.Equal(60, page.Total);
            Assert.Equal(offset, page.Offset);
            seen.AddRange(page.Proposals.Select(item => item.ProposalId));
            if (page.NextCursor is not { } next) break;
            Assert.True(ResultPaging.TryOffset(next, out offset));
        }
        Assert.Equal(all.Select(item => item.ProposalId), seen);

        var pastEnd = list.Window(500, 25);
        Assert.Empty(pastEnd.Proposals);
        Assert.Equal(60, pastEnd.Total);
        Assert.Null(pastEnd.NextCursor);
    }

    [Fact]
    public void ProposalDetailPagesItsOperationsAndKeepsTheRest()
    {
        var operations = Enumerable.Range(0, 23).Select(i =>
            new ProposalOperationView("op" + i, "lexical/lexSense/setGloss", null, null, [], null)).ToArray();
        var detail = new ProposalDetailProjection("proposal/one", "draft", "label", null, null, operations);

        var page = detail.Window(10, 10);

        Assert.Equal(23, page.OperationTotal);
        Assert.Equal(10, page.OperationOffset);
        Assert.Equal(ResultPaging.Cursor(20), page.NextCursor);
        Assert.Equal("op10", page.Operations[0].OperationId);
        Assert.Equal("proposal/one", page.ProposalId);
        Assert.Null(detail.Window(20, 10).NextCursor);
        Assert.Equal(3, detail.Window(20, 10).Operations.Count);
        Assert.Null(detail.OperationTotal);
    }

    [Fact]
    public void AProposalOfMoreThanTenOperationsIsCompleteInJsonAndCutOnlyWhenAPageIsAsked()
    {
        var operations = Enumerable.Range(0, 23).Select(i =>
            new ProposalOperationView("op" + i, "lexical/lexSense/setGloss", null, null, [], null)).ToArray();
        var detail = new ProposalDetailProjection("proposal/one", "draft", "label", null, null, operations);

        var whole = JsonNode.Parse(ProjectionJson.Serialize(detail.WithTotal()))!;
        Assert.Equal(23, whole["operations"]!.AsArray().Count);
        Assert.Equal(23, whole["operationTotal"]!.GetValue<int>());
        Assert.Null(whole["operationsTruncated"]);

        var cut = JsonNode.Parse(ProjectionJson.Serialize(detail.Window(0, 10)))!;
        Assert.Equal(10, cut["operations"]!.AsArray().Count);
        Assert.True(cut["operationsTruncated"]!.GetValue<bool>());
    }

    [Fact]
    public void WarningsPageKeepsEveryCountAndTheCodeRows()
    {
        var findings = Enumerable.Range(0, 30)
            .Select(i => Finding(i, i % 2 == 0 ? GrammarDiagnosticLevel.Warning : GrammarDiagnosticLevel.Information)).ToArray();
        var byKind = new[] { new GrammarWarningSummary("code-0", null, GrammarDiagnosticLevel.Warning, 10) };
        var response = new WarningsResponse(true, true, findings, byKind, 15, 15);

        var page = response.Window(0, 10);

        Assert.Equal(10, page.Findings.Count);
        Assert.Equal(30, page.TotalCount);
        Assert.Equal(30, page.FindingsTotal);
        Assert.Equal(ResultPaging.Cursor(10), page.NextCursor);
        Assert.Same(byKind, page.ByKind);
        Assert.Equal(15, page.WarningCount);
        Assert.Equal(30, response.TotalCount);
        Assert.Null(response.NextCursor);
        Assert.Null(response.Window(30, 10).NextCursor);
        Assert.Empty(response.Window(500, 10).Findings);
    }

    [Fact]
    public void AnOverlongRefusalKeepsItsNextTail()
    {
        var message = new string('x', 5000) + " Next: run motif baseline capture.";

        var terminal = RefusalText.ForTerminal(message, "a.b");

        Assert.StartsWith("[a.b] ", terminal, StringComparison.Ordinal);
        Assert.EndsWith("Next: run motif baseline capture.", terminal, StringComparison.Ordinal);
        Assert.EndsWith("Next: run motif baseline capture.", RefusalText.Bound(message, 1000), StringComparison.Ordinal);
    }

    private static bool WellFormed(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++;
            else if (char.IsSurrogate(text[i])) return false;
        }
        return true;
    }

    [Fact]
    public void ARefusalCutNeverSplitsASurrogatePairOrAGraphemeCluster()
    {
        var atHead = new string('x', 749) + "\U0001F600" + new string('y', 5000);
        var atTail = new string('x', 5000) + "\U0001F600" + new string('y', 249);
        var cluster = new string('x', 748) + "e\u0301\u0302" + new string('y', 5000);

        foreach (var text in new[] { atHead, atTail, cluster })
        {
            var cut = RefusalText.Bound(text, 1000);
            Assert.True(WellFormed(cut), cut);
            Assert.Contains("chars omitted", cut, StringComparison.Ordinal);
        }
        Assert.StartsWith(new string('x', 749) + " ", RefusalText.Bound(atHead, 1000), StringComparison.Ordinal);
        Assert.DoesNotContain("\u0301", RefusalText.Bound(cluster, 1000).Split(" … ")[0].Substring(745), StringComparison.Ordinal);
        Assert.StartsWith("[a.b] ", RefusalText.ForTerminal(atTail + atHead, "a.b"), StringComparison.Ordinal);
        Assert.True(WellFormed(RefusalText.ForTerminal(atTail + atHead, "a.b")));
    }

    [Fact]
    public void AnOversizeNextSentenceKeepsItsLabelAndItsEnd()
    {
        var message = new string('x', 3000) + " Next: START " + new string('n', 5000) + " END";

        var cut = RefusalText.Bound(message, 1000);

        Assert.Contains("Next: START", cut, StringComparison.Ordinal);
        Assert.EndsWith(" END", cut, StringComparison.Ordinal);
        Assert.True(cut.Length < 1200, cut.Length.ToString());
    }

    [Fact]
    public void TheStandingsKeepAtMostOnePageOfUnreadableItemsAndCountTheRest()
    {
        var unreadable = Enumerable.Range(0, 60).Select(i => new UnreadableProposal("p" + i, "l", "bad")).ToArray();
        var standings = new ProposalStandings([], 0) { Unreadable = unreadable };

        var page = standings.Window(0, 25);

        Assert.Equal(ResultPaging.DefaultLimit, page.Unreadable.Count);
        Assert.Equal(60, page.UnreadableTotal);
    }

    [Fact]
    public void GrammarCheckListsErrorsFirstWhateverOrderTheParserGaveThem()
    {
        var findings = Enumerable.Range(0, 30).Select(i => Finding(i, i < 25 ? GrammarDiagnosticLevel.Information : GrammarDiagnosticLevel.Error)).ToArray();

        var page = new GrammarCheckResponse(findings, true).Window(0, 10);

        Assert.Equal(5, page.Findings.Count(f => f.Severity == GrammarDiagnosticLevel.Error));
        Assert.Equal(GrammarDiagnosticLevel.Error, page.Findings[0].Severity);
        Assert.Equal(GrammarDiagnosticLevel.Information, page.Findings[5].Severity);
    }

    [Fact]
    public void GrammarCheckPageKeepsThePerLevelTotals()
    {
        var findings = Enumerable.Range(0, 40).Select(i => Finding(i, (GrammarDiagnosticLevel)(i % 3))).ToArray();
        var response = new GrammarCheckResponse(findings, true);

        var page = response.Window(10, 10);

        Assert.Equal(10, page.Findings.Count);
        Assert.Equal(40, page.FindingsTotal);
        Assert.Equal(ResultPaging.Cursor(20), page.NextCursor);
        Assert.Equal(findings.Count(f => f.Severity == GrammarDiagnosticLevel.Error), page.ErrorTotal);
        Assert.Equal(findings.Count(f => f.Severity == GrammarDiagnosticLevel.Warning), page.WarningTotal);
        Assert.Equal(40, page.ErrorTotal + page.WarningTotal + page.InformationTotal);
    }

    [Fact]
    public void ABatchWithinTheCapIsReturnedAsItIs()
    {
        var batch = Batch(intents: 5, operationsEach: 4);

        Assert.Same(batch, batch.Bounded());
    }

    [Fact]
    public void ALargeBatchListsTheFirstOperationsAndAccountsForTheRest()
    {
        var batch = Batch(intents: 400, operationsEach: 5);

        var bounded = batch.Bounded();

        Assert.Equal(ComposedBatchResponse.ListedOperationLimit, bounded.Intents.Sum(intent => intent.Operations.Count));
        Assert.True(bounded.Intents.Count <= ComposedBatchResponse.ListedOperationLimit);
        Assert.Equal(400, bounded.IntentTotal);
        Assert.Equal(2000, bounded.OperationsAdded);
        Assert.Equal(2000, bounded.OperationCount);
        Assert.Contains("motif show d --project <fwdata> --all", bounded.NextStep, StringComparison.Ordinal);
        Assert.All(bounded.Intents.Where(intent => intent.Operations.Count < 5), intent => Assert.Equal(5, intent.OperationTotal));
        Assert.Equal(400, batch.Intents.Count);
    }

    [Fact]
    public void AnOverlongRefusalKeepsItsStartAndSaysHowMuchWasCut()
    {
        var message = new string('x', 5000);

        Assert.Equal(message, RefusalText.Bound(message));
        var terminal = RefusalText.ForTerminal(message);
        Assert.True(terminal.Length < RefusalText.TextLimit + 120, terminal.Length.ToString());
        Assert.Contains("chars omitted; add --json for the whole message", terminal, StringComparison.Ordinal);
        var facts = RefusalText.Bound(new Dictionary<string, string> { ["a"] = new string('y', 20000), ["b"] = "short" });
        Assert.Contains("chars omitted", facts["a"], StringComparison.Ordinal);
        Assert.True(facts["a"].Length < 16100);
        Assert.Equal("short", facts["b"]);
    }

    private static ComposedBatchResponse Batch(int intents, int operationsEach) => new("d",
        Enumerable.Range(0, intents).Select(i => new ComposedIntentResult(i, "compose-author-phoneme", "composer",
            Enumerable.Range(0, operationsEach).Select(o => new OperationSummary($"op{i}-{o}", "lexical/create")).ToArray()))
            .ToArray(),
        intents * operationsEach);
}
