using System.Text.Json;
using System.Text.RegularExpressions;
using SIL.Motif.Contract.Commands;

namespace SIL.Motif.Host.PanGloss;

/// <summary>Reads fatal conversion issues from structured parser output or the PanGloss 0.6 Debug format.</summary>
public static partial class ParserCompileDiagnosticReader
{
    public static ParserCompileDiagnostic? Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var issues = ReadJson(text) ?? ReadDebug(text);
        if (issues is null) return null;
        var distinct = issues.Distinct().ToArray();
        var remedy = distinct.Length == 1 ? "Fix it in FieldWorks, then Refresh." : "Fix them in FieldWorks, then Refresh.";
        var allEnvironments = distinct.Length > 0 && distinct.All(issue => (issue.ObjectKind ?? issue.Kind) == "PhEnvironment");
        var count = distinct.Length;
        var summary = count == 0 ? "PanGloss can't use this grammar. Fix it in FieldWorks, then Refresh."
            : allEnvironments
                ? $"PanGloss can't use this grammar: {count} environment{(count == 1 ? "" : "s")} it can't read. {remedy}"
                : $"PanGloss can't use this grammar: {count} fatal issue{(count == 1 ? "" : "s")}. {remedy}";
        return new(summary, distinct, text);
    }

    private static List<ParserCompileIssue>? ReadDebug(string text)
    {
        if (!text.Contains("ConversionError", StringComparison.Ordinal))
            return text.Contains("pangloss ", StringComparison.Ordinal) && text.Contains(": compile ", StringComparison.Ordinal)
                ? [] : null;
        var issues = new List<ParserCompileIssue>();
        foreach (Match match in DebugIssue().Matches(text))
        {
            if (match.Groups["fatal"].Value != "true") continue;
            var source = DebugSource().Match(match.Groups["source"].Value);
            var kind = source.Success ? source.Groups["kind"].Value : "Grammar";
            issues.Add(new(match.Groups["code"].Value, kind,
                source.Success ? Decode(source.Groups["id"].Value) : null, null,
                Decode(match.Groups["message"].Value), AdviceFor(kind)));
        }
        return issues.DistinctBy(issue => (issue.Code, issue.Kind, issue.ObjectGuid ?? issue.Text)).ToList();
    }

    private static List<ParserCompileIssue>? ReadJson(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            if (!line.Contains("compile_error", StringComparison.Ordinal)) continue;
            if (ReadJsonObject(line) is { } issues) return issues;
        }
        return ReadJsonObject(text);
    }

    private static List<ParserCompileIssue>? ReadJsonObject(string text)
    {
        // stderr can carry progress before the JSON diagnostic.
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end < start) return null;
        try
        {
            using var document = JsonDocument.Parse(text[start..(end + 1)]);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (String(root, "status") != "compile_error") return null;
            if (!root.TryGetProperty("schema_version", out var version) || version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out var number) || number != 1) return [];
            if (!root.TryGetProperty("issues", out var rows) || rows.ValueKind != JsonValueKind.Array) return [];
            var issues = new List<ParserCompileIssue>();
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object ||
                    !row.TryGetProperty("fatal", out var fatal) || fatal.ValueKind != JsonValueKind.True) continue;
                var kind = String(row, "kind") ?? "Grammar";
                var objectKind = String(row, "object_kind");
                var id = String(row, "object_guid");
                var message = String(row, "text");
                var code = String(row, "code");
                if (message is null || code is null) continue;
                issues.Add(new(code, kind, id, String(row, "field"), message,
                    String(row, "advice") ?? AdviceFor(objectKind ?? kind), objectKind));
            }
            return issues;
        }
        catch (JsonException) { return null; }
    }

    private static string? String(JsonElement element, string key) =>
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Decode(string quoted)
    {
        try { return JsonSerializer.Deserialize<string>(quoted) ?? quoted; }
        catch (JsonException) { return quoted.Trim('"'); }
    }

    private static string AdviceFor(string kind) => kind switch
    {
        "PhEnvironment" => "Fix in FieldWorks: Grammar › Environments, or Lexicon Edit › Allomorph › Environments.",
        "MoForm" or "LexEntry" => "Fix in FieldWorks: Lexicon Edit › Allomorph.",
        _ => "Fix this item in FieldWorks, then Refresh.",
    };

    [GeneratedRegex("ConversionIssue \\{\\s*code: (?<code>\\w+),\\s*class: \\w+,\\s*source: (?<source>None|Some\\(SourceRef \\{[^}]*\\}\\)),\\s*fatal: (?<fatal>true|false),\\s*message: (?<message>\"(?:\\\\.|[^\"\\\\])*\")\\s*\\}", RegexOptions.CultureInvariant)]
    private static partial Regex DebugIssue();

    [GeneratedRegex("kind: (?<kind>\\w+),\\s*id: (?<id>\"(?:\\\\.|[^\"\\\\])*\")", RegexOptions.CultureInvariant)]
    private static partial Regex DebugSource();
}
