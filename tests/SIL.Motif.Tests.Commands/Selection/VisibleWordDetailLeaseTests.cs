using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Texts;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class VisibleWordDetailLeaseTests
{
    [Fact]
    public async Task ActiveWordDetailLeasesShareMorphologyBeyondTheCacheAndReleaseItWithTheirOwnership()
    {
        var textId = Guid.NewGuid();
        var words = Enumerable.Range(0, 64).Select(_ => new TextWordsProjectedWordform(Guid.NewGuid(), [], [], 0,
            false, [])).ToArray();
        var tokens = words.Select((word, index) => new TextWordsProjectedToken($"word-{index}",
            [new WritingSystemText($"word-{index}", "en")], word.WordformId, "unanalysed", null, null,
            null, null, index, null)).ToArray();
        using var fixture = new StoredSelectionFixture(new TextWordsProjection([
            new TextWordsProjectedText(textId, "Words", [new TextWordsProjectedLine(1, "Words", tokens,
                Guid.NewGuid(), Guid.NewGuid(), true)], [])], words));
        var opened = await fixture.OpenAsync(new OpenSelectionReaderRequest(fixture.ProjectPath, [textId], []));
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        await using var reader = opened.Value!;
        var keys = words.Select((word, index) => new TextWordKey(word.WordformId, $"word-{index}", "en")).ToArray();
        var first = await reader.ReadWordDetailsAsync(keys);
        Assert.True(first.Succeeded, first.Refusal?.Message);
        Assert.Equal(64, reader.Diagnostics.WordformRowsDeserialized);
        Assert.Equal(32, reader.Diagnostics.CachedWordDetails);
        var second = await reader.ReadWordDetailsAsync(keys);
        Assert.True(second.Succeeded, second.Refusal?.Message);
        Assert.Equal(64, reader.Diagnostics.WordformRowsDeserialized);
        Assert.Same(first.Value!.Value.Wordforms[keys[0]], second.Value!.Value.Wordforms[keys[0]]);
        first.Value.Dispose();
        second.Value.Dispose();
        Assert.Equal(0, reader.Diagnostics.LeasedWordKeys);
        var afterRelease = await reader.ReadWordDetailsAsync(keys);
        Assert.True(afterRelease.Succeeded, afterRelease.Refusal?.Message);
        using var releasedRead = afterRelease.Value!;
        Assert.Equal(96, reader.Diagnostics.WordformRowsDeserialized);
        Assert.Equal(0, reader.Diagnostics.TextRowsDeserialized);
        Assert.Equal(0, reader.Diagnostics.LiveRowModels);
    }
}
