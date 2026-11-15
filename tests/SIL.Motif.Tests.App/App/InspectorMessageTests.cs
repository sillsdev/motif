using SIL.Motif.App.ViewModels;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class InspectorMessageTests
{
    [Fact]
    public void ARefusedLookupKeepsFoldersOutOfTheMessage() =>
        Assert.Equal("Cannot read store.db now.", WindowPathText.Scrub("Cannot read /home/alice/Secret Place/store.db now."));
}
