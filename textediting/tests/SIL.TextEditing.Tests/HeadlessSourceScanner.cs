using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SIL.TextEditing.Tests;

internal static class HeadlessSourceScanner
{
    private sealed class Token
    {
        public Token(string text, int offset, bool literal = false) { Text = text; Offset = offset; Literal = literal; }
        public string Text { get; }
        public int Offset { get; }
        public bool Literal { get; }
    }

    private static readonly HashSet<string> NativeCalls = new HashSet<string>(StringComparer.Ordinal)
    {
        "SendInput", "keybd_event", "mouse_event", "SetForegroundWindow", "OpenClipboard", "SetClipboardData", "GetClipboardData"
    };
    private static readonly HashSet<string> Backends = new HashSet<string>(StringComparer.Ordinal)
    {
        "UseWin32", "UsePlatformDetect", "UseX11", "UseAvaloniaNative"
    };

    public static IEnumerable<int> Find(string source, bool allowedLauncher = false)
    {
        var tokens = Tokenize(source);
        var results = new List<int>();
        var imports = string.Join(" ", tokens.Where(t => !t.Literal).Select(t => t.Text));
        var forms = Regex.IsMatch(imports, @"\busing\s+(?:global\s*::\s*)?System\s*\.\s*Windows\s*\.\s*Forms\s*;");
        var windows = Regex.IsMatch(imports, @"\busing\s+(?:global\s*::\s*)?System\s*\.\s*Windows\s*;");
        var staticClipboard = Regex.IsMatch(imports, @"\busing\s+static\s+System\s*\.\s*Windows\s*\.\s*(?:Forms\s*\.\s*)?Clipboard\s*;");
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Literal) continue;
            if (i + 1 < tokens.Count && tokens[i + 1].Text == "(" && IsForbiddenCall(tokens, i, staticClipboard))
                results.Add(token.Offset);
            if (token.Text == "Clipboard" && IsClipboard(tokens, i, forms || windows))
                results.Add(token.Offset);
        }
        var executable = tokens.Select((token, index) => (token, index))
            .FirstOrDefault(item => (item.token.Literal && IsGuiExecutable(item.token.Text)
                && !IsSourceDirectory(tokens, item.index)) || IsAppOutput(tokens, item.index));
        if (!allowedLauncher && executable.token != null && tokens.Select((token, index) => (token, index))
                .Any(item => !item.token.Literal && IsProcessReference(tokens, item.index)))
            results.Add(executable.token.Offset);
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

    private static bool IsGuiExecutable(string value)
    {
        var name = value.Replace('\\', '/').Split('/').Last();
        return name.Equals("SIL.Motif.App", StringComparison.OrdinalIgnoreCase)
            || name.Equals("SIL.Motif.App.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("SIL.Motif.App.dll", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Playground.Desktop", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Playground.Desktop.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Host48", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Host48.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAppOutput(List<Token> tokens, int index) =>
        index + 2 < tokens.Count && !tokens[index].Literal && tokens[index].Text == "BuildOutput"
        && tokens[index + 1].Text == "." && (tokens[index + 2].Text == "App"
            || tokens[index + 2].Text == "AppExecutable" || tokens[index + 2].Text == "AppPath");

    private static bool IsSourceDirectory(List<Token> tokens, int index) =>
        index >= 2 && tokens[index - 1].Text == "," && tokens[index - 2].Literal
        && tokens[index - 2].Text == "src";

    private static bool IsProcessReference(List<Token> tokens, int index)
    {
        var name = tokens[index].Text;
        if (name == "Process" && index + 1 < tokens.Count)
            return tokens[index + 1].Text == "." || tokens[index + 1].Text == "(";
        return name == "ProcessStartInfo" || name == "ToolProcess" || name == "CliProcess"
            || name == "InterruptibleCli" || name.EndsWith("ProcessLauncher", StringComparison.Ordinal)
            || name.EndsWith("ProcessRunner", StringComparison.Ordinal);
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
