using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>Pins <see cref="GroupQueryCommand"/> reading a Proposal's Trial copy through --as-of.</summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group1)]
public sealed class GroupAsOfProposalTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.GroupAsOfProposalTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void AsOfAProposalReadsItsTrialCopyAndNamesIt()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.RecordAssessment(proposal, "trial-root", AssessmentKinds.ParseTime);
        var jobId = "trial/group-" + Guid.NewGuid().ToString("N");
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", jobId, ["trial-root"]);
        var scratch = TrialScratchPath.ForJob(jobId);
        try
        {
            var copy = GroupCorpusFixture.Create(pristine);
            CopyDirectory(Path.GetDirectoryName(copy)!, scratch);
            var outcome = GroupQueryCommand.Run(new GroupRequest(fixture.Project.FullFwDataPath, "suffix", Ending: "an",
                AsOf: proposal.Id.Value));
            Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
            Assert.Equal("proposal " + proposal.Id.Value, outcome.Value!.ReadAt);
            Assert.Equal(["kaon", "baha", "lis"], outcome.Value.Rows.Select(row => row.Stem));
        }
        finally { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); }
    }

    [Fact]
    public void AsOfAProposalWithNoTrialIsRefusedAsUnavailable()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        var outcome = GroupQueryCommand.Run(new GroupRequest(fixture.Project.FullFwDataPath, "suffix", Ending: "an",
            AsOf: proposal.Id.Value));
        Assert.Equal(RefusalCodes.GroupAsOfUnavailable, outcome.Refusal!.Code);
    }

    [Fact]
    public void AsOfAnUnknownProposalIsRefusedAsUnavailable()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var known = fixture.SaveProposal().Id.Value;
        var unknown = known[..^1] + (known[^1] == 'a' ? 'b' : 'a');
        var outcome = GroupQueryCommand.Run(new GroupRequest(fixture.Project.FullFwDataPath, "suffix", Ending: "an", AsOf: unknown));
        Assert.Equal(RefusalCodes.GroupAsOfUnavailable, outcome.Refusal!.Code);
    }

    private static void CopyDirectory(string from, string to)
    {
        foreach (var directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, directory)));
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), true);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
