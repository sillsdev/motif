using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

[Collection(LcmCacheTestCollection.Name)]
public sealed class RealParserLimitTests
{
    [RealParserFact]
    public async Task AStepCappedWordFinishesUnderAHigherStepLimit()
    {
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(), "Motif.RealParserLimit", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);

        try
        {
            string projectPath;
            using (var cache = NewLangProjFixture.CreateCache(temporaryRoot))
            {
                ILexEntry root = null!;
                ILexEntry infix = null!;
                NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                {
                    root = MakeEntry(cache, MoMorphTypeTags.kguidMorphStem, "ka", "root");
                    infix = MakeEntry(cache, MoMorphTypeTags.kguidMorphInfix, "i", "infix");
                    var environment = cache.ServiceLocator.GetInstance<IPhEnvironmentFactory>().Create();
                    cache.LangProject.PhonologicalDataOA.EnvironmentsOS.Add(environment);
                    environment.StringRepresentation = TsStringUtils.MakeString("/ k _ a", cache.DefaultVernWs);
                    var infixAllomorph = (IMoAffixAllomorph)infix.LexemeFormOA!;
                    infixAllomorph.PositionRS.Clear();
                    infixAllomorph.PositionRS.Add(environment);
                });
                Approve(cache, "kia", infix, root);
                RealParserProject.PrepareForParsing(cache, "k", "a", "i");
                projectPath = cache.ProjectId.Path;
            }

            var managedRoot = Path.Combine(temporaryRoot, "managed");
            Directory.CreateDirectory(managedRoot);
            var client = RealCommandClient.Create(managedRoot);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var selection = new SelectionRequest(false, [], ["kia"], false, null);

            var capped = await client.AssessAsync(new AssessRequest(projectPath, selection,
                    PerWordLimitMs: 120_000, PerWordStepLimit: new StepCap(1)),
                new Progress<AssessmentProgress>(), timeout.Token);

            Assert.True(capped.Succeeded, capped.Refusal?.Message);
            var cappedWord = Assert.Single(capped.Value!.Words);
            Assert.Equal("kia", cappedWord.Word);
            Assert.True(cappedWord.IsIncomplete);
            Assert.True(cappedWord.Morphology?.Capped);
            Assert.Contains("limit", cappedWord.CompletionStatus, StringComparison.OrdinalIgnoreCase);

            var finished = await client.AssessAsync(new AssessRequest(projectPath, selection,
                    PerWordLimitMs: 120_000, PerWordStepLimit: new StepCap(100_000_000)),
                new Progress<AssessmentProgress>(), timeout.Token);

            Assert.True(finished.Succeeded, finished.Refusal?.Message);
            var finishedWord = Assert.Single(finished.Value!.Words);
            Assert.Equal("kia", finishedWord.Word);
            Assert.False(finishedWord.IsIncomplete, finishedWord.CompletionStatus);
            Assert.Equal("analysed", finishedWord.Outcome);
            Assert.NotNull(finishedWord.Morphology);
            Assert.False(finishedWord.Morphology!.Capped);
            Assert.NotEmpty(finishedWord.Morphology.Analyses);
            Assert.NotEqual(capped.Value.InvocationId, finished.Value.InvocationId);
        }
        finally
        {
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static ILexEntry MakeEntry(LcmCache cache, Guid morphTypeId, string form, string gloss)
    {
        var services = cache.ServiceLocator;
        var morphType = services.GetInstance<IMoMorphTypeRepository>().GetObject(morphTypeId);
        return services.GetInstance<ILexEntryFactory>().Create(morphType,
            TsStringUtils.MakeString(form, cache.DefaultVernWs), gloss,
            new SandboxGenericMSA
            {
                MsaType = morphTypeId == MoMorphTypeTags.kguidMorphStem ? MsaType.kStem : MsaType.kUnclassified,
            });
    }

    private static void Approve(LcmCache cache, string word, params ILexEntry[] entries)
    {
        var services = cache.ServiceLocator;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var wordform = services.GetInstance<IWfiWordformFactory>().Create(
                TsStringUtils.MakeString(word, cache.DefaultVernWs));
            var analysis = services.GetInstance<IWfiAnalysisFactory>().Create();
            wordform.AnalysesOC.Add(analysis);
            foreach (var entry in entries)
            {
                var bundle = services.GetInstance<IWfiMorphBundleFactory>().Create();
                analysis.MorphBundlesOS.Add(bundle);
                bundle.MorphRA = entry.LexemeFormOA;
                bundle.MsaRA = entry.MorphoSyntaxAnalysesOC.Single();
            }
            cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.approves);
        });
    }
}
