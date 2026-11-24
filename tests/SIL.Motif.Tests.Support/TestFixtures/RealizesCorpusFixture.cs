using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.LcmUtils;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// A saved project with the stem kaon and the suffix -an (alternate form -han), and four stored analyses of the suffix: kaonan (an),
/// kaonhan (han, the alternate), kaonon (on, disapproved by a person) and, when asked, kaonxan (a bundle with no written form).
/// </summary>
public static class RealizesCorpusFixture
{
    public static string Create(PristineProjectFixture pristine, bool withUnwrittenBundle = false, bool withAnalyses = true)
    {
        using var cache = pristine.NewScratch();
        var services = cache.ServiceLocator;
        var vern = cache.DefaultVernWs;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var morphTypes = services.GetInstance<IMoMorphTypeRepository>();
            var stemEntry = services.GetInstance<ILexEntryFactory>().Create();
            var stem = services.GetInstance<IMoStemAllomorphFactory>().Create();
            stemEntry.LexemeFormOA = stem;
            stem.MorphTypeRA = morphTypes.GetObject(MoMorphTypeTags.kguidMorphStem);
            stem.Form.set_String(vern, "kaon");
            stemEntry.MorphoSyntaxAnalysesOC.Add(services.GetInstance<IMoStemMsaFactory>().Create());

            var suffixEntry = services.GetInstance<ILexEntryFactory>().Create();
            var lexeme = services.GetInstance<IMoAffixAllomorphFactory>().Create();
            suffixEntry.LexemeFormOA = lexeme;
            lexeme.MorphTypeRA = morphTypes.GetObject(MoMorphTypeTags.kguidMorphSuffix);
            lexeme.Form.set_String(vern, "-an");
            var alternate = services.GetInstance<IMoAffixAllomorphFactory>().Create();
            suffixEntry.AlternateFormsOS.Add(alternate);
            alternate.MorphTypeRA = morphTypes.GetObject(MoMorphTypeTags.kguidMorphSuffix);
            alternate.Form.set_String(vern, "-han");
            var msa = services.GetInstance<IMoInflAffMsaFactory>().Create();
            suffixEntry.MorphoSyntaxAnalysesOC.Add(msa);

            void Word(string form, IMoForm morph, string? written, bool disapproved = false)
            {
                var wordform = services.GetInstance<IWfiWordformFactory>().Create(TsStringUtils.MakeString(form, vern));
                var analysis = services.GetInstance<IWfiAnalysisFactory>().Create();
                wordform.AnalysesOC.Add(analysis);
                var stemBundle = services.GetInstance<IWfiMorphBundleFactory>().Create();
                analysis.MorphBundlesOS.Add(stemBundle);
                stemBundle.MorphRA = stem;
                stemBundle.Form.set_String(vern, "kaon");
                var bundle = services.GetInstance<IWfiMorphBundleFactory>().Create();
                analysis.MorphBundlesOS.Add(bundle);
                bundle.MorphRA = morph;
                bundle.MsaRA = msa;
                // Assigning the morph fills the bundle form, so an unwritten bundle is made by clearing it.
                if (written is not null) bundle.Form.set_String(vern, written);
                else bundle.Form.set_String(vern, TsStringUtils.EmptyString(vern));
                if (disapproved) cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.disapproves);
            }
            if (!withAnalyses) return;
            Word("kaonan", lexeme, "an");
            Word("kaonhan", alternate, "han");
            Word("kaonon", lexeme, "on", disapproved: true);
            if (withUnwrittenBundle) Word("kaonxan", lexeme, null);
        });
        new FwDataProjectLoader().Save(cache);
        return cache.ProjectId.Path;
    }
}
