using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.Preferences;
using Xunit;

namespace SIL.Motif.Tests.Commands.Preferences;

public sealed class DefaultProposalChecksStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "motif-default-checks-tests",
        Guid.NewGuid().ToString("N"));

    private string PathFor() => Path.Combine(_directory, "default-proposal-checks.json");

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void AbsentFileMeansTheBudgetDefault()
    {
        Assert.Equal(StoredAnalysisComparison.Budget, new FileDefaultProposalChecksStore(PathFor()).StoredAnalyses);
        Assert.Equal(StoredAnalysisComparison.Budget, new MemoryDefaultProposalChecksStore().StoredAnalyses);
    }

    [Theory]
    [InlineData(StoredAnalysisComparison.Budget, "budget")]
    [InlineData(StoredAnalysisComparison.All, "all")]
    [InlineData(StoredAnalysisComparison.Off, "off")]
    public void EachChoiceRoundTripsAcrossStoreInstancesAsALineFeedFile(StoredAnalysisComparison mode, string word)
    {
        new FileDefaultProposalChecksStore(PathFor()).SetStoredAnalyses(mode);

        Assert.Equal(mode, new FileDefaultProposalChecksStore(PathFor()).StoredAnalyses);
        var text = File.ReadAllText(PathFor());
        Assert.Equal($"{{\"storedAnalyses\":\"{word}\"}}", text);
        Assert.DoesNotContain('\r', text);
        Assert.Empty(Directory.EnumerateFiles(_directory, ".default-proposal-checks-*.tmp"));
    }

    [Theory]
    [InlineData("{\"storedAnalyses\":\"everything\"}")]
    [InlineData("{\"storedAnalyses\":3}")]
    [InlineData("{}")]
    [InlineData("[1]")]
    [InlineData("not json")]
    public void AnUnreadableOrUnknownValueMeansTheDefaultRatherThanAFailure(string content)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PathFor(), content);

        Assert.Equal(StoredAnalysisComparison.Budget, new FileDefaultProposalChecksStore(PathFor()).StoredAnalyses);
    }

    [Fact]
    public void AnUndefinedModeIsRefusedAndNothingIsWritten()
    {
        var store = new FileDefaultProposalChecksStore(PathFor());

        Assert.Throws<ArgumentOutOfRangeException>(() => store.SetStoredAnalyses((StoredAnalysisComparison)42));
        Assert.False(File.Exists(PathFor()));
    }

    [Fact]
    public void EveryTestProcessReadsAPrivateFileRatherThanTheInstalledUsers()
    {
        var configured = Environment.GetEnvironmentVariable(FileDefaultProposalChecksStore.PathEnvironmentVariable);

        Assert.False(string.IsNullOrWhiteSpace(configured));
        Assert.NotEqual(FileDefaultProposalChecksStore.DefaultPath, Path.GetFullPath(configured!));
        Assert.Equal(StoredAnalysisComparison.Budget, FileDefaultProposalChecksStore.ForInstallation().StoredAnalyses);
    }
}
