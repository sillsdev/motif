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

    internal static string Scrub(string text, IReadOnlyDictionary<string, string> facts)
    {
        text = ReplaceKnownPaths(text, facts);
        if (!HasPathSeparator(text)) return text;
        var result = new StringBuilder(text.Length);
        for (var start = 0; start < text.Length;)
        {
            if (char.IsWhiteSpace(text[start])) { result.Append(text[start++]); continue; }
            var end = start;
            char quote = '\0';
            while (end < text.Length)
            {
                var character = text[end];
                if (quote == '\0' && char.IsWhiteSpace(character)) break;
                if (quote == '\0' && character is '\'' or '"' &&
                    (end == start || "=:([{<".Contains(text[end - 1]))) quote = character;
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
            while (last > first && "\"')]}>,.;:!?".Contains(text[last - 1])) last--;
            var (nameStart, nameLength) = PathTokenFileName(text.AsSpan(first, last - first));
            if (nameStart < 0) result.Append(text.AsSpan(start, end - start));
            else
            {
                result.Append(text.AsSpan(start, first - start));
                result.Append(text.AsSpan(first + nameStart, nameLength));
                result.Append(text.AsSpan(last, end - last));
            }
            start = end;
        }
        return result.ToString();
    }

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
    private static (int Start, int Length) PathTokenFileName(ReadOnlySpan<char> token)
    {
        if (IsUrl(token) || IsRootOnly(token)) return (-1, 0);
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
        var segmentIsDigits = true;
        var hasNumericSegment = false;
        for (var index = 0; index <= token.Length; index++)
        {
            if (index == token.Length || token[index] is '/' or '\\')
            {
                var length = index - segmentStart;
                if (length > 0)
                {
                    nonEmptySegments++;
                    hasNumericSegment |= segmentIsDigits;
                    nameStart = segmentStart;
                    nameLength = length;
                }
                segmentIsDigits = true;
                if (index < token.Length) separators++;
                segmentStart = index + 1;
            }
            else segmentIsDigits &= char.IsAsciiDigit(token[index]);
        }
        if (nameStart < 0) return (-1, 0);
        var name = token.Slice(nameStart, nameLength);
        var dot = name.LastIndexOf('.');
        var hasExtension = dot > 0 && dot < name.Length - 1;
        for (var index = dot + 1; hasExtension && index < name.Length; index++)
            hasExtension = char.IsLetter(name[index]);
        var isPath = explicitPath || (leadingSeparator && nonEmptySegments >= 2) ||
            (!leadingSeparator && separators > 0 && hasExtension && !hasNumericSegment);
        return isPath ? (nameStart, nameLength) : (-1, 0);
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
