using System.Web;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.PanGloss;

/// <summary>
/// Pins that a parser reading's identifiers become the form, gloss and category the project gives them, and
/// that a word links to its own wordform for FieldWorks' Try a Word.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group0)]
public sealed class ParserReadingReaderTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly SeededProject _seed;

    public ParserReadingReaderTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _seed = pristine.Seed;
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void AReadingResolvesToFormGlossAndCategory_LinkedToItsEntry()
    {
        var msa = _cache.ServiceLocator.GetInstance<ILexEntryRepository>()
            .GetObject(_seed.FirstEntryId).MorphoSyntaxAnalysesOC.Single();

        var reading = Assert.Single(ParserReadingReader.Read(_cache, "Sena 3", Evidence(
            new ParseMorph(_seed.FirstLexemeFormId.ToString("D"), msa.Guid.ToString("D"), null, null))));

        var morph = Assert.Single(reading.Morphs);
        Assert.Equal(SeededProject.FirstForm, morph.Form);
        Assert.Equal(SeededProject.FirstGloss, morph.Gloss);
        Assert.Equal(msa.InterlinearAbbr, morph.Category);
        Assert.Equal(NewLangProjFixture.VernacularTag, morph.FormWritingSystem);
        Assert.Equal(NewLangProjFixture.VernacularTag, morph.EntryWritingSystem);
        Assert.Equal(NewLangProjFixture.AnalysisTag, morph.GlossWritingSystem);
        Assert.False(morph.Guessed);
        Assert.Equal($"database=Sena 3&tool=lexiconEdit&guid={_seed.FirstEntryId:D}&tag=",
            HttpUtility.UrlDecode(morph.FieldWorksLink!["silfw://localhost/link?".Length..]));
    }

    [Fact]
    public void AGlossFallbackKeepsItsActualWritingSystemInsteadOfTheDefault()
    {
        var entry = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(_seed.FirstEntryId);
        var msa = entry.MorphoSyntaxAnalysesOC.Single();
        var rtl = _cache.ServiceLocator.WritingSystemManager.Get(SeededProject.RightToLeftTag);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            _cache.ServiceLocator.WritingSystems.AnalysisWritingSystems.Add(rtl);
            _cache.ServiceLocator.WritingSystems.CurrentAnalysisWritingSystems.Add(rtl);
            entry.SensesOS[0].Gloss.set_String(_cache.DefaultAnalWs, "");
            entry.SensesOS[0].Gloss.set_String(rtl.Handle, "عربي");
        });
        var morph = Assert.Single(Assert.Single(ParserReadingReader.Read(_cache, "Sample", Evidence(
            new ParseMorph(_seed.FirstLexemeFormId.ToString("D"), msa.Guid.ToString("D"), null, null)))).Morphs);
        Assert.Equal("عربي", morph.Gloss);
        Assert.Equal(rtl.Id, morph.GlossWritingSystem);
    }

    [Fact]
    public void AReadingKeepsTheAllomorphAndGrammaticalInfoItNames()
    {
        var msa = _cache.ServiceLocator.GetInstance<ILexEntryRepository>()
            .GetObject(_seed.FirstEntryId).MorphoSyntaxAnalysesOC.Single();

        var morph = Assert.Single(Assert.Single(ParserReadingReader.Read(_cache, "Sena 3", Evidence(
            new ParseMorph(_seed.FirstLexemeFormId.ToString("D"), msa.Guid.ToString("D"), null, null)))).Morphs);

        Assert.Equal(_seed.FirstLexemeFormId.ToString("D"), morph.AllomorphId);
        Assert.Equal(msa.Guid.ToString("D"), morph.GrammaticalInfoId);
    }

    [Fact]
    public void IdentifiersTheProjectLacksAreStillCarried_AndAMorphThatNamesNoneHasNoIds()
    {
        var reading = Assert.Single(ParserReadingReader.Read(_cache, "Sena 3", new ParseWordEvidence(
            ParseMorphEvidence.Schema, 0, "word", 1, false, false, false,
            [new ParseAnalysis([
                new ParseMorph("0C686AFA-8D21-4E3B-BC0E-41812150CF4C", "14ff3655-598a-4ce5-8186-805c65398553", null, null),
                new ParseMorph(null, "not a guid", null, "kuon")])], [])));

        Assert.Equal(("0c686afa-8d21-4e3b-bc0e-41812150cf4c", "14ff3655-598a-4ce5-8186-805c65398553"),
            (reading.Morphs[0].AllomorphId, reading.Morphs[0].GrammaticalInfoId));
        Assert.Equal(((string?)null, (string?)null), (reading.Morphs[1].AllomorphId, reading.Morphs[1].GrammaticalInfoId));
    }

    [Fact]
    public void AnIdentifierTheProjectLacksShowsAMarkerRatherThanTheIdentifier()
    {
        var reading = Assert.Single(ParserReadingReader.Read(_cache, "Sena 3", Evidence(
            new ParseMorph("0c686afa-8d21-4e3b-bc0e-41812150cf4c", "14ff3655-598a-4ce5-8186-805c65398553", null, null))));

        var morph = Assert.Single(reading.Morphs);
        Assert.Equal("(missing 0c686afa)", morph.Form);
        Assert.Equal("(missing 14ff3655)", morph.Gloss);
        Assert.Null(morph.FormWritingSystem);
        Assert.Null(morph.GlossWritingSystem);
        Assert.Null(morph.FieldWorksLink);
    }

    [Fact]
    public void AGuessedMorphShowsTheParsersGuess()
    {
        var reading = Assert.Single(ParserReadingReader.Read(_cache, "Sena 3", Evidence(
            new ParseMorph("0c686afa-8d21-4e3b-bc0e-41812150cf4c", "14ff3655-598a-4ce5-8186-805c65398553", null, "kuon"))));

        Assert.Equal(("kuon", true), (reading.Morphs[0].Form, reading.Morphs[0].Guessed));
    }

    [Fact]
    public void AWordLinksToItsWordformInWordAnalyses_AndAnUnknownWordHasNoLink()
    {
        IWfiWordform wordform = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            wordform = _cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("zzmotiftryword", _cache.DefaultVernWs)));

        var link = FieldWorksLinks.ForWordform(_cache, "Sena 3", "zzmotiftryword");

        Assert.Equal($"database=Sena 3&tool=Analyses&guid={wordform.Guid:D}&tag=",
            HttpUtility.UrlDecode(link!["silfw://localhost/link?".Length..]));
        Assert.Null(FieldWorksLinks.ForWordform(_cache, "Sena 3", "zznosuchword"));
    }

    private static ParseWordEvidence Evidence(ParseMorph morph) => new(
        ParseMorphEvidence.Schema, 0, "word", 1, false, false, false, [new ParseAnalysis([morph])], []);
}
