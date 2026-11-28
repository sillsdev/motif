using System.Text;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Query.Corpus;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>A Query pattern and a corpus form are compared in NFC, so a decomposed pattern counts what the composed one does (motif#556).</summary>
public sealed class PatternNormalizationQueryTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("pattern-normalization").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    // Tamil: the vowel sign O (U+0BCB) decomposes to U+0BC7 U+0BBE.
    private static readonly string[] Words = ["கோ", "கோன", "கோன", "கொ", "டு", "plain"];

    private long Count(string where, string[]? words = null)
    {
        var path = Path.Combine(_folder, "words.tsv");
        File.WriteAllText(path, string.Concat((words ?? Words).Select((word, position) => word + "\t" + (position + 1) + "\n")));
        var outcome = CorpusQueryCommand.Forms(new CorpusFormsRequest(path, where, null, null), null, default, null!, Path.Combine(_folder, "cache"));
        return outcome.Refusal is null ? outcome.Value!.Rows.Count : throw new Xunit.Sdk.XunitException(outcome.Refusal.Code + ": " + outcome.Refusal.Message);
    }

    private static string Where(string pattern) => "form ~ \"" + pattern + "\"";

    [Fact]
    public void TheSamePatternInNfdAndNfcCountsTheSameWords()
    {
        var nfc = "கோ";
        var nfd = nfc.Normalize(NormalizationForm.FormD);
        Assert.NotEqual(nfc, nfd);
        var composed = Count(Where(nfc));
        Assert.Equal(composed, Count(Where(nfd)));
        Assert.True(composed >= 2);
        // Words stored decomposed or composed are both found, whichever way the pattern is written.
        Assert.Equal(composed, Count(Where(nfd), [.. Words.Select(word => word.Normalize(NormalizationForm.FormD))]));
        Assert.Equal(composed, Count(Where(nfc), [.. Words.Select(word => word.Normalize(NormalizationForm.FormD))]));
    }

    [Fact]
    public void AnAlternationHoldingAVowelSignCountsTheSameInBothForms()
    {
        var nfc = "க(ு|ோ|ௌ).*";
        Assert.Equal(Count(Where(nfc)), Count(Where(nfc.Normalize(NormalizationForm.FormD))));
    }

    [Fact]
    public void AClassOfWholeLettersAndLoneMarksIsAcceptedInBothForms()
    {
        // The class holds a letter and a lone mark that do not compose with each other.
        var pattern = "[கட]ு?";
        Assert.Equal(Count(Where(pattern)), Count(Where(pattern.Normalize(NormalizationForm.FormD))));
    }

    [Fact]
    public void AClassHoldingALetterAndItsMarkThatComposeIsRefusedNotMatchedOnTheWrongForm()
    {
        var refusal = Assert.Throws<QueryRefusalException>(() => PatternNormalization.Pattern("x[\u0B95\u0BC6\u0BBE]"));
        Assert.Equal(RefusalCodes.QueryPatternNotNormalizable, refusal.Refusal.Code);
        var path = Path.Combine(_folder, "words.tsv");
        File.WriteAllText(path, "a\t1\n");
        var outcome = CorpusQueryCommand.Forms(new CorpusFormsRequest(path, Where("[\u0B95\u0BC6\u0BBE]"), null, null), null, default, null!, _folder);
        Assert.Equal(RefusalCodes.QueryPatternNotNormalizable, outcome.Refusal?.Code);
    }

    [Fact]
    public void AQuantifierOnAMarkThatWouldComposeIsRefused()
    {
        Assert.Equal(RefusalCodes.QueryPatternNotNormalizable,
            Assert.Throws<QueryRefusalException>(() => PatternNormalization.Pattern("கொ?")).Refusal.Code);
    }

    [Fact]
    public void AsciiPatternsAreUnchanged()
    {
        Assert.Same("^a[bc]+(d|e)\\.$", PatternNormalization.Pattern("^a[bc]+(d|e)\\.$"));
        Assert.Equal("a.b", PatternNormalization.Value("a.b"));
        Assert.Equal(1, Count(Where("^plain$")));
        Assert.Equal(0, Count(Where("^plaın$")));
    }
}
