using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ProposalCompareViewModelTests
{
    internal sealed class Arranged : IAsyncDisposable
    {
        public required FakeCommandClient Fake { get; init; }
        public required WorkspaceContext Context { get; init; }
        public required OpenProposalDocuments Documents { get; init; }
        public required ProposalDocumentViewModel First { get; init; }
        public required ProposalDocumentViewModel Second { get; init; }
        public required ProposalComparisonScenario Scenario { get; init; }
        public int Backs { get; set; }
        public List<string> TriedAgain { get; } = [];

        public ProposalCompareViewModel Compare() => new(Context, [First, Second], () => Backs++,
            card => { TriedAgain.Add(card.ProposalId); return Task.CompletedTask; });

        public async ValueTask DisposeAsync()
        {
            foreach (var document in Documents.Items)
                foreach (var editor in document.Comments.ToArray()) editor.CancelCommand.Execute(null);
            await Documents.StopAsync();
            await Context.StopProjectWorkAsync();
        }
    }

    internal static WorkspaceBaseline Baselined() => new(true, string.Empty, string.Empty, string.Empty, string.Empty, null)
        { Token = WorkspaceContextTests.Assessment().Baseline.Token };

    internal static async Task<Arranged> ArrangeAsync(Action<ProposalComparisonScenario>? configure = null)
    {
        var scenario = new ProposalComparisonScenario()
            .Add("one", [new("fills", 0, 1), new("both", 0, 1), new("changes", 1, 2, "x-form")])
            .Add("two", [new("both", 0, 1), new("other", 0, 1), new("changes", 1, 2, "x-form")]);
        configure?.Invoke(scenario);
        var fake = new FakeCommandClient();
        scenario.InstallOn(fake);
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        context.Baseline = Baselined();
        var documents = new OpenProposalDocuments(context);
        var first = documents.Open("one");
        var second = documents.Open("two");
        await documents.RefreshAsync();
        return new Arranged { Fake = fake, Context = context, Documents = documents, First = first, Second = second,
            Scenario = scenario };
    }

    [UiThreadFact]
    public async Task ProvenSameEvidenceListsOnlyTheWordsTheTwoTreatDifferentlyAndNamesTheBasis()
    {
        await using var arranged = await ArrangeAsync();
        using var compare = arranged.Compare();

        Assert.Equal(ProposalComparisonState.Comparable, compare.State);
        Assert.True(compare.IsComparable);
        Assert.Contains("Genesis 1", compare.BasisText);
        Assert.Equal(["fills", "other"], compare.Rows.Select(row => row.Word).Order());
        var fills = compare.Rows.Single(row => row.Word == "fills");
        Assert.Equal(MotifAppStrings.BoardWillFillIn, fills.FirstEffect);
        Assert.Equal(MotifAppStrings.ProposalCompareNoChange, fills.SecondEffect);
        var other = compare.Rows.Single(row => row.Word == "other");
        Assert.Equal(MotifAppStrings.ProposalCompareNoChange, other.FirstEffect);
        Assert.Equal(MotifAppStrings.BoardWillFillIn, other.SecondEffect);
        Assert.DoesNotContain(compare.Rows, row => row.Word is "both" or "changes");
        Assert.True(compare.First.ShowsEffects);
        Assert.Equal(["fills", "both"], compare.First.WillFillIn.Select(word => word.Word));
        Assert.Equal("one", compare.First.Document.ProposalId);
        Assert.Equal("two", compare.Second.Document.ProposalId);
    }

    [UiThreadFact]
    public async Task TheSameLabelWithADifferentResultStillDiffersAndSaysSo()
    {
        await using var arranged = await ArrangeAsync(scenario => scenario
            .Add("two", [new("both", 0, 1), new("changes", 1, 2, "y-form")]));
        using var compare = arranged.Compare();

        var row = Assert.Single(compare.Rows, row => row.Word == "changes");
        Assert.Contains(MotifAppStrings.ProposalReadingChanged, row.FirstEffect);
        Assert.NotEqual(MotifAppStrings.ProposalReadingChanged, row.FirstEffect);
        Assert.Equal(row.FirstEffect, row.SecondEffect);
    }

    [UiThreadFact]
    public async Task TwoProposalsThatAgreeEverywhereAreComparableWithNoDifferingWords()
    {
        await using var arranged = await ArrangeAsync(scenario => scenario
            .Add("two", [new("fills", 0, 1), new("both", 0, 1), new("changes", 1, 2, "x-form")]));
        using var compare = arranged.Compare();

        Assert.True(compare.IsComparable);
        Assert.Empty(compare.Rows);
        Assert.True(compare.ShowsNoDifference);
        Assert.False(compare.HasRows);
    }

    public static IEnumerable<object[]> Breaks() =>
    [
        [ProposalComparisonReason.DifferentText, true, Break(s => s with { TextId = "text/two" })],
        [ProposalComparisonReason.DifferentWords, true,
            Break(s => s with { Before = s.Before with { SelectionSha256 = "sha256:other" },
                After = s.After with { SelectionSha256 = "sha256:other" } })],
        [ProposalComparisonReason.DifferentWords, true,
            Break(s => s with { Before = s.Before with { TokeniserVersion = "2" },
                After = s.After with { TokeniserVersion = "2" } })],
        [ProposalComparisonReason.DifferentWords, true,
            Break(s => s with { Before = s.Before with { GrammarSourceSha256 = "sha256:other" } })],
        [ProposalComparisonReason.DifferentDecisions, true, Break(s => s with { DecisionsDigest = "sha256:other" })],
        [ProposalComparisonReason.DifferentDecisions, true, Break(s => s with { Reference = "source-validated" })],
        [ProposalComparisonReason.DecisionsNotRecorded, true, Break(s => s with { DecisionsDigest = null })],
        [ProposalComparisonReason.DifferentAssessor, true, Break(s => s with { Assessor = "other" })],
        [ProposalComparisonReason.Incomplete, false, Break(s => s with { UnboundOccurrences = 3 })],
        [ProposalComparisonReason.Incomplete, false, Break(s => s with { Limitations =
            [new(HumanAgreementLimitation.Capped, 1, 1)] })],
    ];

    private static Func<ProposalDifferenceSummary, ProposalDifferenceSummary> Break(
        Func<ProposalDifferenceSummary, ProposalDifferenceSummary> change) => change;

    [UiThreadTheory]
    [MemberData(nameof(Breaks))]
    public async Task OneBrokenFactOfTheCommonReferenceMakesTheComparisonUnavailableAndNeverSubtracts(
        ProposalComparisonReason reason, bool remeasurable, Func<ProposalDifferenceSummary, ProposalDifferenceSummary> change)
    {
        await using var arranged = await ArrangeAsync(scenario => scenario.Replace("two", change));
        using var compare = arranged.Compare();

        Assert.Equal(ProposalComparisonState.Unavailable, compare.State);
        Assert.Equal(reason, compare.Reason);
        Assert.False(compare.IsComparable);
        Assert.True(compare.ShowsNotice);
        Assert.Empty(compare.Rows);
        Assert.False(compare.HasRows);
        Assert.False(compare.ShowsNoDifference);
        Assert.Equal(string.Empty, compare.BasisText);
        Assert.NotEqual(string.Empty, compare.NoticeText);
        Assert.Equal(MotifAppStrings.ProposalCompareUnavailableTitle, compare.NoticeTitle);
        Assert.Equal(remeasurable, compare.CanRemeasure);
        Assert.Equal(remeasurable, compare.RemeasureCommand.CanExecute(null));
    }

    [UiThreadFact]
    public async Task NoSavedDifferenceOnASideIsUnavailableNotZero()
    {
        await using var arranged = await ArrangeAsync(scenario => scenario.Missing.Add("two"));
        await arranged.Second.RefreshAsync();
        using var compare = arranged.Compare();

        Assert.Equal(ProposalComparisonState.Unavailable, compare.State);
        Assert.Equal(MotifAppStrings.ProposalCompareReasonNoEvidence, compare.NoticeText);
        Assert.Empty(compare.Rows);
        Assert.False(compare.ShowsNoDifference);
        Assert.True(compare.CanRemeasure);
    }

    [UiThreadFact]
    public async Task TheStandingsOverlapNoteNeverMakesAnUnprovenPairComparable()
    {
        await using var arranged = await ArrangeAsync(scenario => scenario
            .Replace("two", s => s with { TextId = "text/two" }));
        arranged.First.Card = new ProposalCardViewModel(new("one", "One", "proposed", ProposalGroup.NotTried, false, [],
            TrialStanding.NotTried, null, ["two"]));
        arranged.Second.Card = new ProposalCardViewModel(new("two", "Two", "proposed", ProposalGroup.NotTried, false, [],
            TrialStanding.NotTried, null, []));
        using var compare = arranged.Compare();

        Assert.True(compare.HasOverlap);
        Assert.False(compare.IsComparable);
        Assert.Empty(compare.Rows);
        Assert.Equal(string.Empty, compare.BasisText);
    }

    [UiThreadFact]
    public async Task ThePairMustBeExactlyTwoDocuments()
    {
        await using var arranged = await ArrangeAsync();
        Assert.Throws<ArgumentException>(() => new ProposalCompareViewModel(arranged.Context, [arranged.First],
            () => { }));
        Assert.Throws<ArgumentException>(() => new ProposalCompareViewModel(arranged.Context,
            [arranged.First, arranged.Second, arranged.Documents.Open("three")], () => { }));
    }

    [UiThreadFact]
    public async Task BackAndDisposeLeaveTheDocumentsAndTheirChecksAlone()
    {
        await using var arranged = await ArrangeAsync();
        Assert.True(arranged.Documents.Check(arranged.First, true));
        Assert.True(arranged.Documents.Check(arranged.Second, true));
        arranged.First.AddCommentCommand.Execute(null);
        var editor = Assert.Single(arranged.First.Comments);
        editor.Document.Paragraphs[0].Runs[0].Text = "Unsaved while comparing";
        var compare = arranged.Compare();
        compare.BackCommand.Execute(null);
        compare.Dispose();

        Assert.Equal(1, arranged.Backs);
        Assert.True(arranged.First.IsCheckedForComparison);
        Assert.True(arranged.Second.IsCheckedForComparison);
        Assert.Same(editor, Assert.Single(arranged.First.Comments));
        Assert.Equal("Unsaved while comparing", editor.Text());
        Assert.Equal(2, arranged.Documents.Items.Count);
        var changes = 0;
        compare.PropertyChanged += (_, _) => changes++;
        await arranged.Second.RefreshAsync();
        Assert.Equal(0, changes);
    }

    [UiThreadFact]
    public async Task DiagnosticsFollowsThePersonSwitchAndShowsForBothSidesOnlyWhenOn()
    {
        await using var arranged = await ArrangeAsync();
        using var compare = arranged.Compare();
        Assert.False(compare.DiagnosticsVisible);
        arranged.Context.Diagnostics = true;
        Assert.True(compare.DiagnosticsVisible);
        Assert.True(compare.First.Document.DiagnosticsVisible);
        Assert.True(compare.Second.Document.DiagnosticsVisible);
        arranged.Context.Diagnostics = false;
        Assert.False(compare.DiagnosticsVisible);
        Assert.False(compare.First.Document.DiagnosticsVisible);
    }
}
