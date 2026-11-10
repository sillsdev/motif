using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Generator;
using SIL.Motif.Generator.Manifest;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Store;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// A person's rich-text edit of a sense's Definition, one writing system at a time, staged in the pending-changes Draft
/// and applied to a project built at run time with placeholder text (ADR 0076), then read back from the Baseline.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class TextRichStringApplyTests(PristineProjectFixture pristine)
{
    private const string Definition = "LexSense.Definition";

    [Fact]
    public void ADefinitionEditedInTwoWritingSystemsWithACharacterStyleReadsBackWithItsFormatting()
    {
        var path = Project();
        var sense = Sense(path);
        var english = Alternative(sense, "en");
        var arabic = Alternative(sense, SeededProject.RightToLeftTag);
        Assert.Empty(arabic.Runs);

        var typed = english.Runs.Select(Carried).ToList();
        typed[0] = typed[0] with { Text = "x more " };
        typed.Add(new TextRun("bold", "en", Props: Bold));
        Staged(path, sense.Id, Edit(english, typed));
        Staged(path, sense.Id, Edit(arabic, [new TextRun("ar-x", SeededProject.RightToLeftTag, "Emphasis")]));
        Assert.Equal(2, PendingOperations(path));
        Applied(path);

        var after = Sense(path);
        var en = Alternative(after, "en");
        Assert.Equal("x more ybold", en.Text);
        Assert.Equal("Emphasis", en.Runs.Single(run => run.Text == "y").Style);
        Assert.Equal(new GdrIntProperty((int)FwTextPropVar.ktpvEnum, 1), en.Runs.Single(run => run.Text == "bold").Properties.Ints[(int)FwTextPropType.ktptBold]);
        var ar = Alternative(after, SeededProject.RightToLeftTag);
        Assert.Equal(("ar-x", "Emphasis"), (ar.Text, Assert.Single(ar.Runs).Style));
    }

    [Fact]
    public void AStringFieldTakesNoWritingSystemAndIsWritten()
    {
        var path = Project();
        var id = CanonicalId.FromGuid(pristine.Seed.FirstSenseId).Value;

        Staged(path, id, new TextRichStringPayload("LexSense.Source", null, [new TextRun("src", "en", Props: Bold)], new("", TextRunsDigest.Of([]))));
        Applied(path);

        using var cache = new FwDataProjectLoader().LoadScratchCache(path);
        var source = cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(pristine.Seed.FirstSenseId).Source;
        Assert.Equal("src", source.Text);
        Assert.Equal(1, source.get_Properties(0).GetIntPropValues((int)FwTextPropType.ktptBold, out _));
    }

    [Fact]
    public void ALaterSessionUpdatesTheOperationKeepingTheFirstStartAndAnEditBackToTheStartLeavesTheDraft()
    {
        var path = Project();
        var sense = Sense(path);
        var english = Alternative(sense, "en");

        Staged(path, sense.Id, Edit(english, [new TextRun("first", "en")]));
        var second = Staged(path, sense.Id, Edit(english, [new TextRun("second", "en")]));

        var operation = Assert.Single(second.Operations);
        Assert.Equal(TextRichStringOperationKinds.Set, operation.Kind);
        var pending = ProposalCommands.PendingTextRichString(new TextRichStringFieldRequest(path, sense.Id, Definition, "en")).Value!.RichString!;
        Assert.Equal(("second", english.Text, english.Digest), (pending.Text, pending.Expected.Text, pending.Expected.Runs));
        Assert.Equal(0, Staged(path, sense.Id, Edit(english, english.Runs.Select(Carried).ToArray())).OperationCount);
    }

    [Fact]
    public void AFormattingOnlyEditIsStagedAndApplied()
    {
        var path = Project();
        var english = Alternative(Sense(path), "en");
        var unstyled = english.Runs.Select(Carried).Select(run => run with { CharacterStyle = null }).ToArray();

        Assert.Equal(1, Staged(path, Sense(path).Id, Edit(english, unstyled)).OperationCount);
        Applied(path);

        var after = Alternative(Sense(path), "en");
        Assert.Equal(english.Text, after.Text);
        Assert.All(after.Runs, run => Assert.Null(run.Style));
    }

    public static TheoryData<string> FieldWorksChanges => ["text", "format"];

    [Theory]
    [MemberData(nameof(FieldWorksChanges))]
    public void AFieldWorksChangeToTheAlternativeAfterStagingHoldsTheApplyAndKeepsTheDraft(string change)
    {
        var path = Project();
        var sense = Sense(path);
        Staged(path, sense.Id, Edit(Alternative(sense, "en"), [new TextRun("mine", "en")]));
        InFieldWorks(path, (cache, item) =>
        {
            var en = cache.WritingSystemFactory.GetWsFromStr("en");
            if (change == "text")
            {
                item.Definition.set_String(en, TsStringUtils.MakeString("fw", en));
                return;
            }
            var bold = item.Definition.get_String(en).GetBldr();
            bold.SetIntPropValues(0, 1, (int)FwTextPropType.ktptBold, (int)FwTextPropVar.ktpvEnum, 1);
            item.Definition.set_String(en, bold.GetString());
        });

        var refused = AppliedAfterRefresh(path);

        Assert.False(refused.Succeeded);
        Assert.Equal(RefusalCodes.TextFieldStale, refused.Refusal!.Code);
        Assert.Equal((sense.Id, Definition, "en"), (refused.Refusal.Facts!["owner"], refused.Refusal.Facts["field"], refused.Refusal.Facts["ws"]));
        Assert.Equal(1, PendingOperations(path));
        Assert.NotEqual("mine", Alternative(Sense(path), "en").Text);
    }

    [Fact]
    public void AFieldWorksChangeToAnotherWritingSystemIsNotDrift()
    {
        var path = Project();
        var sense = Sense(path);
        Staged(path, sense.Id, Edit(Alternative(sense, "en"), [new TextRun("mine", "en")]));
        InFieldWorks(path, (cache, item) =>
        {
            var ar = cache.WritingSystemFactory.GetWsFromStr(SeededProject.RightToLeftTag);
            item.Definition.set_String(ar, TsStringUtils.MakeString("fw-ar", ar));
        });
        Assert.Equal(RefusalCodes.ChangeRefreshRequired, Apply(path).Refusal?.Code);
        Recaptured(path);

        Applied(path);

        var after = Sense(path);
        Assert.Equal(("mine", "fw-ar"), (Alternative(after, "en").Text, Alternative(after, SeededProject.RightToLeftTag).Text));
    }

    [Fact]
    public void DiscardingTakesOnlyThatWritingSystemsEditOutOfTheDraft()
    {
        var path = Project();
        var sense = Sense(path);
        Staged(path, sense.Id, Edit(Alternative(sense, "en"), [new TextRun("mine", "en")]));
        Staged(path, sense.Id, Edit(Alternative(sense, SeededProject.RightToLeftTag), [new TextRun("ar-x", SeededProject.RightToLeftTag)]));
        var request = new TextRichStringFieldRequest(path, sense.Id, Definition, "en");

        Assert.Equal("mine", ProposalCommands.PendingTextRichString(request).Value!.RichString!.Text);
        var discarded = ProposalCommands.DiscardTextRichString(request);
        Assert.True(discarded.Succeeded, discarded.Refusal?.Message);

        Assert.Null(ProposalCommands.PendingTextRichString(request).Value!.RichString);
        Assert.Equal(1, PendingOperations(path));
    }

    [Fact]
    public void ARichStringEditAndTheGeneratedKindCannotBothEditOneAlternative()
    {
        var target = CanonicalId.Mint();
        var richString = Operation(TextRichStringOperationKinds.Set, target,
            new TextRichStringPayload(Definition, "en", [new TextRun("x", "en")], new("", TextRunsDigest.Of([]))).ToJson());
        var generated = Operation("lexical/lexSense/setDefinition", target, JsonSerializer.SerializeToElement(new { ws = "en", text = "y" }));
        var otherWs = Operation("lexical/lexSense/setDefinition", target, JsonSerializer.SerializeToElement(new { ws = "fr", text = "y" }));

        Assert.Equal(OperationSlotRegistry.Resolve(richString), OperationSlotRegistry.Resolve(generated));
        Assert.NotEqual(OperationSlotRegistry.Resolve(richString), OperationSlotRegistry.Resolve(otherWs));
    }

    [Fact]
    public void TheEditableFieldsAreTheInventorysStringFieldsTheParserNeverReadsLessThoseOtherKindsOwn()
    {
        var rows = ManifestTsvParser.Parse(RepoPaths.DefaultManifestPath())
            .Where(row => row.Scope == "in" && row.Kind == "basic" && row.Sig is "String" or "MultiString").ToArray();
        string[] ownedElsewhere = ["StTxtPara.Contents", "WfiMorphBundle.Form", "RnGenericRec.Title"];
        var expected = rows.Where(row => row.HcReferenced == "no" && row.HcReachable == "no" && row.Verbs.Split('|').Contains("set"))
            .Select(row => $"{row.Class}.{row.Field}").Except(ownedElsewhere).Order().ToArray();

        Assert.Equal(expected, TextRichStringFields.Names.Order());
        Assert.All(rows.Where(row => TextRichStringFields.Names.Contains($"{row.Class}.{row.Field}")),
            row => Assert.Equal(row.Sig == "MultiString", TextRichStringFields.IsMultiString($"{row.Class}.{row.Field}")));
        Assert.Contains("PhEnvironment.StringRepresentation", rows.Select(row => $"{row.Class}.{row.Field}"));
    }

    [Theory]
    [InlineData("{\"field\":\"LexSense.Gloss\",\"ws\":\"en\",\"runs\":[],\"expected\":{\"text\":\"\",\"runs\":\"\"}}")]
    [InlineData("{\"field\":\"PhEnvironment.StringRepresentation\",\"runs\":[],\"expected\":{\"text\":\"\",\"runs\":\"\"}}")]
    [InlineData("{\"field\":\"MoAdhocProhibGr.Description\",\"ws\":\"en\",\"runs\":[],\"expected\":{\"text\":\"\",\"runs\":\"\"}}")]
    [InlineData("{\"field\":\"LexSense.Definition\",\"runs\":[],\"expected\":{\"text\":\"\",\"runs\":\"\"}}")]
    [InlineData("{\"field\":\"LexSense.Source\",\"ws\":\"en\",\"runs\":[],\"expected\":{\"text\":\"\",\"runs\":\"\"}}")]
    [InlineData("{\"field\":\"LexSense.Definition\",\"ws\":\"en\",\"runs\":[]}")]
    [InlineData("{\"field\":\"LexSense.Definition\",\"ws\":\"en\",\"runs\":[],\"expected\":{\"text\":\"\"}}")]
    [InlineData("{\"field\":\"LexSense.Definition\",\"ws\":\"en\",\"runs\":[],\"expected\":{\"text\":\"\",\"runs\":\"\"},\"extra\":1}")]
    [InlineData("{\"field\":\"LexSense.Definition\",\"ws\":\"en\",\"runs\":[{\"text\":\"x\",\"ws\":\"en\",\"ints\":{\"2\":[0,7]}}],\"expected\":{\"text\":\"\",\"runs\":\"\"}}")]
    [InlineData("{\"field\":\"LexSense.Definition\",\"ws\":\"en\",\"runs\":[{\"text\":\"x\",\"ws\":\"en\",\"ints\":{\"50\":[0,1]}}],\"expected\":{\"text\":\"\",\"runs\":\"\"}}")]
    [InlineData("{\"field\":\"LexSense.Definition\",\"ws\":\"en\",\"runs\":[{\"text\":\"x\",\"ws\":\"en\",\"strings\":{\"133\":\"Emphasis\"}}],\"expected\":{\"text\":\"\",\"runs\":\"\"}}")]
    public void AMalformedPayloadIsRefused(string payload)
    {
        using var document = JsonDocument.Parse(payload);

        var refused = Assert.ThrowsAny<ContractParseException>(() => TextRichStringPayload.Parse(document.RootElement));
        Assert.Contains(TextRichStringOperationKinds.Set, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AValueTheProjectCouldNotLoadBackIsRefusedWhenStaged()
    {
        var path = Project();
        var sense = Sense(path);
        var unloadable = new TextProps(new Dictionary<int, (int, int)> { [(int)FwTextPropType.ktptBold] = ((int)FwTextPropVar.ktpvEnum, 7) },
            new Dictionary<int, string>());

        var staged = ProposalCommands.StageTextRichString(new StageTextRichStringRequest(path, sense.Id,
            Edit(Alternative(sense, "en"), [new TextRun("x", "en", Props: unloadable)])));

        Assert.False(staged.Succeeded);
        Assert.Equal(0, PendingOperations(path));
    }

    [Fact]
    public void AnAlternativeClearedToEmptyIsEditedAgainFromEmptyWithoutDrift()
    {
        var path = Project();
        Staged(path, Sense(path).Id, Edit(Alternative(Sense(path), "en"), []));
        Applied(path);
        var cleared = Alternative(Sense(path), "en");
        Assert.Empty(cleared.Runs);

        Staged(path, Sense(path).Id, Edit(cleared, [new TextRun("again", "en")]));
        Applied(path);

        Assert.Equal("again", Alternative(Sense(path), "en").Text);
    }

    [Fact]
    public void AnEditOfAFieldTheTargetDoesNotHaveIsRefusedWhenStaged()
    {
        var path = Project();
        var entry = CanonicalId.FromGuid(pristine.Seed.FirstEntryId).Value;

        var staged = ProposalCommands.StageTextRichString(new StageTextRichStringRequest(path, entry,
            new TextRichStringPayload(Definition, "en", [new TextRun("x", "en")], new("", TextRunsDigest.Of([])))));

        Assert.False(staged.Succeeded);
        Assert.Contains("has no field", staged.Refusal!.Message, StringComparison.Ordinal);
    }

    private static readonly TextProps Bold = new(new Dictionary<int, (int, int)> { [(int)FwTextPropType.ktptBold] = ((int)FwTextPropVar.ktpvEnum, 1) }, new Dictionary<int, string>());

    private static TextRichStringPayload Edit(RichStringAlternative began, IReadOnlyList<TextRun> runs) =>
        new(Definition, began.WritingSystem, runs, new(began.Text, began.Digest));

    private static TextRun Carried(GdrRun run) => new(run.Text, run.WritingSystem, run.Style, new(
        run.Properties.Ints.ToDictionary(pair => pair.Key, pair => (pair.Value.Variation, pair.Value.Value)),
        run.Properties.Strings.Where(pair => pair.Key is not (133 or 6)).ToDictionary(pair => pair.Key, pair => pair.Value)), run.ObjData);

    private static OperationEnvelope Operation(string kind, CanonicalId target, JsonElement after) =>
        new(CanonicalId.Mint(), kind, target: target, after: after);

    private SenseDefinitions Sense(string path)
    {
        var read = RichStringQuery.SenseDefinitions(path);
        Assert.True(read.Succeeded, read.Refusal?.Message);
        return read.Value!.Senses.Single(sense => sense.Id == CanonicalId.FromGuid(pristine.Seed.FirstSenseId).Value);
    }

    private static RichStringAlternative Alternative(SenseDefinitions sense, string ws) => sense.Definition.Single(item => item.WritingSystem == ws);

    private void InFieldWorks(string path, Action<LcmCache, ILexSense> change)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(path);
        var sense = cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(pristine.Seed.FirstSenseId);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => change(cache, sense));
        new FwDataProjectLoader().Save(cache);
    }

    // The first sense's English Definition is "x" then "y" in Emphasis; its Arabic one is empty.
    private string Project()
    {
        using var cache = pristine.NewScratch();
        SeededGdrs.Seed(cache);
        var sense = cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(pristine.Seed.FirstSenseId);
        var en = cache.WritingSystemFactory.GetWsFromStr("en");
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var definition = TsStringUtils.MakeStrBldr();
            definition.Replace(0, 0, "x", TsStringUtils.MakeProps(null, en));
            definition.Replace(1, 1, "y", TsStringUtils.MakeProps("Emphasis", en));
            sense.Definition.set_String(en, definition.GetString());
        });
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        Recaptured(path);
        return path;
    }

    private static ComposedOperationsResponse Staged(string path, string target, TextRichStringPayload richString)
    {
        var staged = ProposalCommands.StageTextRichString(new StageTextRichStringRequest(path, target, richString));
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        return staged.Value!;
    }

    // After a FieldWorks edit Apply first asks for a refresh, then checks the field.
    private static CommandOutcome<ApplyPendingResult> AppliedAfterRefresh(string path)
    {
        Assert.Equal(RefusalCodes.ChangeRefreshRequired, Apply(path).Refusal?.Code);
        Recaptured(path);
        return Apply(path);
    }

    private static void Recaptured(string path) =>
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), RootFor(path)).Succeeded);

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
        Recaptured(path);
    }

    private static int PendingOperations(string path) => ProjectStoreCommand.Run(path, MotifProductVersion.CurrentText,
        (database, _) => CommandOutcome<string>.Success(new ProposalRepository(database).FindPendingChangesDraft()!.ProposalJson!))
        .Value! is { } json ? JsonDocument.Parse(json).RootElement.GetProperty("operations").GetArrayLength() : 0;

    private static string RootFor(string projectPath)
    {
        var root = Path.Combine(Path.GetDirectoryName(projectPath)!, "rich-string-root");
        Directory.CreateDirectory(root);
        return root;
    }
}
