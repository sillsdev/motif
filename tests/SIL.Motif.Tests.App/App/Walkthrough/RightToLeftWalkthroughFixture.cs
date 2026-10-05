using SIL.LCModel;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Tests.TestFixtures;

namespace SIL.Motif.Tests.App.Walkthrough;

internal static class RightToLeftWalkthroughFixture
{
    public static void Configure(LcmCache cache, SeededProject _, SeededText text)
    {
        cache.ServiceLocator.WritingSystemManager.GetOrSet(SeededProject.RightToLeftTag, out var arabic);
        var paragraph = cache.ServiceLocator.GetInstance<IStTxtParaRepository>().GetObject(text.SecondParagraphId);
        var segment = cache.ServiceLocator.GetInstance<ISegmentRepository>().GetObject(text.SecondSegmentId);
        var source = cache.ServiceLocator.GetInstance<ITextRepository>().GetObject(text.TextId);

        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var wordform = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(WritingSystemTestData.Form, arabic.Handle));
            paragraph.Contents = TsStringUtils.MakeString(WritingSystemTestData.Form, arabic.Handle);
            segment.AnalysesRS.Clear();
            segment.AnalysesRS.Add(wordform);
            source.Name.set_String(arabic.Handle,
                TsStringUtils.MakeString(WritingSystemTestData.TextTitle, arabic.Handle));
        });
    }
}
