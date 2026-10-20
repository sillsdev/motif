using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands.Assess;

/// <summary>
/// The one reading of a word list a caller names by file path, shared by the CLI's <c>--words</c> and the
/// <c>wordsFile</c> argument of the agent tools, so both accept the same thing and refuse the same way.
/// </summary>
public static class WordListFile
{
    /// <summary>
    /// The words to measure: the inline <paramref name="words"/>, or the lines of <paramref name="wordsFile"/>
    /// (UTF-8, one word per line, blank lines dropped). An empty result means the caller named no words.
    /// </summary>
    public static CommandOutcome<IReadOnlyList<string>> Resolve(IReadOnlyList<string>? words, string? wordsFile)
    {
        var inline = words ?? [];
        if (string.IsNullOrWhiteSpace(wordsFile))
            return CommandOutcome<IReadOnlyList<string>>.Success(inline);
        if (inline.Count > 0)
            return Refused(RefusalCodes.AssessWordsAmbiguous,
                "Give either words or a words file, not both.", wordsFile);
        if (!File.Exists(wordsFile))
            return Refused(RefusalCodes.AssessWordsFileUnreadable,
                $"The words file '{wordsFile}' does not exist.", wordsFile);
        string[] lines;
        try { lines = File.ReadAllLines(wordsFile, System.Text.Encoding.UTF8); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException)
        {
            return Refused(RefusalCodes.AssessWordsFileUnreadable,
                $"The words file '{wordsFile}' could not be read: {exception.Message}", wordsFile);
        }
        var read = lines.Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();
        return read.Length == 0
            ? Refused(RefusalCodes.AssessWordsFileEmpty,
                $"The words file '{wordsFile}' has no words; write one word per line.", wordsFile)
            : CommandOutcome<IReadOnlyList<string>>.Success(read);
    }

    private static CommandOutcome<IReadOnlyList<string>> Refused(string code, string message, string file) =>
        CommandOutcome<IReadOnlyList<string>>.Refused(new Refusal(code, FailureReason.InvalidArgument, message,
            new Dictionary<string, string> { ["wordsFile"] = file }));
}
