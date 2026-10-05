using SIL.Motif.Host.Texts;
using SIL.Motif.Tests.TestFixtures;
using SIL.LCModel;
using Xunit;

namespace SIL.Motif.Tests.Texts;

/// <summary>
/// Pins <see cref="InterlinearTextReader"/> against <see cref="SeededProject.SeedText"/>'s fixed shape:
/// two paragraphs, an approved analysis with two ordered morph bundles, a punctuation form, and one
/// unanalysed wordform — every occurrence kind the projection has to tell apart.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class InterlinearTextReaderTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly SeededProject _seed;
    private readonly SeededText _text;
    private readonly InterlinearTextProjection _projection;

    public InterlinearTextReaderTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _seed = pristine.Seed;
        _text = SeededProject.SeedText(_cache, _seed);

        var text = _cache.ServiceLocator.GetInstance<ITextRepository>().GetObject(_text.TextId);
        _projection = InterlinearTextReader.Read(_cache, text);
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void WordformOccurrencesMatchTheFullProjectionWithAGlossAndAdditionalWritingSystems()
    {
        var text = _cache.ServiceLocator.GetInstance<ITextRepository>().GetObject(_text.TextId);
        var segment = _cache.ServiceLocator.GetInstance<ISegmentRepository>().GetObject(_text.FirstSegmentId);
        var analysis = Assert.IsAssignableFrom<IWfiAnalysis>(segment.AnalysesRS[0]);
        SIL.LCModel.Infrastructure.NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var gloss = _cache.ServiceLocator.GetInstance<IWfiGlossFactory>().Create();
            analysis.MeaningsOC.Add(gloss);
            segment.AnalysesRS[0] = gloss;
            var wordform = (IWfiWordform)analysis.Owner;
            wordform.Form.set_String(_cache.DefaultAnalWs,
                SIL.LCModel.Core.Text.TsStringUtils.MakeString("café", _cache.DefaultAnalWs));
        });
        var expected = InterlinearTextReader.Read(_cache, text).Paragraphs.SelectMany(paragraph => paragraph.Phrases)
            .SelectMany(phrase => phrase.Words).Where(word => word.WordformGuid is not null).ToArray();

        var actual = InterlinearTextReader.ReadWordforms(text).ToArray();

        Assert.Equal(expected.Select(word => word.WordformGuid), actual.Select(word => (Guid?)word.Wordform.Guid));
        Assert.Equal(expected.Select(word => word.AnalysisStatus != InterlinearAnalysisStatus.Unanalysed),
            actual.Select(word => word.IsAnalysed));
        Assert.Equal(expected.SelectMany(word => word.Items.Where(item => item.Type == "txt").Select(item => item.Value)),
            actual.SelectMany(word => word.Wordform.Form.AvailableWritingSystemIds.Order()
                .Select(ws => word.Wordform.Form.get_String(ws).Text)));
    }

    [Fact]
    public void ReadsTheTextGuidAndOneTitleItemInTheAnalysisWritingSystem()
    {
        Assert.Equal(_text.TextId, _projection.Guid);

        var title = Assert.Single(_projection.Title);
        Assert.Equal("title", title.Type);
        Assert.Equal(_cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultAnalWs), title.Lang);
        Assert.Equal(SeededProject.TextTitle, title.Value);
    }

    [Fact]
    public void PreservesParagraphAndSegmentOrder()
    {
        Assert.Equal(2, _projection.Paragraphs.Count);
        Assert.Equal(_text.FirstParagraphId, _projection.Paragraphs[0].Guid);
        Assert.Equal(_text.SecondParagraphId, _projection.Paragraphs[1].Guid);

        Assert.Equal(_text.FirstSegmentId, Assert.Single(_projection.Paragraphs[0].Phrases).Guid);
        Assert.Equal(_text.SecondSegmentId, Assert.Single(_projection.Paragraphs[1].Phrases).Guid);
    }

    [Fact]
    public void TheAnalysedWordCarriesItsWordformGuidSurfaceFormAndApprovedStatus()
    {
        var word = _projection.Paragraphs[0].Phrases[0].Words[0];
        var vernTag = _cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultVernWs);

        Assert.Equal(_text.AnalysedWordformId, word.WordformGuid);
        Assert.Equal(InterlinearAnalysisStatus.Approved, word.AnalysisStatus);
        Assert.Contains(word.Items, item => item is { Type: "txt", Value: SeededProject.AnalysedWordForm } && item.Lang == vernTag);
    }

    [Fact]
    public void ThePunctuationOccurrenceCarriesNoWordformGuidAndAPunctItem()
    {
        var word = _projection.Paragraphs[0].Phrases[0].Words[1];
        var vernTag = _cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultVernWs);

        Assert.Null(word.WordformGuid);
        Assert.Equal(InterlinearAnalysisStatus.Unanalysed, word.AnalysisStatus);
        Assert.Empty(word.Morphemes);
        Assert.Contains(word.Items, item => item is { Type: "punct", Value: SeededProject.PunctuationForm } && item.Lang == vernTag);
    }

    [Fact]
    public void TheUnanalysedWordformCarriesItsGuidAndUnanalysedStatusWithNoMorphemes()
    {
        var word = Assert.Single(_projection.Paragraphs[1].Phrases[0].Words);
        var vernTag = _cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultVernWs);

        Assert.Equal(_text.UnanalysedWordformId, word.WordformGuid);
        Assert.Equal(InterlinearAnalysisStatus.Unanalysed, word.AnalysisStatus);
        Assert.Empty(word.Morphemes);
        Assert.Contains(word.Items, item => item is { Type: "txt", Value: SeededProject.UnanalysedWordForm } && item.Lang == vernTag);
    }

    [Fact]
    public void MorphBundlesKeepDeclaredOrderWithEachOnesGlossAndTheWordsCategory()
    {
        var word = _projection.Paragraphs[0].Phrases[0].Words[0];

        Assert.Contains(word.Items, item => item is { Type: "pos", Value: "SeededNoun" });

        Assert.Equal(2, word.Morphemes.Count);
        var first = word.Morphemes[0];
        var second = word.Morphemes[1];

        Assert.Equal(_seed.FirstLexemeFormId, first.MorphGuid);
        Assert.Equal(_seed.SecondLexemeFormId, second.MorphGuid);
        Assert.Contains(first.Items, item => item.Type == "gls" && item.Value == SeededProject.FirstGloss);
        Assert.Contains(second.Items, item => item.Type == "gls" && item.Value == SeededProject.SecondGloss);
    }
}
