using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

public sealed class FakeChatReceiverTests
{
    [Fact]
    public void ValidateReportsMissingPromptFilesAndMalformedGrammar()
    {
        using var files = new TemporaryFiles();
        var receiver = new FakeChatReceiver();
        receiver.Drop([files.Write("grammar.json", "not json")]);
        receiver.Paste("Please use `grammar.json` and `handoff.md`.");

        var result = receiver.Validate();

        Assert.Contains(result.Failures, failure => failure.Contains("handoff.md", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("grammar.json", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateAcceptsTheFourFlatFilesAndReportsFlatPathFindings()
    {
        using var files = new TemporaryFiles();
        var receiver = new FakeChatReceiver();
        receiver.Drop([
            files.Write("handoff.md", "See `docs/handoff/assessment-format.md` for the shape of `assessment.json`."),
            files.Write("grammar.json", "[]"),
            files.Write("texts.json", "[{\"key\":\"example-1\"}]"),
            files.Write("assessment.json", "[{\"word\":\"alpha\"}]"),
        ]);
        receiver.Paste("Read `handoff.md` first. Use `grammar.json`, `texts.json`, and `assessment.json`.");

        var result = receiver.Validate();

        Assert.Empty(result.Failures);
        Assert.Contains(
            result.Findings, finding => finding.Contains("docs/handoff/assessment-format.md", StringComparison.Ordinal));
    }

    [Fact]
    public void DropRefusesDirectories()
    {
        using var files = new TemporaryFiles();
        var receiver = new FakeChatReceiver();

        Assert.Throws<InvalidOperationException>(() => receiver.Drop([files.DirectoryPath]));
    }

    [Fact]
    public void DropRefusesBasenameCollisions()
    {
        using var files = new TemporaryFiles();
        var receiver = new FakeChatReceiver();

        receiver.Drop([files.Write("same.txt", "one")]);
        Assert.Throws<InvalidOperationException>(() => receiver.Drop([files.Write("same.txt", "two")]));
    }

    [Fact]
    public void DropRefusesConfiguredCaps()
    {
        using var files = new TemporaryFiles();
        var receiver = new FakeChatReceiver(maxFileCount: 1, maxFileSizeBytes: 3);

        receiver.Drop([files.Write("same.txt", "one")]);
        Assert.Throws<InvalidOperationException>(() => receiver.Drop([files.Write("other.txt", "two")]));
        var sizeLimitedReceiver = new FakeChatReceiver(maxFileSizeBytes: 3);
        Assert.Throws<InvalidOperationException>(() => sizeLimitedReceiver.Drop([files.Write("large.txt", "four")]));
    }

    private sealed class TemporaryFiles : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("motif-fake-chat-").FullName;

        public string DirectoryPath => _root;

        public string Write(string name, string contents)
        {
            var path = Path.Combine(_root, name);
            File.WriteAllText(path, contents);
            return path;
        }

        public void Dispose() => WalkthroughTestFiles.DeleteDirectory(_root);
    }
}
