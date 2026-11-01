using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands.Assess;

/// <summary>The words a list names and, for a file, where it came from and the counts it gave.</summary>
/// <param name="Words">The words in file order, as written once trimmed; duplicates are kept.</param>
/// <param name="List">The file's name, hash and counts; null when the words were typed.</param>
public sealed record WordListContents(IReadOnlyList<string> Words, SelectionWordList? List);

/// <summary>
/// The one reading of a word list a caller names by file path, shared by the CLI's <c>--words</c> and the
/// <c>wordsFile</c> argument of the agent tools, so both accept the same thing and refuse the same way. A line is a
/// word, or a word, a tab and a whole-number count; there is no header line.
/// </summary>
public static class WordListFile
{
    /// <summary>The maximum encoded word-file size, 8 MiB, including any initial UTF-8 signature.</summary>
    public const int MaxBytes = 8 * 1024 * 1024;

    /// <summary>
    /// The words to measure: the inline <paramref name="words"/>, or the words of <paramref name="wordsFile"/>
    /// (strict UTF-8, at most 8 MiB, one word per line, blank lines dropped). An empty result means no words were named.
    /// </summary>
    public static CommandOutcome<IReadOnlyList<string>> Resolve(IReadOnlyList<string>? words, string? wordsFile)
    {
        var contents = ResolveWithCounts(words, wordsFile);
        return contents.Succeeded
            ? CommandOutcome<IReadOnlyList<string>>.Success(contents.Value!.Words)
            : CommandOutcome<IReadOnlyList<string>>.Refused(contents.Refusal!);
    }

    /// <summary>
    /// Like <see cref="Resolve"/>, and for a file also its name, hash and the count each word was given. A count
    /// is the sum over a word's lines once the word is in NFD; a word given no count has none, which is not zero.
    /// </summary>
    public static CommandOutcome<WordListContents> ResolveWithCounts(IReadOnlyList<string>? words, string? wordsFile)
    {
        var inline = words ?? [];
        if (string.IsNullOrWhiteSpace(wordsFile))
        {
            var inlineLines = inline.Select((word, index) => (Number: index + 1, Text: word.Trim()))
                .Where(line => HasInnerSpace(line.Text)).ToArray();
            return inlineLines.Length == 0
                ? CommandOutcome<WordListContents>.Success(new WordListContents(inline, null))
                : MultiWord(inlineLines, "The word list", "entry", "");
        }
        if (inline.Count > 0)
            return Refused(RefusalCodes.AssessWordsAmbiguous,
                "Give either words or a words file, not both.", wordsFile);
        try
        {
            using var stream = File.OpenRead(wordsFile);
            return Read(stream, wordsFile);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return Refused(RefusalCodes.AssessWordsFileUnreadable,
                $"The words file '{Path.GetFileName(wordsFile)}' does not exist.", wordsFile);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException)
        {
            return Refused(RefusalCodes.AssessWordsFileUnreadable,
                $"The words file '{Path.GetFileName(wordsFile)}' could not be read.", wordsFile, exception.Message);
        }
    }

    /// <summary>
    /// Reads at most <see cref="MaxBytes"/> plus one sentinel byte from a caller-owned stream without closing it.
    /// The bound uses bytes actually read, independent of seek support or reported file size.
    /// </summary>
    internal static CommandOutcome<WordListContents> Read(Stream stream, string wordsFile)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (buffer.Length < MaxBytes)
        {
            var read = stream.Read(chunk, 0, Math.Min(chunk.Length, MaxBytes - (int)buffer.Length));
            if (read == 0) break;
            buffer.Write(chunk, 0, read);
        }
        if (buffer.Length == MaxBytes && stream.ReadByte() != -1)
            return Refused(RefusalCodes.AssessWordsFileTooLarge,
                $"The words file '{Path.GetFileName(wordsFile)}' exceeds 8 MiB; split it into smaller word lists and repeat the call.", wordsFile);
        var bytes = buffer.GetBuffer();
        var length = (int)buffer.Length;
        if (Array.IndexOf(bytes, (byte)0, 0, length) >= 0)
            return Refused(RefusalCodes.AssessWordsFileNotText,
                $"The words file '{Path.GetFileName(wordsFile)}' contains NUL bytes; save it as UTF-8 text with one word per line.", wordsFile);
        string text;
        try
        {
            var offset = length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes, offset, length - offset);
        }
        catch (DecoderFallbackException)
        {
            return Refused(RefusalCodes.AssessWordsFileNotText,
                $"The words file '{Path.GetFileName(wordsFile)}' is not valid UTF-8 text; save it as UTF-8 with one word per line.", wordsFile);
        }
        using var reader = new StringReader(text);
        var words = new List<string>();
        var multiWord = new List<(int Number, string Text)>();
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        for (var number = 1; reader.ReadLine() is { } line; number++)
        {
            if (line.Trim().Length == 0) continue;
            var columns = line.Split('\t');
            var word = columns[0].Trim();
            if (word.Length == 0 || columns.Length > 2 || !TryCount(columns.Length == 2 ? columns[1] : null, out var count))
                return Refused(RefusalCodes.AssessWordsFileBadRow,
                    $"Line {number} of the words file '{Path.GetFileName(wordsFile)}' is not a word, or a word, a tab and a whole-number count; there is no header line.", wordsFile);
            words.Add(word);
            if (HasInnerSpace(word)) multiWord.Add((number, word));
            if (count is null) continue;
            var key = word.Normalize(NormalizationForm.FormD);
            var sum = counts.GetValueOrDefault(key) + count.Value;
            if (sum > int.MaxValue)
                return Refused(RefusalCodes.AssessWordsFileBadRow,
                    $"The counts of '{word}' in the words file '{Path.GetFileName(wordsFile)}' add up to more than {int.MaxValue:N0}.", wordsFile);
            counts[key] = sum;
        }
        if (multiWord.Count > 0)
            return MultiWord(multiWord, $"The words file '{Path.GetFileName(wordsFile)}'", "line", wordsFile);
        if (words.Count == 0)
            return Refused(RefusalCodes.AssessWordsFileEmpty,
                $"The words file '{Path.GetFileName(wordsFile)}' has no words; write one word per line.", wordsFile);
        var hash = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes.AsSpan(0, length))).ToLowerInvariant();
        var list = new SelectionWordList(Path.GetFileName(wordsFile), hash,
            counts.ToDictionary(entry => entry.Key, entry => (int)entry.Value, StringComparer.Ordinal));
        return CommandOutcome<WordListContents>.Success(new WordListContents(words, list));
    }

    // An absent or blank count is not known; anything else must be whole digits that fit a count.
    private static bool TryCount(string? text, out long? count)
    {
        count = null;
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return true;
        if (trimmed.Length > 10 || !trimmed.All(char.IsAsciiDigit)) return false;
        var value = long.Parse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture);
        count = value;
        return value <= int.MaxValue;
    }

    private static bool HasInnerSpace(string word) => word.Any(char.IsWhiteSpace);

    // One word per line; a spaced line parses as no word and would block Apply with no reason.
    private static CommandOutcome<WordListContents> MultiWord(
        IReadOnlyList<(int Number, string Text)> lines, string subject, string noun, string file)
    {
        var shown = string.Join(", ", lines.Take(5).Select(line => $"{noun} {line.Number} \"{line.Text}\""));
        var more = lines.Count > 5 ? $" and {lines.Count - 5} more" : "";
        var message = $"{subject} has {lines.Count} multi-word {noun}{(lines.Count == 1 ? "" : "s")}: {shown}{more}. " +
            $"Each {noun} must be one word with no spaces; split these onto separate {noun}s, then repeat the call.";
        return Refused(RefusalCodes.AssessWordsMultiWord, message, file);
    }

    private static CommandOutcome<WordListContents> Refused(string code, string message, string file, string? exception = null)
    {
        var facts = new Dictionary<string, string> { ["wordsFile"] = file };
        if (exception is not null) facts["exceptionDetail"] = exception;
        return CommandOutcome<WordListContents>.Refused(new Refusal(code, FailureReason.InvalidArgument, message, facts));
    }
}
