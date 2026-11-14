using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Xunit;

namespace SIL.Motif.Tests.Contract.Process;

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
                CheckSource(source, relative, violations, relative.Replace('\\', '/') == "tests/SIL.Motif.Tests.App/App/AppSmokeLauncher.cs");
        }
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [InlineData("UseX11()", true)]
    [InlineData("NativeMethods.SendInput()", true)]
    [InlineData("using System.Windows.Forms; Clipboard.SetText(\"hello\");", true)]
    [InlineData("using System.Windows; Clipboard.GetText();", true)]
    [InlineData("using static System.Windows.Forms.Clipboard; SetText(\"hello\");", true)]
    [InlineData("using static System.Windows.Clipboard; SetDataObject(value);", true)]
    [InlineData("System.Windows.Forms.Clipboard.GetText();", true)]
    [InlineData("fake.Clipboard.SetText(\"hello\");", false)]
    [InlineData("// Do not call SendInput(); use a fake input seam.\n", false)]
    [InlineData("var diagnostic = \"UsePlatformDetect() is forbidden\";", false)]
    [InlineData("class Fake { void SendInput() { } void Test() { fake.SendInput(); } }", false)]
    [InlineData("fake.SendInput();", false)]
    public void GuardClassifiesCalls(string source, bool forbidden)
    {
        var violations = new List<string>();
        CheckSource(source, "test.cs", violations);
        Assert.Equal(forbidden, violations.Count > 0);
    }

    [Theory]
    [InlineData("ProcessStartInfo start = new() { FileName = \"SIL.Motif.App\" }; Process.Start(start);", true)]
    [InlineData("var app = \"SIL.Motif.App.exe\"; var s = new ProcessStartInfo(app, \"--smoke\"); Process.Start(s);", true)]
    [InlineData("Process.Start(\"SIL.Motif.App\", new[] { \"--smoke --other\" });", true)]
    [InlineData("Process.Start(new System.Diagnostics.ProcessStartInfo(\"SIL.Motif.App\", \"--smoke\"));", true)]
    [InlineData("var s = new ProcessStartInfo(\"SIL.Motif.App\") { ArgumentList = { \"--smoke\" } }; Process.Start(s);", true)]
    [InlineData("var x = \"Host48.exe\"; ToolProcess.Run(x);", true)]
    [InlineData("var x = \"Playground.Desktop\"; Process.Start(x);", true)]
    [InlineData("var x = \"SIL.Motif.App.dll\"; Process.Start(x);", true)]
    [InlineData("var x = \"SIL.Motif.App.exe\"; File.Exists(x);", false)]
    [InlineData("Process.Start(BuildOutput.App);", true)]
    [InlineData("Process.Start(\"tool.exe\");", false)]
    [InlineData("var p = Path.Combine(root, \"src\", \"SIL.Motif.App\", \"Views\"); Process.Start(\"tool.exe\");", false)]
    [InlineData("// Process.Start(\"SIL.Motif.App\")\n", false)]
    public void GuardRequiresTheDesignatedLauncher(string source, bool forbidden)
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
    public void AppLauncherHasOnlyTheFixedHeadlessStart()
    {
        var source = File.ReadAllText(Path.Combine(FindRoot(), "tests", "SIL.Motif.Tests.App", "App", "AppSmokeLauncher.cs"));
        const string expected = """
            using System.Diagnostics;
            using SIL.Motif.Tests.TestFixtures;

            namespace SIL.Motif.Tests.App;

            internal static class AppSmokeLauncher
            {
                public static Process Start()
                {
                    var app = Path.Combine(BuildOutput.ProductDirectory,
                        OperatingSystem.IsWindows() ? "SIL.Motif.App.exe" : "SIL.Motif.App");
                    var start = new ProcessStartInfo(app, "--smoke")
                    {
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    start.Environment.Remove("ICU_DATA");
                    return Process.Start(start)!;
                }
            }
            """;
        Assert.Equal(expected, source.Replace("\r\n", "\n").TrimEnd('\n'));
    }

    [Fact]
    public void AppSmokeUsesHeadlessPlatform()
    {
        var program = File.ReadAllText(Path.Combine(FindRoot(), "src", "SIL.Motif.App", "Program.cs"));
        var dispatch = program.IndexOf("return Smoke();", StringComparison.Ordinal);
        var smoke = program.IndexOf("private static int Smoke()", StringComparison.Ordinal);
        var next = program.IndexOf("private static int RunCli", smoke, StringComparison.Ordinal);
        Assert.True(dispatch >= 0 && smoke > dispatch && next > smoke, "The smoke entry point must dispatch to Smoke.");
        var body = program.Substring(smoke, next - smoke);
        Assert.Contains(".UseHeadless(", body, StringComparison.Ordinal);
        Assert.False(ForbiddenCall.IsMatch(body), "The smoke path must not use a real platform backend.");
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

    private static void CheckSource(string source, string path, List<string> violations, bool allowedLauncher = false)
    {
        foreach (var offset in HeadlessSourceScanner.Find(source, allowedLauncher))
        {
            var line = 1 + source.Take(offset).Count(c => c == '\n');
            violations.Add(path + ":" + line + ": forbidden desktop access. " + Guidance);
        }
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Motif.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Cannot find Motif.sln.");
    }
}
