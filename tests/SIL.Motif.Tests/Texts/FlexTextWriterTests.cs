using System.Xml.Linq;
using System.Xml.Schema;
using SIL.Motif.Host.Texts;
using SIL.Motif.Tests.TestFixtures;
using SIL.LCModel;
using Xunit;

namespace SIL.Motif.Tests.Texts;

/// <summary>
/// The heart of this slice: <see cref="FlexTextXmlWriter"/> and <see cref="FlexTextJsonWriter"/> serialize
/// the same seeded projection, the XML validates against the real FieldWorks schema, and normalizing both
/// back agrees field-for-field with what <see cref="InterlinearTextReader"/> produced — so the two writers
/// cannot drift from each other unnoticed.
/// </summary>
/// <remarks>
/// Comparisons below walk each record's fields by hand rather than calling <c>Assert.Equal</c> on a whole
/// <see cref="InterlinearTextProjection"/>: a record's compiler-generated equality compares an
/// <c>IReadOnlyList&lt;T&gt;</c> field via <c>EqualityComparer&lt;T&gt;.Default</c>, which is reference
/// equality for <see cref="List{T}"/> since it overrides neither <c>Equals</c> nor <c>GetHashCode</c> — so
/// two freshly-built lists holding equal items would still compare unequal. Comparing an
/// <c>IReadOnlyList&lt;FlexItem&gt;</c> directly with <c>Assert.Equal</c> avoids that: xUnit's own
/// comparer recognizes the enumerable and compares elements, one level up from where the record's own
/// equality would have gone wrong.
/// </remarks>
[Collection(LcmCacheTestCollection.Name)]
public sealed class FlexTextWriterTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly InterlinearTextProjection _projection;

    public FlexTextWriterTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        var seededText = SeededProject.SeedText(_cache, pristine.Seed);
        var text = _cache.ServiceLocator.GetInstance<ITextRepository>().GetObject(seededText.TextId);
        _projection = InterlinearTextReader.Read(_cache, text);
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void XmlValidatesAgainstFlexInterlinearXsd()
    {
        var xml = FlexTextXmlWriter.Write(_projection);
        Assert.Empty(ValidationErrors(xml));
    }

    [Fact]
    public void XmlRoundTripAgreesWithTheOriginalProjection()
    {
        var xml = FlexTextXmlWriter.Write(_projection);
        var roundTripped = FlexTextXmlWriter.Read(xml);
        AssertProjectionsEqual(_projection, roundTripped);
    }

    [Fact]
    public void JsonRoundTripAgreesWithTheOriginalProjection()
    {
        var json = FlexTextJsonWriter.Write(_projection);
        var roundTripped = FlexTextJsonWriter.Read(json);
        AssertProjectionsEqual(_projection, roundTripped);
    }

    [Fact]
    public void JsonUsesFlexTextElementNamesVerbatimWithItemsCollapsedToTypeLangValue()
    {
        var json = FlexTextJsonWriter.Write(_projection);
        var interlinearText = json["document"]!["interlinear-text"]!.AsArray()[0]!.AsObject();

        Assert.True(interlinearText.ContainsKey("item"));
        var paragraph = interlinearText["paragraphs"]!["paragraph"]!.AsArray()[0]!.AsObject();
        var phrase = paragraph["phrases"]!["phrase"]!.AsArray()[0]!.AsObject();
        var word = phrase["words"]!["word"]!.AsArray()[0]!.AsObject();
        var item = word["item"]!.AsArray()[0]!.AsObject();

        Assert.Equal(["type", "lang", "value"], item.Select(pair => pair.Key));
    }

    [Fact]
    public void DuplicateTitlesProduceDistinctFilenamesBecauseOfTheGuidSuffix()
    {
        var second = _projection with { Guid = Guid.NewGuid() };

        var firstName = InterlinearTextFileNaming.BuildFileName(_projection, "flextext.json");
        var secondName = InterlinearTextFileNaming.BuildFileName(second, "flextext.json");

        Assert.NotEqual(firstName, secondName);
        Assert.EndsWith($"{_projection.Guid:N}.flextext.json", firstName);
        Assert.EndsWith($"{second.Guid:N}.flextext.json", secondName);
    }

    [Fact]
    public void AnUnsafeTitleSanitizesIntoAValidFilenameThatStillCarriesTheGuidSuffix()
    {
        var unsafeProjection = _projection with
        {
            Title = [new FlexItem("title", "en", "a/b:c*d?\"e<f>g|h")],
        };

        var name = InterlinearTextFileNaming.BuildFileName(unsafeProjection, "flextext.json");

        Assert.EndsWith($"{unsafeProjection.Guid:N}.flextext.json", name);
        Assert.All(Path.GetInvalidFileNameChars(), invalid => Assert.DoesNotContain(invalid, name));
    }

    private static List<string> ValidationErrors(XDocument xml)
    {
        var schemaPath = Path.Combine(AppContext.BaseDirectory, "Texts", "Fixtures", "FlexInterlinear.xsd");
        var schemas = new XmlSchemaSet();
        schemas.Add(null, schemaPath);

        var errors = new List<string>();
        xml.Validate(schemas, (_, e) => errors.Add(e.Message));
        return errors;
    }

    private static void AssertProjectionsEqual(InterlinearTextProjection expected, InterlinearTextProjection actual)
    {
        Assert.Equal(expected.Guid, actual.Guid);
        Assert.Equal(expected.Title, actual.Title);
        Assert.Equal(expected.Paragraphs.Count, actual.Paragraphs.Count);
        foreach (var (expectedParagraph, actualParagraph) in expected.Paragraphs.Zip(actual.Paragraphs))
            AssertParagraphsEqual(expectedParagraph, actualParagraph);
    }

    private static void AssertParagraphsEqual(InterlinearParagraph expected, InterlinearParagraph actual)
    {
        Assert.Equal(expected.Guid, actual.Guid);
        Assert.Equal(expected.Phrases.Count, actual.Phrases.Count);
        foreach (var (expectedPhrase, actualPhrase) in expected.Phrases.Zip(actual.Phrases))
            AssertPhrasesEqual(expectedPhrase, actualPhrase);
    }

    private static void AssertPhrasesEqual(InterlinearPhrase expected, InterlinearPhrase actual)
    {
        Assert.Equal(expected.Guid, actual.Guid);
        Assert.Equal(expected.Items, actual.Items);
        Assert.Equal(expected.Words.Count, actual.Words.Count);
        foreach (var (expectedWord, actualWord) in expected.Words.Zip(actual.Words))
            AssertWordsEqual(expectedWord, actualWord);
    }

    private static void AssertWordsEqual(InterlinearWord expected, InterlinearWord actual)
    {
        Assert.Equal(expected.WordformGuid, actual.WordformGuid);
        Assert.Equal(expected.AnalysisStatus, actual.AnalysisStatus);
        Assert.Equal(expected.Items, actual.Items);
        Assert.Equal(expected.Morphemes.Count, actual.Morphemes.Count);
        foreach (var (expectedMorpheme, actualMorpheme) in expected.Morphemes.Zip(actual.Morphemes))
        {
            Assert.Equal(expectedMorpheme.MorphGuid, actualMorpheme.MorphGuid);
            Assert.Equal(expectedMorpheme.Items, actualMorpheme.Items);
        }
    }
}
