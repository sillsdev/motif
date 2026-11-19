using SIL.Motif.Commands;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Jobs;

/// <summary>A Trial measures its Before side once per writing-system partition; a later partition's prune must not delete an earlier one's.</summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class TrialBeforePruneTests : IDisposable
{
    private readonly WalkthroughProject _project;
    private readonly string _parser;

    public TrialBeforePruneTests(PristineProjectFixture pristine)
    {
        _project = new WalkthroughProject(pristine);
        _parser = FakeParser.CopyRecordingInvocations(Path.Combine(_project.ManagedRoot, "parser"));
    }

    public void Dispose() => _project.Dispose();

    [Fact]
    public void ALaterPartitionsPruneOverTheByteBudgetKeepsTheEarlierPartitionsBeforeAssessments()
    {
        AssessCommand.ByteBudgetForTests.Value = 1;
        try
        {
            var first = Before(SeededProject.FirstForm, []);
            var second = Before(SeededProject.SecondForm, first);

            using var database = ProjectMotifDatabase.Open(_project.FwDataPath);
            var repository = new AssessmentRepository(database);
            Assert.All(first, id => Assert.NotNull(repository.GetHeader(id)));
            Assert.All(second, id => Assert.NotNull(repository.GetHeader(id)));
        }
        finally { AssessCommand.ByteBudgetForTests.Value = null; }
    }

    [Fact]
    public void WithoutTheEarlierIdsTheByteBudgetPruneDeletesThem()
    {
        AssessCommand.ByteBudgetForTests.Value = 1;
        try
        {
            var first = Before(SeededProject.FirstForm, []);
            Before(SeededProject.SecondForm, []);

            using var database = ProjectMotifDatabase.Open(_project.FwDataPath);
            Assert.All(first, id => Assert.Null(new AssessmentRepository(database).GetHeader(id)));
        }
        finally { AssessCommand.ByteBudgetForTests.Value = null; }
    }

    [Fact]
    public void TheTrialsPartitionLoopHandsEachPartitionTheIdsMeasuredBeforeIt()
    {
        var created = ProposalCommands.New(new NewDraftRequest(_project.FwDataPath, "1.0", "prune", null));
        Assert.True(created.Succeeded, created.Refusal?.Message);
        var edited = ProposalCommands.AddSetGloss(new AddSetGlossRequest(_project.FwDataPath, "1.0", "prune",
            SIL.Motif.Contract.Ids.CanonicalId.FromGuid(_project.Seed.FirstSenseId).Value, "en", "changed gloss"));
        Assert.True(edited.Succeeded, edited.Refusal?.Message);
        var request = new EnqueueTrialRequest(_project.FwDataPath, "1.0", created.Value!.ProposalId,
            Words: [SeededProject.FirstForm, SeededProject.SecondForm]);
        Assert.True(TrialBaselines.EnsureCurrentBaseline(request, _project.ManagedRoot).Succeeded);
        var seen = new List<string[]>();
        var measured = new List<string>();
        AssessCommand.ByteBudgetForTests.Value = 1;
        TrialBaselines.PartitionsForTests.Value =
        [
            new ReviewParsePartition("one", [SeededProject.FirstForm], []),
            new ReviewParsePartition("two", [SeededProject.SecondForm], []),
        ];
        try
        {
            var queued = TrialBaselines.EnqueueWithBaseline(request, (words, limits, writingSystem, onPartial, earlier, _) =>
            {
                seen.Add(earlier.ToArray());
                var assess = TrialBaselines.Assess(_project.FwDataPath, words, _project.ManagedRoot, _parser,
                    CancellationToken.None, limits, writingSystemId: null, onPartial: onPartial, alsoProtect: earlier);
                if (assess.Succeeded) measured.AddRange(assess.Value!.AssessmentIds);
                return assess;
            });

            Assert.True(queued.Succeeded, queued.Refusal?.Message);
            Assert.Equal(2, seen.Count);
            Assert.Empty(seen[0]);
            Assert.NotEmpty(seen[1]);
            Assert.All(seen[1], id => Assert.Contains(id, measured));
        }
        finally
        {
            AssessCommand.ByteBudgetForTests.Value = null;
            TrialBaselines.PartitionsForTests.Value = null;
        }
    }

    [Fact]
    public void InterruptedBeforeCountsOnlyTheBatchTheParserReceived()
    {
        const string observed = "observed";
        const string dispatchedOnly = "dispatched";
        const string neverDispatched = "undispatched";
        var proposal = ProposalCommands.New(new NewDraftRequest(_project.FwDataPath, "1.0", "before-dispatch", null));
        Assert.True(proposal.Succeeded, proposal.Refusal?.Message);
        var request = new EnqueueTrialRequest(_project.FwDataPath, "1.0", proposal.Value!.ProposalId,
            Words: [observed, dispatchedOnly, neverDispatched]);
        Assert.True(TrialBaselines.EnsureCurrentBaseline(request, _project.ManagedRoot).Succeeded);
        var job = TrialBaselines.EnqueueWithBaseline(request, (_, _, _, onPartial, _, onDispatched) =>
        {
            onDispatched([observed, dispatchedOnly], "sha256:" + new string('a', 64));
            onPartial([new("fieldworks-parse-analysis/v1", 0, observed, 0, false, false, false, [], [])]);
            return SIL.Motif.Contract.Commands.CommandOutcome<SIL.Motif.Contract.Responses.AssessCommandResponse>.Refused(
                new(SIL.Motif.Contract.Commands.RefusalCodes.AssessmentStoppedByBudget,
                    SIL.Motif.Contract.Responses.FailureReason.StoppedByBudget, "Stopped"));
        });
        Assert.True(job.Succeeded, job.Refusal?.Message);
        var export = SIL.Motif.Commands.Review.TrialExportReads.Read(new(_project.FwDataPath, job.Value!.JobId));
        Assert.True(export.Succeeded, export.Refusal?.Message);
        Assert.Equal(new SIL.Motif.Contract.Responses.TrialExportSideCounts(2, 1), export.Value!.Denominators.Before);
        using var file = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(export.Value.Path));
        var rows = file.RootElement.GetProperty("words").EnumerateArray().ToDictionary(
            row => row.GetProperty("submittedForm").GetString()!,
            row => row.GetProperty("expandedForms")[0].GetProperty("before"));
        Assert.True(rows[observed].GetProperty("complete").GetBoolean());
        Assert.True(rows[dispatchedOnly].GetProperty("dispatched").GetBoolean());
        Assert.False(rows[dispatchedOnly].GetProperty("complete").GetBoolean());
        Assert.False(rows[neverDispatched].GetProperty("dispatched").GetBoolean());
    }

    private IReadOnlyList<string> Before(string word, IReadOnlyCollection<string> earlier)
    {
        var assess = TrialBaselines.Assess(_project.FwDataPath, [word], _project.ManagedRoot, _parser,
            CancellationToken.None, null, alsoProtect: earlier);
        Assert.True(assess.Succeeded, assess.Refusal?.Message);
        return assess.Value!.AssessmentIds;
    }
}
