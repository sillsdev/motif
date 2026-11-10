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
            var queued = TrialBaselines.EnqueueWithBaseline(request, (words, limits, writingSystem, onPartial, earlier) =>
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

    private IReadOnlyList<string> Before(string word, IReadOnlyCollection<string> earlier)
    {
        var assess = TrialBaselines.Assess(_project.FwDataPath, [word], _project.ManagedRoot, _parser,
            CancellationToken.None, null, alsoProtect: earlier);
        Assert.True(assess.Succeeded, assess.Refusal?.Message);
        return assess.Value!.AssessmentIds;
    }
}
