using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.LCModel;
using System.Text.RegularExpressions;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Records;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Mcp;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.TextEditing.Markdown.Records;
using Xunit;
using static SIL.Motif.Tests.TestFixtures.ImportFamilyScenarios;

namespace SIL.Motif.Tests.Mcp;

/// <summary>
/// The <c>motif_import_record</c> tool, called through the server's tool-call path, for every kind of the custom-field,
/// sense status and type, sense reference-set and Publish In families, with each result held against the CLI's answer
/// for the same file.
/// </summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class ImportFamiliesToolTests(PristineProjectFixture pristine)
{
    private sealed class Scenario : IDisposable
    {
        private readonly IDisposable _root;
        private readonly ToolProfile _profile;

        public Scenario(PristineProjectFixture pristine, Case? item, Action<RecordDocument, Seeded> edit)
        {
            using var cache = pristine.NewScratch();
            var seeded = ImportFamilyScenarios.Seed(cache);
            if (item is not null) Prepare(cache, seeded, item);
            Project = cache.ProjectId.Path;
            Folder = Path.GetDirectoryName(Project)!;
            File = WriteEditedFile(cache, seeded, Folder, "entry.md", edit);
            _root = PrivateManagedRoot.Use(Folder);
            var tag = "import-families-" + Guid.NewGuid().ToString("N");
            KnownProjectTestSetup.AssignUniqueTag(RunnerOptions.ResolveRoot(), Project, tag);
            _profile = new ToolProfile(tag, null, null, "concise", [], AllTools: true);
        }

        public string Project { get; }

        public string Folder { get; }

        public string File { get; }

        public void Dispose() => _root.Dispose();

        public string Hash() => Convert.ToHexString(SHA256.HashData(System.IO.File.ReadAllBytes(Project)));

        public async Task<(bool Error, string Text, JsonNode? Result)> Tool(string? draft = null, string? cursor = null, bool strict = false)
        {
            var arguments = new Dictionary<string, JsonElement>
            {
                ["project"] = JsonSerializer.SerializeToElement(Project),
                ["file"] = JsonSerializer.SerializeToElement(File),
            };
            if (draft is not null) arguments["draft"] = JsonSerializer.SerializeToElement(draft);
            if (cursor is not null) arguments["cursor"] = JsonSerializer.SerializeToElement(cursor);
            if (strict) arguments["strict"] = JsonSerializer.SerializeToElement(true);
            var context = new ServerContext(Project, "0.1.0",
                new NoRunnerLauncher(new JobRunnerLaunchOptions(RunnerOptions.ResolveRoot(), null)),
                new ActivityLog(), _profile, TextWriter.Null, advancedAiModeEnabled: true);
            var called = await MotifMcpServer.CallAsync(context, MotifMcpServer.Expose(_profile), "motif_import_record",
                arguments, CancellationToken.None);
            var text = called.StructuredContent?.GetRawText() ?? "";
            return (called.IsError == true, text, called.IsError == true || called.StructuredContent is null
                ? null : JsonNode.Parse(text)!["result"]);
        }

        public CliProcessResult Cli(params string[] arguments) =>
            CliInProcess.Run(RunnerOptions.ResolveRoot(), FakeParser.ExecutablePath, true, [.. arguments]);

        public CliProcessResult Import(string? draft = null, string? cursor = null, bool strict = false)
        {
            var arguments = new List<string> { "import", "--project", Project, "--file", File, "--json" };
            if (draft is not null) arguments.AddRange(["--draft", draft]);
            if (cursor is not null) arguments.AddRange(["--cursor", cursor]);
            if (strict) arguments.Add("--strict");
            return Cli([.. arguments]);
        }
    }

    public static TheoryData<string> CaseKinds()
    {
        var data = new TheoryData<string>();
        foreach (var item in Cases) data.Add(item.Kind);
        return data;
    }

    private static string[] Kinds(JsonNode node) =>
        node["operations"]!.AsArray().Select(item => item!["kind"]!.GetValue<string>()).ToArray();

    [Theory]
    [MemberData(nameof(CaseKinds))]
    public async Task ADryRunOfAnEditListsExactlyItsKindAndAgreesWithTheCli(string kind)
    {
        var item = Assert.Single(Cases, c => c.Kind == kind);
        using var scenario = new Scenario(pristine, item, item.Edit);
        var before = scenario.Hash();

        var called = await scenario.Tool();
        Assert.False(called.Error, called.Text);
        var cli = scenario.Import();
        Assert.True(cli.ExitCode == 0, cli.Error + cli.Output);
        var cliNode = JsonNode.Parse(cli.Output)!;
        Assert.Equal("dryRun", called.Result!["mode"]!.GetValue<string>());
        Assert.Equal(1, called.Result["operationCount"]!.GetValue<int>());
        Assert.Equal(0, called.Result["refusalCount"]!.GetValue<int>());
        Assert.Equal([kind], Kinds(called.Result));
        Assert.Equal("staged", Assert.Single(called.Result["changes"]!.AsArray())!["status"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(cliNode, called.Result), cli.Output + "\n" + called.Text);
        Assert.Equal(before, scenario.Hash());
    }

    [Fact]
    public async Task ADryRunOfEveryAdditiveEditPagesThroughAllOfThemAndEachPageMatchesTheCli()
    {
        using var scenario = new Scenario(pristine, null, EditAllAdditive);
        var before = scenario.Hash();
        var expected = Additive.Select(item => item.Kind).Order(StringComparer.Ordinal).ToArray();
        var pages = (expected.Length + RecordImportPaging.PageSize - 1) / RecordImportPaging.PageSize;
        Assert.True(pages > 2, "the file must need more than two pages");

        var seen = new List<string>();
        string? cursor = null;
        for (var page = 1; page <= pages; page++)
        {
            var called = await scenario.Tool(cursor: cursor);
            Assert.False(called.Error, called.Text);
            Assert.Equal(page < pages, called.Result!["nextCursor"] is not null);
            Assert.Equal(expected.Length, called.Result["operationCount"]!.GetValue<int>());
            seen.AddRange(Kinds(called.Result));
            var cli = scenario.Import(cursor: cursor);
            Assert.True(cli.ExitCode == 0, cli.Error + cli.Output);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(cli.Output), called.Result), cli.Output + "\n" + called.Text);
            cursor = called.Result["nextCursor"]?.GetValue<string>();
        }
        Assert.Null(cursor);

        Assert.Equal(expected, seen.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(before, scenario.Hash());
    }

    [Fact]
    public async Task AMalformedCursorIsTheSameTypedRefusalAsTheCli()
    {
        using var scenario = new Scenario(pristine, null, EditAllAdditive);

        var called = await scenario.Tool(cursor: "9");
        var cli = scenario.Import(cursor: "9");

        Assert.True(called.Error, called.Text);
        Assert.Contains(RefusalCodes.PagingInvalid, called.Text, StringComparison.Ordinal);
        Assert.NotEqual(0, cli.ExitCode);
        Assert.Equal(RefusalCodes.PagingInvalid, ProjectionJson.Deserialize<FailureEnvelope>(cli.Error)!.Code);
    }

    [Fact]
    public async Task ADraftOfEveryAdditiveEditStagesOneProposalAndMatchesTheCliCounts()
    {
        using var scenario = new Scenario(pristine, null, EditAllAdditive);
        var before = scenario.Hash();

        var called = await scenario.Tool(draft: "mcp-families");
        Assert.False(called.Error, called.Text);
        Assert.Equal("draft", called.Result!["mode"]!.GetValue<string>());
        Assert.Equal(AdditiveCount, called.Result["operationCount"]!.GetValue<int>());
        var listed = ProposalCommands.List(new(scenario.Project, "0.1.0"));
        Assert.True(listed.Succeeded, listed.Refusal?.Message);
        var staged = Assert.Single(listed.Value!.Proposals);
        Assert.Equal(called.Result["proposalId"]!.GetValue<string>(), staged.ProposalId);
        var shown = ProposalCommands.Show(new(scenario.Project, "0.1.0", staged.ProposalId, false));
        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        Assert.Equal(Additive.Select(item => item.Kind).Order(StringComparer.Ordinal).ToArray(),
            shown.Value!.Operations.Select(operation => operation.Kind).Order(StringComparer.Ordinal).ToArray());

        var cli = scenario.Import(draft: "cli-families");
        Assert.True(cli.ExitCode == 0, cli.Error + cli.Output);
        var cliNode = JsonNode.Parse(cli.Output)!;
        Assert.Equal(called.Result["operationCount"]!.GetValue<int>(), cliNode["operationCount"]!.GetValue<int>());
        Assert.Equal(called.Result["refusalCount"]!.GetValue<int>(), cliNode["refusalCount"]!.GetValue<int>());
        Assert.Equal(before, scenario.Hash());
    }

    [Fact]
    public async Task ALoneRefusedChangeIsListedWithTheCliCodeAndStagesNothing()
    {
        using var scenario = new Scenario(pristine, null, EditWrongList);
        var before = scenario.Hash();

        var dry = await scenario.Tool();
        Assert.False(dry.Error, dry.Text);
        Assert.Equal(0, dry.Result!["operationCount"]!.GetValue<int>());
        Assert.Equal(1, dry.Result["refusalCount"]!.GetValue<int>());
        var code = dry.Result["refusals"]![0]!["code"]!.GetValue<string>();
        Assert.Equal(RefusalCodes.RecordImportChangeRefused, code);
        var cli = scenario.Import();
        Assert.True(cli.ExitCode == 0, cli.Error + cli.Output);
        Assert.Equal(code, JsonNode.Parse(cli.Output)!["refusals"]![0]!["code"]!.GetValue<string>());
        Assert.Equal("refused", dry.Result["changes"]![0]!["status"]!.GetValue<string>());
        Assert.Equal(code, dry.Result["changes"]![0]!["refusalCode"]!.GetValue<string>());

        var staged = await scenario.Tool(draft: "refused");
        Assert.True(staged.Error, staged.Text);
        Assert.Contains(RefusalCodes.RecordImportNothingToStage, staged.Text, StringComparison.Ordinal);
        var cliStaged = scenario.Import(draft: "refused-cli");
        Assert.NotEqual(0, cliStaged.ExitCode);
        Assert.Equal(RefusalCodes.RecordImportNothingToStage, ProjectionJson.Deserialize<FailureEnvelope>(cliStaged.Error)!.Code);
        var listed = ProposalCommands.List(new(scenario.Project, "0.1.0"));
        Assert.Empty(listed.Value!.Proposals);
        Assert.Equal(before, scenario.Hash());
    }

    [Fact]
    public async Task StrictWithARefusedChangeIsTheCliRefusalAndStagesNothing()
    {
        using var scenario = new Scenario(pristine, null, (d, s) =>
        {
            EditWrongList(d, s);
            Assert.Single(Additive, item => item.Kind == "customField/setInteger").Edit(d, s);
        });
        var before = scenario.Hash();

        var called = await scenario.Tool(draft: "strict", strict: true);
        var cli = scenario.Import(draft: "strict-cli", strict: true);

        Assert.True(called.Error, called.Text);
        Assert.Contains(RefusalCodes.RecordImportStrictRefused, called.Text, StringComparison.Ordinal);
        Assert.NotEqual(0, cli.ExitCode);
        Assert.Equal(RefusalCodes.RecordImportStrictRefused, ProjectionJson.Deserialize<FailureEnvelope>(cli.Error)!.Code);
        Assert.Empty(ProposalCommands.List(new(scenario.Project, "0.1.0")).Value!.Proposals);
        Assert.Equal(before, scenario.Hash());
    }

    [Fact]
    public async Task StrictWithoutADraftIsTheCliUsageRefusal()
    {
        using var scenario = new Scenario(pristine, null, EditWrongList);

        var called = await scenario.Tool(strict: true);
        var cli = scenario.Import(strict: true);

        Assert.True(called.Error, called.Text);
        Assert.Contains(RefusalCodes.RecordImportStrictNeedsDraft, called.Text, StringComparison.Ordinal);
        Assert.NotEqual(0, cli.ExitCode);
    }

    [Fact]
    public void EveryCatalogKindOfTheFamiliesHasExactlyOneCase()
    {
        OperationRegistryBootstrap.Initialize();
        var family = new Regex(@"^(customField/.+|lexical/(lexSense|lexExampleSentence)/(setStatus|clearStatus|setSenseType|clearSenseType|addRef\w+|removeRef\w+))$");
        var kinds = OperationHandlerRegistry.RegisteredKinds
            .Where(kind => SIL.Motif.Contract.Parsing.OperationKindRegistry.IsKnown(kind) && family.IsMatch(kind))
            .Where(kind => OperationHandlerRegistry.FieldWrites(kind).Any(write => RecordLoweringRules.Lowers(kind, write)))
            .Order(StringComparer.Ordinal).ToArray();

        Assert.Empty(kinds.Except(Cases.Select(item => item.Kind)).Order());
        Assert.Empty(Cases.Select(item => item.Kind).Except(kinds).Order());
    }
}
