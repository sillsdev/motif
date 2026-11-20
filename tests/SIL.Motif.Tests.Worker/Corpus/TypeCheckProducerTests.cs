using System.Text.Json;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Corpus.Ledger;
using SIL.Motif.Worker.Store;
using Xunit;
using static SIL.Motif.Tests.Worker.Corpus.LedgerFixtures;

namespace SIL.Motif.Tests.Worker.Corpus;

public sealed class TypeCheckProducerTests : IDisposable
{
    private const string OtherPin = "other-pin";
    private readonly string _root = Directory.CreateTempSubdirectory("motif-producer-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static readonly ParsedAnalysis Path1 = new("cat", [Id(1), Id(2)], 0, "id-1");

    private static AssessedWord Analysed(string word) => new(word, "analysed", [Path1]);
    private static AssessedWord Miss(string word) => new(word, "no-analysis", []);
    private static AssessedWord Incomplete(string word) => new(word, "timed-out", []) { IsIncomplete = true };

    private static AssessmentRecord Saved(string id, string pin, string savedUtc, params AssessedWord[] words) => new(
        id, null, null, "pangloss", "ParseTime", "{}", "sha256:scope",
        new AssessmentSubject.Words(Selection.Create("p", words.Select(word => word.Word).ToArray()), "none", "1"),
        "{}", null, null, pin, null, null, null, savedUtc, new AssessmentRecordDetails.Words(words, null, null, []));

    private static TypeCheck Produce(string word, params AssessmentRecord[] records) =>
        TypeCheckProducer.Produce(records, Revision)[LedgerJoiner.Key(word)];

    [Fact]
    public void ACheckMadeAtAnotherPinIsNotComparableAndSaysWhy()
    {
        var check = Produce("w", Saved("a", OtherPin, "2026-01-01T00:00:00Z", Miss("w")));

        Assert.False(check.Comparable);
        Assert.Equal(CheckIncomparable.RevisionIncompatible, check.Incomparable);
        Assert.Equal(OtherPin, check.GrammarPin);
    }

    [Fact]
    public void AStoredPinWithTheSha256PrefixMatchesAnUnprefixedReportPin()
    {
        var check = TypeCheckProducer.Produce([Saved("a", "sha256:" + Pin, "2026-01-01T00:00:00Z", Miss("w"))], Pin)["w"];

        Assert.True(check.Comparable);
    }

    [Fact]
    public void ACheckMadeAtTheReportPinIsComparable()
    {
        var check = Produce("w", Saved("a", Revision, "2026-01-01T00:00:00Z", Miss("w")));

        Assert.True(check.Comparable);
        Assert.Equal(CheckIncomparable.None, check.Incomparable);
    }

    [Fact]
    public void AnIncompleteAssessmentGivesNullReadingsAndNoProvedMiss()
    {
        var check = Produce("w", Saved("a", Revision, "2026-01-01T00:00:00Z", Incomplete("w")));

        Assert.Equal(CheckCompleteness.Partial, check.Completeness);
        Assert.Null(check.ProducedReadings);
        Assert.False(check.ProvedPresent);
    }

    [Fact]
    public void ACompleteMissKeepsAnExplicitEmptySetAndSoProvesALoss()
    {
        var check = Produce("w", Saved("a", Revision, "2026-01-01T00:00:00Z", Miss("w")));
        Assert.Equal(CheckCompleteness.Complete, check.Completeness);
        Assert.NotNull(check.ProducedReadings);
        Assert.Empty(check.ProducedReadings);

        var report = Report([Row("w", 5, "Expected", Reading(1, 2))], [("w", 5)],
            new Dictionary<string, TypeCheck> { ["w"] = check });
        Assert.Equal(ReportState.Lost, report.Rows.Single().Decision.State);
        Assert.Equal(StateFreshness.Current, report.Rows.Single().Decision.Freshness);
    }

    [Fact]
    public void AFoundPathProvesPresenceWithoutClaimingAReadingSet()
    {
        var check = Produce("w", Saved("a", Revision, "2026-01-01T00:00:00Z", Analysed("w")));

        Assert.True(check.ProvedPresent);
        Assert.Equal(CheckCompleteness.Partial, check.Completeness);
        Assert.Null(check.ProducedReadings);
    }

    [Fact]
    public void TheLatestCompleteAssessmentAtThePinWins()
    {
        var older = Saved("old", Revision, "2026-01-01T00:00:00Z", Analysed("w"));
        var newer = Saved("new", Revision, "2026-02-01T00:00:00Z", Miss("w"));
        var newestButIncomplete = Saved("late", Revision, "2026-03-01T00:00:00Z", Incomplete("w"));
        var newestOtherPin = Saved("other", OtherPin, "2026-04-01T00:00:00Z", Analysed("w"));

        var check = Produce("w", newestOtherPin, newer, newestButIncomplete, older);

        Assert.True(check.Comparable);
        Assert.Equal(CheckCompleteness.Complete, check.Completeness);
        Assert.False(check.ProvedPresent);
    }

    [Fact]
    public void BetweenTwoChecksOfTheSameStandingTheLaterWins()
    {
        var older = Saved("old", Revision, "2026-01-01T00:00:00Z", Incomplete("w"));
        var newer = Saved("new", Revision, "2026-02-01T00:00:00Z", Analysed("w"));

        Assert.True(Produce("w", older, newer).ProvedPresent);
        Assert.True(Produce("w", newer, older).ProvedPresent);
    }

    [Fact]
    public void ANewerParseOverridesAnOlderMissAndANewerMissOverridesAnOlderParse()
    {
        var miss = Saved("miss", Revision, "2026-01-01T00:00:00Z", Miss("w"));
        var parse = Saved("parse", Revision, "2026-02-01T00:00:00Z", Analysed("w"));
        Assert.True(Produce("w", miss, parse).ProvedPresent);

        var olderParse = Saved("parse2", Revision, "2026-01-01T00:00:00Z", Analysed("w"));
        var newerMiss = Saved("miss2", Revision, "2026-02-01T00:00:00Z", Miss("w"));
        var check = Produce("w", olderParse, newerMiss);
        Assert.Equal(CheckCompleteness.Complete, check.Completeness);
        Assert.False(check.ProvedPresent);
    }

    [Fact]
    public void AnUnfinishedAssessmentNeverOverridesAFinishedOneEitherWay()
    {
        var late = Saved("late", Revision, "2026-03-01T00:00:00Z", Incomplete("w"));
        Assert.True(Produce("w", Saved("p", Revision, "2026-01-01T00:00:00Z", Analysed("w")), late).ProvedPresent);
        Assert.Equal(CheckCompleteness.Complete,
            Produce("w", Saved("m", Revision, "2026-01-01T00:00:00Z", Miss("w")), late).Completeness);
    }

    [Fact]
    public void ALowercaseSecondPassRowChecksNothingForItsOwnForm()
    {
        var row = Miss("w") with { LowercaseOf = "W" };

        var checks = TypeCheckProducer.Produce([Saved("a", Revision, "2026-01-01T00:00:00Z", row)], Revision);

        Assert.Empty(checks);
    }

    private static AssessedWord WithEvidence(string word, bool capped, bool timedOut) => Miss(word) with
    {
        Morphology = new ParseWordEvidence("s", 0, word, 1, capped, timedOut, false, [], []),
    };

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ACappedOrStoppedWordStaysPartialWithNoProvedMiss(bool capped, bool timedOut)
    {
        var check = Produce("w", Saved("a", Revision, "2026-01-01T00:00:00Z", WithEvidence("w", capped, timedOut)));

        Assert.Equal(CheckCompleteness.Partial, check.Completeness);
        Assert.Null(check.ProducedReadings);
        Assert.False(check.ProvedPresent);
    }

    [Fact]
    public void AnUnsupportedGrammarWarningKeepsAMissFromProvingAnything()
    {
        var invocation = new BatchInvocationEvidence("inv", "s.fwdata", "sha", "sha", "w.txt", "sha", "r.tsv", "sha", "e.txt", "sha",
            1000, 200000, 1, true) { GrammarWarnings = "capability: unsupported feature" };
        var record = Saved("a", Revision, "2026-01-01T00:00:00Z", Miss("w")) with
        {
            Details = new AssessmentRecordDetails.Words([Miss("w")], null, null, [], invocation),
        };

        var check = Produce("w", record);

        Assert.Equal(CheckCompleteness.Partial, check.Completeness);
        Assert.Null(check.ProducedReadings);
    }

    [Fact]
    public void ARealStoreLaneGoesFromUnmeasuredToMeasuredDone()
    {
        using var database = MotifDatabase.OpenOwned(Path.Combine(_root, "lane.motif.db"),
            new ProjectLocator(Path.Combine(_root, "lane.fwdata"), "lane"), MotifSchema.CurrentSchema, new Version(1, 0));
        var repository = new AssessmentRepository(database);
        var words = new[] { Analysed("a"), Analysed("b"), Miss("c") };
        repository.Record(NewRecord("one", words));

        var ledger = LedgerTsvReader.Read(Tsv(Row("a", 9, "Unverified"), Row("b", 9, "Unverified"), Row("c", 9, "Open", "", "clitic")));
        var sidecar = Sidecar(ledger.Rows.Count, ledger.TokenSum);
        var frequency = Frequency(("a", 9), ("b", 9), ("c", 9));
        var before = LedgerCoverage.Build(new(ledger, sidecar, frequency));
        Assert.Equal(DoneVerdictKind.Unmeasured, before.Done.Accounted.Verdict);
        Assert.Equal(DoneVerdictKind.Unmeasured, before.Done.Verdict);

        var checks = TypeCheckProducer.FromStore(repository, Revision);
        var after = LedgerCoverage.Build(new(ledger, sidecar, frequency, checks));

        Assert.Equal(DoneVerdictKind.Fail, after.Done.Accounted.Verdict);
        Assert.Equal(["recurring-open-current"], after.Done.Accounted.Reasons);
        Assert.Equal(DoneVerdictKind.Fail, after.Done.Verdict);
        Assert.Equal(0, after.NoState.Types);
        Assert.All(after.Rows, row => Assert.Equal(StateFreshness.Current, row.Decision.Freshness));
    }

    [Fact]
    public void AFixtureLaneWithEveryRecurringTypeProvedPassesAccountedAndCorrectLeavingStableUnmeasured()
    {
        using var database = MotifDatabase.OpenOwned(Path.Combine(_root, "lane2.motif.db"),
            new ProjectLocator(Path.Combine(_root, "lane2.fwdata"), "lane2"), MotifSchema.CurrentSchema, new Version(1, 0));
        var repository = new AssessmentRepository(database);
        repository.Record(NewRecord("one", [Analysed("a"), Analysed("b")]));
        var ledger = LedgerTsvReader.Read(Tsv(Row("a", 9, "Open", "", "clitic"), Row("b", 9, "Unverified")));
        var sidecar = Sidecar(ledger.Rows.Count, ledger.TokenSum);
        var frequency = Frequency(("a", 9), ("b", 9));
        var ids = Enumerable.Range(0, DoneVerdict.MinimumSample).Select(n => "occ-" + n).ToArray();
        var audit = Path.Combine(_root, "audit");
        var manifest = new AuditManifest("m", frequency.Digest, Pin, "seeded", 7, "scorer", "1", "ref-rev", "ref-digest", ["s1"]);
        AuditSampleWriter.Write(audit, manifest, ids, Revision, ids.Select(id => new AuditOutcome(id, "right")).ToArray());
        var checks = TypeCheckProducer.FromStore(repository, Revision);

        var report = LedgerCoverage.Build(new(ledger, sidecar, frequency, checks, null, audit, null));

        Assert.Equal(DoneVerdictKind.Pass, report.Done.Accounted.Verdict);
        Assert.Equal(DoneVerdictKind.Pass, report.Done.Correct.Verdict);
        Assert.Equal(["protected-set-absent"], report.Done.Stable.Reasons);
    }

    private static NewAssessmentRecord NewRecord(string id, AssessedWord[] words) => new(
        id, null, null, "pangloss", "ParseTime", "{\"words\":\"all\"}", "sha256:scope",
        new AssessmentSubject.Words(Selection.Create("p", words.Select(word => word.Word).ToArray()), "none", "1"),
        JsonSerializer.Serialize(new BaselineToken("project", "sha256:" + new string('a', 64), "projection-1",
            "2020-01-01T00:00:00Z", "sha256:" + new string('b', 64))),
        "sha256:o", "sha256:s", Revision, "fp", "pipe", 0,
        new AssessmentRecordMaterial.Words(words, null, null, [], new BatchInvocationEvidence(
            id, "source.fwdata", Revision, "sha256:exe", "words.txt", "sha256:words", "rows.tsv", "sha256:rows",
            "stderr.txt", "sha256:stderr", 1000, 200000, 1, true)));
}
