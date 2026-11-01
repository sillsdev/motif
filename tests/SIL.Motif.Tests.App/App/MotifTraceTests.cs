using System.Diagnostics;
using System.Text.Json;
using SIL.Motif.App.Services;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class MotifTraceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.Tests.CommandScratch", Guid.NewGuid().ToString("N"));

    [Fact]
    public void TraceRotatesAtTheByteLimitAndDropsAnOversizedLine()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "trace.jsonl");
        using (MotifTrace.StartListening(path, 512))
        {
            for (var i = 0; i < 10; i++) MotifTrace.Finish(MotifTrace.Begin("bounded"));
            var oversized = MotifTrace.Begin("oversized");
            oversized!.SetTag("large", new string('x', 1000));
            MotifTrace.Finish(oversized);
        }

        Assert.InRange(new FileInfo(path).Length, 1, 512);
        Assert.InRange(new FileInfo(path + ".1").Length, 1, 512);
        var lines = File.ReadAllLines(path).Concat(File.ReadAllLines(path + ".1")).ToArray();
        Assert.NotEmpty(lines);
        Assert.All(lines, line =>
        {
            using var document = JsonDocument.Parse(line);
            Assert.NotNull(document.RootElement.GetProperty("name").GetString());
        });
    }

    [Fact]
    public void ConcurrentSinksKeepCompleteJsonLinesWithinTheLimit()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "concurrent.jsonl");
        using (MotifTrace.StartListening(path, 1024))
        using (MotifTrace.StartListening(path, 1024))
            Parallel.For(0, 30, _ => MotifTrace.Finish(MotifTrace.Begin("concurrent")));

        Assert.InRange(new FileInfo(path).Length, 1, 1024);
        Assert.InRange(new FileInfo(path + ".1").Length, 1, 1024);
        Assert.All(File.ReadAllLines(path).Concat(File.ReadAllLines(path + ".1")), line =>
        {
            using var document = JsonDocument.Parse(line);
            Assert.NotNull(document.RootElement.GetProperty("name").GetString());
        });
    }

    [Fact]
    public async Task ASpanThatFinishesAfterTheSinkIsDisposedIsDroppedAndDoesNotThrow()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "late.jsonl");
        using var stopping = new ManualResetEventSlim();
        using var disposed = new ManualResetEventSlim();
        // Added before the sink, so Stop reaches it first and holds the sink's callback until the sink is disposed.
        using var holdOpen = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MotifTrace.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.OperationName != "late") return;
                stopping.Set();
                Assert.True(disposed.Wait(TimeSpan.FromSeconds(30)));
            },
        };
        ActivitySource.AddActivityListener(holdOpen);
        var sink = MotifTrace.StartListening(path, 1024);
        var late = MotifTrace.Begin("late");
        Assert.NotNull(late);

        var finisher = Task.Run(() => MotifTrace.Finish(late));
        Assert.True(stopping.Wait(TimeSpan.FromSeconds(30)));
        sink.Dispose();
        disposed.Set();

        await finisher;
        Assert.False(File.Exists(path) && File.ReadAllText(path).Contains("\"late\""));
    }

    [Fact]
    public void DisposingASinkTwiceIsHarmless()
    {
        Directory.CreateDirectory(_root);
        var sink = MotifTrace.StartListening(Path.Combine(_root, "twice.jsonl"), 1024);
        sink.Dispose();
        sink.Dispose();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
