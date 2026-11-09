using SIL.LCModel;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Generator;
using SIL.Motif.Generator.Manifest;
using SIL.Motif.Host;
using SIL.Motif.Host.Gdr;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using SIL.Motif.Host.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// A person's paragraph edit of a rich-text field, staged in the pending-changes Draft and applied to a project
/// built at run time from <see cref="SeededGdrs"/> (ADR 0076), then read back through <c>gdr show</c>.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class TextParagraphsApplyTests(PristineProjectFixture pristine)
{
    private const string Discussion = "CmPossibility.Discussion";

    [Fact]
    public void AnEditedParagraphAppliesAndAnUntouchedParagraphKeepsItsStyledRunLinkAndObject()
    {
        var path = ProjectWithGdrs();
        var shown = Show(path, "GDR-1");
        var (first, second) = (shown.Discussion[0], shown.Discussion[1]);
        var ws = second.Runs[0].WritingSystem;

        Staged(path, shown.Record.Id, Edit([first.Key, second.Key],
            [new TextParagraphChange(1, second.Key, "Normal", [new TextRun("ar-text more", ws)])],
            [], new() { [second.Key!] = "ar-text" }));
        Applied(path);

        var after = Show(path, "GDR-1");
        Assert.Equal([first.Key, second.Key], after.Discussion.Select(paragraph => paragraph.Key));
        Assert.Equal("ar-text more", string.Concat(after.Discussion[1].Runs.Select(run => run.Text)));
        Assert.Equal(Runs(first), Runs(after.Discussion[0]));
        Assert.Contains(after.Discussion[0].Runs, run => run.Style == "Emphasis");
        Assert.Equal(SeededGdrs.HyperlinkData, after.Discussion[0].Runs.Single(run => run.Text == "link").ObjData);
        Assert.Equal(SeededGdrs.ObjectData, after.Discussion[0].Runs.Single(run => run.Text == "￼").ObjData);
    }

    [Fact]
    public void ANewParagraphGetsAKeyAndAJoinRemovesIt()
    {
        var path = ProjectWithGdrs();
        var shown = Show(path, "GDR-1a");
        var only = Assert.Single(shown.Discussion);
        var ws = only.Runs[0].WritingSystem;

        Staged(path, shown.Record.Id, Edit([only.Key, null],
            [new TextParagraphChange(1, null, "Normal", [new TextRun("w", ws)])], [], []));
        Applied(path);
        var split = Show(path, "GDR-1a");
        Assert.Equal(["z", "w"], split.Discussion.Select(Text));
        Assert.Equal(only.Key, split.Discussion[0].Key);
        Assert.NotNull(split.Discussion[1].Key);

        Staged(path, shown.Record.Id, Edit([only.Key],
            [new TextParagraphChange(0, only.Key, "Normal", [new TextRun("zw", ws)])],
            [split.Discussion[1].Key!], new() { [only.Key!] = "z", [split.Discussion[1].Key!] = "w" }));
        Applied(path);

        var joined = Assert.Single(Show(path, "GDR-1a").Discussion);
        Assert.Equal((only.Key, "zw"), (joined.Key, Text(joined)));
    }

    [Fact]
    public void AFieldWithNoTextYetGetsOne()
    {
        var path = ProjectWithGdrs();
        var shown = Show(path, "GDR-2");
        Assert.Empty(shown.Discussion);

        Staged(path, shown.Record.Id, Edit([null],
            [new TextParagraphChange(0, null, "Normal", [new TextRun("first", shown.DefaultAnalysis)])], [], []));
        Applied(path);

        Assert.Equal("first", Text(Assert.Single(Show(path, "GDR-2").Discussion)));
    }

    [Fact]
    public void ALaterSessionReplacesTheFieldsEditAndAnEditBackToTheStartLeavesTheDraft()
    {
        var path = ProjectWithGdrs();
        var shown = Show(path, "GDR-1a");
        var only = shown.Discussion[0];
        var ws = only.Runs[0].WritingSystem;
        TextParagraphsPayload Typed(string text) => Edit([only.Key],
            [new TextParagraphChange(0, only.Key, "Normal", [new TextRun(text, ws)])], [], new() { [only.Key!] = "z" });

        Staged(path, shown.Record.Id, Typed("za"));
        var second = Staged(path, shown.Record.Id, Typed("zab"));

        Assert.Equal(1, second.OperationCount);
        Assert.Equal(TextParagraphsOperationKinds.Set, Assert.Single(second.Operations).Kind);
        Assert.Equal(0, Staged(path, shown.Record.Id, Edit([only.Key], [], [], [])).OperationCount);
    }

    [Fact]
    public void DriftSinceTheEditBeganRefusesThatFieldAndKeepsEveryEditInTheDraftUntilItIsEditedAgain()
    {
        var path = ProjectWithGdrs();
        var nested = Show(path, "GDR-1a");
        var record = Show(path, "GDR-1");
        var ws = nested.Discussion[0].Runs[0].WritingSystem;
        var second = record.Discussion[1];
        Staged(path, record.Record.Id, Edit([record.Discussion[0].Key, second.Key],
            [new TextParagraphChange(1, second.Key, "Normal", [new TextRun("changed", second.Runs[0].WritingSystem)])], [],
            new() { [second.Key!] = "ar-text" }));
        Staged(path, nested.Record.Id, Edit([nested.Discussion[0].Key],
            [new TextParagraphChange(0, nested.Discussion[0].Key, "Normal", [new TextRun("zz", ws)])], [],
            new() { [nested.Discussion[0].Key!] = "y" }));

        var refused = Apply(path);

        Assert.False(refused.Succeeded);
        Assert.Equal(RefusalCodes.TextFieldStale, refused.Refusal!.Code);
        Assert.Equal(nested.Record.Id, refused.Refusal.Facts!["owner"]);
        Assert.Equal(Discussion, refused.Refusal.Facts["field"]);
        Assert.Equal(2, PendingOperations(path));
        Assert.Equal("ar-text", Text(Show(path, "GDR-1").Discussion[1]));

        Staged(path, nested.Record.Id, Edit([nested.Discussion[0].Key],
            [new TextParagraphChange(0, nested.Discussion[0].Key, "Normal", [new TextRun("zz", ws)])], [],
            new() { [nested.Discussion[0].Key!] = "z" }));
        Applied(path);

        Assert.Equal("changed", Text(Show(path, "GDR-1").Discussion[1]));
        Assert.Equal("zz", Text(Assert.Single(Show(path, "GDR-1a").Discussion)));
    }

    [Fact]
    public void TheEditableFieldsAreTheInventorysStTextFieldsLessATextsContentsWithOneFieldPerClass()
    {
        var inventory = ManifestTsvParser.Parse(RepoPaths.DefaultManifestPath())
            .Where(row => row.Scope == "in" && row.Kind == "owning" && row.Sig == "StText" && row.Card == "atomic")
            .Select(row => $"{row.Class}.{row.Field}").ToArray();

        Assert.Contains("Text.Contents", inventory);
        Assert.Equal(inventory.Where(field => field != "Text.Contents").Order(), TextParagraphFields.Names.Order());
        Assert.Equal(TextParagraphFields.Names.Count, TextParagraphFields.Names.Select(name => name.Split('.')[0]).Distinct().Count());
    }

    [Theory]
    [InlineData("{\"field\":\"Text.Contents\",\"order\":[],\"changed\":[],\"removed\":[],\"expected\":{}}")]
    [InlineData("{\"field\":\"CmPossibility.Discussion\",\"order\":[null],\"changed\":[],\"removed\":[],\"expected\":{}}")]
    [InlineData("{\"field\":\"CmPossibility.Discussion\",\"order\":[\"KEY\"],\"changed\":[{\"index\":0,\"key\":\"KEY\",\"runs\":[]}],\"removed\":[],\"expected\":{}}")]
    [InlineData("{\"field\":\"CmPossibility.Discussion\",\"order\":[\"KEY\"],\"changed\":[],\"removed\":[\"KEY\"],\"expected\":{\"KEY\":\"x\"}}")]
    [InlineData("{\"field\":\"CmPossibility.Discussion\",\"order\":[],\"changed\":[],\"removed\":[],\"expected\":{},\"extra\":1}")]
    [InlineData("{\"field\":\"CmPossibility.Discussion\",\"order\":[null],\"changed\":[{\"index\":0,\"runs\":[{\"text\":\"\uFFFC\",\"ws\":\"en\"}]}],\"removed\":[],\"expected\":{}}")]
    public void AMalformedPayloadIsRefused(string payload)
    {
        using var document = System.Text.Json.JsonDocument.Parse(payload.Replace("KEY", CanonicalId.Mint().Value));

        Assert.ThrowsAny<SIL.Motif.Contract.Parsing.ContractParseException>(() => TextParagraphsPayload.Parse(document.RootElement));
    }

    private static TextParagraphsPayload Edit(IReadOnlyList<string?> order, IReadOnlyList<TextParagraphChange> changed,
        IReadOnlyList<string> removed, Dictionary<string, string> expected) => new(Discussion, order, changed, removed, expected);

    private static string Text(GdrParagraph paragraph) => string.Concat(paragraph.Runs.Select(run => run.Text));

    private static IEnumerable<(string, string, string?, string?)> Runs(GdrParagraph paragraph) =>
        paragraph.Runs.Select(run => (run.Text, run.WritingSystem, run.Style, run.ObjData));

    private static GdrShowResponse Show(string path, string id)
    {
        var shown = GdrQuery.Show(new GdrShowRequest(path, id));
        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        return shown.Value!;
    }

    private static ComposedOperationsResponse Staged(string path, string target, TextParagraphsPayload paragraphs)
    {
        var staged = ProposalCommands.StageTextParagraphs(new StageTextParagraphsRequest(path, target, paragraphs));
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        return staged.Value!;
    }

    private static CommandOutcome<ApplyPendingResult> Apply(string path)
    {
        var runner = new InProcessRunnerLauncher(IsolatedRunner.Options(RootFor(path)));
        try
        {
            return PendingChangesWorkflow.Apply(new ApplyPendingRequest(path, null, null),
                dryRunTimeout: TimeSpan.FromMinutes(2), runnerLauncher: runner);
        }
        finally
        {
            runner.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static void Applied(string path)
    {
        var applied = Apply(path);
        Assert.True(applied.Succeeded, applied.Refusal?.Code + ": " + applied.Refusal?.Message);
        Assert.True(applied.Value!.Applied, applied.Value.Summary);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), RootFor(path)).Succeeded);
    }

    private static int PendingOperations(string path) => ProjectStoreCommand.Run(path, MotifProductVersion.CurrentText,
        (database, _) => CommandOutcome<string>.Success(new ProposalRepository(database).FindPendingChangesDraft()!.ProposalJson!))
        .Value! is { } json ? System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("operations").GetArrayLength() : 0;

    private string ProjectWithGdrs()
    {
        using var cache = pristine.NewScratch();
        SeededGdrs.Seed(cache);
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), RootFor(path)).Succeeded);
        return path;
    }

    private static string RootFor(string projectPath)
    {
        var root = Path.Combine(Path.GetDirectoryName(projectPath)!, "text-paragraphs-root");
        Directory.CreateDirectory(root);
        return root;
    }
}
