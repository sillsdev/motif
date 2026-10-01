using SIL.LCModel;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Assess;
using SIL.Motif.Worker.Store;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Parser;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class ParserSeamIntegrationTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    [Fact]
    public async Task MissingExportedDirectoryIsRefusedBeforeInvokingPanGloss()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-parser-missing-source-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new StatsCacheStore(WorkspaceOwnership.Bootstrap(root));
            var invoker = new FakeInvoker();
            var assessor = new PanGlossAssessor(paths, invoker);

            await Assert.ThrowsAsync<DirectoryNotFoundException>(() => assessor.ProduceAsync(
                new([SeededProject.FirstForm], [AssessmentKind.Correctness], TimeSpan.FromSeconds(5)),
                Path.Combine(root, "not-exported"), CancellationToken.None));

            Assert.Empty(invoker.Requests);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [RealParserFact]
    public async Task EveryMorphologyIdentityNamesAnObjectInTheParsedProject()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "m", "o", "t", "i", "f", "a", "b");
        var root = Path.Combine(Path.GetTempPath(), "motif-parser-identities-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new StatsCacheStore(WorkspaceOwnership.Bootstrap(root));
            using var invoker = new PanGlossInvoker();
            var assessor = new PanGlossAssessor(paths, invoker);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var produced = await assessor.ProduceAsync(
                new([SeededProject.FirstForm, SeededProject.SecondForm], [AssessmentKind.Correctness],
                    TimeSpan.FromSeconds(5)),
                Path.GetDirectoryName(cache.ProjectId.Path)!, timeout.Token);
            var assessment = Assert.Single(produced);
            var batch = Assert.IsType<AssessmentRaw.Batch>(assessment.Raw).Analysis;
            Assert.StartsWith("sha256:", assessment.Invocation!.SourceBytesSha256);
            Assert.Equal(2, batch.Words.Count);
            var objects = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
            var checkedIdentities = 0;
            Assert.All(batch.Words, word =>
            {
                Assert.NotNull(word.Morphology);
                Assert.NotEmpty(word.Morphology!.Analyses);
                Assert.All(word.Morphology.Analyses, analysis =>
                {
                    Assert.NotEmpty(analysis.Morphs);
                    Assert.All(analysis.Morphs, morph =>
                    {
                        Assert.Null(morph.GuessedString);
                        Assert.NotNull(morph.Form);
                        foreach (var identity in new[] { morph.Form, morph.Msa, morph.InflType }
                                     .Where(identity => identity is not null))
                        {
                            Assert.True(Guid.TryParse(identity, out var guid),
                                $"The parser emitted a non-GUID identity for '{word.Word}': {identity}");
                            Assert.True(objects.IsValidObjectId(guid),
                                $"The parser emitted an identity absent from its project for '{word.Word}': {identity}");
                            checkedIdentities++;
                        }
                    });
                });
            });
            Assert.True(checkedIdentities > 0);
            var artifactDirectory = Assert.Single(Directory.GetDirectories(Path.Combine(root, "assessment-runs")));
            output.WriteLine("Motif two-stem integration output: TSV {0} bytes, morphology JSONL {1} bytes, stderr {2} bytes.",
                new FileInfo(Path.Combine(artifactDirectory, "out.tsv")).Length,
                new FileInfo(Path.Combine(artifactDirectory, "analyses.jsonl")).Length,
                new FileInfo(Path.Combine(artifactDirectory, "stderr.txt")).Length);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
