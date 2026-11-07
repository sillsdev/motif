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
