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
        receiver.Paste("Please use `grammar.json` and `instructions.md`.");

        var result = receiver.Validate();

        Assert.Contains(result.Failures, failure => failure.Contains("instructions.md", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("grammar.json", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateMatchesSelectionWordsToWordStatisticsRowsAndReportsFlatPathFindings()
    {
        using var files = new TemporaryFiles();
        var receiver = new FakeChatReceiver();
        receiver.Drop([
            files.Write("instructions.md", "See `statistics/word.jsonl`, `reference/grammar-format.md`, and `texts/*.flextext.json`.") ,
            files.Write("grammar.json", "{}"),
            files.Write("selection.txt", "# Selection (2 word(s))\n\nProvenance:\n  pasted: 2\n\nalpha\nbeta\n"),
            files.Write("word.jsonl", "{\"word\":\"alpha\"}\n{\"word\":\"beta\"}\n"),
        ]);
        receiver.Paste("Read `instructions.md` first. Use `grammar.json`, `selection.txt`, and `word.jsonl`.");

        var result = receiver.Validate();

        Assert.Empty(result.Failures);
        Assert.Contains(result.Findings, finding => finding.Contains("statistics/word.jsonl", StringComparison.Ordinal));
        Assert.Contains(result.Findings, finding => finding.Contains("reference/grammar-format.md", StringComparison.Ordinal));
        Assert.Contains(result.Findings, finding => finding.Contains("texts/*.flextext.json", StringComparison.Ordinal));
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
