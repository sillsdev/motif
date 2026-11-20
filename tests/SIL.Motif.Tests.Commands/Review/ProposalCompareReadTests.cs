using SIL.Motif.Commands.Review;
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

public sealed class ProposalCompareReadTests
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
        string[] ids, string? at = null, int? page = null, string? snapshot = null) =>
        ProposalCompareReads.Read(new(project.FullFwDataPath, ids, at, page, snapshot));

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
            Assert.Equal(1, summary.Pages);
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
            Assert.Equal("evidence.invalid-page", Compare(project, ids[..2], page: 0).Refusal?.Code);
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
            var beyond = Compare(project, [a, b], page: 4);
            Assert.Equal("evidence.invalid-page", beyond.Refusal?.Code);

            Assert.Equal(250, first.RowsTotal);
            Assert.Equal(3, first.Pages);
            Assert.Equal(100, first.Page!.Rows.Count);
            Assert.Equal(2, first.Page.NextPage);
            Assert.Equal(50, third.Page!.Rows.Count);
            Assert.Null(third.Page.NextPage);
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
}
