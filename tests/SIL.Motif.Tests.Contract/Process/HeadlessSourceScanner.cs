using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SIL.Motif.Tests.Contract.Process;

internal static class HeadlessSourceScanner
{
    private sealed class Token
    {
        public Token(string text, int offset, bool literal = false) { Text = text; Offset = offset; Literal = literal; }
        public string Text { get; }
        public int Offset { get; }
        public bool Literal { get; }
    }

    private sealed class StartInfo
    {
        public string File = "";
        public readonly List<string> Arguments = new List<string>();
    }

    private static readonly HashSet<string> NativeCalls = new HashSet<string>(StringComparer.Ordinal)
    {
        "SendInput", "keybd_event", "mouse_event", "SetForegroundWindow", "OpenClipboard", "SetClipboardData", "GetClipboardData"
    };
    private static readonly HashSet<string> Backends = new HashSet<string>(StringComparer.Ordinal)
    {
        "UseWin32", "UsePlatformDetect", "UseX11", "UseAvaloniaNative"
    };

    public static IEnumerable<int> Find(string source)
    {
        var tokens = Tokenize(source);
        var results = new List<int>();
        var imports = string.Join(" ", tokens.Where(t => !t.Literal).Select(t => t.Text));
        var forms = Regex.IsMatch(imports, @"\busing\s+(?:global\s*::\s*)?System\s*\.\s*Windows\s*\.\s*Forms\s*;");
        var windows = Regex.IsMatch(imports, @"\busing\s+(?:global\s*::\s*)?System\s*\.\s*Windows\s*;");
        var staticClipboard = Regex.IsMatch(imports, @"\busing\s+static\s+System\s*\.\s*Windows\s*\.\s*(?:Forms\s*\.\s*)?Clipboard\s*;");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var starts = new Dictionary<string, StartInfo>(StringComparer.Ordinal);
        var scopes = new Stack<(Dictionary<string, string>, Dictionary<string, StartInfo>)>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Literal) continue;
            if (token.Text == "{")
            {
                scopes.Push((new Dictionary<string, string>(values), new Dictionary<string, StartInfo>(starts)));
                continue;
            }
            if (token.Text == "}" && scopes.Count > 0)
            {
                var previous = scopes.Pop();
                values = previous.Item1;
                starts = previous.Item2;
                continue;
            }
            if (i + 1 < tokens.Count && tokens[i + 1].Text == "(" && IsForbiddenCall(tokens, i, staticClipboard))
                results.Add(token.Offset);
            if (token.Text == "Clipboard" && IsClipboard(tokens, i, forms || windows))
                results.Add(token.Offset);
            if (i + 2 < tokens.Count && (token.Text == "var" || token.Text == "string") && tokens[i + 2].Text == "=")
            {
                var end = StatementEnd(tokens, i + 3);
                var value = GuiFile(tokens, i + 3, end, values);
                if (value.Length > 0) values[tokens[i + 1].Text] = value;
                else values.Remove(tokens[i + 1].Text);
            }
            if (i + 4 < tokens.Count && tokens[i + 1].Text == "=" && tokens[i + 2].Text == "new")
            {
                var type = i + 3;
                while (type + 2 < tokens.Count && tokens[type + 1].Text == ".") type += 2;
                if (type + 1 < tokens.Count && tokens[type].Text == "ProcessStartInfo")
                {
                    var info = new StartInfo();
                    var end = StatementEnd(tokens, type + 1);
                    if (tokens[type + 1].Text == "(") ReadConstructor(tokens, type + 1, info, values);
                    ReadFileInitializer(tokens, type + 1, end, info, values);
                    starts[token.Text] = info;
                }
            }
            if (i + 4 < tokens.Count && tokens[i + 1].Text == "." && tokens[i + 2].Text == "FileName" && tokens[i + 3].Text == "=" && starts.TryGetValue(token.Text, out var target))
                target.File = GuiFile(tokens, i + 4, StatementEnd(tokens, i + 4), values);
            if (i + 5 < tokens.Count && tokens[i + 1].Text == "." && tokens[i + 2].Text == "ArgumentList" && tokens[i + 3].Text == "." && starts.TryGetValue(token.Text, out var argsTarget))
                ApplyArgumentList(tokens, i + 4, argsTarget);
            if (i + 7 < tokens.Count && tokens[i + 1].Text == "." && tokens[i + 2].Text == "ArgumentList" && tokens[i + 3].Text == "[" && tokens[i + 5].Text == "]" && tokens[i + 6].Text == "=" && starts.TryGetValue(token.Text, out var indexed))
            {
                if (int.TryParse(tokens[i + 4].Text, out var index) && index >= 0 && index < indexed.Arguments.Count)
                    indexed.Arguments[index] = FirstArgument(tokens, i + 7, StatementEnd(tokens, i + 7));
                else indexed.Arguments.Insert(0, "");
            }
            if (i + 4 < tokens.Count && tokens[i + 1].Text == "." && tokens[i + 2].Text == "Arguments" && tokens[i + 3].Text == "=" && starts.TryGetValue(token.Text, out var argTarget))
            {
                argTarget.Arguments.Clear();
                argTarget.Arguments.Add(FirstArgument(tokens, i + 4, StatementEnd(tokens, i + 4)));
            }
            if (token.Text == "Process" && i + 3 < tokens.Count && tokens[i + 1].Text == "." && tokens[i + 2].Text == "Start" && tokens[i + 3].Text == "(")
            {
                var close = Close(tokens, i + 3, "(", ")");
                var parts = SplitArguments(tokens, i + 4, close);
                var info = LaunchInfo(tokens, parts, starts, values);
                if (info != null && IsGui(info.File) && (!IsApp(info.File) || info.Arguments.Count == 0 || info.Arguments[0] != "--smoke"))
                    results.Add(token.Offset);
            }
        }
        return results.Distinct().OrderBy(i => i);
    }

    private static bool IsForbiddenCall(List<Token> tokens, int i, bool staticClipboard)
    {
        var name = tokens[i].Text;
        if (Backends.Contains(name)) return true;
        if (staticClipboard && (name.StartsWith("Set", StringComparison.Ordinal) || name.StartsWith("Get", StringComparison.Ordinal) || name.StartsWith("Contains", StringComparison.Ordinal) || name == "Clear" || name == "Flush"))
            return i == 0 || tokens[i - 1].Text != ".";
        if (!NativeCalls.Contains(name)) return false;
        if (i > 0 && new[] { "void", "bool", "int", "uint", "long", "ulong", "IntPtr" }.Contains(tokens[i - 1].Text))
            return false;
        if (i == 0 || tokens[i - 1].Text != ".") return true;
        return i >= 2 && tokens[i - 2].Text.Length > 0 && char.IsUpper(tokens[i - 2].Text[0]);
    }

    private static bool IsClipboard(List<Token> tokens, int i, bool imported)
    {
        if (i + 1 >= tokens.Count || tokens[i + 1].Text != ".") return false;
        var prefix = string.Join("", tokens.Take(i).Skip(Math.Max(0, i - 6)).Select(t => t.Text));
        if (prefix.EndsWith("System.Windows.", StringComparison.Ordinal) || prefix.EndsWith("System.Windows.Forms.", StringComparison.Ordinal)) return true;
        return imported && (i == 0 || tokens[i - 1].Text != ".");
    }

    private static StartInfo? LaunchInfo(List<Token> t, List<(int start, int end)> parts, Dictionary<string, StartInfo> starts, Dictionary<string, string> values)
    {
        if (parts.Count == 0) return null;
        var first = parts[0];
        if (first.end == first.start + 1 && starts.TryGetValue(t[first.start].Text, out var known)) return known;
        if (first.start + 2 < first.end && t[first.start].Text == "new" && t[first.start + 1].Text == "ProcessStartInfo")
        {
            var nested = new StartInfo();
            if (t[first.start + 2].Text == "(") ReadConstructor(t, first.start + 2, nested, values);
            ReadFileInitializer(t, first.start + 2, first.end, nested, values);
            return nested;
        }
        var result = new StartInfo { File = GuiFile(t, first.start, first.end, values) };
        if (parts.Count > 1) result.Arguments.Add(FirstArgument(t, parts[1].start, parts[1].end));
        return result;
    }

    private static void ReadConstructor(List<Token> t, int open, StartInfo info, Dictionary<string, string> values)
    {
        var parts = SplitArguments(t, open + 1, Close(t, open, "(", ")"));
        if (parts.Count > 0) info.File = GuiFile(t, parts[0].start, parts[0].end, values);
        if (parts.Count > 1) info.Arguments.Add(FirstArgument(t, parts[1].start, parts[1].end));
    }

    private static void ReadFileInitializer(List<Token> t, int start, int end, StartInfo info, Dictionary<string, string> values)
    {
        for (var i = start; i + 2 < end; i++)
        {
            if (t[i].Text == "FileName" && t[i + 1].Text == "=") info.File = GuiFile(t, i + 2, Math.Min(end, i + 3), values);
            if (t[i].Text == "Arguments" && t[i + 1].Text == "=") { info.Arguments.Clear(); info.Arguments.Add(FirstArgument(t, i + 2, Math.Min(end, i + 3))); }
        }
    }

    private static void ApplyArgumentList(List<Token> t, int i, StartInfo info)
    {
        var method = t[i].Text;
        if (i + 1 >= t.Count || t[i + 1].Text != "(") return;
        var parts = SplitArguments(t, i + 2, Close(t, i + 1, "(", ")"));
        if (method == "Clear") info.Arguments.Clear();
        else if (method == "Add" && parts.Count > 0) info.Arguments.Add(FirstArgument(t, parts[0].start, parts[0].end));
        else if (method == "Insert" && parts.Count > 1)
        {
            var value = FirstArgument(t, parts[1].start, parts[1].end);
            if (parts[0].end == parts[0].start + 1 && int.TryParse(t[parts[0].start].Text, out var index) && index >= 0 && index <= info.Arguments.Count) info.Arguments.Insert(index, value);
            else info.Arguments.Insert(0, "");
        }
        else if (method == "Remove" || method == "RemoveAt") info.Arguments.Insert(0, "");
    }

    private static string FirstArgument(List<Token> t, int start, int end)
    {
        if (start >= end) return "";
        if (t[start].Text == "new")
        {
            while (start < end && t[start].Text != "{") start++;
            start++;
        }
        if (start >= end || !t[start].Literal) return "";
        if (start + 1 < end && t[start + 1].Text != "," && t[start + 1].Text != "}") return "";
        return t[start].Text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
    }

    private static string GuiFile(List<Token> t, int start, int end, Dictionary<string, string> values)
    {
        for (var i = start; i < end; i++)
        {
            if (t[i].Literal && IsGui(t[i].Text)) return t[i].Text;
            if (!t[i].Literal && values.TryGetValue(t[i].Text, out var value)) return value;
        }
        return "";
    }
    private static bool IsApp(string value) => value.IndexOf("SIL.Motif.App", StringComparison.OrdinalIgnoreCase) >= 0;
    private static bool IsGui(string value) => IsApp(value) || value.IndexOf("Playground.Desktop", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("Host48", StringComparison.OrdinalIgnoreCase) >= 0;
    private static int StatementEnd(List<Token> t, int start) { var i = start; while (i < t.Count && t[i].Text != ";") i++; return i; }
    private static int Close(List<Token> t, int start, string open, string close)
    {
        var depth = 0;
        for (var i = start; i < t.Count; i++) { if (t[i].Text == open) depth++; else if (t[i].Text == close && --depth == 0) return i; }
        return t.Count;
    }
    private static List<(int start, int end)> SplitArguments(List<Token> t, int start, int end)
    {
        var result = new List<(int, int)>(); var depth = 0; var part = start;
        for (var i = start; i < end; i++)
        {
            if (t[i].Text == "(" || t[i].Text == "{" || t[i].Text == "[") depth++;
            else if (t[i].Text == ")" || t[i].Text == "}" || t[i].Text == "]") depth--;
            else if (t[i].Text == "," && depth == 0) { result.Add((part, i)); part = i + 1; }
        }
        if (part < end) result.Add((part, end));
        return result;
    }

    private static List<Token> Tokenize(string source)
    {
        var tokens = new List<Token>();
        for (var i = 0; i < source.Length;)
        {
            var c = source[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/') { i += 2; while (i < source.Length && source[i] != '\n') i++; continue; }
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*') { i += 2; while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/')) i++; i = Math.Min(source.Length, i + 2); continue; }
            if (c == '"' || c == '\'')
            {
                var start = i; var quote = c; var verbatim = i > 0 && source[i - 1] == '@'; var count = 1;
                if (quote == '"') while (i + count < source.Length && source[i + count] == '"') count++;
                if (count >= 3)
                {
                    i += count; var content = i;
                    while (i < source.Length && !(source[i] == '"' && i + count <= source.Length && source.Substring(i, count).All(ch => ch == '"'))) i++;
                    tokens.Add(new Token(source.Substring(content, i - content), start, true)); i = Math.Min(source.Length, i + count); continue;
                }
                i++; var value = new StringBuilder();
                while (i < source.Length)
                {
                    if (source[i] == quote)
                    {
                        if (verbatim && i + 1 < source.Length && source[i + 1] == quote) { value.Append(quote); i += 2; continue; }
                        i++; break;
                    }
                    if (!verbatim && source[i] == '\\' && i + 1 < source.Length) { i++; value.Append(source[i++]); continue; }
                    value.Append(source[i++]);
                }
                tokens.Add(new Token(value.ToString(), start, true)); continue;
            }
            if (char.IsLetterOrDigit(c) || c == '_') { var start = i++; while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_')) i++; tokens.Add(new Token(source.Substring(start, i - start), start)); continue; }
            tokens.Add(new Token(c.ToString(), i++));
        }
        return tokens;
    }
}
