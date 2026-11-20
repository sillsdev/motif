using System.Text;
using SIL.Motif.Contract.Dispositions;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Corpus.Ledger;
using SIL.Motif.Worker.Store;
using Xunit;
using static SIL.Motif.Tests.Worker.Corpus.LedgerFixtures;

namespace SIL.Motif.Tests.Worker.Corpus;

public sealed class StoreDispositionTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("motif-storedisp-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static StoreDisposition Stored(string key, string category, string evidence = "linguist", string? intended = null) =>
        new(LedgerJoiner.Key(key), category, evidence, intended);

    private static IReadOnlyDictionary<string, StoreDisposition> Store(params StoreDisposition[] rows) =>
        rows.ToDictionary(row => row.Key, StringComparer.Ordinal);

    private static LedgerCoverageReport Build(string[] rows, (string, long)[] frequency, params StoreDisposition[] store)
    {
        var ledger = LedgerTsvReader.Read(Tsv(rows));
        return LedgerCoverage.Build(new(ledger, Sidecar(ledger.Rows.Count, ledger.TokenSum), Frequency(frequency),
            StoreDispositions: Store(store)));
    }

    private static CoverageDecision Only(LedgerCoverageReport report) => Assert.Single(report.Rows).Decision;

    private static string Shape(CoverageRow row) => string.Join('|', row.Type.Key, row.Decision.State, row.Decision.Freshness,
        row.Decision.Rule, row.Decision.Open, row.Decision.DispositionKind, string.Join(',', row.Decision.Reasons));

    private MotifDatabase Open(string name) => MotifDatabase.OpenOwned(Path.Combine(_root, name + ".motif.db"),
        new ProjectLocator(Path.Combine(_root, name + ".fwdata"), name), MotifSchema.CurrentSchema, new Version(1, 0));

    [Fact]
    public void AStoreDispositionMakesATypeWithNoLedgerRowAccountedWithoutAnyCheck()
    {
        var report = Build([Row("other", 5, "Unverified")], [("w", 4), ("other", 5)], Stored("w", "name"));

        var decision = report.Rows.Single(row => row.Type.Key == "w").Decision;
        Assert.Equal((ReportState.Disposition, StateFreshness.Current, "name"), (decision.State, decision.Freshness, decision.DispositionKind));
        Assert.Equal(0, report.Join.MissingTypes);
        Assert.Equal(new Tally(1, 4), report.StoreDispositions.StoreOnly);
        Assert.Equal(new Tally(1, 4), report.DispositionByKind["name"].Current);
    }

    [Fact]
    public void AStoreDispositionResolvesAnOpenLedgerTypeSoTheRecurringOpenFailureGoesAway()
    {
        var rows = new[] { Row("w", 9, "Open", "", "clitic") };
        var without = Build(rows, [("w", 9)]);
        var with = Build(rows, [("w", 9)], Stored("w", "loan"));

        Assert.Equal(new Tally(0, 0), without.RecurringOpenCurrent);
        Assert.Equal(new Tally(1, 9), without.RecurringOpenStale);
        Assert.Equal(new Tally(0, 0), with.RecurringOpenStale);
        Assert.Equal(ReportState.Disposition, Only(with).State);
        Assert.Equal(DoneVerdictKind.Pass, with.Done.Accounted.Verdict);
    }

    [Fact]
    public void AnAgreeingLedgerAndStoreCountAsBothAndAreNotConflicts()
    {
        var report = Build([Row("w", 4, "Disposition", "", "name", "a name")], [("w", 4)], Stored("w", "name"));

        Assert.Equal((new Tally(1, 4), new Tally(1, 4), new Tally(1, 4), default), (report.StoreDispositions.Store,
            report.StoreDispositions.Ledger, report.StoreDispositions.Both, report.StoreDispositions.Conflict));
        Assert.Equal(new Tally(0, 0), report.StoreDispositions.StoreOnly);
    }

    [Fact]
    public void ADisagreementIsCountedWithAnExampleAndTheLedgerWinsByTheStatedRule()
    {
        var report = Build([Row("w", 4, "Disposition", "", "name", "a name"), Row("x", 6, "Expected", Reading(1, 2))],
            [("w", 4), ("x", 6)], Stored("w", "loan"), Stored("x", "non-word"));

        Assert.Equal(new Tally(2, 10), report.StoreDispositions.Conflict);
        Assert.Equal(["x", "w"], report.StoreDispositions.ConflictExamples.Select(item => item.Key));
        Assert.Equal(("loan", "name"), (report.StoreDispositions.ConflictExamples[1].StoreClaim, report.StoreDispositions.ConflictExamples[1].LedgerClaim));
        Assert.Equal("name", report.Rows.Single(row => row.Type.Key == "w").Decision.DispositionKind);
        Assert.Equal((ReportState.Expected, null), (report.Rows.Single(row => row.Type.Key == "x").Decision.State, report.Rows.Single(row => row.Type.Key == "x").Decision.DispositionKind));
    }

    [Fact]
    public void AMisspellingsIntendedFormMatchesThroughNfdWhenTheStoreHoldsNfc()
    {
        var nfc = "café";
        var nfd = "café";
        var agree = Build([Row("w", 4, "Disposition", "", "misspelling", "intended=" + nfd + " | typo")], [("w", 4)],
            Stored("w", "misspelling", "typo", nfc));
        var differ = Build([Row("w", 4, "Disposition", "", "misspelling", "intended=cafe | typo")], [("w", 4)],
            Stored("w", "misspelling", "typo", nfc));

        Assert.Equal(new Tally(1, 4), agree.StoreDispositions.Both);
        Assert.Equal(default, agree.StoreDispositions.Conflict);
        Assert.Equal(new Tally(1, 4), differ.StoreDispositions.Conflict);
    }

    [Fact]
    public void AStoreMisspellingIsEncodedAsIntendedPlusEvidenceAndCountsAsAMisspellingDisposition()
    {
        var disposition = Stored("w", "misspelling", "seen in draft", "café");
        var report = Build([], [("w", 3)], disposition);

        Assert.Equal("intended=café | seen in draft", disposition.StateText);
        Assert.Equal("misspelling", Only(report).DispositionKind);
        Assert.Equal(ReportState.Disposition, Only(report).State);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("loan")]
    [InlineData("other-language")]
    [InlineData("abbreviation")]
    [InlineData("numeral")]
    [InlineData("non-word")]
    [InlineData("misspelling")]
    public void EveryCategoryMapsToItsOwnDispositionState(string category)
    {
        var report = Build([], [("w", 2)], Stored("w", category, "e", category == "misspelling" ? "ww" : null));

        Assert.Equal((ReportState.Disposition, StateFreshness.Current, category), (Only(report).State, Only(report).Freshness, Only(report).DispositionKind));
        Assert.Equal(new Tally(1, 2), report.StoreDispositions.StoreByCategory[category]);
    }

    [Fact]
    public void TheSevenCategoriesAreExactlyTheOnesTheMappingTests()
    {
        Assert.Equal(["name", "loan", "other-language", "abbreviation", "numeral", "non-word", "misspelling"], DispositionCategories.Names);
    }

    [Fact]
    public void StoreRowsOutsideTheFrequencyListAreCountedNotJoined()
    {
        var report = Build([], [("w", 2)], Stored("w", "name"), Stored("zzz", "name"));

        Assert.Equal(1, report.StoreDispositions.StoreRowsOutsideList);
        Assert.Equal(new Tally(1, 2), report.StoreDispositions.Store);
    }

    [Fact]
    public void CoverageIsUnchangedWhenTheStoreHoldsNoDispositions()
    {
        string[] rows = [Row("a", 9, "Open", "", "clitic"), Row("b", 3, "Disposition", "", "name", "n"), Row("c", 2, "Unverified")];
        (string, long)[] frequency = [("a", 9), ("b", 3), ("c", 2), ("d", 1)];
        var baseline = Build(rows, frequency);
        var empty = Build(rows, frequency, []);

        Assert.Equal(baseline.Done.Accounted.Counts, empty.Done.Accounted.Counts);
        Assert.Equal(baseline.Done.Accounted.Reasons, empty.Done.Accounted.Reasons);
        Assert.Equal(baseline.ByState, empty.ByState);
        Assert.Equal(baseline.DispositionByKind, empty.DispositionByKind);
        Assert.Equal(baseline.UnresolvedRecurring, empty.UnresolvedRecurring);
        Assert.Equal(baseline.RecurringOpenStale, empty.RecurringOpenStale);
        Assert.Equal(baseline.Rows.Select(Shape), empty.Rows.Select(Shape));
        Assert.Equal((baseline.Join.MissingTypes, baseline.Join.ResidualTypes), (empty.Join.MissingTypes, empty.Join.ResidualTypes));
        Assert.Same(baseline.Join.Types.GetType(), empty.Join.Types.GetType());
        Assert.Equal(StoreDispositionSummary.Empty.StoreOnly, empty.StoreDispositions.StoreOnly);
        Assert.Equal(new Tally(1, 3), empty.StoreDispositions.Ledger);
    }

    [Fact]
    public void ARealStoreReadsStandingRowsOnlyAndAnEndedRowIsIgnored()
    {
        using var database = Open("lane");
        var repository = new WordTypeDispositionRepository(database);
        repository.Record(["kept", "gone", "cleared"], DispositionCategory.Name, "e", null, "tester", replace: false);
        repository.Record(["gone"], DispositionCategory.Loan, "e", null, "tester", replace: true);
        repository.Clear(["cleared"], "tester", "no longer");
        repository.Record(["café"], DispositionCategory.Misspelling, "typo", "café", "tester", replace: false);

        var store = StoreDispositionReader.Read(repository);

        Assert.Equal(3, store.Count);
        Assert.False(store.ContainsKey("cleared"));
        Assert.Equal("loan", store["gone"].Category);
        var ledger = LedgerTsvReader.Read(Tsv());
        var report = LedgerCoverage.Build(new(ledger, Sidecar(0, 0), Frequency(("kept", 2), ("cleared", 2), ("gone", 3), ("café", 4)),
            StoreDispositions: store));
        Assert.Equal(new Tally(3, 9), report.StoreDispositions.StoreOnly);
        Assert.Equal(ReportState.Disposition, report.Rows.Single(row => row.Type.Key == "kept").Decision.State);
        Assert.Null(report.Rows.Single(row => row.Type.Key == "cleared").Decision.State);
    }

    [Fact]
    public void ABlockedJoinIgnoresTheStoreAndStaysUnmeasured()
    {
        var ledger = LedgerTsvReader.Read(Tsv());
        var frequency = Frequency(("w", 2), ("w", 3));
        var report = LedgerCoverage.Build(new(ledger, Sidecar(0, 0), frequency, StoreDispositions: Store(Stored("w", "name"))));

        Assert.True(report.Join.Blocked);
        Assert.Equal(default, report.StoreDispositions.StoreOnly);
    }

    [Fact]
    public void TheStoreOnlyTallySplitsTypesItResolvedFromTypesTheLedgerSaidNothingAbout()
    {
        var report = Build([Row("o", 9, "Open", "", "clitic"), Row("u", 4, "Unverified")], [("o", 9), ("u", 4), ("m", 2)],
            Stored("o", "name"), Stored("u", "loan"), Stored("m", "numeral"));

        var summary = report.StoreDispositions;
        Assert.Equal(new Tally(3, 15), summary.StoreOnly);
        Assert.Equal(new Tally(2, 13), summary.ResolvedOpen);
        Assert.Equal(new Tally(1, 2), summary.LedgerSilent);
        Assert.Equal(default, summary.ReplacedIncompatible);
        Assert.Equal(["o", "u"], summary.Fills.Where(item => item.Reason == StoreFillReason.ResolvedOpenOrUnverified).Select(item => item.Key));
        Assert.Equal(["m"], summary.Fills.Where(item => item.Reason == StoreFillReason.LedgerSilent).Select(item => item.Key));
    }

    [Fact]
    public void AStoreRowReplacesAnExpectedClaimAtAnIncompatibleRevisionAndItIsCounted()
    {
        var report = Build([Row("x", 6, "Expected", Reading(1, 2), revision: "other-rev"), Row("y", 5, "Lost", Reading(3, 4))],
            [("x", 6), ("y", 5)], Stored("x", "name"), Stored("y", "name"));

        var summary = report.StoreDispositions;
        Assert.Equal(new Tally(1, 6), summary.ReplacedIncompatible);
        Assert.Equal(new Tally(1, 6), summary.StoreOnly);
        Assert.Equal(new Tally(1, 5), summary.Conflict);
        Assert.Equal(ReportState.Disposition, report.Rows.Single(row => row.Type.Key == "x").Decision.State);
        Assert.Equal(StoreFillReason.ReplacedIncompatibleClaim, Assert.Single(summary.Fills).Reason);
    }

    [Fact]
    public void ClearingDuplicateLedgerRowsAndConflictingWeightsIsCountedNotSilent()
    {
        var report = Build([Row("d", 2, "Open"), Row("d", 3, "Unverified"), Row("w", 7, "Unverified")], [("d", 5), ("w", 4)],
            Stored("d", "name"), Stored("w", "name"));

        var summary = report.StoreDispositions;
        Assert.Equal(new Tally(1, 5), summary.ClearedDuplicateRows);
        Assert.Equal(new Tally(1, 4), summary.ClearedConflictingWeights);
        Assert.Equal(0, report.Join.DuplicateTypes);
        Assert.Equal(0, report.Join.ConflictTypes);
    }
}
