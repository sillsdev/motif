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
    public async Task TheRealApprovedAnalysisLoaderCountsSignedApprovalsAndReportsUnverifiedOnesAsUncounted()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var index = new FwHandAnalysisIndex(new Sources(fixture.Project));
        var snapshot = (await index.PinAsync(Guid.NewGuid(), null, CancellationToken.None)).Value!;
        var facts = ((IApprovedReadingSource)snapshot).Approved.Analyses;
        await snapshot.DisposeAsync();
        Assert.Equal(2, facts.Length);
        var signed = Assert.Single(facts, fact => fact.Signature is not null);
        var (ws, form) = signed.Forms!.First();
        var signature = signed.Signature!;
        string[] ids = [];
        var staged = ProjectStoreCommand.Run<string>(fixture.Project, MotifProductVersion.CurrentText, (database, project) =>
        {
            var drops = AddTrial(database, project, [new(form, [], []) { WritingSystemId = ws, BeforeSignatures = [signature], AfterSignatures = [] }]);
            var keeps = AddTrial(database, project, [new(form, [], []) { WritingSystemId = ws, BeforeSignatures = [signature], AfterSignatures = [signature] }]);
            ids = [drops, keeps];
            RecordBaseline(database, project, fixture.Project);
            return CommandOutcome<string>.Success("");
        });
        Assert.True(staged.Succeeded, staged.Refusal?.Message);

        var loadsBefore = ProposalCompareReads.BaselineLoads;
        var summary = ProposalCompareReads.Read(new(fixture.Project, ids)).Value!;
        var loadsAfterSummary = ProposalCompareReads.BaselineLoads;
        var page = ProposalCompareReads.Read(new(fixture.Project, ids, Cursor: SIL.Motif.Contract.Responses.ResultPaging.Cursor(0), Snapshot: summary.Snapshot)).Value!;

        Assert.Equal([2, 1], [summary.ApprovedTotal, summary.ApprovedUncounted]);
        Assert.Equal([0, 1], [summary.Columns[0].ApprovedKept, summary.Columns[0].ApprovedLost]);
        Assert.Equal([1, 0], [summary.Columns[1].ApprovedKept, summary.Columns[1].ApprovedLost]);
        Assert.Contains(summary.Limitations, item => item.Code == "approved-uncounted");
        Assert.False(summary.Complete);
        Assert.Equal(summary.Snapshot, page.Snapshot);
        Assert.Single(page.Page!.Rows);
        Assert.InRange(loadsAfterSummary - loadsBefore, 0, 1);
        Assert.Equal(loadsAfterSummary, ProposalCompareReads.BaselineLoads);
    }
}
