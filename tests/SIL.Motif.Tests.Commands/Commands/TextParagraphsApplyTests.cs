using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
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
    public void AnEditedParagraphKeepsItsStyledRunLinkAndObjectAndAnUntouchedOneIsAsItWas()
    {
        var path = ProjectWithGdrs();
        var shown = Show(path, "GDR-1");
        var (first, second) = (shown.Discussion[0], shown.Discussion[1]);
        var typed = first.Runs.Select(Carried).ToArray();
        typed[0] = typed[0] with { Text = "x more" };

        Staged(path, shown.Record.Id, Edit([first.Key, second.Key],
            [new TextParagraphChange(0, first.Key, "Normal", typed, Props(first.Properties))], [], first));
        Applied(path);

        var after = Show(path, "GDR-1");
        Assert.Equal([first.Key, second.Key], after.Discussion.Select(paragraph => paragraph.Key));
        Assert.Equal("x more" + Text(first)[1..], Text(after.Discussion[0]));
        Assert.Equal(Runs(first).Skip(1), Runs(after.Discussion[0]).Skip(1));
        Assert.Equal("Emphasis", after.Discussion[0].Runs.Single(run => run.Text == "y").Style);
        Assert.Equal(SeededGdrs.HyperlinkData, after.Discussion[0].Runs.Single(run => run.Text == "link").ObjData);
        Assert.Equal(SeededGdrs.ObjectData, after.Discussion[0].Runs.Single(run => run.Text == "￼").ObjData);
        Assert.Equal(TextRunsDigest.Of(second), TextRunsDigest.Of(after.Discussion[1]));
        Assert.True(after.Discussion[1].RightToLeft);
    }

    [Fact]
    public void AnEditedRightToLeftParagraphStaysRightToLeftAndItsTextIsWrittenDecomposed()
    {
        var path = ProjectWithGdrs();
        var shown = Show(path, "GDR-1");
        var second = shown.Discussion[1];

        Staged(path, shown.Record.Id, Edit([shown.Discussion[0].Key, second.Key],
            [new TextParagraphChange(1, second.Key, "Normal", [Carried(second.Runs[0]) with { Text = "café" }],
                Props(second.Properties))], [], second));
        Applied(path);

        var after = Show(path, "GDR-1").Discussion[1];
        Assert.True(after.RightToLeft);
        Assert.Equal("café", Text(after));
    }

    [Fact]
    public void ANewParagraphGetsAKeyAndAJoinRemovesIt()
    {
        var path = ProjectWithGdrs();
        var shown = Show(path, "GDR-1a");
        var only = Assert.Single(shown.Discussion);
        var ws = only.Runs[0].WritingSystem;

        Staged(path, shown.Record.Id, Edit([only.Key, null],
            [new TextParagraphChange(1, null, "Normal", [new TextRun("w", ws)])], []));
        Applied(path);
        var split = Show(path, "GDR-1a");
        Assert.Equal(["z", "w"], split.Discussion.Select(Text));
        Assert.Equal(only.Key, split.Discussion[0].Key);
        Assert.NotNull(split.Discussion[1].Key);

        Staged(path, shown.Record.Id, Edit([only.Key],
            [new TextParagraphChange(0, only.Key, "Normal", [new TextRun("zw", ws)])],
            [split.Discussion[1].Key!], split.Discussion[0], split.Discussion[1]));
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
            [new TextParagraphChange(0, null, "Normal", [new TextRun("first", shown.DefaultAnalysis)])], []));
        Applied(path);

        Assert.Equal("first", Text(Assert.Single(Show(path, "GDR-2").Discussion)));
    }

    [Fact]
    public void ALaterSessionReplacesTheFieldsEditAndAnEditBackToTheStartLeavesTheDraft()
    {
        var path = ProjectWithGdrs();
        var shown = Show(path, "GDR-1a");
        var only = shown.Discussion[0];

        Staged(path, shown.Record.Id, Typed(only, "za"));
        var second = Staged(path, shown.Record.Id, Typed(only, "zab"));

        Assert.Equal(1, second.OperationCount);
        Assert.Equal(TextParagraphsOperationKinds.Set, Assert.Single(second.Operations).Kind);
        Assert.Equal(0, Staged(path, shown.Record.Id, Edit([only.Key], [], [])).OperationCount);
    }

    [Fact]
    public void AFieldWorksEditAfterStagingRefusesThatFieldAndKeepsEveryEditInTheDraftUntilItIsEditedAgain()
    {
        var path = ProjectWithGdrs();
        var nested = Show(path, "GDR-1a");
        var record = Show(path, "GDR-1");
        var second = record.Discussion[1];
        Staged(path, record.Record.Id, Edit([record.Discussion[0].Key, second.Key],
            [new TextParagraphChange(1, second.Key, "Normal", [new TextRun("changed", second.Runs[0].WritingSystem)])], [], second));
        Staged(path, nested.Record.Id, Typed(nested.Discussion[0], "zz"));
        InFieldWorks(path, "GDR-1a", (cache, text) =>
            ((IStTxtPara)text.ParagraphsOS[0]).Contents = TsStringUtils.MakeString("fw", cache.DefaultAnalWs));

        var refused = AppliedAfterRefresh(path);

        Assert.False(refused.Succeeded);
        Assert.Equal(RefusalCodes.TextFieldStale, refused.Refusal!.Code);
        Assert.Equal(nested.Record.Id, refused.Refusal.Facts!["owner"]);
        Assert.Equal(Discussion, refused.Refusal.Facts["field"]);
        Assert.Equal(2, PendingOperations(path));
        Assert.Equal("ar-text", Text(Show(path, "GDR-1").Discussion[1]));

        Staged(path, nested.Record.Id, Typed(Show(path, "GDR-1a").Discussion[0], "fw zz"));
        Applied(path);

        Assert.Equal("changed", Text(Show(path, "GDR-1").Discussion[1]));
        Assert.Equal("fw zz", Text(Assert.Single(Show(path, "GDR-1a").Discussion)));
    }

    public static TheoryData<string> FieldWorksChanges => ["format", "style", "add", "remove", "move"];

    [Theory]
    [MemberData(nameof(FieldWorksChanges))]
    public void AFieldWorksChangeToTheFieldAfterStagingIsDrift(string change)
    {
        var path = ProjectWithGdrs();
        var record = Show(path, "GDR-1");
        var second = record.Discussion[1];
        Staged(path, record.Record.Id, Edit([record.Discussion[0].Key, second.Key],
            [new TextParagraphChange(1, second.Key, "Normal", [new TextRun("changed", second.Runs[0].WritingSystem)])], [], second));
        InFieldWorks(path, "GDR-1", (cache, text) =>
        {
            var paragraphs = text.ParagraphsOS.Cast<IStTxtPara>().ToArray();
            switch (change)
            {
                case "format":
                    var bold = paragraphs[1].Contents.GetBldr();
                    bold.SetIntPropValues(0, 2, (int)FwTextPropType.ktptBold, (int)FwTextPropVar.ktpvEnum, 1);
                    paragraphs[1].Contents = bold.GetString();
                    break;
                case "style":
                    paragraphs[1].StyleName = "Emphasis";
                    break;
                case "add":
                    var added = cache.ServiceLocator.GetInstance<IStTxtParaFactory>().Create();
                    text.ParagraphsOS.Add(added);
                    added.Contents = TsStringUtils.MakeString("new", cache.DefaultAnalWs);
                    break;
                case "remove":
                    text.ParagraphsOS.Remove(paragraphs[0]);
                    break;
                case "move":
                    text.ParagraphsOS.MoveTo(1, 1, text.ParagraphsOS, 0);
                    break;
            }
        });

        var refused = AppliedAfterRefresh(path);

        Assert.False(refused.Succeeded);
        Assert.Equal(RefusalCodes.TextFieldStale, refused.Refusal!.Code);
    }

    [Fact]
    public void AKeyFromAnotherTextIsRefused()
    {
        var path = ProjectWithGdrs();
        var record = Show(path, "GDR-1");
        var foreign = Show(path, "GDR-1a").Discussion[0];
        Staged(path, record.Record.Id, Edit([record.Discussion[0].Key, record.Discussion[1].Key, foreign.Key],
            [new TextParagraphChange(2, foreign.Key, "Normal", [new TextRun("moved", foreign.Runs[0].WritingSystem)])], [], foreign));

        var refused = Apply(path);

        Assert.False(refused.Succeeded);
        Assert.Equal(RefusalCodes.TextFieldStale, refused.Refusal!.Code);
        Assert.Equal("z", Text(Assert.Single(Show(path, "GDR-1a").Discussion)));
    }

    [Fact]
    public void AnEmptyNewParagraphIsWrittenAndReadsBackInTheFieldsDefaultWritingSystem()
    {
        var path = ProjectWithGdrs();
        var shown = Show(path, "GDR-1");

        Staged(path, shown.Record.Id, Edit([shown.Discussion[0].Key, shown.Discussion[1].Key, null],
            [new TextParagraphChange(2, null, "Normal", [])], []));
        Applied(path);

        // FieldWorks saves no Contents for an empty paragraph, so it reads back in the field's default.
        var added = Show(path, "GDR-1").Discussion[2];
        Assert.Equal("", Text(added));
        Assert.Equal(shown.DefaultAnalysis, added.DefaultWritingSystem);
    }

    [Fact]
    public void DiscardingTakesTheFieldsEditOutOfTheDraftEvenWhenItAddedAParagraph()
    {
        var path = ProjectWithGdrs();
        var shown = Show(path, "GDR-1a");
        var only = shown.Discussion[0];
        var request = new TextParagraphsFieldRequest(path, shown.Record.Id, Discussion);
        Staged(path, shown.Record.Id, Edit([only.Key, null],
            [new TextParagraphChange(1, null, "Normal", [new TextRun("w", only.Runs[0].WritingSystem)])], []));
        InFieldWorks(path, "GDR-1a", (cache, text) =>
            ((IStTxtPara)text.ParagraphsOS[0]).Contents = TsStringUtils.MakeString("fw", cache.DefaultAnalWs));

        var pending = ProposalCommands.PendingTextParagraphs(request);
        Assert.True(pending.Succeeded, pending.Refusal?.Message);
        Assert.Equal([only.Key, null], Assert.IsType<TextParagraphsPayload>(pending.Value!.Paragraphs).Order);
        Assert.True(ProposalCommands.DiscardTextParagraphs(request).Succeeded);

        Assert.Equal(0, PendingOperations(path));
        Assert.Null(ProposalCommands.PendingTextParagraphs(request).Value!.Paragraphs);
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
    [InlineData("{\"field\":\"CmPossibility.Discussion\",\"order\":[\"KEY\"],\"changed\":[],\"removed\":[\"KEY\"],\"expected\":{\"KEY\":{\"text\":\"x\",\"style\":\"\",\"runs\":\"\"}}}")]
    [InlineData("{\"field\":\"CmPossibility.Discussion\",\"order\":[\"KEY\"],\"changed\":[{\"index\":0,\"key\":\"KEY\",\"runs\":[{\"text\":\"x\",\"ws\":\"en\"}]}],\"removed\":[],\"expected\":{\"KEY\":\"x\"}}")]
    [InlineData("{\"field\":\"CmPossibility.Discussion\",\"order\":[],\"changed\":[],\"removed\":[],\"expected\":{}}")]
    [InlineData("{\"field\":\"CmPossibility.Discussion\",\"order\":[null],\"changed\":[],\"removed\":[],\"expected\":{},\"extra\":1}")]
    [InlineData("{\"field\":\"CmPossibility.Discussion\",\"order\":[null],\"changed\":[{\"index\":0,\"runs\":[{\"text\":\"x\",\"ws\":\"en\",\"ints\":{\"1\":[0,1]}}]}],\"removed\":[],\"expected\":{}}")]
    [InlineData("{\"field\":\"CmPossibility.Discussion\",\"order\":[null],\"changed\":[{\"index\":0,\"runs\":[{\"text\":\"x\",\"ws\":\"en\",\"strings\":{\"133\":\"Emphasis\"}}]}],\"removed\":[],\"expected\":{}}")]
    [InlineData("{\"field\":\"CmPossibility.Discussion\",\"order\":[null],\"changed\":[{\"index\":0,\"runs\":[{\"text\":\"x\",\"ws\":\"en\",\"ints\":{\"50\":[0,1]}}]}],\"removed\":[],\"expected\":{}}")]
    [InlineData("{\"field\":\"CmPossibility.Discussion\",\"order\":[null],\"changed\":[{\"index\":0,\"runs\":[{\"text\":\"\uFFFC\",\"ws\":\"en\"}]}],\"removed\":[],\"expected\":{}}")]
    public void AMalformedPayloadIsRefused(string payload)
    {
        using var document = System.Text.Json.JsonDocument.Parse(payload.Replace("KEY", CanonicalId.Mint().Value));

        Assert.ThrowsAny<SIL.Motif.Contract.Parsing.ContractParseException>(() => TextParagraphsPayload.Parse(document.RootElement));
    }

    // Each changed or removed paragraph's expected state is what `gdr show` returned for it.
    private static TextParagraphsPayload Edit(IReadOnlyList<string?> order, IReadOnlyList<TextParagraphChange> changed,
        IReadOnlyList<string> removed, params GdrParagraph[] began) => new(Discussion, order, changed, removed,
        began.ToDictionary(paragraph => paragraph.Key!, paragraph => new TextParagraphExpected(Text(paragraph), paragraph.Style ?? "",
            TextRunsDigest.Of(paragraph))));

    private static TextParagraphsPayload Typed(GdrParagraph paragraph, string text) => Edit([paragraph.Key],
        [new TextParagraphChange(0, paragraph.Key, "Normal", [new TextRun(text, paragraph.Runs[0].WritingSystem)])], [], paragraph);

    private static TextRun Carried(GdrRun run) => new(run.Text, run.WritingSystem, run.Style, Props(run.Properties), run.ObjData);

    private static TextProps Props(GdrTextProperties properties) => new(
        properties.Ints.ToDictionary(pair => pair.Key, pair => (pair.Value.Variation, pair.Value.Value)),
        properties.Strings.Where(pair => pair.Key is not (133 or 6)).ToDictionary(pair => pair.Key, pair => pair.Value));

    private static void InFieldWorks(string path, string abbreviation, Action<LcmCache, IStText> change)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(path);
        var record = cache.ServiceLocator.GetInstance<ICmPossibilityRepository>().AllInstances()
            .Single(possibility => possibility.Abbreviation.AnalysisDefaultWritingSystem.Text == abbreviation);
        SIL.LCModel.Infrastructure.NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => change(cache, record.DiscussionOA));
        new FwDataProjectLoader().Save(cache);
    }

    // After a FieldWorks edit Apply first asks for a refresh, then checks the field.
    private static CommandOutcome<ApplyPendingResult> AppliedAfterRefresh(string path)
    {
        var first = Apply(path);
        Assert.Equal(RefusalCodes.ChangeRefreshRequired, first.Refusal?.Code);
        Recaptured(path);
        return Apply(path);
    }

    private static void Recaptured(string path) =>
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), RootFor(path)).Succeeded);

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
