using System.Text;
using System.Text.Json;

namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// The structured refusal PanGloss writes on one standard-error line when a grammar cannot compile: schema
/// version 1 of its compile-error report, whose <c>status</c> is <c>compile_error</c>.
/// </summary>
/// <remarks>
/// Standard error can also carry progress lines, so the report is found by its status, not by position. A
/// line with another schema version is not read, and the caller keeps PanGloss's raw words instead.
/// </remarks>
public sealed record PanGlossCompileError(string Message, IReadOnlyList<PanGlossCompileError.Issue> Issues)
{
    public const int SchemaVersion = PanGlossInterfaceVersions.CompileErrorSchemaVersion;

    /// <summary>One conversion finding; <see cref="Fatal"/> marks the ones that prevented compilation.</summary>
    public sealed record Issue(string Code, string Text, string? Advice, bool Fatal);

    /// <summary>Reads the compile-error report from <paramref name="standardError"/>, if it holds one.</summary>
    public static PanGlossCompileError? TryRead(string standardError)
    {
        foreach (var line in standardError.Split('\n'))
        {
            var candidate = line.Trim();
            if (!candidate.StartsWith('{')) continue;
            try
            {
                using var document = JsonDocument.Parse(candidate);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("status", out var status) || status.GetString() != "compile_error" ||
                    !root.TryGetProperty("schema_version", out var version) || version.ValueKind != JsonValueKind.Number ||
                    version.GetInt32() != SchemaVersion)
                    continue;
                var issues = new List<Issue>();
                if (root.TryGetProperty("issues", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in list.EnumerateArray())
                        issues.Add(new Issue(
                            StringOrEmpty(item, "code"), StringOrEmpty(item, "text"),
                            item.TryGetProperty("advice", out var advice) && advice.ValueKind == JsonValueKind.String
                                ? advice.GetString() : null,
                            item.TryGetProperty("fatal", out var fatal) && fatal.ValueKind == JsonValueKind.True));
                }
                return new PanGlossCompileError(StringOrEmpty(root, "message"), issues);
            }
            catch (JsonException)
            {
                // A progress line that only starts with a brace is not the report; keep looking.
            }
        }
        return null;
    }

    /// <summary>
    /// The refusal in words: the exit, PanGloss's message, and each fatal issue with its advice. An issue whose
    /// text repeats the message contributes only its advice.
    /// </summary>
    public string Describe(string subcommand, int exitCode)
    {
        var text = new StringBuilder($"pangloss {subcommand} exited {exitCode}: it could not load the grammar. {Message}");
        foreach (var issue in Issues.Where(issue => issue.Fatal))
        {
            var parts = new[] { issue.Text == Message ? null : issue.Text, issue.Advice }
                .Where(part => !string.IsNullOrWhiteSpace(part));
            var line = string.Join(" ", parts);
            if (line.Length > 0) text.Append(Environment.NewLine).Append("- ").Append(line);
        }
        return text.ToString();
    }

    private static string StringOrEmpty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
