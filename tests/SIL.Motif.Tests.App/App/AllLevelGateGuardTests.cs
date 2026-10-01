using Xunit;

namespace SIL.Motif.Tests.App.App;

public sealed class AllLevelGateGuardTests
{
    [Fact]
    public void CiProbesPanGlossPinModesBesideTheAllLevelTestStep()
    {
        var ci = File.ReadAllText(Path.Combine(FindRepositoryRoot(), ".github", "workflows", "ci.yml"));
        var testStep = ci.IndexOf("- name: Test (full suite)", StringComparison.Ordinal);
        var probeStep = ci.IndexOf("- name: Verify PanGloss pin mode handling", StringComparison.Ordinal);
        var uploadStep = ci.IndexOf("- name: Upload test results", StringComparison.Ordinal);

        Assert.True(testStep >= 0 && probeStep > testStep && uploadStep > probeStep);
        Assert.Contains("shell: pwsh", ci[probeStep..uploadStep], StringComparison.Ordinal);
        Assert.Contains("./tools/Test-PanGlossRelease.ps1 -RepositoryRoot (Get-Location).Path " +
                        "-ProbeRoot (Join-Path $env:RUNNER_TEMP 'motif-pangloss-pin-probe')",
            ci[probeStep..uploadStep], StringComparison.Ordinal);
    }

    [Fact]
    public void MergeAndMediaTestCallsRequestAllLevels()
    {
        var root = FindRepositoryRoot();
        var ci = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));
        var docs = File.ReadAllText(Path.Combine(root, "tools", "validate-documentation.ps1"));
        var media = File.ReadAllText(Path.Combine(root, "tools", "Build-Media.ps1"));
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "media.yml"));

        AssertAllCallsUseAll(ci, "test.ps1", false);
        AssertAllCallsUseAll(docs, "test.ps1", true);
        AssertAllCallsUseAll(media, "test.ps1", true);
        AssertAllCallsUseAll(workflow, "test.ps1", false);
        Assert.Contains("Build-Media.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("publish_website:", ci, StringComparison.Ordinal);
        Assert.Contains("default: false", ci, StringComparison.Ordinal);
    }

    private static void AssertAllCallsUseAll(string content, string token, bool joinPowerShellLines)
    {
        var lines = content.Split(["\r\n", "\n"], StringSplitOptions.None);
        var calls = new List<string>();
        for (var index = 0; index < lines.Length; index++)
        {
            if (!lines[index].Contains(token, StringComparison.OrdinalIgnoreCase)) continue;
            var call = lines[index];
            while (joinPowerShellLines && call.TrimEnd().EndsWith('`') && index + 1 < lines.Length)
                call += lines[++index];
            calls.Add(call);
        }

        Assert.NotEmpty(calls);
        Assert.All(calls, call => Assert.Contains("-All", call, StringComparison.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Motif.sln"))) return directory.FullName;

        throw new DirectoryNotFoundException("Could not find Motif.sln above the test output directory.");
    }
}
