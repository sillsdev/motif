using SIL.Motif.Cli;
using SIL.Motif.Contract.HandAnalyses;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class HandAnalysisArgvTests(PristineProjectFixture pristine)
{
    [Fact]
    public void MalformedFileReturnsPointerErrorsBeforeOpeningProject()
    {
        var path = Path.GetTempFileName();
        try
        {
            var input = System.Text.Json.Nodes.JsonNode.Parse(HandAnalysisJson.Serialize(HandAnalysisTestInput.Create(Guid.NewGuid())))!;
            input["wordform"]!["unexpected"] = true;
            File.WriteAllText(path, input.ToJsonString());
            var output = new StringWriter();
            var error = new StringWriter();
            var code = SIL.Motif.Cli.Program.Run(["hand-analysis", "check", "--project", path + ".fwdata", "--input", path, "--json"],
                output, error, new Dictionary<string, string?>(), (_, _) => { });
            Assert.Equal(1, code);
            var failure = ProjectionJson.Deserialize<FailureEnvelope>(error.ToString());
            Assert.Equal("hand-analysis.invalid-link", failure!.Code);
            Assert.Contains("/wordform/unexpected", string.Join(" ", failure.Detail!.Keys));
        }
        finally { File.Delete(path); }
    }
    [Theory]
    [InlineData("approve")]
    [InlineData("create-lexical")]
    [InlineData("clear")]
    [InlineData("elsewhere")]
    [InlineData("approve-elsewhere")]
    [InlineData("show")]
    public void OnlyCheckIsExposed(string action)
    {
        Assert.DoesNotContain(SIL.Motif.Commands.Catalog.CommandCatalog.All, command => command.Name == "hand-analysis " + action);
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.NotEqual(0, SIL.Motif.Cli.Program.Run(["hand-analysis", action, "--project", "absent.fwdata", "--json"],
            output, error, new Dictionary<string, string?>(), (_, _) => { }));
        var check = Assert.Single(SIL.Motif.Commands.Catalog.CommandCatalog.All, command => command.Name == "hand-analysis check");
        Assert.Equal(SIL.Motif.Commands.Catalog.AgentClass.Read, check.Agent);
        Assert.False(check.ExposeAsAgentTool);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrMalformedJsonFileIsAnArgumentFailure(bool malformed)
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");
        try
        {
            if (malformed) File.WriteAllText(file, "{");
            var output = new StringWriter();
            var error = new StringWriter();
            var code = SIL.Motif.Cli.Program.Run(["hand-analysis", "check", "--project", "absent.fwdata", "--input", file, "--json"],
                output, error, new Dictionary<string, string?>(), (_, _) => { });
            Assert.Equal(1, code);
            Assert.Equal("hand-analysis.invalid-link", ProjectionJson.Deserialize<FailureEnvelope>(error.ToString())!.Code);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task ValidJsonFileMatchesSharedCheckWithoutChangingProjectFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-hand-analysis-cli", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var project = pristine.CopyProjectFile();
            Guid projectId;
            using (var cache = new SIL.Motif.Host.LcmUtils.FwDataProjectLoader().LoadScratchCache(project)) projectId = cache.LangProject.Guid;
            var json = HandAnalysisJson.Serialize(HandAnalysisTestInput.Create(projectId));
            var input = Path.Combine(root, "hand input.json");
            File.WriteAllText(input, json);
            var bytes = File.ReadAllBytes(project);
            var start = CliProcess.CreateStartInfo(root, null, false,
                "hand-analysis", "check", "--project", project, "--input", input, "--json");
            var cli = await CliProcess.RunAsync(start);
            Assert.True(cli.ExitCode == 0, cli.Error + cli.Output);
            var expected = SIL.Motif.Commands.HandAnalysisCommands.Check(new HandAnalysisCheckRequest(project, json));
            Assert.True(expected.Succeeded, expected.Refusal?.Message);
            var actual = ProjectionJson.Deserialize<HandAnalysisCheckResponse>(cli.Output)!;
            Assert.Equal(ProjectionJson.Serialize(expected.Value), ProjectionJson.Serialize(actual));
            Assert.False(actual.Changed);
            Assert.False(actual.HomeGuardsChecked);
            Assert.Equal(bytes, File.ReadAllBytes(project));
            Assert.False(File.Exists(Path.ChangeExtension(project, ".motif.db")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("missing", "project.not-found", FailureReason.InvalidArgument)]
    [InlineData("invalid", "project.invalid", FailureReason.InvalidArgument)]
    [InlineData("saving", "change.project-saving", FailureReason.Busy)]
    public void ProjectFailuresMatchThePublishedFailureSchema(string scenario, string expectedCode, FailureReason reason)
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-hand-analysis-failures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var project = scenario == "invalid" ? "\0" : Path.Combine(root, "absent.fwdata");
            if (scenario == "saving") File.WriteAllText(project, "incomplete saved file");
            var input = Path.Combine(root, "input.json");
            File.WriteAllText(input, HandAnalysisJson.Serialize(HandAnalysisTestInput.Create(Guid.NewGuid())));
            var output = new StringWriter();
            var error = new StringWriter();
            var exit = SIL.Motif.Cli.Program.Run(["hand-analysis", "check", "--project", project, "--input", input, "--json"],
                output, error, new Dictionary<string, string?>(), (_, _) => { });
            Assert.Equal(FailureEnvelope.ExitCodeFor(reason), exit);
            var failure = AssertPublishedFailure(error.ToString(), expectedCode, reason);
            Assert.Equal(project, failure.Detail!["fwDataPath"]);
            if (scenario != "saving") Assert.False(string.IsNullOrWhiteSpace(failure.Detail["exceptionDetail"]));
            Assert.Equal("", output.ToString());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task WritingSystemLoadFailureMatchesThePublishedFailureSchema()
    {
        var project = pristine.CopyProjectFile();
        var input = Path.Combine(Path.GetDirectoryName(project)!, "hand-input.json");
        File.WriteAllText(input, HandAnalysisJson.Serialize(HandAnalysisTestInput.Create(Guid.NewGuid())));
        var bytes = File.ReadAllBytes(project);
        var start = CliProcess.CreateStartInfo(Path.GetDirectoryName(project)!, null, false,
            "hand-analysis", "check", "--project", project, "--input", input, "--json");
        start.Environment["MOTIF_TEST_FAIL_WRITING_SYSTEM_FOR"] = Path.GetFileName(project);
        var result = await CliProcess.RunAsync(start);
        Assert.True(result.ExitCode == 2, result.Error + result.Output);
        var failure = AssertPublishedFailure(result.Error, "project.writing-system-missing", FailureReason.Refused);
        Assert.Equal(Path.Combine(Path.GetDirectoryName(project)!, "WritingSystemStore"), failure.Detail!["projectWritingSystemStore"]);
        Assert.Equal(bytes, File.ReadAllBytes(project));
        Assert.False(File.Exists(Path.ChangeExtension(project, ".motif.db")));
    }

    private static FailureEnvelope AssertPublishedFailure(string json, string code, FailureReason reason)
    {
        var failure = ProjectionJson.Deserialize<FailureEnvelope>(json)!;
        Assert.Equal(code, failure.Code);
        Assert.Equal(reason, failure.Reason);
        using var stream = typeof(FailureEnvelope).Assembly.GetManifestResourceStream("SIL.Motif.Contract.Schemas.hand-analysis.schema.json")!;
        var definition = System.Text.Json.Nodes.JsonNode.Parse(stream)!;
        definition["$ref"] = "#/$defs/FailureEnvelope";
        var schema = Json.Schema.JsonSchema.FromText(definition.ToJsonString(),
            new Json.Schema.BuildOptions { SchemaRegistry = new Json.Schema.SchemaRegistry() });
        Assert.True(schema.Evaluate(System.Text.Json.JsonSerializer.SerializeToElement(
            System.Text.Json.Nodes.JsonNode.Parse(json))).IsValid, json);
        return failure;
    }

}
