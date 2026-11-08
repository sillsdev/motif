using System.Diagnostics;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Contract.Process;

public sealed class HeadlessTestEnvironmentTests
{
    [Theory]
    [InlineData("test.ps1", "$repoRoot = $PSScriptRoot", "Start-Process -FilePath 'dotnet'")]
    [InlineData("textediting/test.ps1", "$projectPath = Join-Path", "& dotnet @testArguments")]
    public void TestScriptClearsLinuxDisplayVariablesBeforeStartingTests(string path, string startupMarker,
        string launchMarker)
    {
        if (!OperatingSystem.IsLinux()) return;

        var root = Path.GetFullPath(Path.Combine(BuildOutput.ProductDirectory, "..", ".."));
        var script = File.ReadAllText(Path.Combine(root, path));
        var startupEnd = script.IndexOf(startupMarker, StringComparison.Ordinal);
        var testLaunch = script.IndexOf(launchMarker, StringComparison.Ordinal);
        Assert.True(startupEnd > 0 && testLaunch > startupEnd, "The startup probe must precede test launches.");

        var start = new ProcessStartInfo("pwsh");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(script[..startupEnd] + "\n" +
            "if (Test-Path Env:DISPLAY) { throw 'DISPLAY remains set' }\n" +
            "if (Test-Path Env:WAYLAND_DISPLAY) { throw 'WAYLAND_DISPLAY remains set' }");
        start.Environment["DISPLAY"] = ":test";
        start.Environment["WAYLAND_DISPLAY"] = "wayland-test";

        var result = ToolProcess.Run(start);

        Assert.True(result.ExitCode == 0, $"The test startup retained a display variable: {result.Error}");
    }

    [Theory]
    [InlineData("textediting/run.ps1", "desktop10")]
    [InlineData("textediting/docs/images/capture-hosts.ps1", null)]
    public void TextEditingDesktopToolsRequireAnExplicitOptIn(string path, string? argument)
    {
        var root = Path.GetFullPath(Path.Combine(BuildOutput.ProductDirectory, "..", ".."));
        var start = new ProcessStartInfo("pwsh");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(root, path));
        if (argument is not null) start.ArgumentList.Add(argument);
        start.Environment["DISPLAY"] = ":test";
        start.Environment["WAYLAND_DISPLAY"] = "wayland-test";

        var result = ToolProcess.Run(start);

        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Contains("Skipped:", result.Output, StringComparison.Ordinal);
        Assert.Contains("-InteractiveDesktop", result.Output, StringComparison.Ordinal);
    }
}
