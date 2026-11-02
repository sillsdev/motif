using System.Text;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// Cuts every folder out of text the window shows, leaving the file names a person recognises; the problem report
/// keeps the full text.
/// </summary>
internal static class WindowPathText
{
    internal static bool IsPathFact(KeyValuePair<string, string> fact) =>
        fact.Key.EndsWith("Path", StringComparison.OrdinalIgnoreCase) ||
            fact.Key.EndsWith("File", StringComparison.OrdinalIgnoreCase) ||
            fact.Key.EndsWith("Folder", StringComparison.OrdinalIgnoreCase) ||
            fact.Key.EndsWith("Directory", StringComparison.OrdinalIgnoreCase) ||
            IsRootedPath(fact.Value);

    /// <summary>
    /// The text with every folder cut out of every path in it, so only file names remain. A folder name may hold
    /// spaces, so a path can span several words: a rooted path that has not named its file yet takes the words
    /// after it up to the token that does, and a relative fragment takes the fragments that follow it likewise.
    /// </summary>
    internal static string Scrub(string text, IReadOnlyDictionary<string, string> facts)
    {
        text = ReplaceKnownPaths(text, facts);
        if (!HasPathSeparator(text)) return text;
        var tokens = Tokenize(text);
        var result = new StringBuilder(text.Length);
        var emitted = 0;
        var noRunBefore = 0;
        for (var index = 0; index < tokens.Count; index++)
        {
            var head = tokens[index];
            index = RunEnd(tokens, index, ref noRunBefore);
            var tail = tokens[index];
            result.Append(text.AsSpan(emitted, head.Start - emitted));
            if (!tail.Shape.IsPath) result.Append(text.AsSpan(head.Start, tail.End - head.Start));
            else
            {
                result.Append(text.AsSpan(head.Start, head.First - head.Start));
                result.Append(text.AsSpan(tail.First + tail.Shape.NameStart, tail.Shape.NameLength));
                result.Append(text.AsSpan(tail.Last, tail.End - tail.Last));
            }
            emitted = tail.End;
        }
        return result.Append(text.AsSpan(emitted)).ToString();
    }

    // A token is a non-space run or a quoted span; First and Last trim its brackets and punctuation.
    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        for (var start = 0; start < text.Length;)
        {
            if (char.IsWhiteSpace(text[start])) { start++; continue; }
            var end = start;
            char quote = '\0';
            var wrapper = '\0';
            while (end < text.Length)
            {
                var character = text[end];
                if (quote == '\0' && char.IsWhiteSpace(character)) break;
                if (quote == '\0' && character is '\'' or '"' &&
                    (end == start || "=:([{<".Contains(text[end - 1]))) quote = wrapper = character;
                else if (quote != '\0' && character == quote &&
                    (end + 1 == text.Length || char.IsWhiteSpace(text[end + 1]) || ".,;:)]}>!?".Contains(text[end + 1]))) quote = '\0';
                end++;
            }
            var first = start;
            while (first < end && "\"'([{<".Contains(text[first])) first++;
            var prefix = text.AsSpan(first, end - first).IndexOfAny('=', ':');
            if (prefix >= 0 && text[first + prefix] == ':' &&
                (first + prefix + 1 >= end || text[first + prefix + 1] is not ('\'' or '"'))) prefix = -1;
            if (prefix >= 0 && text.AsSpan(first, prefix).IndexOfAny('/', '\\') < 0)
            {
                first += prefix + 1;
                while (first < end && "\"'([{<".Contains(text[first])) first++;
            }
            var last = end;
            while (last > first && TrailsPath(text.AsSpan(first, last - first), wrapper)) last--;
            var body = text.AsSpan(first, last - first);
            tokens.Add(new Token(start, end, first, last, ShapeOf(body), body.IndexOfAny('/', '\\') >= 0));
            start = end;
        }
        return tokens;
    }

    // Sentence punctuation trails a path; a closing bracket or quote does too unless the name itself opened it.
    private static bool TrailsPath(ReadOnlySpan<char> body, char wrapper)
    {
        var character = body[^1];
        if (",.;:!?".Contains(character)) return true;
        if (character is '\'' or '"') return character == wrapper;
        var opener = character switch { ')' => '(', ']' => '[', '}' => '{', '>' => '<', _ => '\0' };
        if (opener == '\0') return false;
        var open = 0;
        foreach (var inner in body[..^1])
            if (inner == opener) open++;
            else if (inner == character && open > 0) open--;
        return open == 0;
    }

    // The last token of the path starting at tokens[first], which is first itself unless the path has spaces.
    private static int RunEnd(List<Token> tokens, int first, ref int noRunBefore)
    {
        var head = tokens[first];
        var openRooted = head.Shape is { IsPath: true, Rooted: true, HasExtension: false } && !head.Terminal;
        var fragment = head.IsFragment;
        if (first < noRunBefore || !(openRooted || fragment)) return first;
        for (var next = first + 1; next < tokens.Count; next++)
        {
            var token = tokens[next];
            if (token.Names) return next;
            if (!token.Terminal && (openRooted || token.IsFragment)) continue;
            noRunBefore = next;
            return first;
        }
        noRunBefore = tokens.Count;
        return first;
    }

    private readonly record struct Token(int Start, int End, int First, int Last, Shape Shape, bool HasSeparator)
    {
        public bool Terminal => Last != End;

        // A token with a separator that is not yet a path, like "private/Alice" in "private/Alice Secret/x.fwdata".
        public bool IsFragment => HasSeparator && Shape is { IsPath: false, Excluded: false } && !Terminal;

        public bool Names => HasSeparator && Shape is { IsPath: true, HasExtension: true };
    }

    // Excluded marks a URL or a bare root, which are never cut.
    private readonly record struct Shape(
        int NameStart, int NameLength, bool IsPath, bool Rooted, bool HasExtension, bool Excluded);

    internal static string ReplaceKnownPaths(string text, IReadOnlyDictionary<string, string> facts)
    {
        var paths = facts.Where(fact => IsPathFact(fact) && fact.Value.Length > 0)
            .Select(fact => fact.Value).OrderByDescending(path => path.Length).ToArray();
        foreach (var path in paths)
            text = text.Replace(path, FileName(path), StringComparison.Ordinal);
        foreach (var path in paths)
        {
            var trimmed = path.TrimEnd('/', '\\');
            var separator = trimmed.LastIndexOfAny(['/', '\\']);
            if (separator < 0) continue;
            var parent = trimmed[..separator];
            if (!IsRootOnly(parent))
                text = text.Replace(parent, FileName(path), StringComparison.Ordinal);
        }
        return text;
    }

    // Preserve fractions, dates and phonological /x/ notation when they are not known paths.
    private static Shape ShapeOf(ReadOnlySpan<char> token)
    {
        if (IsUrl(token) || IsRootOnly(token)) return new Shape(0, 0, false, false, false, true);
        var explicitPath = token.StartsWith("\\\\", StringComparison.Ordinal) ||
            token.StartsWith("~/", StringComparison.Ordinal) || token.StartsWith("./", StringComparison.Ordinal) ||
            token.StartsWith("../", StringComparison.Ordinal) ||
            (token.Length >= 2 && char.IsAsciiLetter(token[0]) && token[1] == ':');
        var leadingSeparator = token.Length > 0 && token[0] is '/' or '\\';
        var separators = 0;
        var nonEmptySegments = 0;
        var segmentStart = 0;
        var nameStart = -1;
        var nameLength = 0;
        for (var index = 0; index <= token.Length; index++)
        {
            if (index < token.Length && token[index] is not ('/' or '\\')) continue;
            if (index > segmentStart)
            {
                nonEmptySegments++;
                nameStart = segmentStart;
                nameLength = index - segmentStart;
            }
            if (index < token.Length) separators++;
            segmentStart = index + 1;
        }
        if (nameStart < 0) return default;
        var name = token.Slice(nameStart, nameLength);
        var dot = name.LastIndexOf('.');
        var hasExtension = dot > 0 && dot < name.Length - 1;
        for (var index = dot + 1; hasExtension && index < name.Length; index++)
            hasExtension = char.IsLetter(name[index]);
        var rooted = explicitPath || (leadingSeparator && nonEmptySegments >= 2);
        var isPath = rooted || (!leadingSeparator && separators > 0 && hasExtension);
        return new Shape(nameStart, nameLength, isPath, rooted, hasExtension, false);
    }

    private static bool IsUrl(ReadOnlySpan<char> token)
    {
        var colon = token.IndexOf("://", StringComparison.Ordinal);
        if (colon < 2 || !char.IsAsciiLetter(token[0])) return false;
        for (var index = 1; index < colon; index++)
            if (!char.IsAsciiLetterOrDigit(token[index]) && token[index] is not '+' and not '-' and not '.') return false;
        return true;
    }

    private static bool IsRootOnly(ReadOnlySpan<char> path)
    {
        while (!path.IsEmpty && path[^1] is '/' or '\\') path = path[..^1];
        return path.IsEmpty || (path.Length == 2 && char.IsAsciiLetter(path[0]) && path[1] == ':');
    }

    private static bool IsRootedPath(string path) => Path.IsPathRooted(path) || IsWindowsPath(path);

    private static bool IsWindowsPath(string path) => path.StartsWith(@"\\", StringComparison.Ordinal) ||
        (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/');

    internal static bool HasPathSeparator(string text) => text.IndexOfAny(['/', '\\']) >= 0;

    internal static string FileName(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        if (trimmed.Length == 0) return path;
        var separator = trimmed.LastIndexOfAny(['\\', '/']);
        return separator < 0 ? trimmed : trimmed[(separator + 1)..];
    }
}
