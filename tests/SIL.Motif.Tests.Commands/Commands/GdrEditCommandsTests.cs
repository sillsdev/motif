using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Commands.Requests;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Gdr;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Store;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>Grammar Guide authoring by Markdown: staged in a Draft, previewed with as-of, then applied.</summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class GdrEditCommandsTests(PristineProjectFixture pristine)
{
    private const string Decision = "- chosen or other, I am choosing chosen, because it fits the evidence.";

    [Fact]
    public void ABodyOnlyFileStagesTheDiscussionAndShowAsOfPreviewsItWithoutChangingTheBaseline()
    {
        var path = ProjectWithGdrs();
        var before = Markdown(path, "GDR-1");

        var staged = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-1", "replaced body"));

        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        Assert.Equal(["discussion"], staged.Value!.Fields);
        Assert.Equal(PendingChanges.DraftName, staged.Value.DraftName);
        Assert.Equal(before, Markdown(path, "GDR-1"));
        var preview = GdrQuery.Show(new GdrShowRequest(path, "GDR-1", AsOf: staged.Value.DraftName)).Value!;
        Assert.Equal("replaced body", Assert.Single(preview.Discussion).Runs.Single().Text);
        Assert.Equal(2, Show(path, "GDR-1").Discussion.Count);
    }

    [Fact]
    public void TheWholeRecordRoundTripsWithNothingToStageAndATitleEditStagesOneOperation()
    {
        var path = ProjectWithGdrs();
        var markdown = Markdown(path, "GDR-1");

        var same = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-1", markdown));
        Assert.True(same.Succeeded, same.Refusal?.Message);
        Assert.Empty(same.Value!.Fields);
        Assert.Equal(0, same.Value.OperationCount);

        var staged = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-1", markdown.Replace("title-1", "a better title", StringComparison.Ordinal)));
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        Assert.Equal(["title"], staged.Value!.Fields);
        Assert.Equal(1, staged.Value.OperationCount);
        Assert.Equal("a better title", GdrQuery.Show(new GdrShowRequest(path, "GDR-1", AsOf: staged.Value.DraftName)).Value!.Record.Title);
        Assert.Equal("title-1", Show(path, "GDR-1").Record.Title);
    }

    [Fact]
    public void AnUnchangedParagraphKeepsItsKeyAndOnlyTheChangedOneIsStaged()
    {
        var path = ProjectWithGdrs();
        var shown = Show(path, "GDR-1");
        var body = new GdrMarkdownView(shown, "p").Fields.Single(field => field.Name == "discussion").Body;

        var staged = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-1", body + "\n\nA new closing paragraph."));

        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        var preview = GdrQuery.Show(new GdrShowRequest(path, "GDR-1", AsOf: staged.Value!.DraftName)).Value!;
        Assert.Equal(3, preview.Discussion.Count);
        Assert.Equal(shown.Discussion.Select(paragraph => paragraph.Key), preview.Discussion.Take(2).Select(paragraph => paragraph.Key));
        Assert.Equal("A new closing paragraph.", preview.Discussion[2].Runs.Single().Text);
    }

    [Fact]
    public void AnEditAppliesWithThePendingChangesAndIsReadBackFromTheProject()
    {
        var path = ProjectWithGdrs();
        Assert.True(GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-2", "first line\n\nsecond line")).Succeeded);

        var applied = Apply(path);
        Assert.True(applied.Succeeded, applied.Refusal?.Code + ": " + applied.Refusal?.Message);
        Assert.True(applied.Value!.Applied, applied.Value.Summary);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), RootFor(path)).Succeeded);

        Assert.Equal(["first line", "second line"], Show(path, "GDR-2").Discussion.Select(paragraph => paragraph.Runs.Single().Text));
    }

    [Fact]
    public void ADecisionIsAppendedUnderTheDecisionsHeadingInTheTemplateForm()
    {
        var path = ProjectWithGdrs(HeadingStyles);

        var first = GdrEditCommands.Decide(new GdrDecideRequest(path, "GDR-2", "chosen", "other", "it fits the evidence"));
        Assert.True(first.Succeeded, first.Refusal?.Message);
        var second = GdrEditCommands.Decide(new GdrDecideRequest(path, "GDR-2", "x", "y", "z."));
        Assert.True(second.Succeeded, second.Refusal?.Message);

        var preview = new GdrMarkdownView(GdrQuery.Show(new GdrShowRequest(path, "GDR-2", AsOf: second.Value!.DraftName)).Value!, "p")
            .Fields.Single(field => field.Name == "discussion").Body;
        Assert.Equal("### Decisions\n\n" + Decision + "\n- x or y, I am choosing x, because z.", preview.Replace("\\", "", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("", "b", "c")]
    [InlineData("a", " ", "c")]
    [InlineData("a", "b", "line one\nline two")]
    public void ADecisionNeedsEveryPartOnOneLine(string chosen, string over, string because)
    {
        var path = ProjectWithGdrs(HeadingStyles);

        var refused = GdrEditCommands.Decide(new GdrDecideRequest(path, "GDR-2", chosen, over, because));

        Assert.Equal(RefusalCodes.GdrDecisionInvalid, refused.Refusal!.Code);
    }

    [Fact]
    public void MarkdownThatCannotBeReadStagesNothing()
    {
        var path = ProjectWithGdrs();
        var header = Markdown(path, "GDR-1")[..(Markdown(path, "GDR-1").IndexOf("\n::: {field=", StringComparison.Ordinal) + 1)];
        string[] unreadable =
        [
            header.Replace("GDR-1", "GDR-9", StringComparison.Ordinal),
            header + "\n::: {field=bogus}\ntext\n:::\n",
            header + "\n::: {field=title}\ntext\n",
        ];

        foreach (var markdown in unreadable)
            Assert.Equal(RefusalCodes.GdrMarkdownInvalid, GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-1", markdown)).Refusal!.Code);

        var pending = ProjectStoreCommand.Run(path, MotifProductVersion.CurrentText,
            (database, _) => CommandOutcome<string>.Success(new ProposalRepository(database).FindPendingChangesDraft()?.ProposalJson ?? "")).Value!;
        Assert.DoesNotContain("setParagraphs", pending, StringComparison.Ordinal);
    }

    [Fact]
    public void ANamedDraftMustExistAndTakesTheEdit()
    {
        var path = ProjectWithGdrs();

        var missing = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-1", "text", "no-such-draft"));
        Assert.False(missing.Succeeded);
        Assert.Equal("draft.not-found", missing.Refusal!.Code);

        Assert.True(ProposalCommands.New(new NewDraftRequest(path, MotifProductVersion.CurrentText, "guide-draft", null, null)).Succeeded);
        var staged = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-1", "text", "guide-draft"));
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        Assert.Equal("guide-draft", staged.Value!.DraftName);
        Assert.Equal("text", Assert.Single(GdrQuery.Show(new GdrShowRequest(path, "GDR-1", AsOf: "guide-draft")).Value!.Discussion).Runs.Single().Text);
    }

    private const string Paragraphs = "Before the decisions.\n\nAfter the decisions.";

    [Fact]
    public void ADecisionAfterAStagedEditKeepsItAndASecondEditFromTheBaselineIsRefused()
    {
        var path = ProjectWithGdrs(HeadingStyles);
        var first = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-2", Paragraphs));
        Assert.True(first.Succeeded, first.Refusal?.Message);

        var decided = GdrEditCommands.Decide(new GdrDecideRequest(path, "GDR-2", "a", "b", "e.g. it fits *well*"));
        Assert.True(decided.Succeeded, decided.Refusal?.Message);

        var preview = AsOf(path, "GDR-2", decided.Value!.DraftName);
        Assert.StartsWith(Paragraphs, preview.Replace("\\", "", StringComparison.Ordinal), StringComparison.Ordinal);
        var runs = string.Concat(GdrQuery.Show(new GdrShowRequest(path, "GDR-2", AsOf: decided.Value.DraftName)).Value!
            .Discussion.SelectMany(paragraph => paragraph.Runs).Select(run => run.Text));
        Assert.Contains("because e.g. it fits *well*.", runs, StringComparison.Ordinal);

        var lost = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-2", Markdown(path, "GDR-2")));
        Assert.Equal(RefusalCodes.GdrMarkdownInvalid, lost.Refusal!.Code);
        Assert.Contains("--as-of", lost.Refusal.Message, StringComparison.Ordinal);
        Assert.Equal(preview, AsOf(path, "GDR-2", decided.Value.DraftName));

        var again = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-2",
            preview.Replace("After the decisions.", "After the decisions, changed.", StringComparison.Ordinal)));
        Assert.True(again.Succeeded, again.Refusal?.Message);
        var final = AsOf(path, "GDR-2", again.Value!.DraftName);
        Assert.Contains("changed", final, StringComparison.Ordinal);
        Assert.Contains("I am choosing a", final, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileFromTheBaselineWithANewParagraphIsRefusedWhenADecisionIsStaged()
    {
        var path = ProjectWithGdrs(HeadingStyles);
        Assert.True(GdrEditCommands.Decide(new GdrDecideRequest(path, "GDR-1", "a", "b", "it fits")).Succeeded);
        var plain = new GdrMarkdownView(Show(path, "GDR-1"), "p").Fields.Single(field => field.Name == "discussion").Body;

        var refused = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-1", plain + "\n\nA new paragraph."));

        Assert.Equal(RefusalCodes.GdrMarkdownInvalid, refused.Refusal!.Code);
        Assert.Contains("--as-of", refused.Refusal.Message, StringComparison.Ordinal);
        Assert.Contains("I am choosing a", AsOf(path, "GDR-1", PendingChanges.DraftName), StringComparison.Ordinal);
    }

    private static readonly string[] StoredBlock =
        ["Before the block.", "```motif-query", "id: forms", "title: Forms", "run: motif query forms", "```", "After the block."];

    private static void SeedStoredBlock(LcmCache cache) => NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
    {
        var record = SeededGdrs.Records(cache).PossibilitiesOS.Single(item => item.Abbreviation.get_String(cache.DefaultAnalWs).Text == "GDR-2");
        record.DiscussionOA = cache.ServiceLocator.GetInstance<IStTextFactory>().Create();
        foreach (var line in StoredBlock)
        {
            var paragraph = cache.ServiceLocator.GetInstance<IStTxtParaFactory>().Create();
            record.DiscussionOA.ParagraphsOS.Add(paragraph);
            paragraph.Contents = TsStringUtils.MakeString(line, cache.DefaultAnalWs);
        }
    });

    [Fact]
    public void AStoredQueryBlockSurvivesANeighbouringEditAndADecisionUnchanged()
    {
        var path = ProjectWithGdrs(cache => { HeadingStyles(cache); SeedStoredBlock(cache); });
        var texts = (GdrShowResponse shown) => shown.Discussion.Select(paragraph => string.Concat(paragraph.Runs.Select(run => run.Text))).ToArray();
        Assert.Equal(StoredBlock, texts(Show(path, "GDR-2")));
        var body = new GdrMarkdownView(Show(path, "GDR-2"), "p").Fields.Single(field => field.Name == "discussion").Body;

        var edited = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-2", body.Replace("After the block.", "After, changed.", StringComparison.Ordinal)));
        Assert.True(edited.Succeeded, edited.Refusal?.Message);
        var afterEdit = texts(GdrQuery.Show(new GdrShowRequest(path, "GDR-2", AsOf: edited.Value!.DraftName)).Value!);
        Assert.Equal(StoredBlock[..6], afterEdit[..6]);
        Assert.Equal("After, changed.", afterEdit[6]);

        var decided = GdrEditCommands.Decide(new GdrDecideRequest(path, "GDR-2", "a", "b", "it fits"));
        Assert.True(decided.Succeeded, decided.Refusal?.Message);
        var afterDecide = texts(GdrQuery.Show(new GdrShowRequest(path, "GDR-2", AsOf: decided.Value!.DraftName)).Value!);
        Assert.Equal(StoredBlock[..6], afterDecide[..6]);
        Assert.Contains("I am choosing a", string.Join("\n", afterDecide), StringComparison.Ordinal);
    }

    private static string PendingJson(string path) => ProjectStoreCommand.Run(path, MotifProductVersion.CurrentText,
        (database, _) => CommandOutcome<string>.Success(new ProposalRepository(database).FindPendingChangesDraft()?.ProposalJson ?? "")).Value!;

    [Fact]
    public void AFencedMotifBlockIsRefusedWithNothingStagedBecauseTheReaderTakesNoCodeBlock()
    {
        var path = ProjectWithGdrs();
        var blocks = "Before.\n\n```motif-query\nid: forms\ntitle: Forms\n\n\nrun: motif query forms --project p\n```\n\n```motif-show\nquery: forms\nshow: table\n```";

        var refused = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-2", blocks));

        Assert.Equal(RefusalCodes.GdrMarkdownInvalid, refused.Refusal!.Code);
        Assert.Contains("nothing was staged", refused.Refusal.Message, StringComparison.Ordinal);
        Assert.Contains("added in FieldWorks", refused.Refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("setParagraphs", PendingJson(path), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownSectionIsRefusedWithTheAddSectionLimitAndANoChangeEditIntoAMissingDraftIsRefused()
    {
        var path = ProjectWithGdrs();

        var unknown = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-99", "text"));
        Assert.Equal(RefusalCodes.GdrNotFound, unknown.Refusal!.Code);
        Assert.Contains("FieldWorks", unknown.Refusal.Message, StringComparison.Ordinal);

        var noChange = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-1", Markdown(path, "GDR-1"), "no-such-draft"));
        Assert.Equal("draft.not-found", noChange.Refusal!.Code);
    }

    [Fact]
    public void ADecisionKeepsAFullStopAndEscapesMarkdownCharacters()
    {
        var entry = GdrEditCommands.Entry(new GdrDecideRequest("p", "GDR-2", "a_b", "[c]", "see e.g."));

        Assert.Equal("- a\\_b or \\[c\\], I am choosing a\\_b, because see e.g.", entry);
        var body = "x\n\n### Decisions\n\n```text\n### Decisions\n\n\n- fenced\n```\n\n### Later";
        Assert.Equal("x\n\n### Decisions\n\n```text\n### Decisions\n\n\n- fenced\n```\n\n- new\n\n### Later",
            GdrEditCommands.WithDecision(body, "- new"));
    }

    [Fact]
    public void ShowAsOfRefusesAnUnknownDraft()
    {
        var path = ProjectWithGdrs();

        var refused = GdrQuery.Show(new GdrShowRequest(path, "GDR-1", AsOf: "nothing-here"));

        Assert.Equal("draft.not-found", refused.Refusal!.Code);
    }

    [Fact]
    public void OnlyGuideSectionEditsAndNotesAreExemptFromTheAutonomousGate()
    {
        var guide = CanonicalId.Mint();
        var other = CanonicalId.Mint();
        var sections = new HashSet<string> { guide.Value };
        OperationEnvelope Op(string kind, CanonicalId target) => new(CanonicalId.Mint(), kind, target: target,
            after: System.Text.Json.JsonSerializer.SerializeToElement(new { }));

        Assert.NotNull(GuideNotesOnlyProposal.ExemptionFor([Op(TextParagraphsOperationKinds.Set, guide), Op(CmPossibilityNameOperationKinds.SetName, guide)], sections));
        Assert.NotNull(GuideNotesOnlyProposal.ExemptionFor([Op(EditTextNotesOperationKinds.Set, other)], sections));
        Assert.Null(GuideNotesOnlyProposal.ExemptionFor([Op(TextParagraphsOperationKinds.Set, other)], sections));
        Assert.Null(GuideNotesOnlyProposal.ExemptionFor([Op(TextParagraphsOperationKinds.Set, guide), Op(CmPossibilityAbbreviationOperationKinds.SetAbbreviation, guide)], sections));
        Assert.Null(GuideNotesOnlyProposal.ExemptionFor([], sections));
    }

    private static void HeadingStyles(LcmCache cache) => NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
    {
        foreach (var name in new[] { "Heading 3", "Bulleted List" })
        {
            if (cache.LangProject.StylesOC.Any(style => style.Name == name)) continue;
            var style = cache.ServiceLocator.GetInstance<IStStyleFactory>().Create();
            cache.LangProject.StylesOC.Add(style);
            style.Name = name;
            style.Type = StyleType.kstParagraph;
        }
    });

    private static GdrShowResponse Show(string path, string id)
    {
        var shown = GdrQuery.Show(new GdrShowRequest(path, id));
        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        return shown.Value!;
    }

    private static string Markdown(string path, string id) => new GdrMarkdownView(Show(path, id), Path.GetFileNameWithoutExtension(path)).Text;

    private static string AsOf(string path, string id, string draft) =>
        new GdrMarkdownView(GdrQuery.Show(new GdrShowRequest(path, id, AsOf: draft)).Value!, Path.GetFileNameWithoutExtension(path))
            .Fields.Single(field => field.Name == "discussion").Body;

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

    private string ProjectWithGdrs(Action<LcmCache>? alsoSeed = null)
    {
        using var cache = pristine.NewScratch();
        SeededGdrs.Seed(cache);
        alsoSeed?.Invoke(cache);
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), RootFor(path)).Succeeded);
        return path;
    }

    private static string RootFor(string projectPath)
    {
        var root = Path.Combine(Path.GetDirectoryName(projectPath)!, "gdr-edit-root");
        Directory.CreateDirectory(root);
        return root;
    }
}
