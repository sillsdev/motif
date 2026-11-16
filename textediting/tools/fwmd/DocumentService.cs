using System;
using System.Collections.Generic;
using System.Linq;
using SIL.TextEditing.Markdown;
using SIL.TextEditing.Markdown.Records;

namespace SIL.TextEditing.CommandLine;

internal interface IRecordSchemaLoader
{
    RecordSchema Load(string path);
}

internal sealed class UnavailableSchemaLoader : IRecordSchemaLoader
{
    public RecordSchema Load(string path) => throw new UsageException("--schema is not yet available in this build.");
}

internal sealed class UsageException(string message) : Exception(message);

internal sealed class DocumentAnalysis(IReadOnlyList<MarkdownDiagnostic> findings, Func<string>? format, bool record)
{
    public IReadOnlyList<MarkdownDiagnostic> Findings { get; } = findings;
    public bool IsRecord { get; } = record;
    public bool HasErrors => Findings.Any(f => f.Severity == MarkdownSeverity.Error);
    public string Format()
    {
        if (HasErrors) throw new InvalidOperationException("Refusing to format a file with errors.");
        if (format == null) throw new UsageException("Record formatting requires a record schema; schema loading is not yet available.");
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
                schema == null || result.Document == null ? null : () => RecordFileWriter.Write(result.Document, schema), true);
        }
        var parsed = MarkdownConverter.Parse(text, options);
        return new DocumentAnalysis(parsed.Diagnostics, () => MarkdownConverter.ToMarkdown(parsed.Document, options), false);
    }
}
