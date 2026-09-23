using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App.Walkthrough;

/// <summary>
/// Models a fresh chat receiving flat file uploads and one pasted prompt, with the current OpenAI
/// FAQ allowances of 80 files per three hours and 512 MiB per file.
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
    /// `DropRefusesDirectories`.</summary>
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

        if (!_files.ContainsKey("handoff.md"))
            failures.Add("handoff.md was not received.");

        if (_files.TryGetValue("grammar.json", out var grammar))
        {
            try { using var _ = JsonDocument.Parse(grammar.Bytes); }
            catch (JsonException exception) { failures.Add($"grammar.json is not valid JSON: {exception.Message}"); }
        }
        else
        {
            failures.Add("grammar.json was not received.");
        }

        ValidateJsonFileIfReceived("texts.json", failures);
        ValidateJsonFileIfReceived("assessment.json", failures);
        ReportNestedPathFindings(findings);
        foreach (var finding in findings) _output?.WriteLine("Finding: " + finding);
        return new ChatValidationResult(failures, findings);
    }

    private static IReadOnlyList<string> FileNamesMentionedByPrompt(string prompt) =>
        CodeSpan.Matches(prompt).Select(match => match.Groups[1].Value)
            .Where(value => value.Contains('.') && !value.Contains(' '))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    // Optional: a Baseline-only Handoff carries neither file, so only whichever was received is checked.
    private void ValidateJsonFileIfReceived(string name, List<string> failures)
    {
        if (!_files.TryGetValue(name, out var file)) return;
        try { using var _ = JsonDocument.Parse(file.Bytes); }
        catch (JsonException exception) { failures.Add($"{name} is not valid JSON: {exception.Message}"); }
    }

    private void ReportNestedPathFindings(List<string> findings)
    {
        if (!_files.TryGetValue("handoff.md", out var handoffMarkdown)) return;
        foreach (var path in CodeSpan.Matches(System.Text.Encoding.UTF8.GetString(handoffMarkdown.Bytes))
                     .Select(match => match.Groups[1].Value)
                     .Where(value => value.Contains('/') && !value.Contains("\n") && !value.Contains("://"))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            findings.Add($"handoff.md refers to nested path '{path}', but flat upload keeps only its basename.");
        }
    }
}

public sealed record ReceivedFile(string Name, long Size, byte[] Bytes);

public sealed record ChatValidationResult(IReadOnlyList<string> Failures, IReadOnlyList<string> Findings);
