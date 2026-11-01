using System.Text;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands.Assess;

/// <summary>
/// The one reading of a word list a caller names by file path, shared by the CLI's <c>--words</c> and the
/// <c>wordsFile</c> argument of the agent tools, so both accept the same thing and refuse the same way.
/// </summary>
public static class WordListFile
{
    /// <summary>The maximum encoded word-file size, 8 MiB, including any initial UTF-8 signature.</summary>
    public const int MaxBytes = 8 * 1024 * 1024;

    /// <summary>
    /// The words to measure: the inline <paramref name="words"/>, or the lines of <paramref name="wordsFile"/>
    /// (strict UTF-8, at most 8 MiB, one word per line, blank lines dropped). An empty result means no words were named.
    /// </summary>
    public static CommandOutcome<IReadOnlyList<string>> Resolve(IReadOnlyList<string>? words, string? wordsFile)
    {
        var inline = words ?? [];
        if (string.IsNullOrWhiteSpace(wordsFile))
        {
            var inlineLines = inline.Select((word, index) => (Number: index + 1, Text: word.Trim()))
                .Where(line => HasInnerSpace(line.Text)).ToArray();
            return inlineLines.Length == 0
                ? CommandOutcome<IReadOnlyList<string>>.Success(inline)
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
    internal static CommandOutcome<IReadOnlyList<string>> Read(Stream stream, string wordsFile)
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
        for (var number = 1; reader.ReadLine() is { } line; number++)
        {
            var word = line.Trim();
            if (word.Length == 0) continue;
            words.Add(word);
            if (HasInnerSpace(word)) multiWord.Add((number, word));
        }
        if (multiWord.Count > 0)
            return MultiWord(multiWord, $"The words file '{Path.GetFileName(wordsFile)}'", "line", wordsFile);
        return words.Count == 0
            ? Refused(RefusalCodes.AssessWordsFileEmpty,
                $"The words file '{Path.GetFileName(wordsFile)}' has no words; write one word per line.", wordsFile)
            : CommandOutcome<IReadOnlyList<string>>.Success(words);
    }

    private static bool HasInnerSpace(string word) => word.Any(char.IsWhiteSpace);

    // One word per line; a spaced line parses as no word and would block Apply with no reason.
    private static CommandOutcome<IReadOnlyList<string>> MultiWord(
        IReadOnlyList<(int Number, string Text)> lines, string subject, string noun, string file)
    {
        var shown = string.Join(", ", lines.Take(5).Select(line => $"{noun} {line.Number} \"{line.Text}\""));
        var more = lines.Count > 5 ? $" and {lines.Count - 5} more" : "";
        var message = $"{subject} has {lines.Count} multi-word {noun}{(lines.Count == 1 ? "" : "s")}: {shown}{more}. " +
            $"Each {noun} must be one word with no spaces; split these onto separate {noun}s, then repeat the call.";
        return Refused(RefusalCodes.AssessWordsMultiWord, message, file);
    }

    private static CommandOutcome<IReadOnlyList<string>> Refused(string code, string message, string file, string? exception = null)
    {
        var facts = new Dictionary<string, string> { ["wordsFile"] = file };
        if (exception is not null) facts["exceptionDetail"] = exception;
        return CommandOutcome<IReadOnlyList<string>>.Refused(new Refusal(code, FailureReason.InvalidArgument, message, facts));
    }
}
