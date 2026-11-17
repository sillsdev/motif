using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SIL.Machine.Corpora;
using SIL.Motif.Host.Corpus;
using SIL.Scripture;

namespace SIL.Motif.Scripture;

/// <summary>A chapter's Machine verse rows, with notes excluded and whitespace normalized.</summary>
public sealed record ParatextPassage(string Locator, string Hash, IReadOnlyList<ParatextVerse> Verses);

/// <summary>One verse row in the Source's own versification.</summary>
public sealed record ParatextVerse(string Reference, string AnchorReference, string Text);

/// <summary>A tokenizer word with its permanent, one-based verse anchor (ADR 0024).</summary>
public sealed record ParatextWord(string Anchor, string Verse, string Surface);

/// <summary>Reads Paratext chapters through Machine and applies the permanent anchor policy.</summary>
public static class ParatextPassages
{
    /// <summary>The tokeniser's <c>Name</c> and <c>Version</c>, so a change to either rebuilds every stored index.</summary>
    public const string TokenizerVersion = "fieldworks-word-forming-1";
    public const string AnchorVersion = "scripture-1";

    /// <summary>
    /// Reads canonical verse rows in Machine order; chapter hashes ignore whitespace and notes. Only the books named
    /// in <paramref name="books"/> are read when it is given.
    /// </summary>
    public static IReadOnlyList<ParatextPassage> Read(
        string folder, CancellationToken cancellationToken = default, IReadOnlySet<string>? books = null)
    {
        if (!File.Exists(Path.Combine(folder, "Settings.xml")))
            throw new InvalidDataException("The Source folder has no Settings.xml.");
        ParatextTextCorpus corpus;
        try { corpus = new ParatextTextCorpus(folder); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new InvalidDataException("Machine could not open the Paratext Source: " + exception.Message, exception);
        }
        var passages = new List<ParatextPassage>();
        foreach (var text in corpus.Texts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Canon.IsCanonical(Canon.BookIdToNumber(text.Id)) || books is not null && !books.Contains(text.Id)) continue;
            var chapters = new Dictionary<int, List<ParatextVerse>>();
            foreach (var row in text.GetRows())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var reference = (ScriptureRef)row.Ref;
                if (!reference.IsVerse || reference.ChapterNum < 1 || reference.VerseNum < 1) continue;
                var normalized = Regex.Replace(row.Text, @"\s+", " ").Trim();
                if (!chapters.TryGetValue(reference.ChapterNum, out var verses))
                    chapters[reference.ChapterNum] = verses = [];
                verses.Add(new(reference.ToString(), $"{text.Id} {reference.ChapterNum}:{reference.VerseNum}", normalized));
            }
            foreach (var chapter in chapters)
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                foreach (var verse in chapter.Value)
                {
                    Append(hash, verse.Reference);
                    Append(hash, verse.Text);
                }
                passages.Add(new($"{text.Id} {chapter.Key}", Convert.ToHexStringLower(hash.GetHashAndReset()), chapter.Value));
            }
        }
        return passages;
    }

    /// <summary>Segments with the given tokeniser, so the index holds the words the corpus denominator counts; bridges anchor to their first verse.</summary>
    public static IReadOnlyList<ParatextWord> Tokenize(ParatextPassage passage, IWordTokeniser tokeniser)
    {
        ArgumentNullException.ThrowIfNull(tokeniser);
        var words = new List<ParatextWord>();
        var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var verse in passage.Verses)
        foreach (var surface in tokeniser.Tokenise(verse.Text))
        {
            var ordinal = ordinals.GetValueOrDefault(verse.AnchorReference) + 1;
            ordinals[verse.AnchorReference] = ordinal;
            words.Add(new(verse.AnchorReference + "#" + ordinal, verse.AnchorReference, surface));
        }
        return words;
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(bytes.Length)));
        hash.AppendData(bytes);
    }
}
