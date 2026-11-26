using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Gdr;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>Grammar Guide authoring of new sections: staged in a Draft, applied, and read back.</summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class GdrAddCommandsTests(PristineProjectFixture pristine)
{
    private const string TamilTitle = "வினைச்சொல் இணைப்பு";
    private const string TamilBody = "### How it works\n\nவினைச்சொல் வேர் + விகுதி.\n\n### Examples\n\nநடந்தான்";

    [Fact]
    public void AddOnAProjectWithNoListCreatesTheListAndGdr1AndAppliesWithATamilTitleAndBodyByteExact()
    {
        var path = ProjectWithoutList();
        Assert.Empty(GdrQuery.List(new GdrListRequest(path)).Value!.Records);

        var added = GdrAddCommands.Add(new GdrAddRequest(path, TamilTitle, Markdown: TamilBody, WritingSystem: "en"));

        Assert.True(added.Succeeded, added.Refusal?.Message);
        Assert.Equal("GDR-1", added.Value!.Abbreviation);
        Assert.True(added.Value.ListCreated);
        Assert.Equal(["discussion"], added.Value.Fields);
        Assert.Empty(GdrQuery.List(new GdrListRequest(path)).Value!.Records);

        Applied(path);

        var record = Assert.Single(GdrQuery.List(new GdrListRequest(path)).Value!.Records);
        Assert.Equal("GDR-1", record.Abbreviation);
        // LCModel stores NFD (FieldWorks convention); the text is canonically equal and byte-exact in NFD.
        Assert.Equal(TamilTitle.Normalize(System.Text.NormalizationForm.FormD), record.Title);
        var shown = GdrQuery.Show(new GdrShowRequest(path, "GDR-1")).Value!;
        Assert.Equal(TamilBody.Normalize(System.Text.NormalizationForm.FormD), new GdrMarkdownView(shown, "p").Fields.Single(field => field.Name == "discussion").Body);
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(TamilTitle.Normalize(System.Text.NormalizationForm.FormD)), System.Text.Encoding.UTF8.GetBytes(shown.Record.Title));
        Assert.Equal(TamilTitle, shown.Record.Title.Normalize(System.Text.NormalizationForm.FormC));
    }

    [Fact]
    public void ThreeAddsIntoThePendingChangesApplyOnceAndNumberGdr1To3InOrder()
    {
        var path = ProjectWithoutList();
        foreach (var title in new[] { "one", "two", "three" })
        {
            var added = GdrAddCommands.Add(new GdrAddRequest(path, title));
            Assert.True(added.Succeeded, added.Refusal?.Message);
        }

        Applied(path);

        Assert.Equal([("GDR-1", "one"), ("GDR-2", "two"), ("GDR-3", "three")],
            GdrQuery.List(new GdrListRequest(path)).Value!.Records.Select(record => (record.Abbreviation, record.Title)));
    }

    [Fact]
    public void AddThenShowThenEditThenApplyOnASeededGuideKeepsPositionAndParent()
    {
        var path = ProjectWithGdrs();
        var part = GdrAddCommands.Add(new GdrAddRequest(path, "A new part"));
        Assert.True(part.Succeeded, part.Refusal?.Message);
        Assert.False(part.Value!.ListCreated);
        Assert.Equal("GDR-3", part.Value.Abbreviation);
        var child = GdrAddCommands.Add(new GdrAddRequest(path, "Child", Parent: "GDR-1", After: "GDR-1a", Markdown: "### How it works\n\nbody"));
        Assert.True(child.Succeeded, child.Refusal?.Message);
        var between = GdrAddCommands.Add(new GdrAddRequest(path, "Between", After: "GDR-1"));
        Assert.True(between.Succeeded, between.Refusal?.Message);
        Applied(path);

        var records = GdrQuery.List(new GdrListRequest(path)).Value!.Records;
        Assert.Equal(["title-1", "title-1a", "Child", "Between", "title-2", "title-3", "A new part"], records.Select(record => record.Title));
        Assert.Equal(records.Single(record => record.Title == "title-1").Id, records.Single(record => record.Title == "Child").ParentId);
        Assert.Null(records.Single(record => record.Title == "Between").ParentId);
        var shown = GdrQuery.Show(new GdrShowRequest(path, child.Value!.Abbreviation)).Value!;
        Assert.Equal("### How it works\n\nbody", new GdrMarkdownView(shown, "p").Fields.Single(field => field.Name == "discussion").Body);

        var edited = GdrEditCommands.Edit(new GdrEditRequest(path, child.Value.Abbreviation, "### How it works\n\nedited"));
        Assert.True(edited.Succeeded, edited.Refusal?.Message);
        Applied(path);
        Assert.Contains("edited", new GdrMarkdownView(GdrQuery.Show(new GdrShowRequest(path, child.Value.Abbreviation)).Value!, "p").Text);
    }

    [Fact]
    public void ANumberIsNeverReusedAfterTheHighestItemIsDeleted() => Assert.Equal("GDR-4", AddDeleteThenAdd(_ => { }, clearDescription: false));

    [Fact]
    public void AClearedListDescriptionStillNeverReusesANumberWhileTheStoreHoldsTheMark() =>
        Assert.Equal("GDR-4", AddDeleteThenAdd(_ => { }, clearDescription: true));

    [Fact]
    public void TheListDescriptionMarkAloneKeepsANumberFromBeingReusedOnAFreshStore() =>
        Assert.Equal("GDR-4", AddDeleteThenAdd(path => _ = ProjectStoreCommand.Run(path, MotifProductVersion.CurrentText, (database, _) =>
        {
            File.Delete(new GuideNumberMarkRepository(database).MarkPath);
            return CommandOutcome<string>.Success("cleared");
        }), clearDescription: false));

    [Fact]
    public void ANumberInAnOpenDraftIsNotTakenAgain()
    {
        var path = ProjectWithGdrs();
        var first = GdrAddCommands.Add(new GdrAddRequest(path, "one", Draft: null));
        var second = GdrAddCommands.Add(new GdrAddRequest(path, "two"));
        Assert.Equal(["GDR-3", "GDR-4"], [first.Value!.Abbreviation, second.Value!.Abbreviation]);
    }

    private string AddDeleteThenAdd(Action<string> beforeSecondAdd, bool clearDescription)
    {
        var path = ProjectWithGdrs();
        var first = GdrAddCommands.Add(new GdrAddRequest(path, "added"));
        Assert.True(first.Succeeded, first.Refusal?.Message);
        Assert.Equal("GDR-3", first.Value!.Abbreviation);
        Applied(path);

        using (var cache = new FwDataProjectLoader().LoadScratchCache(path))
        {
            var added = cache.ServiceLocator.GetInstance<ICmPossibilityRepository>().AllInstances()
                .Single(item => item.Abbreviation.get_String(cache.DefaultAnalWs).Text == "GDR-3");
            var list = cache.ServiceLocator.GetInstance<ICmPossibilityListRepository>().AllInstances()
                .Single(item => item.Name.get_String(cache.DefaultAnalWs).Text == "Grammar Guide");
            Assert.Contains("GDR-3", list.Description.get_String(cache.DefaultAnalWs).Text, StringComparison.Ordinal);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                added.Delete();
                if (clearDescription) list.Description.set_String(cache.DefaultAnalWs, TsStringUtils.MakeString("", cache.DefaultAnalWs));
            });
            new FwDataProjectLoader().Save(cache);
        }
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), RootFor(path)).Succeeded);
        Assert.DoesNotContain(GdrQuery.List(new GdrListRequest(path)).Value!.Records, record => record.Abbreviation == "GDR-3");
        beforeSecondAdd(path);

        var second = GdrAddCommands.Add(new GdrAddRequest(path, "after the delete"));
        Assert.True(second.Succeeded, second.Refusal?.Message);
        return second.Value!.Abbreviation;
    }

    [Fact]
    public void TheNewGuideListHasThePropertiesFieldWorksGivesACustomList()
    {
        var path = ProjectWithoutList();
        Assert.True(GdrAddCommands.Add(new GdrAddRequest(path, "first")).Succeeded);
        Applied(path);

        using var cache = new FwDataProjectLoader().LoadScratchCache(path);
        var lists = cache.ServiceLocator.GetInstance<ICmPossibilityListRepository>();
        var guide = lists.AllInstances().Single(list => list.Name.get_String(cache.DefaultAnalWs).Text == "Grammar Guide");
        ICmPossibilityList reference = null!;
        // The calls of xWorks CustomListDlg for a hierarchical, unsorted, analysis list.
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            reference = cache.ServiceLocator.GetInstance<ICmPossibilityListFactory>().CreateUnowned("Reference", cache.DefaultAnalWs);
            reference.DisplayOption = (int)PossNameType.kpntName;
            reference.PreventDuplicates = false;
            reference.IsSorted = false;
            reference.WsSelector = WritingSystemServices.kwsAnals;
            reference.IsVernacular = false;
            reference.Depth = 127;
        });
        Assert.Equal(reference.ItemClsid, guide.ItemClsid);
        Assert.Equal(CmCustomItemTags.kClassId, guide.ItemClsid);
        Assert.Equal(reference.Depth, guide.Depth);
        Assert.Equal(127, guide.Depth);
        Assert.Equal(reference.IsSorted, guide.IsSorted);
        Assert.Equal(reference.DisplayOption, guide.DisplayOption);
        Assert.Equal(reference.WsSelector, guide.WsSelector);
        Assert.Equal(reference.IsVernacular, guide.IsVernacular);
        Assert.Equal(reference.PreventDuplicates, guide.PreventDuplicates);
        Assert.Equal(reference.Owner, guide.Owner);
        Assert.Null(guide.Owner);
    }

    [Fact]
    public void AnUnknownWritingSystemIsRefusedAVernacularOneIsAcceptedAndAfterAtTheTopLevelReportsNoParent()
    {
        string vernacular = "";
        var path = ProjectWith(cache =>
        {
            SeededGdrs.Seed(cache);
            vernacular = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        });
        Assert.NotEmpty(vernacular);
        Assert.Equal(RefusalCodes.GdrTitleInvalid, GdrAddCommands.Add(new GdrAddRequest(path, "x", WritingSystem: "zz-unknown")).Refusal!.Code);
        var vern = GdrAddCommands.Add(new GdrAddRequest(path, "x", WritingSystem: vernacular));
        Assert.True(vern.Succeeded, vern.Refusal?.Message);
        var after = GdrAddCommands.Add(new GdrAddRequest(path, "after one", After: "GDR-1"));
        Assert.True(after.Succeeded, after.Refusal?.Message);
        Assert.Null(after.Value!.ParentId);
    }

    [Fact]
    public void AMarkdownBodyThatCannotBeReadStagesNothingAndABlankTitleIsRefused()
    {
        var path = ProjectWithGdrs();
        var fenced = GdrAddCommands.Add(new GdrAddRequest(path, "x", Markdown: "```python\nprint(1)\n```"));
        Assert.False(fenced.Succeeded);
        Assert.Equal(RefusalCodes.GdrMarkdownInvalid, fenced.Refusal!.Code);
        Assert.Equal(RefusalCodes.GdrTitleInvalid, GdrAddCommands.Add(new GdrAddRequest(path, " ")).Refusal!.Code);
        Assert.Equal(RefusalCodes.GdrNotFound, GdrAddCommands.Add(new GdrAddRequest(path, "x", Parent: "GDR-99")).Refusal!.Code);
        var attempt = TryApply(path);
        Assert.False(attempt.Succeeded && attempt.Value!.Applied);
    }

    [Fact]
    public void AListStillNamedGrammarDesignRecordsAndTwoCandidateListsAreRefused()
    {
        var old = ProjectWithGdrs(SeededGdrs.SeedOldListName);
        Assert.Equal(RefusalCodes.GdrListNotWritable, GdrAddCommands.Add(new GdrAddRequest(old, "x")).Refusal!.Code);
        var two = ProjectWithGdrs(SeededGdrs.SeedSecondList);
        Assert.Equal(RefusalCodes.GdrAmbiguous, GdrAddCommands.Add(new GdrAddRequest(two, "x")).Refusal!.Code);
    }

    private static CommandOutcome<ApplyPendingResult> TryApply(string path)
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
        var applied = TryApply(path);
        Assert.True(applied.Succeeded, applied.Refusal?.Code + ": " + applied.Refusal?.Message);
        Assert.True(applied.Value!.Applied, applied.Value.Summary);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), RootFor(path)).Succeeded);
    }

    private string ProjectWithoutList() => ProjectWith(null);

    private string ProjectWithGdrs(Action<LcmCache>? alsoSeed = null) => ProjectWith(cache =>
    {
        SeededGdrs.Seed(cache);
        alsoSeed?.Invoke(cache);
    });

    private string ProjectWith(Action<LcmCache>? seed)
    {
        using var cache = pristine.NewScratch();
        HeadingStyles(cache);
        seed?.Invoke(cache);
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), RootFor(path)).Succeeded);
        return path;
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

    private static string RootFor(string projectPath)
    {
        var root = Path.Combine(Path.GetDirectoryName(projectPath)!, "gdr-add-root");
        Directory.CreateDirectory(root);
        return root;
    }
}
