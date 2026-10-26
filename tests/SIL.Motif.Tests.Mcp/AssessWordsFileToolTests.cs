using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Mcp;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Trait("MotifTestLevel", "Unit")]
public sealed class AssessWordsFileToolTests
{
    [Theory]
    [InlineData("motif_assess")]
    [InlineData("motif_trial")]
    public void TheToolTakesAWordsFileInCamelCase(string tool)
    {
        var exposed = MotifMcpServer.Expose(ToolProfile.Builtin).Single(entry => entry.Name == tool);

        var properties = exposed.Tool.InputSchema["properties"]!.AsObject();
        Assert.Equal("string", properties["wordsFile"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void TheAssessToolPagesAStoredAssessmentWithOffsetAndAssessment()
    {
        var exposed = MotifMcpServer.Expose(ToolProfile.Builtin).Single(entry => entry.Name == "motif_assess");

        var properties = exposed.Tool.InputSchema["properties"]!.AsObject();
        Assert.True(properties.ContainsKey("assessment"));
        Assert.True(properties.ContainsKey("offset"));
    }

    [Fact]
    public void TheSharedWordsFileReaderAcceptsA7206EntrySyntheticSelection()
    {
        var directory = Path.Combine(Path.GetTempPath(), "motif-words-file-capacity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "words.txt");
        var words = Enumerable.Range(0, 7206).Select(index => $"synthetic-{index:D4}").ToArray();
        try
        {
            File.WriteAllLines(path, words);

            var resolved = WordListFile.Resolve(null, path);

            Assert.True(resolved.Succeeded, resolved.Refusal?.Message);
            Assert.Equal(words.Length, resolved.Value!.Count);
            Assert.Equal(words[0], resolved.Value[0]);
            Assert.Equal(words[^1], resolved.Value[^1]);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("motif_assess", "words", "assess.words-ambiguous")]
    [InlineData("motif_trial", "words", "assess.words-ambiguous")]
    [InlineData("motif_assess", null, "assess.words-file-unreadable")]
    [InlineData("motif_trial", null, "assess.words-file-unreadable")]
    public async Task AWordsFileThatCannotBeUsedIsRefusedWithARegisteredCode(string tool, string? inline, string code)
    {
        var directory = Path.Combine(Path.GetTempPath(), "motif-words-file-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var project = Path.Combine(directory, "project.fwdata");
        File.WriteAllText(project, "");
        var wordsFile = inline is null ? Path.Combine(directory, "missing.txt") : Path.Combine(directory, "words.txt");
        File.WriteAllText(Path.Combine(directory, "words.txt"), "kaon\n");
        try
        {
            using (var machine = MachineDatabase.Open(RunnerOptions.ResolveRoot()))
                new KnownProjectRegistry(machine).Record(ProjectWorkspaceKey.Compute(
                    new ProjectLocator(project, "project")), project, DateTimeOffset.UtcNow);
            var context = new ServerContext(project, "1.0", new NoRunnerLauncher(
                new JobRunnerLaunchOptions(RunnerOptions.ResolveRoot(), null)), new ActivityLog(null),
                ToolProfile.Builtin, TextWriter.Null, advancedAiModeEnabled: true);
            var arguments = new Dictionary<string, JsonElement>
            {
                ["project"] = JsonSerializer.SerializeToElement(project),
                ["wordsFile"] = JsonSerializer.SerializeToElement(wordsFile),
            };
            if (inline is not null) arguments["words"] = JsonSerializer.SerializeToElement(new[] { "baga" });
            if (tool == "motif_trial") arguments["proposal"] = JsonSerializer.SerializeToElement("proposal/x");

            var result = await MotifMcpServer.CallAsync(context, MotifMcpServer.Expose(ToolProfile.Builtin),
                tool, arguments, CancellationToken.None);

            Assert.True(result.IsError);
            Assert.Equal(code, result.StructuredContent!.Value.GetProperty("code").GetString());
        }
        finally { Directory.Delete(directory, true); }
    }
}

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class AssessWordsFileIngressTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task MotifAssessUsesTheWordsFileSelectionAndPagesTheStoredAssessmentWithoutReparsing()
    {
        using var project = new WalkthroughProject(pristine);
        PrepareProject(project.FwDataPath);
        var capture = BaselineCaptureCommand.Capture(
            new BaselineCaptureRequest(project.FwDataPath), RunnerOptions.ResolveRoot());
        Assert.True(capture.Succeeded, capture.Refusal?.Message);

        var words = new[] { "motifa", "motifb", "motifc" };
        var wordsFile = Path.Combine(project.ManagedRoot, "words.txt");
        File.WriteAllLines(wordsFile, words);
        var parser = RecordingParser(project, "assess-parser", words);
        var context = Context(project.FwDataPath, parser);

        var fileResponse = await CallAsync(context, "motif_assess", Arguments(project.FwDataPath,
            ("wordsFile", JsonSerializer.SerializeToElement(wordsFile)),
            ("limit", JsonSerializer.SerializeToElement(2))));
        var filePage = fileResponse.GetProperty("result");
        var assessment = filePage.GetProperty("assessment").GetString();
        Assert.False(string.IsNullOrWhiteSpace(assessment));
        Assert.Equal(words.Length, filePage.GetProperty("total").GetInt32());
        Assert.Equal(words[..2], PageWords(filePage));

        var parserCallsBeforeStoredPage = FakeParser.Invocations(parser).ToArray();
        Assert.NotEmpty(parserCallsBeforeStoredPage);
        var storedResponse = await CallAsync(context, "motif_assess", Arguments(project.FwDataPath,
            ("assessment", JsonSerializer.SerializeToElement(assessment!)),
            ("offset", JsonSerializer.SerializeToElement(2)),
            ("limit", JsonSerializer.SerializeToElement(2))));
        var storedPage = storedResponse.GetProperty("result");

        Assert.Equal(assessment, storedPage.GetProperty("assessment").GetString());
        Assert.Equal(words.Length, storedPage.GetProperty("total").GetInt32());
        Assert.Equal(2, storedPage.GetProperty("offset").GetInt32());
        Assert.Equal(words[2..], PageWords(storedPage));
        Assert.False(storedPage.TryGetProperty("nextOffset", out var nextOffset) &&
            nextOffset.ValueKind != JsonValueKind.Null);
        Assert.True(JsonElement.DeepEquals(filePage.GetProperty("counts"), storedPage.GetProperty("counts")));
        Assert.Equal(parserCallsBeforeStoredPage, FakeParser.Invocations(parser));

        var inlineResponse = await CallAsync(context, "motif_assess", Arguments(project.FwDataPath,
            ("words", JsonSerializer.SerializeToElement(words)),
            ("limit", JsonSerializer.SerializeToElement(words.Length))));
        var inlinePage = inlineResponse.GetProperty("result");
        Assert.Equal(words.Length, inlinePage.GetProperty("total").GetInt32());
        Assert.Equal(words, PageWords(inlinePage));
        Assert.True(JsonElement.DeepEquals(filePage.GetProperty("counts"), inlinePage.GetProperty("counts")));
    }

    [Fact]
    public async Task MotifTrialQueuesTheWordsFileAndEquivalentInlineSelection()
    {
        using var project = new WalkthroughProject(pristine);
        PrepareProject(project.FwDataPath);
        var capture = BaselineCaptureCommand.Capture(
            new BaselineCaptureRequest(project.FwDataPath), RunnerOptions.ResolveRoot());
        Assert.True(capture.Succeeded, capture.Refusal?.Message);

        var words = new[] { "motifa", "motifb" };
        var wordsFile = Path.Combine(project.ManagedRoot, "words.txt");
        File.WriteAllLines(wordsFile, words);
        var parser = RecordingParser(project, "trial-parser", words);
        var context = Context(project.FwDataPath, parser);

        var fileProposal = await StartDraftAsync(context, project.FwDataPath, "words-file-trial");
        var fileResponse = await CallAsync(context, "motif_trial", Arguments(project.FwDataPath,
            ("proposal", JsonSerializer.SerializeToElement(fileProposal)),
            ("wordsFile", JsonSerializer.SerializeToElement(wordsFile)),
            ("wait_seconds", JsonSerializer.SerializeToElement(1))));
        var fileJob = fileResponse.GetProperty("result").GetProperty("job").GetString()!;
        var fileInput = TrialJobInput.Parse(ReadJobInput(project.FwDataPath, fileJob));

        var inlineProposal = await StartDraftAsync(context, project.FwDataPath, "inline-words-trial");
        var inlineResponse = await CallAsync(context, "motif_trial", Arguments(project.FwDataPath,
            ("proposal", JsonSerializer.SerializeToElement(inlineProposal)),
            ("words", JsonSerializer.SerializeToElement(words)),
            ("wait_seconds", JsonSerializer.SerializeToElement(1))));
        var inlineJob = inlineResponse.GetProperty("result").GetProperty("job").GetString()!;
        var inlineInput = TrialJobInput.Parse(ReadJobInput(project.FwDataPath, inlineJob));

        Assert.Equal(words, fileInput.Words);
        Assert.Equal(words, inlineInput.Words);
        Assert.Equal(fileInput.Words, inlineInput.Words);
    }

    private static async Task<string> StartDraftAsync(ServerContext context, string projectPath, string name)
    {
        var response = await CallAsync(context, "motif_start_proposal", Arguments(projectPath,
            ("draft", JsonSerializer.SerializeToElement(name)),
            ("label", JsonSerializer.SerializeToElement("Measure a bounded word selection")),
            ("rationale", JsonSerializer.SerializeToElement("Keep the acceptance fixture small."))));
        return response.GetProperty("result").GetProperty("proposalId").GetString()!;
    }

    private static string ReadJobInput(string projectPath, string jobId)
    {
        var stored = ProjectStoreCommand.Run(projectPath, "1.0", (database, _) =>
            CommandOutcome<string>.Success(new JobRepository(database).Get(jobId)!.InputJson));
        Assert.True(stored.Succeeded, stored.Refusal?.Message);
        return stored.Value!;
    }

    private static ServerContext Context(string projectPath, string parser)
    {
        var runner = new NoRunnerLauncher(new JobRunnerLaunchOptions(RunnerOptions.ResolveRoot(), parser));
        return new ServerContext(projectPath, "1.0", runner, new ActivityLog(null), ToolProfile.Builtin,
            TextWriter.Null, advancedAiModeEnabled: true, parserPath: parser);
    }

    private static void PrepareProject(string projectPath)
    {
        var opened = ProposalCommands.Open(new OpenRequest(projectPath));
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        KnownProjectTestSetup.AssignUniqueTag(RunnerOptions.ResolveRoot(), projectPath,
            Path.GetFileNameWithoutExtension(projectPath));
    }

    private static string RecordingParser(WalkthroughProject project, string directoryName,
        IReadOnlyList<string> words)
    {
        var parser = FakeParser.CopyRecordingInvocations(Path.Combine(project.ManagedRoot, directoryName));
        FakeParser.BehaveBesideExecutable(parser, new
        {
            words = words.Select(word => new { word, outcome = "complete" }).ToArray(),
        });
        return parser;
    }

    private static async Task<JsonElement> CallAsync(ServerContext context, string tool,
        Dictionary<string, JsonElement> arguments)
    {
        var response = await MotifMcpServer.CallAsync(context, MotifMcpServer.Expose(ToolProfile.Builtin), tool,
            arguments, CancellationToken.None);
        Assert.True(response.IsError != true, $"MCP {tool} returned {response.StructuredContent?.ToString()}");
        var result = response.StructuredContent!.Value;
        Assert.True(result.GetProperty("ok").GetBoolean(), result.ToString());
        return result;
    }

    private static Dictionary<string, JsonElement> Arguments(string projectPath,
        params (string Name, JsonElement Value)[] values)
    {
        var arguments = new Dictionary<string, JsonElement>
        {
            ["project"] = JsonSerializer.SerializeToElement(projectPath),
        };
        foreach (var (name, value) in values) arguments[name] = value;
        return arguments;
    }

    private static string[] PageWords(JsonElement page) => page.GetProperty("words").EnumerateArray()
        .Select(word => word.GetProperty("word").GetString()!).ToArray();
}
