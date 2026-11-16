using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using SIL.TextEditing.Markdown;

namespace SIL.TextEditing.CommandLine;

internal sealed class CliApplication(IRecordSchemaLoader? schemaLoader = null, DocumentService? service = null)
{
    public const string Usage = """
        fwmd check [paths|-] [--json] [--warnings-as-errors] [--schema <file.json>] [--config <fwmd.json>]
        fwmd format [paths|-] [--write | --check]
        fwmd --help

        Directories recurse over *.md. No paths means standard input; '-' must be the only input.
        check: 0 clean, 1 errors (or warnings with --warnings-as-errors), 2 usage or input error.
        format: canonical text to stdout; --write rewrites files; --check returns 1 if different.
        Files with errors are never formatted. Record formatting requires a record schema.
        --schema is not yet available. Config lists writingSystems and styles; front matter wins.
        Without config, style checks are off. Object references and project paths are not checked.
        Without a record schema, record fields and rich values are not checked; only the frame is checked.
        """;

    public int Run(string[] args, TextReader input, TextWriter output, TextWriter error)
    {
        try
        {
            if (args.Length == 1 && (args[0] == "--help" || args[0] == "-h"))
            {
                output.WriteLine(Usage);
                return 0;
            }
            var arguments = Arguments.Parse(args);
            var config = arguments.Config == null ? new ToolConfig() : ToolConfig.Load(arguments.Config);
            var schema = arguments.Schema == null ? null : (schemaLoader ?? new UnavailableSchemaLoader()).Load(arguments.Schema);
            var documents = service ?? new DocumentService(config, schema);
            var paths = Expand(arguments.Paths);
            if (arguments.Write && paths.Contains("-")) throw new UsageException("--write requires files, not standard input.");
            var results = new List<Result>();
            foreach (var path in paths)
            {
                var text = path == "-" ? input.ReadToEnd() : File.ReadAllText(path);
                results.Add(new Result(path, text, documents.Analyse(text)));
            }
            if (arguments.Command == "check")
            {
                if (arguments.Json) WriteJson(output, results, config.ChecksStyles, schema != null);
                else
                {
                    foreach (var result in results) WriteFindings(output, result);
                    output.WriteLine($"{results.Count} file(s), {results.Sum(r => r.Analysis.Findings.Count)} finding(s).");
                    output.WriteLine("Style checks: " + (config.ChecksStyles ? "on" : "off (no styles supplied)")
                        + "; object references and project paths: not checked. Writing systems: front matter and config defaults.");
                    if (schema == null && results.Any(r => r.Analysis.IsRecord))
                        output.WriteLine("Record fields and rich values: not checked (no schema); frame checks only.");
                }
                return results.Any(r => r.Analysis.HasErrors || arguments.WarningsAsErrors
                    && r.Analysis.Findings.Any(d => d.Severity == MarkdownSeverity.Warning)) ? 1 : 0;
            }
            if (results.Any(r => r.Analysis.HasErrors))
            {
                foreach (var result in results) WriteFindings(error, result);
                error.WriteLine("Refusing to format files with errors.");
                return 1;
            }
            var formatted = results.Select(r => (Result: r, Text: r.Analysis.Format())).ToList();
            bool changed = false;
            foreach (var item in formatted)
            {
                bool differs = item.Result.Text != item.Text;
                changed |= differs;
                if (arguments.Check)
                {
                    if (differs) output.WriteLine(item.Result.Path + ": not canonical");
                }
                else if (arguments.Write)
                {
                    if (differs) File.WriteAllText(item.Result.Path, item.Text, new UTF8Encoding(false));
                }
                else output.Write(item.Text);
            }
            return arguments.Check && changed ? 1 : 0;
        }
        catch (Exception exception) when (exception is UsageException or IOException or UnauthorizedAccessException
            or JsonException or ArgumentException or NotSupportedException)
        {
            error.WriteLine("fwmd: " + exception.Message);
            return 2;
        }
        catch (MarkdownWriteException exception)
        {
            error.WriteLine("fwmd: cannot format: " + exception.Message);
            return 1;
        }
    }

    static List<string> Expand(List<string> inputs)
    {
        if (inputs.Count == 0) inputs.Add("-");
        if (inputs.Contains("-"))
        {
            if (inputs.Count != 1) throw new UsageException("'-' must be the only input.");
            return inputs;
        }
        var paths = new List<string>();
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (string input in inputs)
        {
            if (Directory.Exists(input))
            {
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = false,
                    AttributesToSkip = FileAttributes.ReparsePoint };
                foreach (var path in Directory.EnumerateFiles(input, "*.md", options).OrderBy(p => p, StringComparer.Ordinal)) Add(path);
            }
            else
            {
                if (!File.Exists(input)) throw new UsageException("Input does not exist: " + input);
                Add(input);
            }
        }
        return paths;

        void Add(string path)
        {
            if (seen.Add(Path.GetFullPath(path))) paths.Add(path);
        }
    }

    static void WriteFindings(TextWriter output, Result result)
    {
        foreach (var d in result.Analysis.Findings)
            output.WriteLine($"{result.Path}:{d.Line}:{d.Column}: {d.Severity.ToString().ToLowerInvariant()} {d.Id}: {d.Message}");
    }

    static void WriteJson(TextWriter output, List<Result> results, bool styles, bool schema)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartArray();
            foreach (var result in results)
            {
                json.WriteStartObject();
                json.WriteString("path", result.Path);
                json.WriteStartArray("findings");
                foreach (var d in result.Analysis.Findings)
                {
                    json.WriteStartObject();
                    json.WriteString("id", d.Id);
                    json.WriteString("severity", d.Severity.ToString().ToLowerInvariant());
                    json.WriteNumber("line", d.Line);
                    json.WriteNumber("col", d.Column);
                    json.WriteNumber("endLine", d.EndLine);
                    json.WriteNumber("endCol", d.EndColumn);
                    json.WriteString("message", d.Message);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteStartObject("checks");
                json.WriteBoolean("styles", styles);
                json.WriteBoolean("objectReferences", false);
                json.WriteBoolean("projectPaths", false);
                json.WriteString("writingSystems", "front matter and config defaults");
                if (result.Analysis.IsRecord) json.WriteBoolean("recordFields", schema);
                json.WriteEndObject();
                json.WriteEndObject();
            }
            json.WriteEndArray();
        }
        output.WriteLine(Encoding.UTF8.GetString(buffer.ToArray()));
    }

    sealed record Result(string Path, string Text, DocumentAnalysis Analysis);

    sealed class Arguments
    {
        public string Command = "";
        public List<string> Paths { get; } = new();
        public bool Json, WarningsAsErrors, Write, Check;
        public string? Schema, Config;

        public static Arguments Parse(string[] args)
        {
            if (args.Length == 0 || (args[0] != "check" && args[0] != "format")) throw new UsageException(Usage);
            var result = new Arguments { Command = args[0] };
            bool pathsOnly = false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 1; i < args.Length; i++)
            {
                string arg = args[i];
                if (!pathsOnly && arg == "--") { pathsOnly = true; continue; }
                if (pathsOnly || arg == "-" || !arg.StartsWith("-", StringComparison.Ordinal)) { result.Paths.Add(arg); continue; }
                if (!seen.Add(arg)) throw new UsageException("Repeated option: " + arg);
                switch (arg)
                {
                    case "--json" when result.Command == "check": result.Json = true; break;
                    case "--warnings-as-errors" when result.Command == "check": result.WarningsAsErrors = true; break;
                    case "--schema" when result.Command == "check": result.Schema = Value(); break;
                    case "--config" when result.Command == "check": result.Config = Value(); break;
                    case "--write" when result.Command == "format": result.Write = true; break;
                    case "--check" when result.Command == "format": result.Check = true; break;
                    default: throw new UsageException("Unknown option for " + result.Command + ": " + arg);
                }
                string Value()
                {
                    if (++i >= args.Length || args[i].StartsWith("-", StringComparison.Ordinal))
                        throw new UsageException(arg + " requires a file path.");
                    return args[i];
                }
            }
            if (result.Write && result.Check) throw new UsageException("--write and --check are mutually exclusive.");
            return result;
        }
    }
}
