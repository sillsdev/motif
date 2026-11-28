using System.Collections.Immutable;
using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Host.HandAnalysis;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;
using static SIL.Motif.Tests.TestFixtures.ProposalCompareExports;

namespace SIL.Motif.Tests.Commands.Review;

public sealed class ProposalCompareReadTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static FormSpec F(string form, int[] before, int[] after, string? ws = "ws", bool dispatched = true,
        bool complete = true) => new(form, before, after, ws, dispatched, complete);

    private static void InStore(Action<MotifDatabase, ProjectLocator> test)
    {
        var root = Directory.CreateTempSubdirectory("motif-proposal-compare-").FullName;
        try
        {
            var path = Path.Combine(root, "project.fwdata");
            File.WriteAllText(path, "");
            var project = new ProjectLocator(path, "project");
            using var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
                MotifSchema.CurrentSchema, new(1, 0));
            test(database, project);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static SIL.Motif.Contract.Commands.CommandOutcome<ProposalComparison> Compare(ProjectLocator project,
        string[] ids, string? at = null, int? page = null, string? snapshot = null,
        ProposalCompareReads.ApprovedLoader? approved = null, string[]? jobs = null, string? cursor = null) =>
        ProposalCompareReads.Read(new(project.FullFwDataPath, ids,
            at, cursor ?? (page is { } number ? ResultPaging.Cursor((number - 1) * CompareRowPage.Size) : null), snapshot, jobs), approved ?? Approvals([]));

    private static ProposalCompareReads.ApprovedLoader Approvals(ApprovedAnalysisFact[] facts, string stamp = "stamp-1",
        int skipped = 0) => (_, _, _) => CommandOutcome<ProposalCompareReads.ApprovedBasis>.Success(new(stamp, ApprovedReadingSet.From(facts, skipped)));

    private static int fact;

    private static ApprovedAnalysisFact Approved(int reading, string form, string ws = "ws")
    {
        var number = Interlocked.Increment(ref fact);
        return new(new Guid(number, 1, 1, [1, 1, 1, 1, 1, 1, 1, 1]), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), [],
            ProposalCompareExports.Signature(reading), null,
            ImmutableDictionary<string, string>.Empty.Add(ws, form.Normalize(System.Text.NormalizationForm.FormD)));
    }

    private static int[] Counts(CompareColumn column) =>
        [column.Gained, column.Lost, column.Unchanged, column.NotMeasured, column.Incomplete];

    [Fact]
    public void AWorkingSetTrialIsComparedOnlyWithAnotherOverTheSameWordList()
    {
        InStore((database, project) =>
        {
            var working = SIL.Motif.Contract.Responses.TrialSampleKind.WorkingSet;
            var full = AddTrial(database, project, [F("f1", [1], [1, 2])]);
            var one = AddTrial(database, project, [F("f1", [1], [1, 2])], sample: working, wordListDigest: "sha256:one");
            var same = AddTrial(database, project, [F("f1", [1], [1])], sample: working, wordListDigest: "sha256:one");
            var other = AddTrial(database, project, [F("f1", [1], [1])], sample: working, wordListDigest: "sha256:two");

            Assert.Equal("trial.working-set-not-a-gate", Compare(project, [full, one]).Refusal?.Code);
            Assert.Equal("trial.working-set-not-a-gate", Compare(project, [one, full]).Refusal?.Code);
            Assert.Equal("trial.working-set-lists-differ", Compare(project, [one, other]).Refusal?.Code);
            var allowed = Compare(project, [one, same]);
            Assert.True(allowed.Succeeded, allowed.Refusal?.Message);
        });
    }

    [Fact]
    public void GainsLossesTiesAndUnmeasuredFormsAreCountedPerProposalFromTheirOwnTrials()
    {
        InStore((database, project) =>
        {
            var a = AddTrial(database, project, [F("f1", [1], [1, 2]), F("f2", [1], []), F("f3", [1], [1]),
                F("f4", [1], [1], dispatched: false), F("f5", [1], [1], complete: false)]);
            var b = AddTrial(database, project, [F("f1", [1], [1, 2]), F("f2", [1], [1]), F("f3", [1], [1, 3]),
                F("f4", [1], [1]), F("f6", [], [4])]);
            var result = Compare(project, [a, b]);

            Assert.True(result.Succeeded, result.Refusal?.Message);
            var summary = result.Value!;
            Assert.Equal("trial-before-after", summary.Basis);
            Assert.Equal(6, summary.FormsCompared);
            Assert.Equal([a, b], summary.Columns.Select(column => column.ProposalId));
            Assert.Equal([1, 1, 2, 2, 1], Counts(summary.Columns[0]));
            Assert.Equal([3, 0, 2, 1, 0], Counts(summary.Columns[1]));
            Assert.False(summary.Complete);
            Assert.Contains(summary.Limitations, item => item.Code == "not-measured" && item.Count == 2);
            Assert.Null(summary.Page);
        });
    }

    [Fact]
    public void RowsJoinOnWritingSystemFormKeyAndReadingIdAndTiesDoNotDiverge()
    {
        InStore((database, project) =>
        {
            var a = AddTrial(database, project, [F("f1", [1], [1, 2]), F("f1", [1], [1, 2], ws: "other"), F("f2", [1], [])]);
            var b = AddTrial(database, project, [F("f1", [1], [1, 2]), F("f1", [1], [1, 3], ws: "other"), F("f2", [1], [])]);
            var summary = Compare(project, [a, b], page: 1).Value!;

            var rows = summary.Page!.Rows;
            Assert.Equal(3, rows.Count);
            var same = rows.Single(row => row.Form == "f1" && row.WritingSystemId == "ws");
            Assert.False(same.Diverges);
            Assert.Equal([ReadingId(2)], same.Cells[0].GainedIds);
            Assert.Equal(same.Cells[0].GainedIds, same.Cells[1].GainedIds);
            var other = rows.Single(row => row.WritingSystemId == "other");
            Assert.True(other.Diverges);
            Assert.Equal([ReadingId(2)], other.Cells[0].GainedIds);
            Assert.Equal([ReadingId(3)], other.Cells[1].GainedIds);
            var lost = rows.Single(row => row.Form == "f2");
            Assert.Equal([ReadingId(1)], lost.Cells[0].LostIds);
            Assert.False(lost.Diverges);
            Assert.Equal(1, summary.FormsDiverging);
            Assert.True(rows[0].Diverges);
        });
    }

    [Fact]
    public void AFormOneTrialDidNotDispatchOrNeverSawIsNotMeasuredAndNeverUnchanged()
    {
        InStore((database, project) =>
        {
            var a = AddTrial(database, project, [F("f1", [1], [1], dispatched: false), F("f2", [1], [1])]);
            var b = AddTrial(database, project, [F("f1", [1], [1]), F("f3", [1], [1])]);
            var rows = Compare(project, [a, b], page: 1).Value!.Page!.Rows;

            Assert.Equal(3, rows.Count);
            Assert.All(rows, row => Assert.Contains(row.Cells, cell => !cell.Measured));
            Assert.All(rows.SelectMany(row => row.Cells.Where(cell => !cell.Measured)),
                cell => Assert.Empty(cell.GainedIds.Concat(cell.LostIds)));
        });
    }

    [Fact]
    public void AProposalWithoutACompletedTrialRefusesAndNamesIt()
    {
        InStore((database, project) =>
        {
            var a = AddTrial(database, project, [F("f1", [1], [1, 2])]);
            var unfinished = AddTrial(database, project, [F("f1", [1], [1, 2])], completed: false);
            var never = CanonicalId.Mint("proposal/").Value;

            foreach (var other in new[] { unfinished, never })
            {
                var result = Compare(project, [a, other]);
                Assert.Equal("proposal.compare-trial-missing", result.Refusal?.Code);
                Assert.Equal(other, result.Refusal!.Facts["proposalId"]);
            }
        });
    }

    [Fact]
    public void TheNewestCompletedTrialIsTheColumn()
    {
        InStore((database, project) =>
        {
            var a = AddTrial(database, project, [F("f1", [1], [1, 2])], minute: 1);
            AddTrial(database, project, [F("f1", [1], [])], a, minute: 2);
            var b = AddTrial(database, project, [F("f1", [1], [1])]);
            var result = Compare(project, [a, b]);
            Assert.True(result.Succeeded, result.Refusal?.Code + " " + result.Refusal?.Message);
            var counts = result.Value!.Columns[0];

            Assert.Equal(1, counts.Lost);
            Assert.Equal(0, counts.Gained);
        });
    }

    [Fact]
    public void TrialsOnDifferentBaselinesRefuseWithoutAnyOverride()
    {
        InStore((database, project) =>
        {
            var a = AddTrial(database, project, [F("f1", [1], [1, 2])], baseline: 'a');
            var b = AddTrial(database, project, [F("f1", [1], [1, 2])], baseline: 'b');
            var result = Compare(project, [a, b]);

            Assert.Equal("proposal.compare-baseline-mismatch", result.Refusal?.Code);
            Assert.Equal(["aaaaaaaa", "bbbbbbbb"], new[] { result.Refusal!.Facts[a], result.Refusal.Facts[b] });
        });
    }

    [Fact]
    public void AtMustMatchEveryTrialsBaseline()
    {
        InStore((database, project) =>
        {
            var a = AddTrial(database, project, [F("f1", [1], [1, 2])]);
            var b = AddTrial(database, project, [F("f1", [1], [1, 2])]);

            Assert.True(Compare(project, [a, b], BaselineDigest('a')).Succeeded);
            var wrong = Compare(project, [a, b], BaselineDigest('c'));
            Assert.Equal("proposal.compare-at-mismatch", wrong.Refusal?.Code);
        });
    }

    [Fact]
    public void NineProposalsDuplicatesOneProposalAndBadPagesRefuse()
    {
        InStore((database, project) =>
        {
            var ids = Enumerable.Range(0, 9).Select(_ => AddTrial(database, project, [F("f1", [1], [1])])).ToArray();
            Assert.Equal("proposal.compare-proposals-invalid", Compare(project, ids).Refusal?.Code);
            Assert.True(Compare(project, ids[..8]).Succeeded);
            Assert.Equal("proposal.compare-proposals-invalid", Compare(project, [ids[0]]).Refusal?.Code);
            Assert.Equal("proposal.compare-proposals-invalid", Compare(project, [ids[0], ids[0]]).Refusal?.Code);
            Assert.Equal("proposal.compare-proposals-invalid", Compare(project, [ids[0], "nonsense"]).Refusal?.Code);
            Assert.Equal("paging.invalid", Compare(project, ids[..2], cursor: "0").Refusal?.Code);
        });
    }

    [Fact]
    public void RowsPageInAFrozenOrderWithDivergingFormsFirst()
    {
        InStore((database, project) =>
        {
            var forms = Enumerable.Range(0, 250).Select(index => F($"w{index:000}", [1], [1, 2])).ToList();
            var a = AddTrial(database, project, forms);
            var b = AddTrial(database, project, forms.Select(form => form with { After = [1] }));
            var first = Compare(project, [a, b], page: 1).Value!;
            var third = Compare(project, [a, b], page: 3).Value!;
            var beyond = Compare(project, [a, b], page: 4).Value!;
            Assert.Empty(beyond.Page!.Rows);
            Assert.Null(beyond.Page.NextCursor);

            Assert.Equal(250, first.RowsTotal);
            Assert.Equal(100, first.Page!.Rows.Count);
            Assert.Equal(ResultPaging.Cursor(100), first.Page.NextCursor);
            Assert.Equal(50, third.Page!.Rows.Count);
            Assert.Null(third.Page.NextCursor);
            Assert.Empty(Compare(project, [a, b], cursor: ResultPaging.Cursor(300)).Value!.Page!.Rows);
            Assert.Equal(forms.Select(form => "key/" + form.Form).Order(StringComparer.Ordinal),
                Enumerable.Range(1, 3).SelectMany(page => Compare(project, [a, b], page: page).Value!.Page!.Rows)
                    .Select(row => row.FormKey));
            Assert.Null(Compare(project, [a, b]).Value!.Page);
        });
    }

    [Fact]
    public void APageIsRefusedWhenANewerTrialReplacesAPinnedOne()
    {
        InStore((database, project) =>
        {
            var a = AddTrial(database, project, [F("f1", [1], [1, 2])], minute: 1);
            var b = AddTrial(database, project, [F("f1", [1], [1, 3])]);
            var first = Compare(project, [a, b]).Value!;
            Assert.Matches("^[0-9a-f]{12}$", first.Snapshot);

            var same = Compare(project, [a, b], page: 1, snapshot: first.Snapshot);
            Assert.True(same.Succeeded, same.Refusal?.Message);
            Assert.Equal(first.Snapshot, same.Value!.Snapshot);

            AddTrial(database, project, [F("f1", [1], [])], a, minute: 5);
            var stale = Compare(project, [a, b], page: 1, snapshot: first.Snapshot);
            Assert.Equal("proposal.compare-snapshot-changed", stale.Refusal?.Code);
            var fresh = Compare(project, [a, b]).Value!;
            Assert.NotEqual(first.Snapshot, fresh.Snapshot);
        });
    }

    [Fact]
    public void LimitationsCountColumnsNotForms()
    {
        InStore((database, project) =>
        {
            var a = AddTrial(database, project, [F("f1", [1], [1], complete: false)]);
            var b = AddTrial(database, project, [F("f1", [1], [1], complete: false)]);
            var limitations = Compare(project, [a, b]).Value!.Limitations;
            Assert.Equal(new CompareLimitation("incomplete-trial", 2), Assert.Single(limitations));
        });
    }

    [Fact]
    public void ApprovedAnalysesAreKeptOrLostPerProposalByTheSenseAwareRule()
    {
        InStore((database, project) =>
        {
            var a = AddTrial(database, project, [F("f1", [1], [1]), F("f2", [1001], [2002]), F("f3", [4], [4]),
                F("f4", [], [5]), F("f5", [6], [], complete: false)]);
            var b = AddTrial(database, project, [F("f1", [1], []), F("f2", [1001], [3001]), F("f3", [4], [4]),
                F("f4", [5], [5]), F("f5", [6], [])]);
            var loader = Approvals([Approved(1, "f1"), Approved(1001, "f2"), Approved(3, "f3"), Approved(5, "f4"),
                Approved(6, "f5")], skipped: 2);

            var summary = Compare(project, [a, b], page: 1, approved: loader).Value!;

            Assert.Equal([2, 1], [summary.Columns[0].ApprovedKept, summary.Columns[0].ApprovedLost]);
            Assert.Equal([2, 2], [summary.Columns[1].ApprovedKept, summary.Columns[1].ApprovedLost]);
            Assert.Equal(2, summary.ApprovedSkippedOtherAgents);
            Assert.Contains(summary.Limitations, item => item.Code == "approved-other-agents" && item.Count == 2);
            Assert.False(summary.Complete);

            var rows = summary.Page!.Rows;
            var f1 = rows.Single(row => row.Form == "f1");
            Assert.Equal([ReadingId(1)], f1.Cells[0].ApprovedKeptIds);
            Assert.Empty(f1.Cells[0].ApprovedLostIds);
            Assert.Equal([ReadingId(1)], f1.Cells[1].ApprovedLostIds);
            Assert.True(f1.Diverges);
            var f2 = rows.Single(row => row.Form == "f2");
            Assert.Equal([ReadingId(1001)], f2.Cells[0].ApprovedLostIds);
            Assert.Equal([ReadingId(1001)], f2.Cells[1].ApprovedKeptIds);
            var f4 = rows.Single(row => row.Form == "f4");
            Assert.Equal([ReadingId(5)], f4.Cells[0].ApprovedKeptIds);
            Assert.DoesNotContain(rows, row => row.Form == "f3");
            var f5 = rows.Single(row => row.Form == "f5");
            Assert.Empty(f5.Cells[0].ApprovedLostIds);
            Assert.Empty(f5.Cells[0].ApprovedKeptIds);
        });
    }

    [Fact]
    public void AnApprovalNotYieldedBeforeAndNotAfterIsInNeitherCountAndUnmeasuredFormsCountNowhere()
    {
        InStore((database, project) =>
        {
            var a = AddTrial(database, project, [F("f1", [2], [2]), F("f2", [1], [1], dispatched: false)]);
            var b = AddTrial(database, project, [F("f1", [2], [2]), F("f2", [1], [1])]);
            var loader = Approvals([Approved(1, "f1"), Approved(1, "f2")]);

            var summary = Compare(project, [a, b], approved: loader).Value!;

            Assert.Equal([0, 0], [summary.Columns[0].ApprovedKept, summary.Columns[0].ApprovedLost]);
            Assert.Equal([1, 0], [summary.Columns[1].ApprovedKept, summary.Columns[1].ApprovedLost]);
        });
    }

    [Fact]
    public void ApprovedFormsJoinInNfdAndOnlyInTheirOwnWritingSystem()
    {
        InStore((database, project) =>
        {
            var a = AddTrial(database, project, [F("\u00e9", [1], [1]), F("g", [1], [1], ws: "other")]);
            var b = AddTrial(database, project, [F("\u00e9", [1], []), F("g", [1], [], ws: "other")]);
            var loader = Approvals([Approved(1, "e\u0301"), Approved(1, "g")]);

            var summary = Compare(project, [a, b], approved: loader).Value!;

            Assert.Equal([1, 0], [summary.Columns[0].ApprovedKept, summary.Columns[0].ApprovedLost]);
            Assert.Equal([0, 1], [summary.Columns[1].ApprovedKept, summary.Columns[1].ApprovedLost]);
        });
    }

    [Fact]
    public void AnUnsignedApprovalIsNeverCompared()
    {
        InStore((database, project) =>
        {
            var a = AddTrial(database, project, [F("f1", [1], [1])]);
            var b = AddTrial(database, project, [F("f1", [1], [])]);
            var unsigned = Approved(1, "f1") with { Signature = null, UnsignedCause = ApprovedUnsignedCause.NoMorphBundles };

            var summary = Compare(project, [a, b], approved: Approvals([unsigned])).Value!;

            Assert.All(summary.Columns, column => Assert.Equal([0, 0], [column.ApprovedKept, column.ApprovedLost]));
        });
    }

    [Fact]
    public void ADifferentApprovedSnapshotStampRefusesLaterPagesAsChanged()
    {
        InStore((database, project) =>
        {
            var a = AddTrial(database, project, [F("f1", [1], [1])]);
            var b = AddTrial(database, project, [F("f1", [1], [])]);
            var first = Compare(project, [a, b], approved: Approvals([], "stamp-1")).Value!;

            var same = Compare(project, [a, b], page: 1, snapshot: first.Snapshot, approved: Approvals([], "stamp-1"));
            Assert.True(same.Succeeded, same.Refusal?.Message);
            var changed = Compare(project, [a, b], page: 1, snapshot: first.Snapshot, approved: Approvals([], "stamp-2"));
            Assert.Equal("proposal.compare-snapshot-changed", changed.Refusal?.Code);
            Assert.NotEqual(first.Snapshot, Compare(project, [a, b], approved: Approvals([], "stamp-2")).Value!.Snapshot);
        });
    }

    [Fact]
    public void AnUnavailableApprovedSnapshotRefusesWithTheIndexsOwnCode()
    {
        InStore((database, project) =>
        {
            var a = AddTrial(database, project, [F("f1", [1], [1])]);
            var b = AddTrial(database, project, [F("f1", [1], [])]);
            ProposalCompareReads.ApprovedLoader expired = (_, _, _) => CommandOutcome<ProposalCompareReads.ApprovedBasis>.Refused(
                new Refusal(RefusalCodes.HandAnalysisSnapshotExpired, FailureReason.Refused, "gone"));

            Assert.Equal("hand-analysis.snapshot-expired", Compare(project, [a, b], approved: expired).Refusal?.Code);
        });
    }

    [Fact]
    public void WithoutAStoredBaselineTheDefaultLoaderRefusesAsExpired()
    {
        InStore((database, project) =>
        {
            var a = AddTrial(database, project, [F("f1", [1], [1])]);
            var b = AddTrial(database, project, [F("f1", [1], [])]);

            var result = ProposalCompareReads.Read(new(project.FullFwDataPath, [a, b]));

            Assert.Equal("hand-analysis.snapshot-expired", result.Refusal?.Code);
        });
    }

    [Fact]
    public void AnAmbiguousParserSenseThatIncludesTheApprovedSenseIsKept()
    {
        InStore((database, project) =>
        {
            var a = AddTrial(database, project, [F("f1", [1001], [3001])]);
            var b = AddTrial(database, project, [F("f1", [1001], [1001])]);

            var summary = Compare(project, [a, b], approved: Approvals([Approved(1001, "f1")])).Value!;

            Assert.Equal([1, 0], [summary.Columns[0].ApprovedKept, summary.Columns[0].ApprovedLost]);
        });
    }

    [Fact]
    public void AParserThatResolvesADifferentSenseLosesTheApproval()
    {
        InStore((database, project) =>
        {
            var a = AddTrial(database, project, [F("f1", [1001], [2001])]);
            var b = AddTrial(database, project, [F("f1", [1001], [1001])]);

            var summary = Compare(project, [a, b], approved: Approvals([Approved(1001, "f1")])).Value!;

            Assert.Equal([0, 1], [summary.Columns[0].ApprovedKept, summary.Columns[0].ApprovedLost]);
        });
    }

    [Fact]
    public void AnAnalysisApprovedInTwoWritingSystemsCountsOncePerProposalAndKeptWinsOverLost()
    {
        InStore((database, project) =>
        {
            var a = AddTrial(database, project, [F("f1", [1], []), F("g1", [1], [], ws: "other")]);
            var b = AddTrial(database, project, [F("f1", [1], [1]), F("g1", [1], [], ws: "other")]);
            var c = AddTrial(database, project, [F("f1", [1], [1]), F("g1", [1], [1], ws: "other")]);
            var one = Approved(1, "f1") with
            {
                Forms = ImmutableDictionary<string, string>.Empty.Add("ws", "f1").Add("other", "g1"),
            };

            var summary = Compare(project, [a, b, c], approved: Approvals([one])).Value!;

            Assert.Equal([0, 1], [summary.Columns[0].ApprovedKept, summary.Columns[0].ApprovedLost]);
            Assert.Equal([1, 0], [summary.Columns[1].ApprovedKept, summary.Columns[1].ApprovedLost]);
            Assert.Equal([1, 0], [summary.Columns[2].ApprovedKept, summary.Columns[2].ApprovedLost]);
        });
    }

    private static string JobOf(MotifDatabase database, ProjectLocator project, string proposal, int index = 0) =>
        SIL.Motif.Commands.Trials.NewestFirst(SIL.Motif.Commands.Trials.JobsFor(database, project, CanonicalId.Parse(proposal)))[index].JobId;

    private static string StoredJson(MotifDatabase database, string proposal) =>
        new SIL.Motif.Worker.Store.ProposalRepository(database).Get(CanonicalId.Parse(proposal)).ProposalJson!;

    // A set matching nothing takes the full path; each run needs its own stamp, as the join is cached per stamp.
    [Fact]
    public void AnEmptyApprovedSetGivesTheSameComparisonAsASetNothingInTheExportMatches()
    {
        InStore((database, project) =>
        {
            var a = AddTrial(database, project, [F("f1", [1], [1, 2]), F("f2", [3], []), F("f3", [4], [4], complete: false),
                F("f4", [], [5]), F("f5", [6], [6], dispatched: false), F("f6", [1], [1], ws: null)]);
            var b = AddTrial(database, project, [F("f1", [1], []), F("f2", [3], [3]), F("f3", [4], [4]),
                F("f4", [], []), F("f5", [6], [6]), F("f6", [1], [2], ws: null)]);

            var early = Compare(project, [a, b], page: 1, approved: Approvals([], "stamp-empty")).Value!;
            var full = Compare(project, [a, b], page: 1, approved: Approvals([Approved(777, "elsewhere")], "stamp-full")).Value!;

            Assert.Equal(1, full.ApprovedTotal);
            Assert.Equal(0, early.ApprovedTotal);
            Assert.All(full.Columns.Concat(early.Columns), column => Assert.Equal([0, 0], [column.ApprovedKept, column.ApprovedLost]));
            Assert.Equal(early.Columns.Select(Counts), full.Columns.Select(Counts));
            Assert.Equal(early.Columns.Select(column => (column.Gained, column.Lost, column.Incomplete)),
                full.Columns.Select(column => (column.Gained, column.Lost, column.Incomplete)));
            Assert.Equal(early.Page!.Rows.Count, full.Page!.Rows.Count);
            Assert.Equal(Json(early.Page.Rows), Json(full.Page.Rows));
            Assert.Equal(early.Complete, full.Complete);
            Assert.All(early.Page.Rows.SelectMany(row => row.Cells), cell =>
            {
                Assert.Empty(cell.ApprovedKeptIds);
                Assert.Empty(cell.ApprovedLostIds);
            });
        });
    }

    // Deliberate: with nothing approved no signature is read, so a damaged one does not refuse.
    [Fact]
    public void AReadingWithoutASignatureOnlyRefusesTheComparisonWhenSomethingIsApproved()
    {
        InStore((database, project) =>
        {
            var a = AddTrial(database, project, [new FormSpec("f1", [1], [1]) { AfterReadingWithoutSignature = true }]);
            var b = AddTrial(database, project, [F("f1", [1], [1])]);

            var none = Compare(project, [a, b], page: 1, approved: Approvals([])).Value!;
            Assert.Equal(1, none.Page!.Rows.Count(row => row.Form == "f1"));

            var refused = Compare(project, [a, b], page: 1, approved: Approvals([Approved(1, "f1")], "stamp-signed"));
            Assert.Equal("proposal.compare-trial-missing", refused.Refusal?.Code);
        });
    }

    private static string Json<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value, SIL.Motif.Contract.MotifJson.CreateOptions());

    [Fact]
    public void ApprovalsWithNoSignatureOrOnUnmeasuredFormsAreUncountedAndMakeTheComparisonIncomplete()
    {
        InStore((database, project) =>
        {
            var a = AddTrial(database, project, [F("f1", [1], [1]), F("f2", [1], [1], dispatched: false)]);
            var b = AddTrial(database, project, [F("f1", [1], [1]), F("f2", [1], [1], dispatched: false)]);
            var unsigned = Approved(1, "f1") with { Signature = null, UnsignedCause = ApprovedUnsignedCause.NoMorphBundles };
            var counted = Compare(project, [a, b], approved: Approvals([Approved(1, "f1")])).Value!;
            Assert.Equal([1, 0], [counted.ApprovedTotal, counted.ApprovedUncounted]);
            Assert.DoesNotContain(counted.Limitations, item => item.Code == "approved-uncounted");

            var summary = Compare(project, [a, b],
                approved: Approvals([Approved(1, "f1"), unsigned, Approved(1, "f2"), Approved(1, "elsewhere")], "stamp-2")).Value!;

            Assert.Equal(4, summary.ApprovedTotal);
            Assert.Equal(3, summary.ApprovedUncounted);
            Assert.Contains(summary.Limitations, item => item.Code == "approved-uncounted" && item.Count == 2);
            Assert.False(summary.Complete);
        });
    }

    [Fact]
    public void ATrialOlderThanTheProposalsCurrentRevisionIsStaleAndAMatchingOneIsNot()
    {
        InStore((database, project) =>
        {
            var a = AddTrial(database, project, [F("f1", [1], [1, 2])]);
            var b = AddTrial(database, project, [F("f1", [1], [1, 2])]);
            Assert.DoesNotContain(Compare(project, [a, b]).Value!.Limitations, item => item.Code == "trial-stale");

            SaveProposal(database, b, OtherProposalJson(b));
            var summary = Compare(project, [a, b]).Value!;

            Assert.Equal(new CompareLimitation("trial-stale", 1), Assert.Single(summary.Limitations));
            Assert.False(summary.Complete);
        });
    }

    [Fact]
    public void ARunningNewerAttemptOrASupersededProposalMakesTheTrialSupersededButAFailedOneDoesNot()
    {
        InStore((database, project) =>
        {
            var a = AddTrial(database, project, [F("f1", [1], [1, 2])], minute: 1);
            AddTrial(database, project, [F("f1", [1], [1, 2])], a, minute: 2, completed: false);
            var b = AddTrial(database, project, [F("f1", [1], [1, 2])]);
            var c = AddTrial(database, project, [F("f1", [1], [1, 2])]);
            SaveProposal(database, c, StoredJson(database, c), supersededBy: a);

            Assert.DoesNotContain(Compare(project, [a, b]).Value!.Limitations, item => item.Code == "trial-superseded");
            using (var connection = database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "UPDATE Jobs SET Status='running', ArchivedUtc=NULL, ResultJson=NULL, FailureCategory='none' WHERE Status='failed';";
                command.ExecuteNonQuery();
            }
            Assert.Equal(new CompareLimitation("trial-superseded", 1), Assert.Single(Compare(project, [a, b]).Value!.Limitations));
            Assert.Equal(new CompareLimitation("trial-superseded", 1), Assert.Single(Compare(project, [b, c]).Value!.Limitations));
        });
    }

    [Fact]
    public void AMissingProposalRowReadsAsStaleNotAsSilentlyFresh()
    {
        InStore((database, project) =>
        {
            var a = AddTrial(database, project, [F("f1", [1], [1, 2])]);
            var b = AddTrial(database, project, [F("f1", [1], [1, 2])]);
            using (var connection = database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "DELETE FROM ProposalRevisions WHERE ProposalId=$id; DELETE FROM Proposals WHERE ProposalId=$id;";
                command.Parameters.AddWithValue("$id", b);
                command.ExecuteNonQuery();
            }

            Assert.Equal(new CompareLimitation("trial-stale", 1), Assert.Single(Compare(project, [a, b]).Value!.Limitations));
        });
    }

    [Fact]
    public void EqualTimestampsChooseTheGreaterJobIdWhateverTheInsertionOrder()
    {
        InStore((database, project) =>
        {
            var a = AddTrial(database, project, [F("f1", [1], [1, 2])], jobId: "job/zzzz", minute: 3, second: 7);
            AddTrial(database, project, [F("f1", [1], [])], a, jobId: "job/aaaa", minute: 3, second: 7);
            var b = AddTrial(database, project, [F("f1", [1], [1])]);
            var summary = Compare(project, [a, b]).Value!;

            Assert.Equal(1, summary.Columns[0].Gained);
            Assert.Equal(0, summary.Columns[0].Lost);
            Assert.Equal("job/zzzz", JobOf(database, project, a));
            Assert.DoesNotContain(summary.Limitations, item => item.Code == "trial-superseded");
        });
    }

    [Fact]
    public void AParserVersionThatDiffersWithinOrAcrossTrialsIsNamedInALimitation()
    {
        InStore((database, project) =>
        {
            var same = AddTrial(database, project, [F("f1", [1], [1])], parserBefore: "1.0", parserAfter: "1.0");
            var alsoSame = AddTrial(database, project, [F("f1", [1], [1])], parserBefore: "1.0", parserAfter: "1.0");
            var moved = AddTrial(database, project, [F("f1", [1], [1])], parserBefore: "1.0", parserAfter: "1.1");
            var other = AddTrial(database, project, [F("f1", [1], [1])], parserBefore: "2.0", parserAfter: "2.0");

            Assert.DoesNotContain(Compare(project, [same, alsoSame]).Value!.Limitations, item => item.Code == "parser-version-differs");
            var within = Compare(project, [same, moved]).Value!;
            var limitation = Assert.Single(within.Limitations);
            Assert.Equal(new CompareLimitation("parser-version-differs", 1) { Detail = "1.0 | 1.1" }, limitation);
            Assert.False(within.Complete);
            Assert.Equal(new CompareLimitation("parser-version-differs", 2) { Detail = "1.0 | 2.0" },
                Assert.Single(Compare(project, [same, other]).Value!.Limitations));
        });
    }

    [Fact]
    public void AJobPinSelectsThatTrialAndAnOlderOneIsSuperseded()
    {
        InStore((database, project) =>
        {
            var a = AddTrial(database, project, [F("f1", [1], [1, 2])], minute: 1);
            AddTrial(database, project, [F("f1", [1], [])], a, minute: 2);
            var b = AddTrial(database, project, [F("f1", [1], [1])]);
            var older = JobOf(database, project, a, 1);

            var pinned = Compare(project, [a, b], jobs: [older]).Value!;
            Assert.Equal([1, 0], [pinned.Columns[0].Gained, pinned.Columns[0].Lost]);
            Assert.Contains(pinned.Limitations, item => item.Code == "trial-superseded" && item.Count == 1);
            var newest = Compare(project, [a, b], jobs: [JobOf(database, project, a)]).Value!;
            Assert.Equal([0, 1], [newest.Columns[0].Gained, newest.Columns[0].Lost]);
            Assert.NotEqual(pinned.Snapshot, newest.Snapshot);
            Assert.Equal(newest.Snapshot, Compare(project, [a, b]).Value!.Snapshot);
        });
    }

    [Fact]
    public void ABadJobPinRefusesWithItsOwnCode()
    {
        InStore((database, project) =>
        {
            var a = AddTrial(database, project, [F("f1", [1], [1])], minute: 1);
            AddTrial(database, project, [F("f1", [1], [1])], a, minute: 2);
            var b = AddTrial(database, project, [F("f1", [1], [1])]);
            var c = AddTrial(database, project, [F("f1", [1], [1])]);
            var unfinished = AddTrial(database, project, [F("f1", [1], [1])], completed: false);
            string Job(string proposal) => JobOf(database, project, proposal);

            Assert.Equal("proposal.compare-trial-missing", Compare(project, [a, b], jobs: ["job/nonexistent"]).Refusal?.Code);
            Assert.Equal("proposal.compare-trial-missing", Compare(project, [a, b], jobs: [Job(unfinished)]).Refusal?.Code);
            Assert.Equal("proposal.compare-proposals-invalid", Compare(project, [a, b], jobs: [Job(c)]).Refusal?.Code);
            Assert.Equal("proposal.compare-proposals-invalid",
                Compare(project, [a, b], jobs: [Job(a), JobOf(database, project, a, 1)]).Refusal?.Code);
        });
    }

    [Fact]
    public void PagesOfOneSnapshotShareTheFormJoinAndTheBaselineLoadIsPerStore()
    {
        InStore((database, project) =>
        {
            var forms = Enumerable.Range(0, 450).Select(index => F($"w{index:000}", [1], [1, 2])).ToList();
            var a = AddTrial(database, project, forms);
            var b = AddTrial(database, project, forms.Select(form => form with { After = [1] }));
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var first = Compare(project, [a, b]).Value!;
            var coldMs = timer.ElapsedMilliseconds;
            Assert.Equal(1, ProposalCompareReads.JoinsComputedFor(database.FullPath));

            timer.Restart();
            for (var page = 1; page <= 5; page++)
                Assert.True(Compare(project, [a, b], page: page, snapshot: first.Snapshot).Succeeded);
            var pagesMs = timer.ElapsedMilliseconds;

            Assert.Equal(1, ProposalCompareReads.JoinsComputedFor(database.FullPath));
            output.WriteLine($"forms={forms.Count} columns=2 pages=5 coldSummaryMs={coldMs} warmPagesMs={pagesMs} joins=1");
        });
    }
}
