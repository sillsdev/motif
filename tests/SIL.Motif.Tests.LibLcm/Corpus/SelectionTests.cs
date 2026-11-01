using System.Security.Cryptography;
using System.Text;
using SIL.Motif.Host.Corpus;
using Xunit;

namespace SIL.Motif.Tests.Corpus;

/// <summary>
/// Unit tests for the pure hashing/ordering half of a Selection — no <c>LcmCache</c>, no parser,
/// just a word list in and a deterministic hash out. See
/// <see cref="SIL.Motif.Tests.Corpus.LcmWordformCorpusTests"/> for the part that needs a real project.
/// </summary>
public class SelectionTests
{
    [Fact]
    public void Create_SortsWordsOrdinally_RegardlessOfInputOrder()
    {
        var selection = Selection.Create("test-corpus", new[] { "nkazi", "anthu", "mbali" });

        Assert.Equal(new[] { "anthu", "mbali", "nkazi" }, selection.Words);
    }

    [Fact]
    public void Create_StoresEveryWordDecomposedSoATrialAndAnAssessOfOneWordAgree()
    {
        // பொன் composed (U+0BCA) and decomposed (U+0BC6 U+0BBE): one word, stored decomposed (NFD) either way.
        const string composed = "\u0BAA\u0BCA\u0BA9\u0BCD";
        const string decomposed = "\u0BAA\u0BC6\u0BBE\u0BA9\u0BCD";

        var selection = Selection.Create("test-corpus", new[] { composed, decomposed });

        Assert.Equal(new[] { decomposed }, selection.Words);
        Assert.Equal(Selection.Create("test-corpus", new[] { decomposed }).Sha256, selection.Sha256);
    }

    [Fact]
    public void Create_SameWordsInADifferentInputOrder_ProduceTheSameHash()
    {
        var first = Selection.Create("test-corpus", new[] { "mbali", "ya", "nkazi" });
        var second = Selection.Create("test-corpus", new[] { "nkazi", "mbali", "ya" });

        // The whole point of sorting before hashing: differently-enumerated same wordforms must not look like drift.
        Assert.Equal(first.Sha256, second.Sha256);
        Assert.Equal(first.Words, second.Words);
    }

    [Fact]
    public void Create_HashChangesWhenTheWordListChanges()
    {
        var original = Selection.Create("test-corpus", new[] { "mbali", "ya", "nkazi" });
        var withOneMoreWord = Selection.Create("test-corpus", new[] { "mbali", "ya", "nkazi", "munthu" });
        var withOneWordRemoved = Selection.Create("test-corpus", new[] { "mbali", "ya" });

        Assert.NotEqual(original.Sha256, withOneMoreWord.Sha256);
        Assert.NotEqual(original.Sha256, withOneWordRemoved.Sha256);
    }

    [Fact]
    public void Create_HashIsStableAcrossRepeatedCallsWithIdenticalInput()
    {
        var words = new[] { "mbali", "ya", "nkazi" };

        var first = Selection.Create("test-corpus", words);
        var second = Selection.Create("test-corpus", words);

        Assert.Equal(first.Sha256, second.Sha256);
    }

    [Fact]
    public void Create_HashDoesNotDependOnTheName()
    {
        // The name is a label; the hash is a content fact — differently-labelled selections over the same words match.
        var first = Selection.Create("sena-3", new[] { "mbali", "ya" });
        var second = Selection.Create("a different label", new[] { "mbali", "ya" });

        Assert.Equal(first.Sha256, second.Sha256);
        Assert.NotEqual(first.Name, second.Name);
    }

    [Fact]
    public void Create_HashIsPrefixedLikeOtherDigestsInThisCodebase()
    {
        var selection = Selection.Create("test-corpus", new[] { "mbali" });

        Assert.StartsWith("sha256:", selection.Sha256);
        Assert.Equal("sha256:".Length + 64, selection.Sha256.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RejectsAMissingName(string? name)
    {
        Assert.Throws<ArgumentException>(() => Selection.Create(name!, new[] { "mbali" }));
    }

    [Fact]
    public void Create_RejectsNullWords()
    {
        Assert.Throws<ArgumentNullException>(() => Selection.Create("test-corpus", null!));
    }

    [Fact]
    public void Create_AcceptsAnEmptyCorpus()
    {
        // An empty selection is a legitimate (if useless) input; hashing and ordering do not require words to exist.
        var selection = Selection.Create("empty-corpus", Array.Empty<string>());

        Assert.Empty(selection.Words);
        Assert.StartsWith("sha256:", selection.Sha256);
    }

    [Fact]
    public void ACorpusIsASet_SoDuplicatesDoNotChangeItsIdentity()
    {
        // GrammarCoverageFigure compares with set semantics; a duplicate-counting hash would fake drift.
        var once = Selection.Create("sena", new[] { "mbali", "ya", "miseru" });
        var twice = Selection.Create("sena", new[] { "mbali", "ya", "mbali", "miseru", "ya" });

        Assert.Equal(once.Sha256, twice.Sha256);
        Assert.Equal(once.Words, twice.Words);
        Assert.Equal(3, twice.Words.Count);
    }
    // பொன் composed (U+0BCA) and decomposed (U+0BC6 U+0BBE).
    private const string TamilComposed = "\u0BAA\u0BCA\u0BA9\u0BCD";
    private const string TamilDecomposed = "\u0BAA\u0BC6\u0BBE\u0BA9\u0BCD";

    // The hash a Selection carried before its words were decomposed: sha256 of the stored words, newline-joined.
    private static string HashOfWordsAsTheyAre(params string[] orderedWords) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", orderedWords))))
            .ToLowerInvariant();

    [Fact]
    public void ASelectionOfWordsAlreadyInNfdKeepsTheHashItAlwaysHad()
    {
        string[] words = ["anthu", TamilDecomposed, "mbali"];

        var selection = Selection.Create("kept", words);

        Assert.Equal(HashOfWordsAsTheyAre(words.OrderBy(w => w, StringComparer.Ordinal).ToArray()), selection.Sha256);
    }

    [Fact]
    public void ASelectionStoredWithAComposedWordStillAgreesWithItsOwnHash()
    {
        var stored = new Selection("old", [TamilComposed], HashOfWordsAsTheyAre(TamilComposed));

        Assert.True(stored.HashMatchesStoredWords());
        // Rebuilding it decomposes the word, so the rebuilt hash differs: a stored record is checked, not rebuilt.
        Assert.NotEqual(stored.Sha256, Selection.Create("old", stored.Words).Sha256);
    }

    [Fact]
    public void ASelectionWhoseHashIsNotTheHashOfItsWordsDoesNotAgree()
    {
        Assert.False(new Selection("bad", ["anthu"], HashOfWordsAsTheyAre("mbali")).HashMatchesStoredWords());
        Assert.True(Selection.Create("good", [TamilComposed, "anthu"]).HashMatchesStoredWords());
    }

    [Fact]
    public void ARightToLeftWordIsStoredInNfdAndIsCanonicallyEquivalentToWhatWasTyped()
    {
        // Arabic alef-with-madda and Hebrew with points: NFD changes the code points, never the text they spell.
        string[] words = ["\u0622\u0645\u0646", "\u05E9\u05C1\u05DC\u05D5\u05B9\u05DD"];

        var selection = Selection.Create("rtl", words);

        Assert.Equal(words.Select(w => w.Normalize(NormalizationForm.FormD)).OrderBy(w => w, StringComparer.Ordinal),
            selection.Words);
        // Both spellings are canonically equivalent to the typed word, so a renderer draws and copies the same text.
        Assert.Equal(words.Select(w => w.Normalize(NormalizationForm.FormC)).OrderBy(w => w, StringComparer.Ordinal),
            selection.Words.Select(w => w.Normalize(NormalizationForm.FormC)).OrderBy(w => w, StringComparer.Ordinal));
    }
}
