using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class WordContextArgvTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-word-context-argv-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AllOpinionsAreAvailableBeforeAnAssessmentAndAgreeWithTheWindowQuery()
    {
        Directory.CreateDirectory(_root);
        string path;
        using (var cache = pristine.NewScratch())
        {
            var text = SeededProject.SeedText(cache, pristine.Seed);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisRepository>().GetObject(text.ApprovedAnalysisId);
                var wordform = (IWfiWordform)analysis.Owner;
                cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.disapproves);
                var candidate = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
                wordform.AnalysesOC.Add(candidate);
                foreach (var source in analysis.MorphBundlesOS)
                {
                    var bundle = cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
                    candidate.MorphBundlesOS.Add(bundle);
                    bundle.MorphRA = source.MorphRA;
                    bundle.MsaRA = source.MsaRA;
                    bundle.SenseRA = source.SenseRA;
                }
            });
            new FwDataProjectLoader().Save(cache);
            path = cache.ProjectId.Path;
        }
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), _root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        File.SetLastWriteTimeUtc(path, captured.Value!.SourceLastWriteUtc.UtcDateTime.AddMinutes(1));
        using var owner = new FieldWorksSimulator(path).Hold();
        var expected = WordContextQuery.Query(new WordContextRequest(path, SeededProject.AnalysedWordForm));
        var result = await CliProcess.RunAsync(_root, null, false, "word-context", "--project", path,
            "--word", SeededProject.AnalysedWordForm, "--json");
        Assert.True(result.ExitCode == 0, result.Error);
        var actual = ProjectionJson.Deserialize<WordContextResponse>(result.Output)!;
        Assert.Equal(ProjectionJson.Serialize(expected.Value), ProjectionJson.Serialize(actual));
        Assert.True(actual.IsStale);
        Assert.True(actual.IsInFieldWorks);
        Assert.Equal(["disapproved", "candidate"], actual.Analyses.Select(analysis => analysis.StoredAnalysisOpinion));
        Assert.Null(actual.ExpectedAnalysis);
        var human = await CliProcess.RunAsync(_root, null, false, "word-context", "--project", path,
            "--word", SeededProject.AnalysedWordForm);
        Assert.True(human.ExitCode == 0, human.Error);
        Assert.Contains("Disapproved", human.Output);
        Assert.Contains("Unknown", human.Output);
        Assert.Contains("FieldWorks saved since", human.Output);
    }

    [Fact]
    public async Task BeforeABaselineMembershipIsUnknownAndMissingWordIsUsage()
    {
        Directory.CreateDirectory(_root);
        var path = pristine.CopyProjectFile();
        var result = await CliProcess.RunAsync(_root, null, false, "word-context", "--project", path,
            "--word", "word", "--json");
        Assert.True(result.ExitCode == 0, result.Error);
        var context = ProjectionJson.Deserialize<WordContextResponse>(result.Output)!;
        Assert.False(context.HasBaseline);
        Assert.Null(context.IsInFieldWorks);
        var invalid = await CliProcess.RunAsync(_root, null, false, "word-context", "--project", path);
        Assert.True(invalid.ExitCode == FailureEnvelope.ExitCodeFor(FailureReason.InvalidArgument), invalid.Error);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
