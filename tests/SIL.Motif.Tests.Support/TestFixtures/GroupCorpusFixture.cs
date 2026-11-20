using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.LcmUtils;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>A saved project whose one Text holds kaonan (approved), baha, bahaan (parsed), lisan and Lisan, with kaon as its one lexicon stem.</summary>
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
}
