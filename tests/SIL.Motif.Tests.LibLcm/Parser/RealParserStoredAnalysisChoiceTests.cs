using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Parser;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class RealParserStoredAnalysisChoiceTests(PristineProjectFixture pristine)
{
    private const string StoredAnalysisPrefix = "grammar.stored-analysis.";

    [RealParserFact]
    public void OffRunsNoStoredAnalysisComparisonAndTheBudgetIsWhatAnUnconfiguredCheckDoes()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-stored-analysis-choice-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = SeedProjectWhoseStoredAnalysisNoLongerParses();
            Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);

            var unconfigured = Check(new GrammarCheckRequest(path));
            var budget = Check(new GrammarCheckRequest(path, StoredAnalyses: StoredAnalysisComparison.Budget));
            var all = Check(new GrammarCheckRequest(path, StoredAnalyses: StoredAnalysisComparison.All));
            var off = Check(new GrammarCheckRequest(path, StoredAnalyses: StoredAnalysisComparison.Off));

            Assert.NotEmpty(StoredAnalysisFindings(budget));
            Assert.Equal(Shape(budget), Shape(unconfigured));
            Assert.Equal(Shape(budget), Shape(all));
            Assert.Empty(StoredAnalysisFindings(off));
            Assert.Equal(Shape(budget.Where(finding => !IsStoredAnalysis(finding))),
                Shape(off));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private string SeedProjectWhoseStoredAnalysisNoLongerParses()
    {
        using var cache = pristine.NewScratch();
        var services = cache.ServiceLocator;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var root = services.GetInstance<ILexEntryFactory>().Create(
                services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem),
                TsStringUtils.MakeString("ka", cache.DefaultVernWs), "root",
                new SandboxGenericMSA { MsaType = MsaType.kStem });
            var wordform = services.GetInstance<IWfiWordformFactory>().Create(
                TsStringUtils.MakeString("kata", cache.DefaultVernWs));
            var analysis = services.GetInstance<IWfiAnalysisFactory>().Create();
            wordform.AnalysesOC.Add(analysis);
            var bundle = services.GetInstance<IWfiMorphBundleFactory>().Create();
            analysis.MorphBundlesOS.Add(bundle);
            bundle.MorphRA = root.LexemeFormOA;
            bundle.MsaRA = root.MorphoSyntaxAnalysesOC.Single();
            cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.approves);
        });
        RealParserProject.PrepareForParsing(cache, "k", "a", "t");
        return cache.ProjectId.Path;
    }

    private static GrammarWarning[] Check(GrammarCheckRequest request)
    {
        var outcome = GrammarCheckQuery.Query(request);
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        return outcome.Value!.Findings.ToArray();
    }

    private static bool IsStoredAnalysis(GrammarWarning finding) =>
        finding.Code?.StartsWith(StoredAnalysisPrefix, StringComparison.Ordinal) == true;

    private static GrammarWarning[] StoredAnalysisFindings(IEnumerable<GrammarWarning> findings) =>
        findings.Where(IsStoredAnalysis).ToArray();

    private static string[] Shape(IEnumerable<GrammarWarning> findings) => findings
        .Select(finding => $"{finding.Code}|{finding.Severity}|{finding.Description}")
        .Order(StringComparer.Ordinal).ToArray();
}
