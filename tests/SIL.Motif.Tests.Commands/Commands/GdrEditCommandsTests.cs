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
    public void EveryDirectionIsShownAsRightToLeftAndRoundTripsThroughEditApplyAndShow()
    {
        var path = ProjectWithGdrs();
        var markdown = Markdown(path, "GDR-1");
        Assert.Contains("rightToLeft=1", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("p128", markdown, StringComparison.Ordinal);
        var unchanged = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-1", markdown));
        Assert.True(unchanged.Succeeded, unchanged.Refusal?.Message);
        Assert.Equal(0, unchanged.Value!.OperationCount);

        // Flip the first paragraph's direction and add a right to left one.
        var body = new GdrMarkdownView(Show(path, "GDR-1"), "p").Fields.Single(field => field.Name == "discussion").Body;
        Assert.Contains("rightToLeft=1", body, StringComparison.Ordinal);
        var flipped = body.Replace("rightToLeft=1", "rightToLeft=0", StringComparison.Ordinal)
            + "\n\n::: {rightToLeft=1}\nnew right to left\n:::";
        var staged = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-1", flipped));
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        Assert.True(Apply(path).Succeeded);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), RootFor(path)).Succeeded);

        var shown = Show(path, "GDR-1");
        Assert.Equal([false, false, true], shown.Discussion.Select(paragraph => paragraph.RightToLeft));
        var after = Markdown(path, "GDR-1");
        Assert.Contains("rightToLeft=1", after, StringComparison.Ordinal);
        Assert.Contains("rightToLeft=0", after, StringComparison.Ordinal);
        Assert.DoesNotContain("p128", after, StringComparison.Ordinal);
        var again = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-1", after));
        Assert.True(again.Succeeded, again.Refusal?.Message);
        Assert.Equal(0, again.Value!.OperationCount);
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

        var decided = GdrEditCommands.Decide(new GdrDecideRequest(path, "GDR-2", "a", "b", "e.g. it fits \\*well\\*"));
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

    private static string[] StagedTexts(string path, string id, GdrEditResponse edited) =>
        GdrQuery.Show(new GdrShowRequest(path, id, AsOf: edited.DraftName)).Value!.Discussion
            .Select(paragraph => string.Concat(paragraph.Runs.Select(run => run.Text))).ToArray();

    [Fact]
    public void AStoredBlockIsShownAsARealFenceAndShowEditShowIsLossless()
    {
        var path = ProjectWithGdrs(cache => { HeadingStyles(cache); SeedStoredBlock(cache); });
        var markdown = Markdown(path, "GDR-2");

        Assert.Contains("\n```motif-query\nid: forms\ntitle: Forms\nrun: motif query forms\n```\n", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("\\`", markdown, StringComparison.Ordinal);
        var same = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-2", markdown));
        Assert.True(same.Succeeded, same.Refusal?.Message);
        Assert.Empty(same.Value!.Fields);
        Assert.Equal(0, same.Value.OperationCount);
    }

    [Fact]
    public void FencedBlocksNextToProseAndHeadingsAreStoredAsOneParagraphPerLineAndTheAppParserReadsThem()
    {
        var path = ProjectWithGdrs(HeadingStyles);
        var blocks = "### Evidence\n\nBefore.\n\n```motif-query\nid: forms\ntitle: Forms\n\n\nrun: motif query forms\n```\n\n" +
            "Between.\n\n```motif-show\nquery: forms\nshow: table\n```\n\n### After\n\nEnd.";

        var staged = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-2", blocks));

        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        Assert.Empty(staged.Value!.Warnings);
        var texts = StagedTexts(path, "GDR-2", staged.Value);
        Assert.Equal(["Evidence", "Before.", "```motif-query", "id: forms", "title: Forms", "", "", "run: motif query forms", "```",
            "Between.", "```motif-show", "query: forms", "show: table", "```", "After", "End."], texts);
        var found = SIL.Motif.Contract.Gdr.GuideQueryBlocks.Parse(string.Join("\n", texts));
        Assert.Equal("forms", Assert.Single(found.Queries).Id);
        Assert.Equal("forms", Assert.Single(found.Shows).Query);
        Assert.Empty(found.Diagnostics);
        var again = new GdrMarkdownView(GdrQuery.Show(new GdrShowRequest(path, "GDR-2", AsOf: staged.Value.DraftName)).Value!, "p")
            .Fields.Single(field => field.Name == "discussion").Body;
        Assert.Contains("```motif-query\nid: forms\ntitle: Forms\n\n\nrun: motif query forms\n```", again, StringComparison.Ordinal);
    }

    [Fact]
    public void AMalformedBlockIsKeptAsPlainTextWithAWarningAndTamilProseAroundABlockSurvives()
    {
        var path = ProjectWithGdrs();
        var tamil = "தமிழ் உரை முன்பு.\n\n```motif-query\nid: forms\nrun: motif query forms\n```\n\nஉரை பின்பு.\n\n```motif-show\nshow: table\n```";

        var staged = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-2", tamil));

        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        Assert.Contains(staged.Value!.Warnings, warning => warning.Contains("kept as plain text", StringComparison.Ordinal));
        Assert.Equal(["தமிழ் உரை முன்பு.", "```motif-query", "id: forms", "run: motif query forms", "```", "உரை பின்பு.",
            "```motif-show", "show: table", "```"], StagedTexts(path, "GDR-2", staged.Value));

        var unclosed = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-1", "Text.\n\n```motif-show\nquery: forms"));
        Assert.True(unclosed.Succeeded, unclosed.Refusal?.Message);
        Assert.NotEmpty(unclosed.Value!.Warnings);
        Assert.Equal(["Text.", "```motif-show", "query: forms"], StagedTexts(path, "GDR-1", unclosed.Value));
    }

    private static void SeedLines(LcmCache cache, string abbreviation, params (string Text, int Ws, string? Style)[] lines) =>
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var record = SeededGdrs.Records(cache).PossibilitiesOS.Single(item => item.Abbreviation.get_String(cache.DefaultAnalWs).Text == abbreviation);
            record.DiscussionOA = cache.ServiceLocator.GetInstance<IStTextFactory>().Create();
            foreach (var (text, ws, style) in lines)
            {
                var paragraph = cache.ServiceLocator.GetInstance<IStTxtParaFactory>().Create();
                record.DiscussionOA.ParagraphsOS.Add(paragraph);
                var builder = TsStringUtils.MakeStrBldr();
                var props = TsStringUtils.MakePropsBldr();
                props.SetIntPropValues((int)FwTextPropType.ktptWs, 0, ws);
                if (style is not null) props.SetStrPropValue((int)FwTextPropType.ktptNamedStyle, style);
                builder.Replace(0, 0, text, props.GetTextProps());
                paragraph.Contents = builder.GetString();
            }
        });

    [Fact]
    public void AStoredBlockLineWithACharacterStyleOrAnotherWritingSystemIsNotLiftedAndRoundTripsUnchanged()
    {
        foreach (var odd in new[] { "style", "ws" })
        {
            var path = ProjectWithGdrs(cache =>
            {
                var analysis = cache.DefaultAnalWs;
                var other = odd == "ws" ? cache.DefaultVernWs : analysis;
                SeedLines(cache, "GDR-2", ("```motif-query", analysis, null), ("id: forms", analysis, null),
                    ("title: Forms", other, odd == "style" ? SeededGdrs.StrongStyle : null), ("run: motif query forms", analysis, null), ("```", analysis, null));
            });
            var markdown = Markdown(path, "GDR-2");

            Assert.DoesNotContain("\n```motif-query\n", markdown, StringComparison.Ordinal);
            Assert.Contains("motif-query", markdown, StringComparison.Ordinal);
            var same = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-2", markdown));
            Assert.True(same.Succeeded, odd + ": " + same.Refusal?.Message);
            Assert.Equal(0, same.Value!.OperationCount);
        }
    }

    [Fact]
    public void BlocksAtTheStartAndEndAdjacentWithTrailingSpacesAndATildeFenceRoundTripUnchanged()
    {
        string[] lines = ["```motif-query", "id: forms", "title: Forms  ", "run: motif query forms", "```", "~~~motif-show", "query: forms", "show: table", "~~~"];
        var path = ProjectWithGdrs(cache => SeedLines(cache, "GDR-2", lines.Select(line => (line, cache.DefaultAnalWs, (string?)null)).ToArray()));
        var markdown = Markdown(path, "GDR-2");

        Assert.Contains("\n" + string.Join("\n", lines[..5]) + "\n\n" + string.Join("\n", lines[5..]) + "\n", markdown, StringComparison.Ordinal);
        var same = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-2", markdown));
        Assert.True(same.Succeeded, same.Refusal?.Message);
        Assert.Equal(0, same.Value!.OperationCount);

        var other = ProjectWithGdrs();
        var staged = GdrEditCommands.Edit(new GdrEditRequest(other, "GDR-2", string.Join("\n", lines[..5]) + "\n" + string.Join("\n", lines[5..])));
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        Assert.Equal(lines, StagedTexts(other, "GDR-2", staged.Value!));
    }

    [Fact]
    public void AnOtherCodeBlockIsStillRefusedWithNothingStaged()
    {
        var path = ProjectWithGdrs();

        var refused = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-2", "Before.\n\n```python\nprint(1)\n```"));

        Assert.Equal(RefusalCodes.GdrMarkdownInvalid, refused.Refusal!.Code);
        Assert.Contains("nothing was staged", refused.Refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("setParagraphs", PendingJson(path), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownSectionIsRefusedWithTheAddSectionLimitAndANoChangeEditIntoAMissingDraftIsRefused()
    {
        var path = ProjectWithGdrs();

        var unknown = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-99", "text"));
        Assert.Equal(RefusalCodes.GdrNotFound, unknown.Refusal!.Code);
        Assert.Contains("guide add", unknown.Refusal.Message, StringComparison.Ordinal);

        var noChange = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-1", Markdown(path, "GDR-1"), "no-such-draft"));
        Assert.Equal("draft.not-found", noChange.Refusal!.Code);
    }

    [Fact]
    public void ADecisionKeepsAFullStopAndWritesItsPartsAsGivenMarkdown()
    {
        var entry = GdrEditCommands.Entry(new GdrDecideRequest("p", "GDR-2", "a\\_b", "\\[c\\]", "see e.g."));

        Assert.Equal("- a\\_b or \\[c\\], I am choosing a\\_b, because see e.g.", entry);
        var body = "x\n\n### Decisions\n\n```text\n### Decisions\n\n\n- fenced\n```\n\n### Later";
        Assert.Equal("x\n\n### Decisions\n\n```text\n### Decisions\n\n\n- fenced\n```\n\n- new\n\n### Later",
            GdrEditCommands.WithDecision(body, "- new"));
    }

    [Fact]
    public void ADecisionPartWithAVernacularSpanStagesAVernacularRunAndShowReExportsTheSpan()
    {
        var path = ProjectWithGdrs(HeadingStyles);

        var decided = GdrEditCommands.Decide(new GdrDecideRequest(path, "GDR-2", "accept [alila]{.v}", "reject", "a [word]{.v} test"));
        Assert.True(decided.Succeeded, decided.Refusal?.Message);

        var shown = GdrQuery.Show(new GdrShowRequest(path, "GDR-2", AsOf: decided.Value!.DraftName)).Value!;
        var runs = shown.Discussion.SelectMany(paragraph => paragraph.Runs).ToArray();
        var vernacular = runs.Where(run => run.Text is "alila" or "word").ToArray();
        Assert.Equal(["alila", "alila", "word"], vernacular.Select(run => run.Text).ToArray());
        Assert.All(vernacular, run => Assert.Equal(shown.DefaultVernacular, run.WritingSystem));
        Assert.All(runs.Where(run => run.Text.Contains("because", StringComparison.Ordinal)), run => Assert.NotEqual(shown.DefaultVernacular, run.WritingSystem));
        var markdown = AsOf(path, "GDR-2", decided.Value.DraftName);
        Assert.Contains("- accept [alila]{.v} or reject, I am choosing accept [alila]{.v}, because a [word]{.v} test.", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEscapedBracketStaysLiteralAndPlainTextIsUnchanged()
    {
        var path = ProjectWithGdrs(HeadingStyles);

        var decided = GdrEditCommands.Decide(new GdrDecideRequest(path, "GDR-2", "a \\[b\\]", "c", "plain words"));
        Assert.True(decided.Succeeded, decided.Refusal?.Message);

        var text = string.Concat(GdrQuery.Show(new GdrShowRequest(path, "GDR-2", AsOf: decided.Value!.DraftName)).Value!
            .Discussion.SelectMany(paragraph => paragraph.Runs).Select(run => run.Text));
        Assert.Contains("a [b] or c, I am choosing a [b], because plain words.", text, StringComparison.Ordinal);
        Assert.Equal("- chosen or other, I am choosing chosen, because it fits the evidence.",
            GdrEditCommands.Entry(new GdrDecideRequest("p", "GDR-2", "chosen", "other", "it fits the evidence")));
    }

    [Theory]
    [InlineData("*a", "c", "d*", "--choosing and --because has an unbalanced marker", "--over")]
    [InlineData("a [b", "c", "d](http://x) e", "--choosing and --because has an unbalanced marker", "--over")]
    [InlineData("a", "_c", "d_", "--over and --because has an unbalanced marker", "--choosing")]
    [InlineData("`a", "b", "c`", "--choosing has an unbalanced marker", "--over")]
    [InlineData("a\\", "c", "d", "--choosing has an unbalanced marker", "--over")]
    [InlineData("a", "c", "[b]{ws=", "--because is not inline Markdown", "--over")]
    [InlineData("[a", "c", "b]{ws=en}", "--choosing and --because has an unbalanced marker", "--over")]
    public void AnUnbalancedMarkerThatWouldPairAcrossPartsIsRefusedNamingOnlyThoseParts(string chosen, string over, string because,
        string named, string notNamed)
    {
        var path = ProjectWithGdrs(HeadingStyles);

        var refused = GdrEditCommands.Decide(new GdrDecideRequest(path, "GDR-2", chosen, over, because));

        Assert.Equal(RefusalCodes.GdrDecisionInvalid, refused.Refusal!.Code);
        Assert.StartsWith(named, refused.Refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(notNamed, refused.Refusal.Message, StringComparison.Ordinal);
        Assert.Contains("\\<", refused.Refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a\\*b", "c", "d")]
    [InlineData("a \\_ b", "c \\` d", "e \\< f \\[ g \\\\ h")]
    [InlineData("*a*", "_b_", "**c**")]
    [InlineData("a < b", "<c", "d>")]
    [InlineData("[x]{.v}", "[y]{.v}", "[z]{.v}")]
    [InlineData("accept [alila]{ws=en}", "reject", "x")]
    [InlineData("plain words", "other words", "it fits.")]
    public void BalancedPartsAreStagedAndReadPartByPart(string chosen, string over, string because)
    {
        var path = ProjectWithGdrs(HeadingStyles);

        var decided = GdrEditCommands.Decide(new GdrDecideRequest(path, "GDR-2", chosen, over, because));

        Assert.True(decided.Succeeded, decided.Refusal?.Message);
    }

    [Fact]
    public void ALoneMarkerInOnePartMakesNoEmphasisWithAnother()
    {
        var path = ProjectWithGdrs(HeadingStyles);

        Assert.False(GdrEditCommands.Decide(new GdrDecideRequest(path, "GDR-2", "*a", "c", "d*")).Succeeded);
        var escaped = GdrEditCommands.Decide(new GdrDecideRequest(path, "GDR-2", "a \\* b", "c", "d \\* e"));
        Assert.True(escaped.Succeeded, escaped.Refusal?.Message);
        var runs = GdrQuery.Show(new GdrShowRequest(path, "GDR-2", AsOf: escaped.Value!.DraftName)).Value!
            .Discussion.SelectMany(paragraph => paragraph.Runs).ToArray();
        Assert.Contains("a * b or c, I am choosing a * b, because d * e.", string.Concat(runs.Select(run => run.Text)), StringComparison.Ordinal);
        Assert.All(runs.Where(run => run.Text.Contains("b or c", StringComparison.Ordinal)), run => Assert.Equal(runs[^1].Properties, run.Properties));
    }

    [Theory]
    [InlineData("# heading", "b", "c")]
    [InlineData("a", "- item", "c")]
    [InlineData("a", "b", "> quote")]
    [InlineData("a", "b", "```")]
    public void ADecisionPartThatWouldBeABlockIsRefused(string chosen, string over, string because)
    {
        var path = ProjectWithGdrs(HeadingStyles);

        var refused = GdrEditCommands.Decide(new GdrDecideRequest(path, "GDR-2", chosen, over, because));

        Assert.Equal(RefusalCodes.GdrDecisionInvalid, refused.Refusal!.Code);
        Assert.Contains("inline", refused.Refusal.Message, StringComparison.OrdinalIgnoreCase);
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

    private static string Pending(string path) => ProjectStoreCommand.Run(path, MotifProductVersion.CurrentText,
        (database, _) => CommandOutcome<string>.Success(new ProposalRepository(database).FindPendingChangesDraft()?.ProposalJson ?? "")).Value!;

    [Fact]
    public void ABatchStagesEveryRowUnderItsSectionInFileOrder()
    {
        var path = ProjectWithGdrs(HeadingStyles);
        var rows = GdrEditCommands.ParseDecisions(
            "{\"section\":\"GDR-2\",\"choosing\":\"a\",\"over\":\"b\",\"because\":\"one\"}\n\n" +
            "{\"section\":\"GDR-1\",\"choosing\":\"c\",\"over\":\"d\",\"because\":\"two\"}\n" +
            "{\"section\":\"GDR-2\",\"choosing\":\"e\",\"over\":\"f\",\"because\":\"three.\"}\n");
        Assert.True(rows.Succeeded, rows.Refusal?.Message);
        Assert.Equal([1, 3, 4], rows.Value!.Select(row => row.Row).ToArray());

        var staged = GdrEditCommands.DecideBatch(new GdrDecideBatchRequest(path, rows.Value!));

        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        Assert.True(staged.Value!.Staged);
        Assert.Equal(["GDR-2", "GDR-1", "GDR-2"], staged.Value.Rows.Select(row => row.Abbreviation).ToArray());
        var two = AsOf(path, "GDR-2", staged.Value.DraftName).Replace("\\", "", StringComparison.Ordinal);
        Assert.Contains("- a or b, I am choosing a, because one.\n- e or f, I am choosing e, because three.", two, StringComparison.Ordinal);
        Assert.Contains("- c or d, I am choosing c, because two.", AsOf(path, "GDR-1", staged.Value.DraftName).Replace("\\", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("raw html", "the <NP> node")]
    [InlineData("a citation", "see @smith2001")]
    public void ABadRowRefusesTheWholeBatchNamesItsLineAndStagesNothing(string why, string bad)
    {
        var path = ProjectWithGdrs(HeadingStyles);
        var jsonl = "{\"section\":\"GDR-2\",\"choosing\":\"a\",\"over\":\"b\",\"because\":\"fine\"}\n" +
                    "{\"section\":\"GDR-1\",\"choosing\":\"" + bad.Replace("\"", "\\\"", StringComparison.Ordinal) + "\",\"over\":\"d\",\"because\":\"two\"}\n";
        var rows = GdrEditCommands.ParseDecisions(jsonl).Value!;
        var before = Pending(path);

        var refused = GdrEditCommands.DecideBatch(new GdrDecideBatchRequest(path, rows));

        Assert.False(refused.Succeeded, why);
        Assert.Equal(RefusalCodes.GdrDecisionsInvalid, refused.Refusal!.Code);
        Assert.Equal("2", refused.Refusal.Facts["row"]);
        Assert.Contains("Line 2", refused.Refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Nothing was staged", refused.Refusal.Message, StringComparison.Ordinal);
        Assert.Equal(before, Pending(path));
    }

    [Fact]
    public void AnUnknownSectionOrAnUnreadableLineRefusesTheBatchWithItsRow()
    {
        var path = ProjectWithGdrs(HeadingStyles);
        var missing = GdrEditCommands.DecideBatch(new GdrDecideBatchRequest(path, [new GdrDecisionRow(7, "GDR-99", "a", "b", "c")]));
        Assert.Equal(RefusalCodes.GdrNotFound, missing.Refusal!.Code);
        Assert.Equal("7", missing.Refusal.Facts["row"]);

        foreach (var (text, line) in new[]
        {
            ("{\"section\":\"GDR-2\",\"choosing\":\"a\"}", "1"),
            ("{\"section\":\"GDR-2\",\"choosing\":\"a\",\"over\":\"b\",\"because\":\"c\"}\nnot json", "2"),
            ("{\"section\":\"GDR-2\",\"choosing\":\"a\",\"over\":\"b\",\"because\":\"c\",\"nonsense\":\"1\"}", "1"),
            ("{\"section\":\"GDR-2\",\"choosing\":1,\"over\":\"b\",\"because\":\"c\"}", "1"),
        })
        {
            var parsed = GdrEditCommands.ParseDecisions(text);
            Assert.Equal(RefusalCodes.GdrDecisionsInvalid, parsed.Refusal!.Code);
            Assert.Equal(line, parsed.Refusal.Facts["row"]);
        }
        Assert.Equal(RefusalCodes.GdrDecisionsInvalid, GdrEditCommands.ParseDecisions("\n \n").Refusal!.Code);
    }

    [Fact]
    public void ADryRunReportsEachRowAndStagesNothing()
    {
        var path = ProjectWithGdrs(HeadingStyles);
        var before = Pending(path);

        var dry = GdrEditCommands.DecideBatch(new GdrDecideBatchRequest(path, [new GdrDecisionRow(1, "GDR-2", "a", "b", "c")], DryRun: true));

        Assert.True(dry.Succeeded, dry.Refusal?.Message);
        Assert.False(dry.Value!.Staged);
        Assert.Equal("- a or b, I am choosing a, because c.", Assert.Single(dry.Value.Rows).Entry);
        Assert.Equal(before, Pending(path));
    }

    [Fact]
    public void ALateFailureWhileStagingLeavesNoSectionStaged()
    {
        var path = ProjectWithGdrs(HeadingStyles);
        var good = GdrMarkdownImport.Read(Show(path, "GDR-1"), "p", "first body").Edits!;
        var moved = GdrMarkdownImport.Read(Show(path, "GDR-2"), "p", "second body").Edits! with { Identity = new GdrMarkdownIdentity("GDR-99", null, 0) };

        var refused = ProposalCommands.StageGdrMarkdownBatch(path, [good, moved]);

        Assert.False(refused.Succeeded);
        Assert.DoesNotContain("first body", Pending(path), StringComparison.Ordinal);
        Assert.True(ProposalCommands.StageGdrMarkdownBatch(path, [good]).Succeeded);
        Assert.Contains("first body", Pending(path), StringComparison.Ordinal);
    }

    [Fact]
    public void RungAndTriedAreWrittenIntoTheReasonAndTheOptionSetAside()
    {
        var rows = GdrEditCommands.ParseDecisions(
            "{\"section\":\"GDR-2\",\"choosing\":\"a\",\"tried\":\"b, c\",\"because\":\"r\",\"rung\":\"2\"}\n" +
            "{\"section\":\"GDR-2\",\"choosing\":\"a\",\"over\":\"x\",\"tried\":\"y\",\"because\":\"r\"}\n").Value!;

        Assert.Equal(("b, c", "r (rung: 2)"), (rows[0].Over, rows[0].Because));
        Assert.Equal("x (tried: y)", rows[1].Over);
        Assert.Contains("lacks the key \"over\"", GdrEditCommands.ParseDecisions("{\"section\":\"GDR-2\",\"choosing\":\"a\",\"because\":\"r\"}").Refusal!.Message, StringComparison.Ordinal);
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
