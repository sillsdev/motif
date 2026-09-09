using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Parser;

/// <summary>
/// Pins that every remaining PanGloss process seam — <see cref="PanGlossParser.AnalyseBatch"/>,
/// <see cref="PanGlossParser.Assess"/>, and <see cref="PanGlossStatsProcess"/>'s <c>batch --stats --cache</c>
/// pass — assigns its launched process to a supplied governor immediately after starting.
/// </summary>
/// <remarks>
/// The fake parser answers <c>batch</c> but not <c>assess</c>, so two of these runs succeed and one is
/// refused. Containment happens the instant the process starts, before either side knows how the run will
/// end, so the assertion is the same in both cases.
/// </remarks>
public sealed class PanGlossGovernorTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "motif-pangloss-governor-" + Guid.NewGuid().ToString("N"));

    public PanGlossGovernorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
    }

    [Fact]
    public void AnalyseBatch_AssignsTheStartedProcessToTheSuppliedGovernor()
    {
        var projectFilePath = Project("analyse-batch");
        var parser = new PanGlossParser(FakeParser.ExecutablePath);
        var governor = new RecordingGovernor();

        parser.AnalyseBatch(projectFilePath, ["motifa"], governor: governor);

        Assert.Single(governor.ContainedProcessIds);
    }

    [Fact]
    public void Assess_AssignsTheStartedProcessToTheSuppliedGovernor_EvenWhenTheRunIsRefused()
    {
        // The fake does not answer this argv shape, so the run fails; containment already happened.
        var projectFilePath = Project("assess");
        var parser = new PanGlossParser(FakeParser.ExecutablePath);
        var governor = new RecordingGovernor();

        Assert.ThrowsAny<Exception>(() => parser.Assess(projectFilePath, ["motifa"], governor: governor));

        Assert.Single(governor.ContainedProcessIds);
    }

    [Fact]
    public async Task StatsProcessRunBatchAsync_AssignsTheStartedProcessToTheSuppliedGovernor()
    {
        var projectFilePath = Project("stats-process");
        var cachePath = Path.Combine(_root, "cache.bin");
        var statsRunner = new PanGlossStatsProcess(FakeParser.ExecutablePath);
        var governor = new RecordingGovernor();

        await statsRunner.RunBatchAsync(
            projectFilePath, ["motifa"], ParserEngine.FstPrunedByHermitCrab, TimeSpan.FromSeconds(5), cachePath,
            CancellationToken.None, governor);

        Assert.Single(governor.ContainedProcessIds);
    }

    private string Project(string name)
    {
        var path = Path.Combine(_root, name + ".fwdata");
        File.WriteAllText(path, "the fake parser never reads this.");
        return path;
    }
}
