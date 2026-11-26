using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Query.Corpus;

/// <summary>
/// Reads a Guide block's <c>run</c> line of the form <c>motif query corpus forms --corpus ... forms where '...' [--group-by ...] --grammar ...</c>
/// into the request the read-only evaluator runs. A line that would write (<c>--write-words</c>) or whose answer a block cannot show is refused here, before anything runs.
/// Relative paths in the line are resolved by <see cref="Resolve"/> against the project's folder.
/// </summary>
public static class CorpusRunLine
{
    /// <summary>The words that start a corpus line after <c>motif query</c>.</summary>
    public const string Words = "corpus forms";

    private static readonly HashSet<string> Switches = new(StringComparer.Ordinal) { "fold-case", "split-hyphens", "thorough", "adjacent", "json" };

    private static readonly string[] ValueFlags =
        ["corpus", "where", "grammar", "segments", "limit", "segment-test", "group-by", "sum", "sample", "seed", "twin", "baseline-grammar", "project"];

    private static readonly string[] WriteFlags = ["write-words", "with-counts", "force"];

    private static readonly string[] NotShownFlags = ["query-json", "what-if-retire", "count-by", "by-morph"];

    /// <summary>Whether the tokens after <c>motif query</c> start a corpus line.</summary>
    public static bool IsCorpusLine(IReadOnlyList<string> arguments) => arguments is ["corpus", "forms", ..];

    /// <summary>Parses the tokens after <c>motif query corpus forms</c>.</summary>
    public static bool TryParse(IReadOnlyList<string> arguments, [NotNullWhen(true)] out CorpusFormsRequest? request, [NotNullWhen(false)] out Refusal? refusal)
    {
        request = null;
        var flags = new Dictionary<string, string>(StringComparer.Ordinal);
        var twins = new List<string>();
        var corpora = new List<string>();
        var positionals = new List<string>();
        for (var i = 0; i < arguments.Count; i++)
        {
            var token = arguments[i];
            if (!token.StartsWith("--", StringComparison.Ordinal)) { positionals.Add(token); continue; }
            var name = token[2..];
            if (WriteFlags.Contains(name))
            {
                refusal = new Refusal(RefusalCodes.GuideQueryRunNotReadOnly, FailureReason.Refused,
                    $"A Guide block only reads; --{name} writes a file, so a block cannot use it. Run it from the command line instead.", Facts(name));
                return false;
            }
            if (NotShownFlags.Contains(name))
            {
                refusal = new Refusal(RefusalCodes.GuideQueryRunInvalid, FailureReason.InvalidArgument,
                    $"A Guide block does not show the answer of --{name} yet; use a where, --group-by, --twin or --baseline-grammar.", Facts(name));
                return false;
            }
            if (Switches.Contains(name)) { flags[name] = "true"; continue; }
            if (!ValueFlags.Contains(name))
            {
                refusal = Invalid($"A corpus run line does not take --{name}.", name);
                return false;
            }
            if (i + 1 >= arguments.Count || arguments[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                refusal = Invalid($"--{name} needs a value.", name);
                return false;
            }
            var value = arguments[++i];
            if (name == "twin") twins.Add(value);
            else if (name == "corpus") corpora.Add(value);
            else flags[name] = value;
        }
        string? where = flags.GetValueOrDefault("where");
        if (positionals is ["forms", "where", var text]) where = where is null ? text : null;
        else if (positionals is not ["forms"])
        {
            refusal = Invalid("A corpus run line is: motif query corpus forms --corpus <words> forms where '<expression>' [more flags].", "positionals");
            return false;
        }
        if (corpora.Count == 0)
        {
            refusal = Invalid("A corpus run line names its words with --corpus.", "corpus");
            return false;
        }
        if (positionals is ["forms", "where", _] && flags.ContainsKey("where"))
        {
            refusal = Invalid("Give the where once: after forms, or as --where.", "where");
            return false;
        }
        if (!TryNumber(flags, "limit", out var limit, out refusal) || !TryNumber(flags, "sample", out var sample, out refusal)) return false;
        long? seed = null;
        if (flags.TryGetValue("seed", out var rawSeed))
        {
            if (!long.TryParse(rawSeed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsedSeed))
            {
                refusal = Invalid("--seed is a whole number.", "seed");
                return false;
            }
            seed = parsedSeed;
        }
        request = new CorpusFormsRequest(string.Join("\n", corpora), where, null, flags.GetValueOrDefault("grammar"), flags.GetValueOrDefault("segments"),
            flags.ContainsKey("fold-case"), flags.ContainsKey("split-hyphens"), limit, flags.GetValueOrDefault("segment-test"), flags.GetValueOrDefault("project"),
            flags.GetValueOrDefault("group-by"), null, flags.ContainsKey("thorough"), flags.GetValueOrDefault("sum"), sample, seed, null, false, false, null,
            flags.ContainsKey("adjacent"), false, twins.Count == 0 ? null : twins, flags.GetValueOrDefault("baseline-grammar"));
        refusal = null;
        return true;
    }

    /// <summary>The request with every relative path resolved against <paramref name="baseDirectory"/>; absolute paths are kept.</summary>
    public static CorpusFormsRequest Resolve(CorpusFormsRequest request, string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(request);
        string? One(string? path) => string.IsNullOrWhiteSpace(path) ? path : Path.GetFullPath(path, baseDirectory);
        var sources = request.CorpusPath.Split(['\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(path => One(path)!);
        return request with
        {
            CorpusPath = string.Join("\n", sources), GrammarPath = One(request.GrammarPath), SegmentsPath = One(request.SegmentsPath),
            ProjectPath = One(request.ProjectPath), BaselineGrammarPath = One(request.BaselineGrammarPath),
        };
    }

    /// <summary>
    /// <see cref="Resolve"/> for a Guide, which is shared text: a path that is absolute or resolves outside <paramref name="baseDirectory"/>
    /// is refused, so a block can only read files inside the project's folder.
    /// </summary>
    public static bool TryResolve(CorpusFormsRequest request, string baseDirectory, [NotNullWhen(true)] out CorpusFormsRequest? resolved,
        [NotNullWhen(false)] out Refusal? refusal)
    {
        ArgumentNullException.ThrowIfNull(request);
        resolved = null;
        var root = Path.GetFullPath(baseDirectory);
        var named = new List<(string Flag, string? Path)>();
        named.AddRange(request.CorpusPath.Split(['\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(path => ("corpus", (string?)path)));
        named.Add(("grammar", request.GrammarPath));
        named.Add(("segments", request.SegmentsPath));
        named.Add(("project", request.ProjectPath));
        named.Add(("baseline-grammar", request.BaselineGrammarPath));
        foreach (var (flag, path) in named)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            var relative = Path.IsPathRooted(path) ? null : Path.GetRelativePath(root, Path.GetFullPath(path, root));
            if (relative is not null && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                !relative.StartsWith("../", StringComparison.Ordinal) && !Path.IsPathRooted(relative)) continue;
            refusal = new Refusal(RefusalCodes.GuideQueryRunPathOutsideProject, FailureReason.Refused,
                $"--{flag} {path} is outside the project's folder; a Guide block reads only files inside it, by a relative path.", Facts(flag));
            return false;
        }
        resolved = Resolve(request, baseDirectory);
        refusal = null;
        return true;
    }

    private static bool TryNumber(Dictionary<string, string> flags, string name, out int? value, [NotNullWhen(false)] out Refusal? refusal)
    {
        value = null;
        refusal = null;
        if (!flags.TryGetValue(name, out var raw)) return true;
        if (int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)) { value = parsed; return true; }
        refusal = Invalid($"--{name} is a whole number.", name);
        return false;
    }

    private static Refusal Invalid(string message, string flag) => new(RefusalCodes.GuideQueryRunInvalid, FailureReason.InvalidArgument, message, Facts(flag));

    private static Dictionary<string, string> Facts(string flag) => new(StringComparer.Ordinal) { ["flag"] = flag };
}
