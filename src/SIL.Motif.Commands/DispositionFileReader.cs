using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.Dispositions;

namespace SIL.Motif.Commands;

/// <summary>One row of a disposition file, read but not yet checked; <see cref="Problem"/> is set when the row could not be read at all.</summary>
internal sealed record RawDispositionRow(
    int Row, string Form, string? Category, string? Intended, string? Evidence, string? Problem, bool Truncated = false);

/// <summary>The rows of a disposition file in the format they were read as.</summary>
internal sealed record ParsedDispositionFile(string Format, IReadOnlyList<RawDispositionRow> Rows, int Skipped);

/// <summary>
/// Reads the three files <c>wordform dispose --from-file</c> takes: a TSV with form, category, intended and evidence
/// columns, a JSONL file of those four fields, and a grammar lane ledger, whose Disposition rows are the ones read. Row
/// numbers are physical line numbers, so a person can open the file at the row a refusal names.
/// </summary>
internal static class DispositionFileReader
{
    public const string Tsv = "tsv";
    public const string Jsonl = "jsonl";
    public const string Ledger = "ledger";

    private const string LedgerHeader = "word\ttokens\tstate\texpected_readings\tclass\tevidence\tprovenance\tgrammar_revision";
    private const int LedgerFields = 8;
    private const string IntendedPrefix = "intended=";
    private static readonly string[] TsvColumns = ["form", "category", "intended", "evidence"];

    /// <summary>The formats <c>--format</c> accepts, apart from detection.</summary>
    public static readonly IReadOnlyList<string> Formats = [Tsv, Jsonl, Ledger];

    /// <summary>Reads the file as <paramref name="format"/>, or detects the format from its first line; a problem with the file as a whole is returned as text.</summary>
    public static (ParsedDispositionFile? File, string? Problem) Read(string path, string? format)
    {
        List<string> lines;
        try
        {
            using var reader = new StreamReader(path, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
            lines = [];
            while (reader.ReadLine() is { } line) lines.Add(line);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException
            or ArgumentException or NotSupportedException)
        {
            return (null, exception is DecoderFallbackException
                ? $"The file '{path}' is not valid UTF-8."
                : $"The file '{path}' could not be read: {exception.Message}");
        }
        var first = lines.FindIndex(line => line.Trim().Length > 0);
        if (first < 0) return (null, $"The file '{path}' is empty.");
        format ??= Detect(lines[first]);
        return format switch
        {
            Jsonl => (new(Jsonl, ReadJsonl(lines), 0), null),
            Ledger => ReadLedger(lines),
            _ => ReadTsv(lines, first),
        };
    }

    private static string Detect(string firstLine)
    {
        var text = firstLine.TrimStart();
        if (text.StartsWith('{')) return Jsonl;
        if (firstLine.TrimEnd('\r').StartsWith("word\ttokens\tstate\t", StringComparison.Ordinal)) return Ledger;
        return Tsv;
    }

    private static (ParsedDispositionFile?, string?) ReadTsv(List<string> lines, int firstLine)
    {
        var header = lines[firstLine].Split('\t').Select(cell => cell.Trim().ToLowerInvariant()).ToArray();
        var hasHeader = header.Contains("form", StringComparer.Ordinal);
        var columns = hasHeader ? header : TsvColumns;
        var form = Array.IndexOf(columns, "form");
        var category = Array.IndexOf(columns, "category");
        var intended = Array.IndexOf(columns, "intended");
        var evidence = Array.IndexOf(columns, "evidence");
        if (hasHeader && (category < 0 || evidence < 0))
            return (null, "The TSV header must name the columns form, category and evidence, and may name intended.");
        var rows = new List<RawDispositionRow>();
        for (var i = hasHeader ? firstLine + 1 : firstLine; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Trim().Length == 0) continue;
            var fields = line.Split('\t');
            string? At(int column) => column >= 0 && column < fields.Length ? fields[column] : null;
            rows.Add(fields.Length > columns.Length
                ? new(i + 1, At(form) ?? "", null, null, null, $"The row has {fields.Length} fields; the file has {columns.Length} columns.")
                : new(i + 1, At(form) ?? "", At(category), At(intended), At(evidence), null));
        }
        return (new(Tsv, rows, 0), null);
    }

    private static List<RawDispositionRow> ReadJsonl(List<string> lines)
    {
        var rows = new List<RawDispositionRow>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Trim().Length == 0) continue;
            try
            {
                using var document = JsonDocument.Parse(lines[i]);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    rows.Add(new(i + 1, "", null, null, null, "The line is not a JSON object."));
                    continue;
                }
                var problem = (string?)null;
                string? Field(string name)
                {
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                        if (property.Value.ValueKind == JsonValueKind.String) return property.Value.GetString();
                        if (property.Value.ValueKind != JsonValueKind.Null) problem = $"The field '{name}' is not a string.";
                    }
                    return null;
                }
                var form = Field("form");
                rows.Add(new(i + 1, form ?? "", Field("category"), Field("intended"), Field("evidence"), problem));
            }
            catch (JsonException)
            {
                rows.Add(new(i + 1, "", null, null, null, "The line is not valid JSON."));
            }
        }
        return rows;
    }

    private static (ParsedDispositionFile?, string?) ReadLedger(List<string> lines)
    {
        if (lines[0].TrimEnd('\r') != LedgerHeader)
            return (null, "The ledger header is not the eight literal columns " + LedgerHeader.Replace('\t', ',') + ".");
        var rows = new List<RawDispositionRow>();
        var skipped = 0;
        for (var i = 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Length == 0) continue;
            var fields = line.Split('\t');
            if (fields.Length != LedgerFields)
            {
                rows.Add(new(i + 1, fields[0], null, null, null, $"A ledger row must have {LedgerFields} fields; this has {fields.Length}."));
                continue;
            }
            if (fields[2] != "Disposition") { skipped++; continue; }
            var kind = fields[4];
            var evidence = fields[5];
            string? intended = null;
            if (kind == "misspelling" && evidence.StartsWith(IntendedPrefix, StringComparison.Ordinal))
            {
                // Same split as the Coverage state rule: the intended form is everything up to " | ".
                var body = evidence[IntendedPrefix.Length..];
                var bar = body.IndexOf(" | ", StringComparison.Ordinal);
                intended = bar >= 0 ? body[..bar] : body;
                if (bar >= 0 && body[(bar + 3)..].Trim().Length > 0) evidence = body[(bar + 3)..];
            }
            var truncated = evidence.Length > WordTypeDispositionCommands.MaxTextLength;
            if (truncated) evidence = evidence[..(WordTypeDispositionCommands.MaxTextLength - 3)] + "...";
            rows.Add(new(i + 1, fields[0], kind, intended, evidence, null, truncated));
        }
        return (new(Ledger, rows, skipped), null);
    }
}
