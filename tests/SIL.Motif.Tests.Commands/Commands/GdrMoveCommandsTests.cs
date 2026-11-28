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

/// <summary>Moving a Guide section with its subtree under another parent, keeping its GDR number and id.</summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class GdrMoveCommandsTests(PristineProjectFixture pristine)
{
    // Seed: GDR-1 { GDR-1a }, GDR-2, title-3; then GDR-3 "A" and GDR-4 "B" are added at the top and applied.
    private string Project()
    {
        var path = ProjectWith(cache => SeededGdrs.Seed(cache));
        Assert.True(GdrAddCommands.Add(new GdrAddRequest(path, "A")).Succeeded);
        Assert.True(GdrAddCommands.Add(new GdrAddRequest(path, "B")).Succeeded);
        Applied(path);
        return path;
    }

    private static string[] Titles(string path) => GdrQuery.List(new GdrListRequest(path)).Value!.Records.Select(record => record.Title).ToArray();

    [Fact]
    public void MovingASectionWithItsSubtreeUnderANewParentKeepsNumbersAndIdsAndAppliesInPlace()
    {
        var path = Project();
        var before = GdrQuery.List(new GdrListRequest(path)).Value!.Records;
        var moved = GdrMoveCommands.Move(new GdrMoveRequest(path, "GDR-1", Parent: "GDR-4"));

        Assert.True(moved.Succeeded, moved.Refusal?.Message);
        Assert.Equal("GDR-1", moved.Value!.Abbreviation);
        Assert.Equal(before.Single(r => r.Abbreviation == "GDR-4").Id, moved.Value.ParentId);
        Assert.Null(moved.Value.PreviousParentId);
        Assert.Equal(before.Select(r => r.ParentId), GdrQuery.List(new GdrListRequest(path)).Value!.Records.Select(r => r.ParentId));

        Applied(path);

        var after = GdrQuery.List(new GdrListRequest(path)).Value!.Records;
        Assert.Equal(["title-2", "title-3", "A", "B", "title-1", "title-1a"], after.Select(r => r.Title));
        Assert.Equal(before.Single(r => r.Abbreviation == "GDR-1").Id, after.Single(r => r.Abbreviation == "GDR-1").Id);
        Assert.Equal(after.Single(r => r.Abbreviation == "GDR-4").Id, after.Single(r => r.Abbreviation == "GDR-1").ParentId);
        Assert.Equal(after.Single(r => r.Abbreviation == "GDR-1").Id, after.Single(r => r.Abbreviation == "GDR-1a").ParentId);
    }

    [Fact]
    public void AfterAndBeforePlaceTheSectionAmongItsNewSiblingsAndAMoveToTheTopLevelIsAllowed()
    {
        var path = Project();
        Assert.True(GdrMoveCommands.Move(new GdrMoveRequest(path, "GDR-4", After: "GDR-1")).Succeeded);
        Assert.True(GdrMoveCommands.Move(new GdrMoveRequest(path, "GDR-1a", Parent: "GDR-2", Draft: null)).Succeeded);
        Applied(path);
        Assert.Equal(["title-1", "B", "title-2", "title-1a", "title-3", "A"], Titles(path));
    }

    [Fact]
    public void BeforePutsTheSectionFirstAmongSiblingsAndMovingToTopLevelWithNoParentWorks()
    {
        var path = Project();
        Assert.True(GdrMoveCommands.Move(new GdrMoveRequest(path, "GDR-3", Before: "GDR-1")).Succeeded);
        Assert.True(GdrMoveCommands.Move(new GdrMoveRequest(path, "GDR-1a")).Succeeded);
        Applied(path);
        Assert.Equal(["A", "title-1", "title-2", "title-3", "B", "title-1a"], Titles(path));
        Assert.Null(GdrQuery.List(new GdrListRequest(path)).Value!.Records.Single(r => r.Abbreviation == "GDR-1a").ParentId);
    }

    [Fact]
    public void AMissingSectionParentOrSiblingIsRefusedNotFoundWithANextStep()
    {
        var path = Project();
        foreach (var request in new[]
        {
            new GdrMoveRequest(path, "GDR-99", Parent: "GDR-1"),
            new GdrMoveRequest(path, "GDR-1", Parent: "GDR-99"),
            new GdrMoveRequest(path, "GDR-3", After: "GDR-99"),
        })
        {
            var refused = GdrMoveCommands.Move(request).Refusal!;
            Assert.Equal(RefusalCodes.GdrNotFound, refused.Code);
            Assert.NotNull(RefusalCatalog.Specs[refused.Code].Next);
        }
    }

    [Fact]
    public void MovingASectionUnderItselfOrItsOwnDescendantIsRefusedAndStagesNothing()
    {
        var path = Project();
        foreach (var parent in new[] { "GDR-1", "GDR-1a" })
        {
            var refused = GdrMoveCommands.Move(new GdrMoveRequest(path, "GDR-1", Parent: parent));
            Assert.Equal(RefusalCodes.GdrMoveCycle, refused.Refusal!.Code);
            Assert.NotNull(RefusalCatalog.Specs[RefusalCodes.GdrMoveCycle].Next);
        }
        Assert.Equal(["title-1", "title-1a", "title-2", "title-3", "A", "B"], Titles(path));
        var pending = GdrQuery.Show(new GdrShowRequest(path, "GDR-1", AsOf: SIL.Motif.Commands.PendingChanges.DraftName));
        Assert.True(pending.Succeeded is false || pending.Value!.Record.ParentId is null);
    }

    [Fact]
    public void ADraftThatAlreadyMovesTheSectionIsStaleAndASecondMoveIsRefused()
    {
        var path = Project();
        Assert.True(GdrMoveCommands.Move(new GdrMoveRequest(path, "GDR-1", Parent: "GDR-4")).Succeeded);
        var refused = GdrMoveCommands.Move(new GdrMoveRequest(path, "GDR-1", Parent: "GDR-3"));
        Assert.Equal(RefusalCodes.GdrMoveStale, refused.Refusal!.Code);
        Assert.NotNull(RefusalCatalog.Specs[RefusalCodes.GdrMoveStale].Next);
        Assert.Equal(RefusalCodes.DraftNotFound, GdrMoveCommands.Move(new GdrMoveRequest(path, "GDR-2", Parent: "GDR-3", Draft: "nope")).Refusal!.Code);
    }

    [Fact]
    public void EditThenMoveOfTheSameSectionBothApplyAndTheBodyIsKept()
    {
        var path = Project();
        Assert.True(GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-2", "### How it works\n\nedited")).Succeeded);
        Assert.True(GdrMoveCommands.Move(new GdrMoveRequest(path, "GDR-2", Parent: "GDR-3")).Succeeded);
        Applied(path);
        var records = GdrQuery.List(new GdrListRequest(path)).Value!.Records;
        Assert.Equal(records.Single(r => r.Abbreviation == "GDR-3").Id, records.Single(r => r.Abbreviation == "GDR-2").ParentId);
        Assert.Contains("edited", new GdrMarkdownView(GdrQuery.Show(new GdrShowRequest(path, "GDR-2")).Value!, "p").Text);
    }

    [Fact]
    public void MovingAnAncestorAfterAMoveAppliesAndMovingItUnderItsNewDescendantIsACycle()
    {
        var path = Project();
        Assert.True(GdrMoveCommands.Move(new GdrMoveRequest(path, "GDR-1a", Parent: "GDR-2")).Succeeded);
        Assert.Equal(RefusalCodes.GdrMoveCycle, GdrMoveCommands.Move(new GdrMoveRequest(path, "GDR-2", Parent: "GDR-1a")).Refusal!.Code);
        Assert.True(GdrMoveCommands.Move(new GdrMoveRequest(path, "GDR-2", Parent: "GDR-4")).Succeeded);
        Applied(path);
        var records = GdrQuery.List(new GdrListRequest(path)).Value!.Records;
        Assert.Equal(records.Single(r => r.Abbreviation == "GDR-2").Id, records.Single(r => r.Abbreviation == "GDR-1a").ParentId);
        Assert.Equal(records.Single(r => r.Abbreviation == "GDR-4").Id, records.Single(r => r.Abbreviation == "GDR-2").ParentId);
    }

    [Fact]
    public void AfterWithBeforeOrASiblingUnderAnotherParentIsRefusedWithNextAndStagesNothing()
    {
        var path = Project();
        foreach (var request in new[]
        {
            new GdrMoveRequest(path, "GDR-3", After: "GDR-1", Before: "GDR-2"),
            new GdrMoveRequest(path, "GDR-3", Parent: "GDR-2", After: "GDR-1"),
            new GdrMoveRequest(path, "GDR-3", After: "GDR-3"),
        })
        {
            var refused = GdrMoveCommands.Move(request).Refusal!;
            Assert.Equal(RefusalCodes.GdrMovePosition, refused.Code);
            Assert.NotNull(RefusalCatalog.Specs[refused.Code].Next);
        }
        Assert.Equal(["title-1", "title-1a", "title-2", "title-3", "A", "B"], Titles(path));
    }

    [Fact]
    public void TheDraftPreviewShowsTheStagedParentWhileTheBaselineKeepsTheOldOne()
    {
        var path = Project();
        var parent = GdrQuery.List(new GdrListRequest(path)).Value!.Records.Single(r => r.Abbreviation == "GDR-4").Id;
        Assert.True(GdrMoveCommands.Move(new GdrMoveRequest(path, "GDR-1", Parent: "GDR-4")).Succeeded);
        Assert.Equal(parent, GdrQuery.Show(new GdrShowRequest(path, "GDR-1", AsOf: SIL.Motif.Commands.PendingChanges.DraftName)).Value!.Record.ParentId);
        Assert.Null(GdrQuery.Show(new GdrShowRequest(path, "GDR-1")).Value!.Record.ParentId);
    }

    [Fact]
    public void AMoveWhoseRecordedParentIsNoLongerTheSectionsParentIsRefusedAtApplyAsStale()
    {
        using var cache = pristine.NewScratch();
        SeededGdrs.Seed(cache);
        var child = cache.ServiceLocator.GetInstance<ICmPossibilityRepository>().AllInstances().Single(p => p.Abbreviation.AnalysisDefaultWritingSystem.Text == "GDR-1a");
        var operation = new SIL.Motif.Contract.Model.OperationEnvelope(SIL.Motif.Contract.Ids.CanonicalId.Mint(), SIL.Motif.Runner.Operations.GdrSectionOperationKinds.Move,
            target: SIL.Motif.Contract.Ids.CanonicalId.FromGuid(child.Guid),
            after: System.Text.Json.JsonSerializer.SerializeToElement(new { from = SIL.Motif.Contract.Ids.CanonicalId.Mint().Value }));
        var thrown = Assert.ThrowsAny<SIL.Motif.Runner.Execution.ProposalRefusalException>(() =>
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                SIL.Motif.Runner.Operations.OperationHandlerRegistry.Resolve(operation.Kind, "test")
                    .ApplyAndCaptureEffect(cache, operation, new List<SIL.Motif.Contract.Ids.CanonicalId>())));
        Assert.Equal(RefusalCodes.GdrMoveStale, thrown.Code);
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
        var root = Path.Combine(Path.GetDirectoryName(projectPath)!, "gdr-move-root");
        Directory.CreateDirectory(root);
        return root;
    }
}
