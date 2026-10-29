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

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
