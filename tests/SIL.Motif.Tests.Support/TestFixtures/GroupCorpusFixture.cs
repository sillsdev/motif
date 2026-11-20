using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.LcmUtils;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// A saved project whose one Text holds kaonan (approved), baha, bahaan (parsed), lisan and Lisan, with kaon (alternate form kaonan, gloss eat) as its
/// one lexicon stem. <see cref="AddBahaStem"/> gives a copy of it the stem baha and a parse for the word baha.
/// </summary>
public static class GroupCorpusFixture
{
    public static string Create(PristineProjectFixture pristine)
    {
        using var cache = pristine.NewScratch();
        var services = cache.ServiceLocator;
        var vern = cache.DefaultVernWs;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var entry = services.GetInstance<ILexEntryFactory>().Create();
            var lexeme = services.GetInstance<IMoStemAllomorphFactory>().Create();
            entry.LexemeFormOA = lexeme;
            lexeme.MorphTypeRA = services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem);
            lexeme.Form.set_String(vern, "kaon");
            var alternate = services.GetInstance<IMoAffixAllomorphFactory>().Create();
            entry.AlternateFormsOS.Add(alternate);
            alternate.Form.set_String(vern, "kaonan");
            var sense = services.GetInstance<ILexSenseFactory>().Create();
            entry.SensesOS.Add(sense);
            sense.Gloss.set_String(cache.DefaultAnalWs, "eat");

            var text = services.GetInstance<ITextFactory>().Create();
            text.Name.set_String(cache.DefaultAnalWs, "Group fixture");
            var contents = services.GetInstance<IStTextFactory>().Create();
            text.ContentsOA = contents;
            var paragraph = services.GetInstance<IStTxtParaFactory>().Create();
            contents.ParagraphsOS.Add(paragraph);
            paragraph.Contents = TsStringUtils.MakeString("kaonan baha bahaan lisan", vern);
            var segment = paragraph.SegmentsOS.Count > 0 ? paragraph.SegmentsOS[0] : services.GetInstance<ISegmentFactory>().Create();
            if (paragraph.SegmentsOS.Count == 0) paragraph.SegmentsOS.Add(segment);

            IWfiWordform Word(string form, int times, bool analysed = false, bool approved = false)
            {
                var wordform = services.GetInstance<IWfiWordformFactory>().Create(TsStringUtils.MakeString(form, vern));
                if (analysed)
                {
                    var analysis = services.GetInstance<IWfiAnalysisFactory>().Create();
                    wordform.AnalysesOC.Add(analysis);
                    var bundle = services.GetInstance<IWfiMorphBundleFactory>().Create();
                    analysis.MorphBundlesOS.Add(bundle);
                    if (form == "kaonan")
                    {
                        bundle.MorphRA = lexeme;
                        bundle.SenseRA = sense;
                        bundle.Form.set_String(vern, "kaon");
                    }
                    if (approved) cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.approves);
                }
                for (var i = 0; i < times; i++) segment.AnalysesRS.Add(wordform);
                return wordform;
            }
            Word("kaonan", 3, analysed: true, approved: true);
            Word("baha", 1);
            Word("bahaan", 2, analysed: true);
            Word("lisan", 1);
            Word("Lisan", 1);
            paragraph.ParseIsCurrent = true;
        });
        new FwDataProjectLoader().Save(cache);
        return cache.ProjectId.Path;
    }

    /// <summary>Adds the stem baha and a parse of the word baha to the saved project at <paramref name="path"/>.</summary>
    public static void AddBahaStem(string path)
    {
        var loader = new FwDataProjectLoader();
        using var cache = loader.LoadCache(path);
        var services = cache.ServiceLocator;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var entry = services.GetInstance<ILexEntryFactory>().Create();
            var lexeme = services.GetInstance<IMoStemAllomorphFactory>().Create();
            entry.LexemeFormOA = lexeme;
            lexeme.MorphTypeRA = services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem);
            lexeme.Form.set_String(cache.DefaultVernWs, "baha");
            var wordform = services.GetInstance<IWfiWordformRepository>().AllInstances()
                .Single(word => word.Form.get_String(cache.DefaultVernWs).Text == "baha");
            wordform.AnalysesOC.Add(services.GetInstance<IWfiAnalysisFactory>().Create());
        });
        loader.Save(cache);
    }

    /// <summary>Adds the entry kaonvar, a variant of both the kaon stem and the baha stem, to the saved project at <paramref name="path"/>.</summary>
    public static void AddTwoComponentVariant(string path)
    {
        var loader = new FwDataProjectLoader();
        using var cache = loader.LoadCache(path);
        var services = cache.ServiceLocator;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            ILexEntry Stem(string text) => services.GetInstance<ILexEntryRepository>().AllInstances()
                .Single(entry => entry.LexemeFormOA?.Form.get_String(cache.DefaultVernWs).Text == text);
            var variant = services.GetInstance<ILexEntryFactory>().Create();
            var lexeme = services.GetInstance<IMoStemAllomorphFactory>().Create();
            variant.LexemeFormOA = lexeme;
            lexeme.MorphTypeRA = services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem);
            lexeme.Form.set_String(cache.DefaultVernWs, "kaonvar");
            var reference = services.GetInstance<ILexEntryRefFactory>().Create();
            variant.EntryRefsOS.Add(reference);
            reference.RefType = LexEntryRefTags.krtVariant;
            reference.ComponentLexemesRS.Add(Stem("kaon"));
            reference.ComponentLexemesRS.Add(Stem("baha"));
        });
        loader.Save(cache);
    }
}
