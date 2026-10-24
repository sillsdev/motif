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
            return CommandOutcome<IReadOnlyList<string>>.Success(inline);
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
                $"The words file '{wordsFile}' does not exist.", wordsFile);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException)
        {
            return Refused(RefusalCodes.AssessWordsFileUnreadable,
                $"The words file '{wordsFile}' could not be read: {exception.Message}", wordsFile);
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
                $"The words file '{wordsFile}' exceeds 8 MiB; split it into smaller word lists and repeat the call.", wordsFile);
        var bytes = buffer.GetBuffer();
        var length = (int)buffer.Length;
        if (Array.IndexOf(bytes, (byte)0, 0, length) >= 0)
            return Refused(RefusalCodes.AssessWordsFileNotText,
                $"The words file '{wordsFile}' contains NUL bytes; save it as UTF-8 text with one word per line.", wordsFile);
        string text;
        try
        {
            var offset = length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes, offset, length - offset);
        }
        catch (DecoderFallbackException)
        {
            return Refused(RefusalCodes.AssessWordsFileNotText,
                $"The words file '{wordsFile}' is not valid UTF-8 text; save it as UTF-8 with one word per line.", wordsFile);
        }
        using var reader = new StringReader(text);
        var words = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            var word = line.Trim();
            if (word.Length > 0) words.Add(word);
        }
        return words.Count == 0
            ? Refused(RefusalCodes.AssessWordsFileEmpty,
                $"The words file '{wordsFile}' has no words; write one word per line.", wordsFile)
            : CommandOutcome<IReadOnlyList<string>>.Success(words);
    }

    private static CommandOutcome<IReadOnlyList<string>> Refused(string code, string message, string file) =>
        CommandOutcome<IReadOnlyList<string>>.Refused(new Refusal(code, FailureReason.InvalidArgument, message,
            new Dictionary<string, string> { ["wordsFile"] = file }));
}
