using SIL.Motif.Commands;
using SIL.Motif.Commands.Review;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host;
using SIL.Motif.Host.HandAnalysis;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Projects;
using Xunit;
using static SIL.Motif.Tests.TestFixtures.ProposalCompareExports;

namespace SIL.Motif.Tests.Commands.Review;

[Collection(LcmCacheParallelCollections.Group0)]
public sealed class ProposalCompareRealApprovalsTests
{
    private sealed class Sources(string live) : IHandAnalysisProjectSources
    {
        public string? LiveFwData(Guid projectId) => live;
        public string? BaselineFwData(Guid projectId, BaselineSemanticIdentity identity) => null;
    }

    private static void RecordBaseline(MotifDatabase database, SIL.Motif.Contract.Projects.ProjectLocator project, string fwdata)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Baselines (ProjectKey, ProjectIdentity, SemanticSnapshotDigest, ProjectionVersion, CapturedUtc,
                BundleDigest, RootDirectory, FwDataPath, PublishedUtc, SourceLastWriteUtc)
            VALUES ($project, 'project', $digest, 'test', '2026-01-01T00:00:00Z', $digest, $root, $fwdata,
                '2026-01-01T00:00:00.0000000+00:00', '2026-01-01T00:00:00.0000000+00:00');
            """;
        command.Parameters.AddWithValue("$project", ProjectWorkspaceKey.Compute(project));
        command.Parameters.AddWithValue("$digest", BaselineDigest('a'));
        command.Parameters.AddWithValue("$root", Path.GetDirectoryName(fwdata)!);
        command.Parameters.AddWithValue("$fwdata", fwdata);
        command.ExecuteNonQuery();
    }

    [Fact]
    public async Task TheRealApprovedAnalysisLoaderCountsKeptAndLostApprovalsOnARealProject()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var index = new FwHandAnalysisIndex(new Sources(fixture.Project));
        var snapshot = (await index.PinAsync(Guid.NewGuid(), null, CancellationToken.None)).Value!;
        var facts = ((IApprovedReadingSource)snapshot).Approved.Analyses;
        await snapshot.DisposeAsync();
        Assert.Equal(2, facts.Length);
        var ws = Assert.Single(facts.Select(fact => fact.Forms!.Keys.Single()).Distinct());
        var both = facts.Select(fact => fact.Signature!).ToArray();
        string[] ids = [];
        var staged = ProjectStoreCommand.Run<string>(fixture.Project, MotifProductVersion.CurrentText, (database, project) =>
        {
            var drops = AddTrial(database, project, [new("approved", [], []) { WritingSystemId = ws, BeforeSignatures = both, AfterSignatures = [both[0]] }]);
            var keeps = AddTrial(database, project, [new("approved", [], []) { WritingSystemId = ws, BeforeSignatures = both, AfterSignatures = both }]);
            ids = [drops, keeps];
            RecordBaseline(database, project, fixture.Project);
            return CommandOutcome<string>.Success("");
        });
        Assert.True(staged.Succeeded, staged.Refusal?.Message);

        var loadsBefore = ProposalCompareReads.BaselineLoads;
        var summary = ProposalCompareReads.Read(new(fixture.Project, ids)).Value!;
        var loadsAfterSummary = ProposalCompareReads.BaselineLoads;
        var page = ProposalCompareReads.Read(new(fixture.Project, ids, Page: 1, Snapshot: summary.Snapshot)).Value!;

        Assert.Equal([2, 0], [summary.ApprovedTotal, summary.ApprovedUncounted]);
        Assert.Equal([1, 1], [summary.Columns[0].ApprovedKept, summary.Columns[0].ApprovedLost]);
        Assert.Equal([2, 0], [summary.Columns[1].ApprovedKept, summary.Columns[1].ApprovedLost]);
        Assert.True(summary.Complete, string.Join(",", summary.Limitations.Select(item => item.Code)));
        Assert.Equal(summary.Snapshot, page.Snapshot);
        Assert.Equal(2, page.Page!.Rows.Single().Cells[1].ApprovedKeptIds.Count);
        Assert.InRange(loadsAfterSummary - loadsBefore, 0, 1);
        Assert.Equal(loadsAfterSummary, ProposalCompareReads.BaselineLoads);
    }
}
