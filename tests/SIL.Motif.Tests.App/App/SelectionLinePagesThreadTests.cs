using System.Diagnostics;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Texts;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App;

/// <summary>Line pages are single-thread, as the UI thread that drives them in the App is.</summary>
public sealed class SelectionLinePagesThreadTests(ITestOutputHelper output)
{
    private const int LineCount = 12;
    private const int ReadsPerLine = 40;
    private static readonly Guid TextId = Guid.NewGuid();

    [Fact]
    public async Task PoolThreadsReadingLinePagesWhileThePumpRunsCorruptItsWaitingPages()
    {
        using var store = Store();
        var clock = Stopwatch.StartNew();
        Exception? raced = null;
        var attempt = 0;
        while (raced is null && clock.Elapsed < TimeSpan.FromSeconds(60))
        {
            attempt++;
            raced = await Record(() => DriveAsync(store, work => Task.Run(work)));
        }
        // A torn waiter list fails in any shape (invalid operation, null reference, a lost read): any proves it.
        Assert.NotNull(raced);
        output.WriteLine($"Attempt {attempt} raced after {clock.Elapsed.TotalSeconds:F1} s: {raced}");
    }

    [Fact]
    public async Task OneThreadReadingTheSameLinePagesCompletesEveryRead()
    {
        using var store = Store();
        for (var attempt = 0; attempt < 5; attempt++)
            await SingleThreadPump.RunAsync(() => DriveAsync(store, work => work()));
    }

    private static async Task<Exception?> Record(Func<Task> work)
    {
        try
        {
            await work();
            return null;
        }
        catch (Exception exception) { return exception; }
    }

    private static StoredSelectionFixture Store()
    {
        var wordId = Guid.NewGuid();
        var lines = Enumerable.Range(1, LineCount).Select(number => new TextWordsProjectedLine(number,
            $"Sentence {number}", Enumerable.Range(0, 20).Select(index => new TextWordsProjectedToken("word",
                [new WritingSystemText("word", "en")], wordId, "unanalysed", null, null, null, null, index, null))
                .ToArray(), Guid.NewGuid(), Guid.NewGuid(), true)).ToArray();
        return new StoredSelectionFixture(new TextWordsProjection(
            [new TextWordsProjectedText(TextId, "Paged lines", lines, [])],
            [new TextWordsProjectedWordform(wordId, [], [], 0, false, [])]));
    }

    // Each reader alternates offsets, so every read after the first waits on a pump pass that another one changed.
    private static async Task DriveAsync(StoredSelectionFixture store, Func<Func<Task>, Task> start)
    {
        var opened = await store.OpenAsync(new OpenSelectionReaderRequest(store.ProjectPath, [TextId], []),
            CancellationToken.None);
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        await using var reader = opened.Value!;
        var summarized = await reader.ReadSummaryAsync(new SelectionViewRequest());
        Assert.True(summarized.Succeeded, summarized.Refusal?.Message);
        using var summary = summarized.Value!;
        var text = Assert.Single(summary.Value.Texts);
        var owner = new SelectionLinePages(reader, text, () => summary.Lease.RegisterModel(SelectionModelKind.Line));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            var pages = text.Lines.Select(header => (IProgressivePageSource)owner.RealizeLine(header).TokenSource!)
                .ToArray();
            await Task.WhenAll(pages.Select(page => start(async () =>
            {
                for (var read = 0; read < ReadsPerLine; read++)
                    await page.ReadPageAsync(read % 2 * 10, 20, deadline.Token).ConfigureAwait(true);
            })));
            await owner.Pending;
        }
        catch (Exception failure)
        {
            // A faulted pump explains a lost read; stopping a corrupted owner may then fail in its own way.
            var pump = owner.Pending.Exception?.InnerException;
            try { await owner.StopAsync(); }
            catch (Exception) { }
            if (pump is not null) throw new AggregateException(pump, failure);
            throw;
        }
        await owner.StopAsync();
    }
}
