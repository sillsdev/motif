using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Config;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands.Review;

[Collection(LcmCacheParallelCollections.Group0)]
public sealed class ReviewTrialCaptureTests(PristineProjectFixture pristine)
{
    [Fact]
    public void ChangedDraftCannotQueueCapturedInputsOrPassTheAtomicInsertCheck()
    {
        string path;
        using (var cache = pristine.NewScratch())
        {
            new FwDataProjectLoader().Save(cache);
            path = cache.ProjectId.Path;
        }
        var root = Directory.CreateTempSubdirectory("motif-review-revision-").FullName;
        try
        {
            var baseline = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root);
            Assert.True(baseline.Succeeded, baseline.Refusal?.Message);
            var draft = SIL.Motif.Commands.ProposalCommands.New(new(path, "1.0", "capture-revision", null));
            Assert.True(draft.Succeeded, draft.Refusal?.Message);
            var project = new ProjectLocator(path, Path.GetFileNameWithoutExtension(path));
            using var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
                MotifSchema.CurrentSchema, new Version(1, 0));
            var repository = new ProposalRepository(database);
            AddWordChange(repository, "capturealpha");
            var request = new EnqueueTrialRequest(path, "1.0", draft.Value!.ProposalId);
            var frozen = SIL.Motif.Commands.JobCommands.FreezeTrialProposal(request);
            Assert.True(frozen.Succeeded, frozen.Refusal?.Message);
            var words = SIL.Motif.Commands.JobCommands.ResolveTrialWords(frozen.Value!);
            Assert.True(words.Succeeded, words.Refusal?.Message);
            Assert.Equal("capturealpha", Assert.Single(words.Value!));
            var captured = ReviewTrialCapture.Capture(frozen.Value!, words.Value, null);
            Assert.True(captured.Succeeded, captured.Refusal?.Message);
            Assert.NotNull(captured.Value!.ReviewInputs);

            var original = repository.GetDraft("capture-revision");
            AddWordChange(repository, "capturebeta");

            var queued = SIL.Motif.Commands.JobCommands.EnqueueTrialAdmitted(captured.Value, null, null);
            Assert.False(queued.Succeeded);
            Assert.Equal(SIL.Motif.Contract.Commands.RefusalCodes.TrialReviewInputUnavailable, queued.Refusal!.Code);
            Assert.Empty(Directory.GetFiles(database.FullPath + ".review-inputs", "*.json"));
            var atomic = SIL.Motif.Commands.JobCommands.QueueTrial(new JobRepository(database), original,
                SIL.Motif.Contract.Ids.CanonicalId.Mint("job/").Value,
                SIL.Motif.Worker.Projects.ProjectWorkspaceKey.Compute(project), "{}", null);
            Assert.False(atomic.Succeeded);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void AddWordChange(ProposalRepository repository, string word)
    {
        var id = SIL.Motif.Contract.Ids.CanonicalId.FromGuid(Guid.NewGuid()).Value;
        var draft = System.Text.Json.Nodes.JsonNode.Parse(repository.GetDraft("capture-revision").ProposalJson!)!.AsObject();
        draft["contractVersions"]!["analysis"] = "1.0";
        draft["operations"]!.AsArray().Add(new System.Text.Json.Nodes.JsonObject
        {
            ["operationId"] = SIL.Motif.Contract.Ids.CanonicalId.Mint().Value,
            ["kind"] = "analysis/wfiWordform/setSpellingStatus",
            ["target"] = id,
            ["after"] = new System.Text.Json.Nodes.JsonObject { ["value"] = 2 },
            ["extensions"] = new System.Text.Json.Nodes.JsonObject
            {
                ["changeFit"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["wordformId"] = id,
                    ["wordformForm"] = word,
                    ["baselineToken"] = "captured-baseline",
                },
            },
        });
        repository.SaveDraft("capture-revision", draft.ToJsonString());
    }

    [Fact]
    public async Task SharedInputsRemainUntilTheLastTrialIsDiscarded()
    {
        string path;
        using (var cache = pristine.NewScratch())
        {
            new FwDataProjectLoader().Save(cache);
            path = cache.ProjectId.Path;
        }
        var root = Directory.CreateTempSubdirectory("motif-review-retention-").FullName;
        try
        {
            var baseline = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root);
            Assert.True(baseline.Succeeded, baseline.Refusal?.Message);
            var draft = SIL.Motif.Commands.ProposalCommands.New(new(path, "1.0", "retention", null));
            Assert.True(draft.Succeeded, draft.Refusal?.Message);
            var request = new EnqueueTrialRequest(path, "1.0", draft.Value!.ProposalId, Words: ["word"]);
            var first = SIL.Motif.Commands.JobCommands.EnqueueTrial(request);
            var second = SIL.Motif.Commands.JobCommands.EnqueueTrial(request);
            Assert.True(first.Succeeded, first.Refusal?.Message);
            Assert.True(second.Succeeded, second.Refusal?.Message);
            var project = new ProjectLocator(path, Path.GetFileNameWithoutExtension(path));
            using var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
                MotifSchema.CurrentSchema, new Version(1, 0));
            var directory = database.FullPath + ".review-inputs";
            var inputPath = Assert.Single(Directory.GetFiles(directory, "*.json"));
            var artifact = ReviewInputArtifacts.Read(database, new(Path.GetFileNameWithoutExtension(inputPath)));
            var jobs = new JobRepository(database);
            jobs.CancelActive(first.Value!.JobId);
            jobs.DeleteArchived(first.Value.JobId);
            ReviewInputArtifacts.Sweep(database);
            Assert.Single(Directory.GetFiles(directory, "*.json"));
            jobs.CancelActive(second.Value!.JobId);
            var discarded = SIL.Motif.Commands.ProposalCommands.DiscardDraft(new(path, "1.0", "retention"));
            Assert.True(discarded.Succeeded, discarded.Refusal?.Message);
            Assert.Empty(Directory.GetFiles(directory, "*.json"));

            using var lease = ReviewInputArtifacts.Acquire(database.FullPath);
            ReviewInputArtifacts.Store(database, artifact);
            ReviewInputArtifacts.TrySweep(database);
            Assert.Single(Directory.GetFiles(directory, "*.json"));
            using var started = new ManualResetEventSlim();
            var sweep = Task.Run(() => { started.Set(); ReviewInputArtifacts.Sweep(database); });
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            Assert.NotSame(sweep, await Task.WhenAny(sweep, Task.Delay(100)));
            Assert.Single(Directory.GetFiles(directory, "*.json"));
            lease.Dispose();
            await sweep.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Empty(Directory.GetFiles(directory, "*.json"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void DecisionReleasesInputsAfterAnActiveTrialStops()
    {
        string path;
        using (var cache = pristine.NewScratch())
        {
            new FwDataProjectLoader().Save(cache);
            path = cache.ProjectId.Path;
        }
        var root = Directory.CreateTempSubdirectory("motif-review-decision-").FullName;
        try
        {
            var baseline = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root);
            Assert.True(baseline.Succeeded, baseline.Refusal?.Message);
            var draft = SIL.Motif.Commands.ProposalCommands.New(new(path, "1.0", "decision", null));
            Assert.True(draft.Succeeded, draft.Refusal?.Message);
            var queued = SIL.Motif.Commands.JobCommands.EnqueueTrial(new(path, "1.0", draft.Value!.ProposalId, Words: ["word"]));
            Assert.True(queued.Succeeded, queued.Refusal?.Message);
            var project = new ProjectLocator(path, Path.GetFileNameWithoutExtension(path));
            using var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
                MotifSchema.CurrentSchema, new Version(1, 0));
            var directory = database.FullPath + ".review-inputs";
            new ProposalRepository(database).SetStatus(SIL.Motif.Contract.Ids.CanonicalId.Parse(draft.Value.ProposalId),
                "rejected", null);
            Assert.Single(Directory.GetFiles(directory, "*.json"));
            new JobRepository(database).CancelActive(queued.Value!.JobId);
            Assert.Empty(Directory.GetFiles(directory, "*.json"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void CaptureUsesTheConfiguredManifestAndKeepsItsBytes()
    {
        string projectPath;
        using (var cache = pristine.NewScratch())
        {
            new FwDataProjectLoader().Save(cache);
            projectPath = cache.ProjectId.Path;
        }
        var root = Directory.CreateTempSubdirectory("motif-review-capture-").FullName;
        try
        {
            var baseline = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), root);
            Assert.True(baseline.Succeeded, baseline.Refusal?.Message);
            var project = new ProjectLocator(projectPath, Path.GetFileNameWithoutExtension(projectPath));
            var manifestPath = Path.Combine(Path.GetDirectoryName(projectPath)!, "review-members.jsonl");
            var bytes = System.Text.Encoding.UTF8.GetBytes(
                "{\"schema\":\"motif.review-word-set\",\"schemaVersion\":1}\n" +
                "{\"caseId\":\"case\",\"word\":\"control\",\"writingSystemId\":\"qaa\"," +
                "\"source\":{\"citation\":\"source\"},\"expectationRefs\":[]}\n");
            File.WriteAllBytes(manifestPath, bytes);
            File.WriteAllText(ProjectConfigurationReader.PathFor(project),
                "[[review-word-set]]\nid = \"control\"\nname = \"Controls\"\nrole = \"reference\"\nmanifest = \"review-members.jsonl\"\n");

            var captured = ReviewTrialCapture.Capture(new EnqueueTrialRequest(projectPath, "1.0", "proposal", Words: ["touched"]),
                ["touched"], null);

            Assert.True(captured.Succeeded, captured.Refusal?.Message);
            Assert.Equal(new[] { "control", "touched" }, captured.Value!.Words!.Order().ToArray());
            Assert.NotNull(captured.Value.ReviewInputs);
            using var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
                MotifSchema.CurrentSchema, new Version(1, 0));
            var artifact = ReviewInputArtifacts.Read(database, captured.Value.ReviewInputs!);
            Assert.Equal(bytes, artifact.Sources["control"]);
            File.WriteAllText(manifestPath, "changed");
            Assert.Equal(bytes, ReviewInputArtifacts.Read(database, captured.Value.ReviewInputs!).Sources["control"]);
            var subset = ReviewTrialCapture.Capture(new EnqueueTrialRequest(projectPath, "1.0", "proposal", Words: ["touched"])
                { ReviewSetIds = [] }, ["touched"], null);
            Assert.True(subset.Succeeded, subset.Refusal?.Message);
            Assert.Equal("touched", Assert.Single(subset.Value!.Words!));
            Assert.Equal("control", Assert.Single(ReviewInputArtifacts.Read(database, subset.Value.ReviewInputs!).Inputs.OmittedSetIds));
            File.Delete(ProjectConfigurationReader.PathFor(project));
            var noSets = ReviewTrialCapture.Capture(new EnqueueTrialRequest(projectPath, "1.0", "proposal", Words: ["touched"]),
                ["touched"], null);
            Assert.True(noSets.Succeeded, noSets.Refusal?.Message);
            var emptyCapture = ReviewInputArtifacts.Read(database, Assert.IsType<ReviewInputReference>(noSets.Value!.ReviewInputs));
            Assert.Empty(emptyCapture.Inputs.Sets);
            Assert.Equal("touched", Assert.Single(emptyCapture.Inputs.Cases).Word);
            var probes = ReviewTrialCapture.Capture(new EnqueueTrialRequest(projectPath, "1.0", "proposal", Words: ["touched"],
                NegativeWords: ["probe"]) { ReviewSetIds = [] }, ["touched"], null);
            Assert.True(probes.Succeeded, probes.Refusal?.Message);
            var probeCapture = ReviewInputArtifacts.Read(database, Assert.IsType<ReviewInputReference>(probes.Value!.ReviewInputs));
            var probe = Assert.Single(probeCapture.Inputs.Cases, item => item.Word == "probe");
            Assert.Empty(probe.Assertions);
            Assert.Equal("Inline negative probes", Assert.Single(probeCapture.Inputs.Sets).Name);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
