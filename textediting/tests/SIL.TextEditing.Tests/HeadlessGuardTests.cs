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
    [InlineData("var app = \"SIL.Motif.App.exe\"; Process." + "Start(new ProcessStartInfo(app, \"--other\"));")]
    [InlineData("Process." + "Start(\"Playground.Desktop.exe\", \"--smoke\");")]
    public void GuardDetectsForbiddenSource(string source)
    {
        var violations = new List<string>();
        CheckSource(source, "test.cs", violations);
        Assert.NotEmpty(violations);
    }

    [Theory]
    [InlineData("var s = new System.Diagnostics.ProcessStartInfo(\"SIL.Motif.App\"); Process.Start(s);", true)]
    [InlineData("var s = new ProcessStartInfo(); s.FileName = \"SIL.Motif.App\"; Process.Start(s);", true)]
    [InlineData("var s = new ProcessStartInfo { FileName = \"Host48.exe\" }; Process.Start(s);", true)]
    [InlineData("var s = new ProcessStartInfo { FileName = \"Playground.Desktop\" }; Process.Start(s);", true)]
    [InlineData("var s = new ProcessStartInfo { FileName = \"tool.exe\" }; Process.Start(s);", false)]
    [InlineData("Process.Start(\"SIL.Motif.App\" /* \"--smoke\" */);", true)]
    [InlineData("Process.Start(\"SIL.Motif.App\", new[] { \"--other\", \"--smoke\" });", true)]
    [InlineData("Process.Start(\"SIL.Motif.App\", new[] { \"--smoke\", \"--other\" });", false)]
    [InlineData("var s = new ProcessStartInfo(\"SIL.Motif.App\", \"--smoke\"); s.ArgumentList.Insert(0, \"--other\"); Process.Start(s);", true)]
    [InlineData("var s = new ProcessStartInfo(\"SIL.Motif.App\", \"--smoke\"); Process.Start(s);", false)]
    [InlineData("using System.Windows.Forms; Clipboard.SetText(\"hello\");", true)]
    [InlineData("using System.Windows; Clipboard.GetText();", true)]
    [InlineData("using static System.Windows.Forms.Clipboard; SetText(\"hello\");", true)]
    [InlineData("fake.Clipboard.SetText(\"hello\");", false)]
    [InlineData("// Do not call SendInput(); use a fake input seam.\n", false)]
    [InlineData("var diagnostic = \"UsePlatformDetect() is forbidden\";", false)]
    [InlineData("using static System.Windows.Clipboard; SetDataObject(value);", true)]
    [InlineData("Process.Start(\"SIL.Motif.App\", \"--smoke\" + suffix);", true)]
    [InlineData("var s = new ProcessStartInfo(\"SIL.Motif.App\"); s.ArgumentList.Add(\"--smoke\"); s.ArgumentList[0] = \"--other\"; Process.Start(s);", true)]
    [InlineData("class Fake { void SendInput() { } void Test() { fake.SendInput(); } }", false)]
    [InlineData("fake.SendInput();", false)]
    [InlineData("Process.Start(\"SIL.Motif.App\", GetArgs(\"--smoke\"));", true)]
    [InlineData("{ var s = new ProcessStartInfo(\"SIL.Motif.App\"); } { Process.Start(s); }", false)]
    [InlineData("var s = new ProcessStartInfo(\"SIL.Motif.App\"); s.ArgumentList.Add(\"--other\"); s.ArgumentList.Add(\"--smoke\"); Process.Start(s);", true)]
    [InlineData("var s = new ProcessStartInfo(\"SIL.Motif.App\"); s.ArgumentList.Add(\"--smoke\"); Process.Start(s);", false)]
    [InlineData("SendInput();", true)]
    public void ReviewCasesAreClassified(string source, bool forbidden)
    {
        var violations = new List<string>();
        CheckSource(source, "test.cs", violations);
        Assert.Equal(forbidden, violations.Count > 0);
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
        CheckSource("var app = \"SIL.Motif.App.exe\"; Process." + "Start(new ProcessStartInfo(app, \"--smoke\"));", "test.cs", violations);
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
        foreach (var offset in HeadlessSourceScanner.Find(source))
        {
            var line = 1 + source.Take(offset).Count(c => c == '\n');
            violations.Add(path + ":" + line + ": forbidden desktop access. " + Guidance);
        }
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SIL.TextEditing.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Cannot find SIL.TextEditing.sln.");
    }
}
