using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App.Walkthrough;

/// <summary>
/// Models a fresh chat receiving flat file uploads and one pasted prompt, with the current OpenAI
/// FAQ allowances of 80 files per three hours and 512 MiB per file; it also documents a two-million-token text cap.
/// </summary>
public sealed class FakeChatReceiver
{
    private static readonly Regex CodeSpan = new("`([^`]+)`", RegexOptions.Compiled);
    private readonly Dictionary<string, ReceivedFile> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly ITestOutputHelper? _output;
    private readonly int _maxFileCount;
    private readonly long _maxFileSizeBytes;

    public FakeChatReceiver(
        ITestOutputHelper? output = null,
        int maxFileCount = 80,
        long maxFileSizeBytes = 512L * 1024 * 1024)
    {
        if (maxFileCount <= 0) throw new ArgumentOutOfRangeException(nameof(maxFileCount));
        if (maxFileSizeBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxFileSizeBytes));
        _output = output;
        _maxFileCount = maxFileCount;
        _maxFileSizeBytes = maxFileSizeBytes;
    }

    public IReadOnlyDictionary<string, ReceivedFile> Files => _files;

    public string? PastedText { get; private set; }

    /// <summary>Refuses a directory because a file uploader receives files, not a folder tree, pinned by
    /// `DropRefusesDirectoriesBasenameCollisionsAndConfiguredCaps`.</summary>
    public void Drop(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (_files.Count + paths.Count > _maxFileCount)
            throw new InvalidOperationException($"The upload exceeds the {_maxFileCount}-file cap.");

        var incoming = new List<ReceivedFile>();
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
                throw new InvalidOperationException("The upload accepts files, not directories.");

            var name = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("The upload path has no file name.");
            if (_files.ContainsKey(name) || incoming.Any(file =>
                    string.Equals(file.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"The upload contains a basename collision for '{name}'.");

            var bytes = File.ReadAllBytes(path);
            if (bytes.LongLength > _maxFileSizeBytes)
                throw new InvalidOperationException($"The file '{name}' exceeds the {_maxFileSizeBytes}-byte cap.");
            incoming.Add(new ReceivedFile(name, bytes.LongLength, bytes));
        }

        foreach (var file in incoming) _files.Add(file.Name, file);
    }

    public void Paste(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        PastedText = text;
    }

    public ChatValidationResult Validate()
    {
        var failures = new List<string>();
        var findings = new List<string>();
        if (PastedText is null)
        {
            failures.Add("No prompt was pasted into the fresh chat.");
            return new ChatValidationResult(failures, findings);
        }

        foreach (var name in FileNamesMentionedByPrompt(PastedText))
        {
            if (name.Contains('*'))
            {
                var suffix = name[(name.IndexOf('*') + 1)..];
                if (!_files.Keys.Any(file => file.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
                    failures.Add($"Prompt file pattern '{name}' has no received match.");
            }
            else if (!_files.ContainsKey(name))
            {
                failures.Add($"Prompt names '{name}', but that file was not received.");
            }
        }

        if (!_files.ContainsKey("instructions.md"))
            failures.Add("instructions.md was not received.");

        if (_files.TryGetValue("grammar.json", out var grammar))
        {
            try { using var _ = JsonDocument.Parse(grammar.Bytes); }
            catch (JsonException exception) { failures.Add($"grammar.json is not valid JSON: {exception.Message}"); }
        }
        else
        {
            failures.Add("grammar.json was not received.");
        }

        ValidateSelectionAgainstWordRows(failures);
        ReportNestedPathFindings(findings);
        foreach (var finding in findings) _output?.WriteLine("Finding: " + finding);
        return new ChatValidationResult(failures, findings);
    }

    private static IReadOnlyList<string> FileNamesMentionedByPrompt(string prompt) =>
        CodeSpan.Matches(prompt).Select(match => match.Groups[1].Value)
            .Where(value => value.Contains('.') && !value.Contains(' '))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private void ValidateSelectionAgainstWordRows(List<string> failures)
    {
        if (!_files.TryGetValue("selection.txt", out var selection))
        {
            failures.Add("selection.txt was not received.");
            return;
        }

        var words = SelectionWords(selection.Bytes);
        if (!_files.TryGetValue("word.jsonl", out var wordRows))
        {
            failures.Add("word.jsonl was not received for the selected words.");
            return;
        }

        var rows = new HashSet<string>(StringComparer.Ordinal);
        using var reader = new StringReader(System.Text.Encoding.UTF8.GetString(wordRows.Bytes));
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var document = JsonDocument.Parse(line);
                var value = document.RootElement.TryGetProperty("form", out var form) ? form
                    : document.RootElement.TryGetProperty("word", out var word) ? word : default;
                if (value.ValueKind == JsonValueKind.String) rows.Add(value.GetString()!);
            }
            catch (JsonException exception)
            {
                failures.Add($"word.jsonl contains invalid JSON: {exception.Message}");
            }
        }

        foreach (var word in words)
            if (!rows.Contains(word)) failures.Add($"selection.txt word '{word}' has no word.jsonl row.");
    }

    private static IReadOnlyList<string> SelectionWords(byte[] bytes)
    {
        var lines = System.Text.Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n").Split('\n');
        var provenance = Array.FindIndex(lines, line => line == "Provenance:");
        if (provenance < 0) return [];
        var firstWord = -1;
        for (var index = provenance + 1; index < lines.Length; index++)
        {
            if (!string.IsNullOrWhiteSpace(lines[index])) continue;
            firstWord = index;
            break;
        }
        return firstWord < 0 ? [] : lines[(firstWord + 1)..]
            .Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#')).ToList();
    }

    private void ReportNestedPathFindings(List<string> findings)
    {
        if (!_files.TryGetValue("instructions.md", out var instructions)) return;
        foreach (var path in CodeSpan.Matches(System.Text.Encoding.UTF8.GetString(instructions.Bytes))
                     .Select(match => match.Groups[1].Value)
                     .Where(value => value.Contains('/') && !value.Contains("\n") && !value.Contains("://"))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            findings.Add($"instructions.md refers to nested path '{path}', but flat upload keeps only its basename.");
        }
    }
}

public sealed record ReceivedFile(string Name, long Size, byte[] Bytes);

public sealed record ChatValidationResult(IReadOnlyList<string> Failures, IReadOnlyList<string> Findings);
