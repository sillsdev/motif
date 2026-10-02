using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class TraceViewPreferenceTests
{
    [Fact]
    public void TheChosenModeIsRememberedWithoutSharingATestPersonsFile()
    {
        using var folder = new TempDirectory();
        var path = Path.Combine(folder.Path, "trace-view.json");
        var first = new TraceWordViewModel(preferences: new FileTraceViewPreferences(path));
        Assert.False(first.IsExpert);
        first.IsExpert = true;
        var reopened = new TraceWordViewModel(preferences: new FileTraceViewPreferences(path));
        Assert.True(reopened.IsExpert);
        reopened.IsExpert = false;
        Assert.False(new TraceWordViewModel(preferences: new FileTraceViewPreferences(path)).IsExpert);
        Assert.False(new TraceWordViewModel().IsExpert);
    }

    [Theory]
    [InlineData("broken")]
    [InlineData("{\"mode\":\"Future\"}")]
    public void AnUnreadablePreferenceDoesNotInventAnExpertChoice(string text)
    {
        using var folder = new TempDirectory();
        var path = Path.Combine(folder.Path, "trace-view.json");
        File.WriteAllText(path, text);
        Assert.False(new TraceWordViewModel(preferences: new FileTraceViewPreferences(path)).IsExpert);
    }
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("motif-trace-view-").FullName;
        public void Dispose() => Directory.Delete(Path, true);
    }
}
