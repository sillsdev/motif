using SIL.Motif.App.Services;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the production <see cref="CommandClient"/>'s Text words read: it answers a cancelled request with a typed
/// cancellation and never starts the query, so a read the window has already superseded costs nothing.
/// </summary>
public sealed class TextWordsCommandClientTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.TextWordsCommandClientTests",
        Guid.NewGuid().ToString("N"));

    public TextWordsCommandClientTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task ACancelledReadIsATypedCancellationAndNeverRunsTheQuery()
    {
        var client = new CommandClient(Path.Combine(_root, "managed"));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var missingProject = Path.Combine(_root, "absent.fwdata");

        var outcome = await client.ListTextWordsAsync(new TextWordsRequest(missingProject, []), cancelled.Token);

        Assert.False(outcome.Succeeded);
        Assert.Equal(FailureReason.Cancelled, outcome.Refusal!.Reason);
        Assert.Equal("texts.words-cancelled", outcome.Refusal.Code);
    }

    [Fact]
    public async Task AnUncancelledReadRunsTheQuery()
    {
        var client = new CommandClient(Path.Combine(_root, "managed"));
        var missingProject = Path.Combine(_root, "absent.fwdata");

        var outcome = await client.ListTextWordsAsync(new TextWordsRequest(missingProject, []), CancellationToken.None);

        Assert.Equal("project.not-found", outcome.Refusal?.Code);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
