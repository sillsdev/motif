using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.TestInfrastructure;

public sealed class TestFailureLogTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "motif-failure-log-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void AFailureIsAppendedAsOneTabSeparatedLineAndEarlierLinesStay()
    {
        var path = Path.Combine(_folder, "nested", "failures.tsv");
        var log = TestFailureLog.At(path)!;

        log.Record("SIL.Motif.Tests.App", "3/20", "SIL.Motif.Tests.App.OneTests.First", "first\nsecond line");
        log.Record("SIL.Motif.Tests.App", null, "SIL.Motif.Tests.App.OneTests.Second", null);

        var lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        var first = lines[0].Split('\t');
        Assert.Equal(5, first.Length);
        Assert.EndsWith("Z", first[0]);
        Assert.Equal(["SIL.Motif.Tests.App", "3/20", "SIL.Motif.Tests.App.OneTests.First", "first"], first[1..]);
        Assert.Equal(["SIL.Motif.Tests.App", "-", "SIL.Motif.Tests.App.OneTests.Second", ""], lines[1].Split('\t')[1..]);
    }

    [Fact]
    public void AMessageTooLongIsCutAndATabInsideItCannotSplitTheLine()
    {
        var line = TestFailureLog.Line(DateTime.UtcNow, "A", null, "T", "x\ty" + new string('z', 1000));

        var fields = line.Split('\t');
        Assert.Equal(5, fields.Length);
        Assert.Equal(300, fields[4].Length);
    }

    [Fact]
    public void WithoutAPathNothingIsRecorded() => Assert.Null(TestFailureLog.At(" "));
}
