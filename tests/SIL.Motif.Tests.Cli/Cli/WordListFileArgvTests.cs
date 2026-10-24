using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class WordListFileArgvTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-word-file-argv-" + Guid.NewGuid().ToString("N"));

    public WordListFileArgvTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("assess", "large", "assess.words-file-too-large")]
    [InlineData("trial", "large", "assess.words-file-too-large")]
    [InlineData("assess", "malformed", "assess.words-file-not-text")]
    [InlineData("trial", "malformed", "assess.words-file-not-text")]
    [InlineData("assess", "truncated", "assess.words-file-not-text")]
    [InlineData("trial", "truncated", "assess.words-file-not-text")]
    [InlineData("assess", "nul", "assess.words-file-not-text")]
    [InlineData("trial", "nul", "assess.words-file-not-text")]
    public async Task InvalidWordFilesAreTypedRefusalsBeforeProjectParserOrJobAccess(string verb, string kind, string code)
    {
        var project = Path.Combine(_root, "project.fwdata");
        File.WriteAllText(project, "");
        var words = Path.Combine(_root, "words.txt");
        byte[] bytes = kind switch
        {
            "large" => Enumerable.Repeat((byte)'a', 8 * 1024 * 1024 + 1).ToArray(),
            "malformed" => [0x61, 0xff],
            "truncated" => [0x61, 0xc3],
            _ => [0x61, 0x00, 0x62],
        };
        File.WriteAllBytes(words, bytes);
        var parser = FakeParser.CopyRecordingInvocations(Path.Combine(_root, "parser"));
        var arguments = verb == "assess"
            ? new[] { "assess", project, "--words", words, "--json" }
            : ["trial", "--project", project, "proposal/unused", "--words", words, "--json"];

        var start = CliProcess.CreateStartInfo(Path.Combine(_root, "worker"), parser, false, arguments);
        CliProcess.EnableAdvancedAiMode(start, Path.Combine(_root, "preferences"));
        var result = await CliProcess.RunAsync(start);

        Assert.True(result.ExitCode == 1, $"Exit {result.ExitCode}: {result.Error}{result.Output}");
        Assert.Empty(result.Output);
        var refusal = ProjectionJson.Deserialize<FailureEnvelope>(result.Error)!;
        Assert.Equal(code, refusal.Code);
        Assert.Equal(FailureReason.InvalidArgument, refusal.Reason);
        Assert.Equal(words, refusal.Detail!["wordsFile"]);
        Assert.Empty(FakeParser.Invocations(parser));
        var database = ProjectDatabaseCatalog.DatabasePathFor(new ProjectLocator(project, "project"));
        Assert.False(File.Exists(database), "No project store or job should be created for an unusable word file.");
    }
}
