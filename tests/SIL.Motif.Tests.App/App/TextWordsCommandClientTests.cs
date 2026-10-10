using SIL.Motif.App.Services;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Pins cancellation and missing-project refusals at the production Selection-reader seam.</summary>
public sealed class TextWordsCommandClientTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.TextWordsCommandClientTests",
        Guid.NewGuid().ToString("N"));

    public TextWordsCommandClientTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task ACancelledReadIsATypedCancellationBeforeTheStoreIsOpened()
    {
        var client = RealCommandClient.Create(Path.Combine(_root, "managed"));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var missingProject = Path.Combine(_root, "absent.fwdata");

        var outcome = await client.OpenSelectionReaderAsync(new OpenSelectionReaderRequest(missingProject, [], []), cancelled.Token);

        Assert.False(outcome.Succeeded);
        Assert.Equal(FailureReason.Cancelled, outcome.Refusal!.Reason);
        Assert.Equal("selection-reader.cancelled", outcome.Refusal.Code);
    }

    [Fact]
    public async Task AnUncancelledReadRunsTheQuery()
    {
        var client = RealCommandClient.Create(Path.Combine(_root, "managed"));
        var missingProject = Path.Combine(_root, "absent.fwdata");

        var outcome = await client.OpenSelectionReaderAsync(new OpenSelectionReaderRequest(missingProject, [], []), CancellationToken.None);

        Assert.Equal("project.not-found", outcome.Refusal?.Code);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
