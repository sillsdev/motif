using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TextWordsReloadCommandTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedReaderReloadHasAnObservableTaskAndPublishesLoadingAndRefusal(bool throws)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var fixture = TextWordsReloadTests.Records();
            var client = new FakeCommandClient();
            var reads = client.ReaderOwner;
            var words = new TextWordsViewModel(client, new SelectionViewModel(client), reads);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            client.SelectionReaderHandler = async (request, cancellation) =>
            {
                entered.SetResult();
                await resume.Task;
                if (throws) throw new IOException("store unavailable");
                return await fixture.OpenAsync(request, cancellation);
            };
            try
            {
                await words.SetProjectAsync(fixture.ProjectPath);
                var reload = reads.ReloadAsync(fixture.ProjectPath, [], []);
                await entered.Task;
                Assert.Same(reload, reads.Pending);
                Assert.False(reload.IsCompleted);
                Assert.True(words.IsLoading);
                resume.SetResult();
                await reload;
                Assert.True(reload.IsCompletedSuccessfully);
                Assert.False(words.IsLoading);
                Assert.Equal(throws ? "store unavailable" : null, words.Refusal?.Message);

            }
            finally
            {
                resume.TrySetResult();
                await words.StopAsync();
                await reads.StopAsync();
            }
        }, TimeSpan.FromSeconds(30));
    }
}
