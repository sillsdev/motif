using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SIL.LCModel;
using SIL.Motif.Commands.Records;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.TestFixtures.ImportFamilyScenarios;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// The <c>import</c> verb through the in-process CLI for every kind of the custom-field, sense status and type, sense
/// reference-set and Publish In families: a dry run, a paged listing, a staged Draft read back with <c>show</c>, and a
/// file whose only change is refused.
/// </summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class ImportFamiliesCliTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-import-families-cli-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (IOException) { }
    }

    private CliProcessResult Run(params string[] arguments) =>
        CliInProcess.Run(Path.Combine(_root, "machine"), FakeParser.ExecutablePath, true, arguments);

    private (string Project, string File) Project(Case? item, Action<SIL.TextEditing.Markdown.Records.RecordDocument, Seeded> edit)
    {
        using var cache = pristine.NewScratch();
        var seeded = Seed(cache);
        if (item is not null) Prepare(cache, seeded, item);
        var file = WriteEditedFile(cache, seeded, Path.Combine(_root, "files"), "entry.md", edit);
        return (cache.ProjectId.Path, file);
    }

    private static string Hash(string project) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(project)));

    private static string CodeOf(CliProcessResult result)
    {
        var line = (result.Error + "\n" + result.Output).Split('\n').FirstOrDefault(item => item.TrimStart().StartsWith('{'));
        Assert.True(line is not null, "no failure envelope in: " + result.Error + result.Output);
        return ProjectionJson.Deserialize<FailureEnvelope>(line!)!.Code;
    }

    public static TheoryData<string> CaseKinds()
    {
        var data = new TheoryData<string>();
        foreach (var item in Cases) data.Add(item.Kind);
        return data;
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
        Assert.Equal(Cases.Count, Cases.Select(item => item.Kind).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(CaseKinds))]
    public void ADryRunOfAnEditListsExactlyItsKindAndWritesNothing(string kind)
    {
        var item = Assert.Single(Cases, c => c.Kind == kind);
        var (project, file) = Project(item, item.Edit);
        var before = Hash(project);

        var json = Run("import", "--project", project, "--file", file, "--json");
        Assert.True(json.ExitCode == 0, json.Error + json.Output);
        var node = JsonNode.Parse(json.Output)!;
        Assert.Equal("dryRun", node["mode"]!.GetValue<string>());
        Assert.Equal(0, node["refusalCount"]!.GetValue<int>());
        Assert.Equal(1, node["operationCount"]!.GetValue<int>());
        Assert.Equal(kind, Assert.Single(node["operations"]!.AsArray())!["kind"]!.GetValue<string>());
        Assert.Equal("staged", Assert.Single(node["changes"]!.AsArray())!["status"]!.GetValue<string>());

        var text = Run("import", "--project", project, "--file", file);
        Assert.True(text.ExitCode == 0, text.Error + text.Output);
        Assert.Contains("Dry run of the", text.Output, StringComparison.Ordinal);
        Assert.Contains("1 operation(s). Nothing was written.", text.Output, StringComparison.Ordinal);
        Assert.Contains("  " + kind + "  ", text.Output, StringComparison.Ordinal);
        Assert.Equal(before, Hash(project));
    }

    [Fact]
    public void ADryRunOfEveryAdditiveEditPagesThroughAllOfThemAndWritesNothing()
    {
        var (project, file) = Project(null, EditAllAdditive);
        var before = Hash(project);
        var expected = Additive.Select(item => item.Kind).Order(StringComparer.Ordinal).ToArray();
        var pages = (expected.Length + RecordImportPaging.PageSize - 1) / RecordImportPaging.PageSize;
        Assert.True(pages > 2, "the file must need more than two pages");

        var seen = new List<string>();
        var ids = new HashSet<string>();
        for (var page = 1; page <= pages; page++)
        {
            var json = Run("import", "--project", project, "--file", file, "--page", page.ToString(), "--json");
            Assert.True(json.ExitCode == 0, json.Error + json.Output);
            var node = JsonNode.Parse(json.Output)!;
            Assert.Equal(page, node["page"]!.GetValue<int>());
            Assert.Equal(pages, node["pageCount"]!.GetValue<int>());
            Assert.Equal(expected.Length, node["operationCount"]!.GetValue<int>());
            Assert.Equal(0, node["refusalCount"]!.GetValue<int>());
            foreach (var operation in node["operations"]!.AsArray())
            {
                seen.Add(operation!["kind"]!.GetValue<string>());
                Assert.True(ids.Add(operation["operationId"]!.GetValue<string>()), "an operation listed twice across pages");
            }
            Assert.Equal(page == pages ? expected.Length - (pages - 1) * RecordImportPaging.PageSize : RecordImportPaging.PageSize,
                node["operations"]!.AsArray().Count);

            var text = Run("import", "--project", project, "--file", file, "--page", page.ToString());
            Assert.True(text.ExitCode == 0, text.Error + text.Output);
            Assert.Contains($"page {page} of {pages}", text.Output, StringComparison.Ordinal);
            Assert.Equal(page < pages, text.Output.Contains($"More: --page {page + 1}", StringComparison.Ordinal));
        }

        Assert.Equal(expected, seen.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(before, Hash(project));
    }

    [Fact]
    public void APagePastTheEndIsATypedRefusalWithANonZeroExit()
    {
        var (project, file) = Project(null, EditAllAdditive);
        var pages = (AdditiveCount + RecordImportPaging.PageSize - 1) / RecordImportPaging.PageSize;

        var result = Run("import", "--project", project, "--file", file, "--page", (pages + 1).ToString(), "--json");

        Assert.NotEqual(0, result.ExitCode);
        var envelope = ProjectionJson.Deserialize<FailureEnvelope>(result.Error)!;
        Assert.Equal(RefusalCodes.RecordImportInvalidPage, envelope.Code);
        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.InvalidArgument), result.ExitCode);
    }

    [Fact]
    public void ADraftOfEveryAdditiveEditStagesOneProposalThatShowReadsBack()
    {
        var (project, file) = Project(null, EditAllAdditive);
        var before = Hash(project);

        var staged = Run("import", "--project", project, "--file", file, "--draft", "families", "--json");
        Assert.True(staged.ExitCode == 0, staged.Error + staged.Output);
        var node = JsonNode.Parse(staged.Output)!;
        Assert.Equal("draft", node["mode"]!.GetValue<string>());
        Assert.Equal(AdditiveCount, node["operationCount"]!.GetValue<int>());
        var proposalId = node["proposalId"]!.GetValue<string>();

        var listed = Run("proposals", "--project", project, "--json");
        Assert.True(listed.ExitCode == 0, listed.Error);
        Assert.Equal(proposalId, Assert.Single(JsonNode.Parse(listed.Output)!["proposals"]!.AsArray())!["proposalId"]!.GetValue<string>());

        var shown = Run("show", proposalId, "--project", project, "--all", "--json");
        Assert.True(shown.ExitCode == 0, shown.Error + shown.Output);
        var kinds = JsonNode.Parse(shown.Output)!["operations"]!.AsArray().Select(item => item!["kind"]!.GetValue<string>())
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(Additive.Select(item => item.Kind).Order(StringComparer.Ordinal).ToArray(), kinds);

        var text = Run("import", "--project", project, "--file", file, "--draft", "families-text");
        Assert.True(text.ExitCode == 0, text.Error + text.Output);
        Assert.Contains($"Staged {AdditiveCount} operation(s) from the", text.Output, StringComparison.Ordinal);
        Assert.Contains("Draft families-text", text.Output, StringComparison.Ordinal);
        Assert.Contains("Nothing was applied.", text.Output, StringComparison.Ordinal);
        Assert.Equal(before, Hash(project));
    }

    [Fact]
    public void ARefusedChangeIsListedWithItsCodeAndALoneRefusedChangeStagesNothing()
    {
        var (project, file) = Project(null, EditWrongList);
        var before = Hash(project);

        var dry = Run("import", "--project", project, "--file", file, "--json");
        Assert.True(dry.ExitCode == 0, dry.Error + dry.Output);
        var node = JsonNode.Parse(dry.Output)!;
        Assert.Equal(0, node["operationCount"]!.GetValue<int>());
        Assert.Equal(1, node["refusalCount"]!.GetValue<int>());
        Assert.Equal(RefusalCodes.RecordImportChangeRefused,
            Assert.Single(node["refusals"]!.AsArray())!["code"]!.GetValue<string>());
        var change = Assert.Single(node["changes"]!.AsArray())!;
        Assert.Equal("refused", change["status"]!.GetValue<string>());
        Assert.Equal(RefusalCodes.RecordImportChangeRefused, change["refusalCode"]!.GetValue<string>());
        var text = Run("import", "--project", project, "--file", file);
        Assert.Contains("REFUSED " + RefusalCodes.RecordImportChangeRefused, text.Output, StringComparison.Ordinal);
        Assert.Contains("refused " + RefusalCodes.RecordImportChangeRefused, text.Output, StringComparison.Ordinal);

        var staged = Run("import", "--project", project, "--file", file, "--draft", "refused", "--json");
        Assert.NotEqual(0, staged.ExitCode);
        Assert.Equal(RefusalCodes.RecordImportNothingToStage, ProjectionJson.Deserialize<FailureEnvelope>(staged.Error)!.Code);
        var listed = Run("proposals", "--project", project, "--json");
        Assert.Empty(JsonNode.Parse(listed.Output)!["proposals"]!.AsArray());
        Assert.Equal(before, Hash(project));
    }

    [Fact]
    public void ARefusedChangeBesideAnAcceptedOneStagesOnlyTheAcceptedOne()
    {
        var (project, file) = Project(null, (d, s) =>
        {
            EditWrongList(d, s);
            Assert.Single(Additive, item => item.Kind == "customField/setInteger").Edit(d, s);
        });

        var staged = Run("import", "--project", project, "--file", file, "--draft", "mixed", "--json");

        Assert.True(staged.ExitCode == 0, staged.Error + staged.Output);
        var node = JsonNode.Parse(staged.Output)!;
        Assert.Equal(1, node["operationCount"]!.GetValue<int>());
        Assert.Equal(1, node["refusalCount"]!.GetValue<int>());
        Assert.Equal(RefusalCodes.RecordImportChangeRefused, node["refusals"]![0]!["code"]!.GetValue<string>());
        var text = Run("import", "--project", project, "--file", file, "--draft", "mixed-text");
        Assert.True(text.ExitCode == 0, text.Error + text.Output);
        Assert.Contains("1 change(s) refused and not staged", text.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void StrictWithARefusedChangeExitsNonZeroAndStagesNothingWhetherAloneOrBesideAnAcceptedOne()
    {
        foreach (var edit in new Action<SIL.TextEditing.Markdown.Records.RecordDocument, Seeded>[]
        {
            EditWrongList,
            (d, s) =>
            {
                EditWrongList(d, s);
                Assert.Single(Additive, item => item.Kind == "customField/setInteger").Edit(d, s);
            },
        })
        {
            var (project, file) = Project(null, edit);
            var before = Hash(project);

            var json = Run("import", "--project", project, "--file", file, "--draft", "strict", "--strict", "--json");
            Assert.NotEqual(0, json.ExitCode);
            Assert.Equal(RefusalCodes.RecordImportStrictRefused, CodeOf(json));
            Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.Refused), json.ExitCode);
            var text = Run("import", "--project", project, "--file", file, "--draft", "strict", "--strict");
            Assert.NotEqual(0, text.ExitCode);
            Assert.Contains("Strict mode", text.Error + text.Output, StringComparison.Ordinal);

            var listed = Run("proposals", "--project", project, "--json");
            Assert.Empty(JsonNode.Parse(listed.Output)!["proposals"]!.AsArray());
            Assert.Equal(before, Hash(project));
        }
    }

    [Fact]
    public void StrictWithNothingRefusedStagesTheDraft()
    {
        var (project, file) = Project(null, EditAllAdditive);

        var staged = Run("import", "--project", project, "--file", file, "--draft", "strict-clean", "--strict", "--json");

        Assert.True(staged.ExitCode == 0, staged.Error + staged.Output);
        Assert.Equal(AdditiveCount, JsonNode.Parse(staged.Output)!["operationCount"]!.GetValue<int>());
    }

    [Fact]
    public void StrictWithoutADraftIsAUsageError()
    {
        var (project, file) = Project(null, EditWrongList);
        var before = Hash(project);

        var result = Run("import", "--project", project, "--file", file, "--strict");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("import --project <fwdata> --file <path>", result.Error + result.Output, StringComparison.Ordinal);
        Assert.Equal(before, Hash(project));
    }
}
