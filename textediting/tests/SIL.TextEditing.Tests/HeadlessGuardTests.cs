using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Xunit;

namespace SIL.TextEditing.Tests;

public sealed class HeadlessGuardTests
{
    private const string Guidance = "Use the headless platform, a faked seam, or a manual row in textediting/docs/manual-testing.md.";
    private static readonly Regex ForbiddenCall = new(
        @"\b(?:UseWin32|UsePlatformDetect|UseX11|UseAvaloniaNative|SendInput|keybd_event|mouse_event|SetForegroundWindow|OpenClipboard|SetClipboardData|GetClipboardData)\s*\(|\bSystem\s*\.\s*Windows\s*\.\s*(?:Forms\s*\.\s*)?Clipboard\b",
        RegexOptions.CultureInvariant);
    private static readonly Regex GuiName = new(@"\b(?:SIL\.Motif\.App|Playground\.Desktop|Host48)(?:\.exe)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Assignment = new(@"\b(?:var|string)\s+(?<name>\w+)\s*=\s*(?<value>[^;]+);", RegexOptions.CultureInvariant);
    private static readonly Regex Launch = new(@"\b(?:new\s+(?:System\.Diagnostics\.)?ProcessStartInfo|(?:System\.Diagnostics\.)?Process\.Start)\s*\((?<args>[^;]+?)\)(?=\s*[!;{])", RegexOptions.CultureInvariant);

    [Fact]
    public void AutomatedTestsStayHeadless()
    {
        var root = FindRoot();
        var tests = Path.Combine(root, "tests");
        var violations = new List<string>();
        foreach (var path in Directory.EnumerateFiles(tests, "*", SearchOption.AllDirectories).Where(IsSource))
        {
            var source = File.ReadAllText(path);
            var relative = path.Substring(root.Length + 1);
            if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                CheckProject(source, relative, violations);
            else
                CheckSource(source, relative, violations);
        }
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [InlineData("Use" + "X11()")]
    [InlineData("Send" + "Input()")]
    [InlineData("keybd_" + "event()")]
    [InlineData("System.Windows.Forms." + "Clipboard.GetText()")]
    [InlineData("Process." + "Start(\"SIL.Motif.App\")!;")]
    [InlineData("System.Windows." + "Clipboard.GetText()")]
    [InlineData("Process." + "Start(\"SIL.Motif.App\");")]
    [InlineData("var app = \"SIL.Motif.App.exe\"; new " + "ProcessStartInfo(app, \"--other\");")]
    [InlineData("Process." + "Start(\"Playground.Desktop.exe\", \"--smoke\");")]
    public void GuardDetectsForbiddenSource(string source)
    {
        var violations = new List<string>();
        CheckSource(source, "test.cs", violations);
        Assert.NotEmpty(violations);
    }

    [Theory]
    [InlineData("Desktop")]
    [InlineData("Win32")]
    [InlineData("X11")]
    [InlineData("Native")]
    public void GuardDetectsDirectBackendPackages(string backend)
    {
        var violations = new List<string>();
        CheckProject("<Project>\n<PackageReference Include=\"Avalonia." + backend + "\" />\n</Project>",
            "test.csproj", violations);
        Assert.Single(violations);
        Assert.Contains("test.csproj:2", violations[0]);
    }

    [Fact]
    public void GuardAllowsHeadlessAppSmoke()
    {
        var violations = new List<string>();
        CheckSource("var app = \"SIL.Motif.App.exe\"; new " + "ProcessStartInfo(app, \"--smoke\");", "test.cs", violations);
        Assert.Empty(violations);
        CheckSource("var app = \"SIL.Motif.App.exe\"; var start = new " +
            "ProcessStartInfo(app); start.ArgumentList.Add(\"--smoke\"); Process." + "Start(start);",
            "test.cs", violations);
        Assert.Empty(violations);
    }

    private static bool IsSource(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !parts.Any(part => part.Equals("obj", StringComparison.OrdinalIgnoreCase) || part.Equals("bin", StringComparison.OrdinalIgnoreCase))
            && (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
    }

    private static void CheckProject(string source, string path, List<string> violations)
    {
        var document = XDocument.Parse(source, LoadOptions.SetLineInfo);
        foreach (var item in document.Descendants().Where(e => e.Name.LocalName == "PackageReference"))
        {
            var name = (string?)item.Attribute("Include") ?? (string?)item.Attribute("Update") ?? "";
            if (Regex.IsMatch(name, @"^Avalonia\.(Desktop|Win32|X11|Native)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                violations.Add(path + ":" + ((IXmlLineInfo)item).LineNumber + ": " + name + ". " + Guidance);
        }
    }

    private static void CheckSource(string source, string path, List<string> violations)
    {
        foreach (Match match in ForbiddenCall.Matches(source))
            AddViolation(source, path, match, violations);

        var variables = Assignment.Matches(source).Cast<Match>()
            .Where(m => GuiName.IsMatch(m.Groups["value"].Value))
            .Select(m => m.Groups["name"].Value).Distinct().ToArray();
        foreach (Match launch in Launch.Matches(source))
        {
            var args = launch.Groups["args"].Value;
            var namesGui = GuiName.IsMatch(args) || variables.Any(v => Regex.IsMatch(args, @"\b" + Regex.Escape(v) + @"\b"));
            if (!namesGui)
                continue;
            var isSample = Regex.IsMatch(args, @"Playground\.Desktop|Host48", RegexOptions.IgnoreCase)
                || Assignment.Matches(source).Cast<Match>().Any(m => variables.Contains(m.Groups["name"].Value)
                    && Regex.IsMatch(args, @"\b" + Regex.Escape(m.Groups["name"].Value) + @"\b")
                    && Regex.IsMatch(m.Groups["value"].Value, @"Playground\.Desktop|Host48", RegexOptions.IgnoreCase));
            if (isSample || !HasSmokeArgument(source, launch, args))
                AddViolation(source, path, launch, violations);
        }
    }

    private static bool HasSmokeArgument(string source, Match launch, string args)
    {
        if (Regex.IsMatch(args, "[\"']--smoke[\"']"))
            return true;
        var prefix = source.Substring(0, launch.Index);
        var declaration = Regex.Match(prefix, @"(?:var|ProcessStartInfo)\s+(?<name>\w+)\s*=\s*$");
        if (!declaration.Success)
            return false;
        var name = Regex.Escape(declaration.Groups["name"].Value);
        var following = source.Substring(launch.Index + launch.Length);
        var start = Regex.Match(following, @"\bProcess\.Start\s*\(\s*" + name + @"\s*\)");
        if (!start.Success)
            return false;
        var arguments = following.Substring(0, start.Index);
        return Regex.IsMatch(arguments, @"\b" + name + "\\.ArgumentList\\.Add\\s*\\(\\s*[\"']--smoke[\"']\\s*\\)");
    }

    private static void AddViolation(string source, string path, Match match, List<string> violations)
    {
        var line = 1 + source.Take(match.Index).Count(c => c == '\n');
        violations.Add(path + ":" + line + ": " + match.Value.Trim() + ". " + Guidance);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SIL.TextEditing.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Cannot find SIL.TextEditing.sln.");
    }
}
