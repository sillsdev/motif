using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;

namespace SIL.Motif.Tests.App.RealClient;

/// <summary>Creates the same saved, approved analysis for App tests that exercise analysis changes.</summary>
internal static class StoredAnalysisFixture
{
    /// <summary>Adds a wordform with an approved analysis that contains the seeded lexical entry.</summary>
    /// <param name="fwDataPath">The FieldWorks project to edit.</param>
    /// <param name="lexicalEntryId">The lexical entry used in the analysis bundle.</param>
    /// <param name="word">The wordform's spelling.</param>
    /// <returns>The saved wordform and analysis identities.</returns>
    internal static (Guid WordformId, Guid AnalysisId) Add(string fwDataPath, Guid lexicalEntryId, string word)
    {
        var wordformId = Guid.Empty;
        var analysisId = Guid.Empty;
        new FieldWorksSimulator(fwDataPath).SaveEdit(cache =>
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var wordform = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString(word, cache.DefaultVernWs));
                wordformId = wordform.Guid;
                var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
                wordform.AnalysesOC.Add(analysis);
                analysisId = analysis.Guid;
                var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(lexicalEntryId);
                var bundle = cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
                analysis.MorphBundlesOS.Add(bundle);
                bundle.MorphRA = entry.LexemeFormOA;
                bundle.MsaRA = entry.MorphoSyntaxAnalysesOC.First();
            }));
        return (wordformId, analysisId);
    }
}
