using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Phonology;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class PhonologyCheckCommandTests(PristineProjectFixture pristine)
{
    [Fact]
    public void CheckIsRefusedWithoutABaselineAnUnknownRevisionOrUnboundedCeilings()
    {
        var projectPath = pristine.CopyProjectFile();
        var revisionId = SaveSet(projectPath);

        var noBaseline = PhonologyExampleCommands.Check(new PhonologyCheckRequest(projectPath, revisionId));
        var unknown = PhonologyExampleCommands.Check(new PhonologyCheckRequest(projectPath,
            SIL.Motif.Contract.Ids.CanonicalId.Mint("example-revision/").Value));
        var unbounded = PhonologyExampleCommands.Check(new PhonologyCheckRequest(projectPath, revisionId, null,
            new PhonologyLimits(PhonologyExampleValidation.MaxStepCap + 1, 1, 1)));
        var zero = PhonologyExampleCommands.Check(new PhonologyCheckRequest(projectPath, revisionId, null,
            new PhonologyLimits(1, 1, 0)));

        Assert.Equal("phonology-examples.no-baseline", noBaseline.Refusal?.Code);
        Assert.Equal("phonology-examples.revision-not-found", unknown.Refusal?.Code);
        Assert.Equal("phonology-examples.invalid", unbounded.Refusal?.Code);
        Assert.Equal("phonology-examples.invalid", zero.Refusal?.Code);
    }

    [Fact]
    public void CheckQueuesOneJobBoundToTheCurrentBaselineAndRefusesAnotherBaseline()
    {
        var projectPath = pristine.CopyProjectFile();
        var root = Path.Combine(Path.GetDirectoryName(projectPath)!, "check-root");
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var revisionId = SaveSet(projectPath);

        var queued = PhonologyExampleCommands.Check(new PhonologyCheckRequest(projectPath, revisionId));

        Assert.True(queued.Succeeded, queued.Refusal?.Message);
        Assert.Equal(PhonologyCheckJobHandler.JobKind, queued.Value!.Kind);
        var input = ProjectStoreCommand.Run(projectPath, MotifProductVersion.CurrentText, (database, _) =>
            SIL.Motif.Contract.Commands.CommandOutcome<string>.Success(
                new JobRepository(database).Get(queued.Value.JobId)!.InputJson)).Value!;
        var queuedInput = JsonSerializer.Deserialize<PhonologyCheckJobInput>(input, MotifJson.CreateOptions())!;
        Assert.Equal(revisionId, queuedInput.RevisionId);
        Assert.Equal(PhonologyExampleValidation.DefaultLimits, queuedInput.Limits);
        Assert.NotNull(queuedInput.BaselineToken);

        var other = new BaselineToken("other", "sha256:" + new string('a', 64), "projection",
            "2026-01-01T00:00:00Z", "sha256:" + new string('b', 64));
        var stale = PhonologyExampleCommands.Check(new PhonologyCheckRequest(projectPath, revisionId, other));
        Assert.Equal("phonology-examples.baseline-changed", stale.Refusal?.Code);
    }

    private static string SaveSet(string projectPath)
    {
        var saved = PhonologyExampleCommands.Save(new SavePhonologyExamplesRequest(projectPath, "Set",
            [new PhonologyExample("front", "a-ku", ["e-ki"], new PhonologyGenerationContext.HcXmlEntry("entry-17"))]));
        Assert.True(saved.Succeeded, saved.Refusal?.Message);
        return saved.Value!.ExampleSet.Revision.RevisionId;
    }
}
