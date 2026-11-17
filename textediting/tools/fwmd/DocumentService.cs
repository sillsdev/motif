using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SIL.TextEditing.Markdown;
using SIL.TextEditing.Markdown.Records;

namespace SIL.TextEditing.CommandLine;

internal interface IRecordSchemaLoader
{
    RecordSchema Load(string path);
}

/// <summary>Reads a serialised record schema (RecordSchema.ToJson) from a file. Any failure is a usage error naming the file.</summary>
internal sealed class FileRecordSchemaLoader : IRecordSchemaLoader
{
    public RecordSchema Load(string path)
    {
        try
        {
            return RecordSchema.FromJson(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is FormatException or IOException or UnauthorizedAccessException
            or JsonException or ArgumentException or NotSupportedException)
        {
            throw new UsageException("Cannot load record schema '" + path + "': " + exception.Message);
        }
    }
}

internal sealed class UsageException(string message) : Exception(message);

internal sealed class DocumentAnalysis(IReadOnlyList<MarkdownDiagnostic> findings, Func<string>? format, bool record)
{
    public IReadOnlyList<MarkdownDiagnostic> Findings { get; } = findings;
    public bool IsRecord { get; } = record;
    public bool HasErrors => Findings.Any(f => f.Severity == MarkdownSeverity.Error);
    /// <summary>False for a record file read without a schema: only its frame was checked, so it cannot be written.</summary>
    public bool CanFormat => format != null;
    public string Format()
    {
        if (HasErrors) throw new InvalidOperationException("Refusing to format a file with errors.");
        if (format == null) throw new InvalidOperationException("Record formatting requires a record schema.");
        return format();
    }
}

internal sealed class DocumentService(ToolConfig? config = null, RecordSchema? schema = null,
    MarkdownOptions? hostOptions = null, RecordReadOptions? recordOptions = null)
{
    public DocumentAnalysis Analyse(string text)
    {
        var probe = MarkdownConverter.Parse(text);
        bool record = probe.Diagnostics.Any(d => d.Id == MarkdownDiagnosticIds.RecordFile);
        var frame = record ? RecordFileReader.ReadFrame(text) : null;
        var options = hostOptions ?? (config ?? new ToolConfig()).Options(probe.FrontMatter, frame?.Document);
        if (record)
        {
            var readOptions = recordOptions ?? new RecordReadOptions { Markdown = options };
            var result = schema == null ? RecordFileReader.ReadFrame(text, readOptions) : RecordFileReader.Read(text, schema, readOptions);
            return new DocumentAnalysis(result.Diagnostics,
                schema == null ? null : () => RecordFileWriter.Write(result.Document!, schema), true);
        }
        var parsed = MarkdownConverter.Parse(text, options);
        return new DocumentAnalysis(parsed.Diagnostics, () => MarkdownConverter.ToMarkdown(parsed.Document, options), false);
    }
}
