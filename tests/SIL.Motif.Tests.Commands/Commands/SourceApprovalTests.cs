using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Scripture;
using SIL.Motif.Tests.QueryScale;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Approving and clearing Occurrences of a Paratext Source through the pending changes, applied to disposable copies
/// of a project: the choice of analysis, the Opinion it carries, the guess-only path, stale approvals and bulk Approve.
/// </summary>
[Trait("MotifTestLevel", "Integration")]
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class SourceApprovalTests(PristineProjectFixture pristine)
{
    private sealed record World(string Path, string Folder, CanonicalId LampA, CanonicalId LampB, CanonicalId Keeps, CanonicalId LampWordform,
        IReadOnlyDictionary<string, CanonicalId> Extras);

    [Fact]
    public void ApprovingChoosesTheAnalysisAndApprovesItsUnknownOpinionAndASecondApplyHasNothingLeft()
    {
        var world = World_();
        Assert.Equal(Opinions.noopinion, Opinion(world.Path, world.LampA));

        var staged = Stage(world, "MAT 1:1#2", world.LampA);

        Assert.Equal((1, 0, 1), (staged.Approved, staged.Cleared, staged.Chapters));
        Assert.Contains(staged.Operations, operation => operation.Kind == "analysis/wfiAnalysis/addRefEvaluations");
        Assert.Contains(staged.Operations, operation => operation.Kind == ScriptureApprovalOperationKinds.Approve);
        Assert.NotEmpty(Pending(world.Path).Changes);
        Assert.Empty(Approvals(world));

        Applied(world.Path);

        Assert.Equal(Opinions.approves, Opinion(world.Path, world.LampA));
        var approval = Assert.Single(Approvals(world));
        Assert.Equal(("MAT 1:1#2", world.LampA.ToGuid(), ApprovalState.Approved), (approval.Anchor, approval.Analysis, approval.State));
        Assert.False(Apply(world.Path).Value?.Applied ?? false);
        Assert.Equal(0, Stage(world, "MAT 1:1#2", world.LampA).Approved);
    }

    [Fact]
    public void ApprovalsAreOneSubitemPerPassageAndInstallationUnderTheSourceInTheSortedSourcesList()
    {
        var world = World_();
        using (var before = new FwDataProjectLoader().LoadCache(world.Path))
        {
            Assert.False(HasField(before));
            Assert.True(ScriptureSourceLookup.FindList(before)!.IsSorted);
            // A project whose Sources list was made before it was sorted.
            NonUndoableUnitOfWorkHelper.Do(before.ActionHandlerAccessor, () => ScriptureSourceLookup.FindList(before)!.IsSorted = false);
            new FwDataProjectLoader().Save(before);
        }
        Capture(world.Path);
        Stage(world, "MAT 1:1#2", world.LampA);
        Stage(world, "MAT 1:2#2", world.LampA);
        Applied(world.Path);

        using var cache = new FwDataProjectLoader().LoadCache(world.Path);
        var sources = ScriptureSourceLookup.FindList(cache)!;
        Assert.True(sources.IsSorted);
        Assert.DoesNotContain(cache.ServiceLocator.GetInstance<ICmPossibilityListRepository>().AllInstances(),
            list => list.Name.BestAnalysisAlternative.Text.StartsWith("Motif Approvals", StringComparison.Ordinal));
        var source = Assert.Single(sources.PossibilitiesOS);
        var installation = ScriptureLocalStore.ForInstallation().InstallationId();
        Assert.Equal(installation, ScriptureLocalStore.ForInstallation().InstallationId());
        var item = Assert.Single(source.SubPossibilitiesOS);
        Assert.Equal(ScriptureApprovalFields.ItemGuid(source.Guid, "MAT 1", installation), item.Guid);
        Assert.Equal(("MAT 1", "WEB MAT 1 " + installation.ToString("N")[..8], ScriptureApprovalFields.ItemDescription),
            (ScriptureSourceLookup.IdOf(item), item.Name.BestAnalysisAlternative.Text, item.Description.BestAnalysisAlternative.Text));
        var blob = ScriptureApprovalFields.TryRead(cache, item, FieldId(cache), out _)!;
        Assert.Equal((source.Guid, "MAT 1", installation), (blob.Source, blob.Passage, blob.Installation));
        Assert.Equal(["MAT 1:1#2", "MAT 1:2#2"], blob.Entries.Keys);
        Assert.True(HasField(cache));
    }

    [Fact]
    public void ASourceHoldingApprovalsIsListedOnceAndASecondSourceCanStillBeAdded()
    {
        var world = World_();
        Stage(world, "MAT 1:1#2", world.LampA);
        Stage(world, "MAT 2:1#2", world.LampA);
        Applied(world.Path);
        var added = ProposalCommands.StageSourceAdd(new SourceAddRequest(world.Path, "WEB2", "Second", "English", "MAT", Folder: world.Folder));
        Assert.True(added.Succeeded, added.Refusal?.Message);
        Applied(world.Path);

        using (var cache = new FwDataProjectLoader().LoadCache(world.Path))
        {
            Assert.Equal(2, Assert.Single(ScriptureSourceLookup.FindList(cache)!.PossibilitiesOS, item => ScriptureSourceLookup.IdOf(item) == "WEB")
                .SubPossibilitiesOS.Count);
            Assert.Equal(["WEB", "WEB2"], SourceList.Read(cache).Select(entry => entry.Id));
            Assert.All(SourceList.Read(cache), entry => Assert.True(entry.IsValid, entry.Error));
        }
        var listed = SourceQuery.List(new SourceListRequest(world.Path, All: true));
        Assert.True(listed.Succeeded, listed.Refusal?.Message);
        Assert.Equal(["WEB", "WEB2"], listed.Value!.Sources.Select(source => source.Id));
        Assert.Equal(ApprovalState.Approved, Assert.Single(Approvals(world), approval => approval.Anchor == "MAT 1:1#2").State);
    }

    [Fact]
    public void ASourceHoldingApprovalsKeepsThemThroughRenameRemoveAndRestoreAndEveryPublicReadAgrees()
    {
        var world = World_();
        Stage(world, "MAT 1:1#2", world.LampA);
        Stage(world, "MAT 2:1#2", world.LampA);
        Applied(world.Path);
        var items = ApprovalItemGuids(world.Path);
        Assert.Equal(2, items.Count);
        Assert.Equal(((int?)2, "available"), Progress(world));

        void Step(Func<bool> stage, string status, string name)
        {
            Assert.True(stage());
            Applied(world.Path);
            // Applied saved the project; every read below opens it again.
            Assert.Equal(items, ApprovalItemGuids(world.Path));
            Assert.Equal(["MAT 1:1#2", "MAT 2:1#2"], Approvals(world).Where(approval => approval.State == ApprovalState.Approved)
                .Select(approval => approval.Anchor));
            var listed = SourceQuery.List(new SourceListRequest(world.Path, All: true));
            Assert.True(listed.Succeeded, listed.Refusal?.Message);
            var only = Assert.Single(listed.Value!.Sources);
            Assert.Equal(("WEB", name, status), (only.Id, only.Name, only.Status));
            var shown = SourceQuery.Show(new SourceShowRequest(world.Path, "WEB"));
            Assert.True(shown.Succeeded, shown.Refusal?.Message);
            Assert.Equal(status, shown.Value!.Source.Status);
        }

        Step(() => ProposalCommands.StageSourceSetName(new SourceSetNameRequest(world.Path, "WEB", "Renamed")).Succeeded, "Active", "Renamed");
        Assert.Equal(((int?)2, "available"), Progress(world));

        Step(() => ProposalCommands.StageSourceRemove(new SourceRemoveRequest(world.Path, "WEB")).Succeeded, "Removed", "Renamed");
        Assert.Equal(RefusalCodes.SourceIndexStale, ProgressRefusal(world));
        var refused = ProposalCommands.StageSourceApprove(new SourceApproveRequest(world.Path, "WEB", "MAT 3:1#2", Analysis: world.LampA.Value));
        Assert.Equal(RefusalCodes.SourceIdRemoved, refused.Refusal?.Code);

        Step(() => ProposalCommands.StageSourceRestore(new SourceRestoreRequest(world.Path, "WEB")).Succeeded, "Active", "Renamed");
        Assert.Equal(((int?)2, "available"), Progress(world));
        Assert.Equal(1, Stage(world, "MAT 3:1#2", world.LampA).Approved);
    }

    [Fact]
    public void DeletingASourceItemDeletesItsApprovalsWithItAndNothingReadsThemAfterwards()
    {
        var world = World_();
        Stage(world, "MAT 1:1#2", world.LampA);
        Stage(world, "MAT 2:1#2", world.LampA);
        Applied(world.Path);
        Assert.Equal(2, ApprovalItemGuids(world.Path).Count);

        using (var cache = new FwDataProjectLoader().LoadCache(world.Path))
        {
            UndoableUnitOfWorkHelper.Do("test", "test", cache.ServiceLocator.GetInstance<IActionHandler>(), () =>
                ScriptureSourceLookup.FindList(cache)!.PossibilitiesOS.Remove(ScriptureSourceLookup.FindItem(cache, "WEB")!));
            new FwDataProjectLoader().Save(cache);
        }
        Capture(world.Path);

        Assert.Empty(ApprovalItemGuids(world.Path));
        using (var cache = new FwDataProjectLoader().LoadCache(world.Path))
        {
            Assert.Empty(ScriptureSourceLookup.FindList(cache)!.PossibilitiesOS);
            Assert.Empty(SourceApprovals.ReadReporting(cache, "WEB").Documents);
        }
        var listed = SourceQuery.List(new SourceListRequest(world.Path, All: true));
        Assert.True(listed.Succeeded, listed.Refusal?.Message);
        Assert.Empty(listed.Value!.Sources);
    }

    [Fact]
    public void SendReceiveCombinesTwoInstallationsApprovalsAndASettledConflictReopensOnlyForALaterDifferentApproval()
    {
        var world = World_();
        var two = new TwoInstallations(world);
        void AsTheirs(Action action) => two.AsTheirs(action);
        string Side(byte[] start, string name, Action work) => two.Side(start, name, work);
        string Merge(byte[] start, string ours, string theirs) => two.Merge(start, ours, theirs);

        var original = File.ReadAllBytes(world.Path);
        var ours = Side(original, "ours", () => { Stage(world, "MAT 1:1#2", world.LampA); Stage(world, "MAT 2:1#2", world.LampA); });
        var theirs = Side(original, "theirs", () => AsTheirs(() =>
        {
            Stage(world, "MAT 1:1#2", world.LampB);
            Stage(world, "MAT 2:1#2", world.LampA);
        }));
        Merge(original, ours, theirs);

        using (var cache = new FwDataProjectLoader().LoadCache(world.Path))
        {
            Assert.Equal(4, ApprovalItems(cache).Count());
            var read = SourceApprovals.ReadReporting(cache, "WEB");
            var conflict = Assert.Single(read.Conflicts);
            Assert.Equal("MAT 1:1#2", conflict.Key);
            Assert.Equal(new[] { world.LampA.ToGuid(), world.LampB.ToGuid() }.OrderBy(guid => guid.ToString("D")), conflict.Value);
            Assert.Equal(["MAT 2:1#2"], read.Documents.SelectMany(pair => pair.Value.Approvals.Keys));
            Assert.Empty(read.Unreadable);
        }
        using (var database = ProjectMotifDatabase.Open(world.Path)) SIL.Motif.Worker.Sources.SourceIndexRefresh.Run(database);
        var progress = SourceProgressQuery.Query(world.Path, "WEB");
        Assert.True(progress.Succeeded, progress.Refusal?.Message);
        var reported = Assert.Single(progress.Value!.ConflictedApprovals);
        Assert.Equal(("MAT 1:1#2", 2), (reported.Anchor, reported.Analyses.Count));
        Assert.Equal(1, progress.Value.ApprovedPlaces);
        var passage = SourcePassageQuery.Query(new SourcePassageRequest(world.Path, "WEB", "MAT 1", 200));
        Assert.Equal("MAT 1:1#2", Assert.Single(passage.Value!.Places, place => place.State == "conflict").Anchor);

        // Approving again settles it for every reader; meanwhile the other installation approves differently.
        var merged = File.ReadAllBytes(world.Path);
        var settled = Side(merged, "settled", () => Assert.Equal(1, Stage(world, "MAT 1:1#2", world.LampB).Approved));
        var resettled = Approvals(world).Single(approval => approval.Anchor == "MAT 1:1#2");
        Assert.Equal((world.LampB.ToGuid(), ApprovalState.Approved), (resettled.Analysis, resettled.State));
        var later = Side(merged, "later", () => AsTheirs(() => Assert.Equal(1, Stage(world, "MAT 1:1#2", world.LampA).Approved)));
        Merge(merged, settled, later);

        using (var cache = new FwDataProjectLoader().LoadCache(world.Path))
        {
            Assert.Equal(4, ApprovalItems(cache).Count());
            Assert.Equal("MAT 1:1#2", Assert.Single(SourceApprovals.ReadReporting(cache, "WEB").Conflicts).Key);
        }
    }

    [Fact]
    public void ACopiedProfileOnAnotherComputerApprovesUnderItsOwnIdentityAndSendReceiveKeepsBothComputersApprovals()
    {
        var world = World_();
        var ours = ScriptureLocalStore.ForInstallation().InstallationId();
        // The other computer has a copy of this profile's installation-id without our fingerprint.
        var two = new TwoInstallations(world, copiedId: ours);
        var original = File.ReadAllBytes(world.Path);
        var mine = two.Side(original, "ours", () => Stage(world, "MAT 1:1#2", world.LampA));
        var theirs = two.Side(original, "theirs", () => two.AsTheirs(() =>
        {
            Assert.NotEqual(ours, ScriptureLocalStore.ForInstallation().InstallationId());
            Stage(world, "MAT 1:2#2", world.LampA);
        }));
        two.Merge(original, mine, theirs);

        Assert.Equal(["MAT 1:1#2", "MAT 1:2#2"], Approvals(world).Where(approval => approval.State == ApprovalState.Approved)
            .Select(approval => approval.Anchor));
        two.AsTheirs(() => Assert.Equal(two.TheirsId, ScriptureLocalStore.ForInstallation().InstallationId()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnIntactProfileCopiedToASameNamedComputerWithAnotherMachineIdApprovesUnderItsOwnIdentityInEitherMergeDirection(
        bool theirsFirst)
    {
        var ourMachine = new InstallationMachine("SAME-HOST", "same-user", "machine-ours", "test");
        using var computer = InstallationMachine.Use(ourMachine);
        var world = World_();
        var ours = ScriptureLocalStore.ForInstallation().InstallationId();
        var idFile = Path.Combine(Path.GetDirectoryName(ScriptureLocalStore.ForInstallation().FilePath)!, "installation-id");
        var two = new TwoInstallations(world, copiedIdFile: idFile, theirMachine: ourMachine with { SystemId = "machine-theirs" });
        var original = File.ReadAllBytes(world.Path);
        var mine = two.Side(original, "ours", () => Stage(world, "MAT 1:1#2", world.LampA));
        var theirs = two.Side(original, "theirs", () => two.AsTheirs(() => Stage(world, "MAT 1:2#2", world.LampA)));
        Assert.NotEqual(ours, two.TheirsId);

        // A shared writer would give both items one Guid; Merge asserts that no approval item clashes.
        if (theirsFirst) two.Merge(original, theirs, mine);
        else two.Merge(original, mine, theirs);

        Assert.Equal(["MAT 1:1#2", "MAT 1:2#2"], Approvals(world).Where(approval => approval.State == ApprovalState.Approved)
            .Select(approval => approval.Anchor));
        Assert.Equal(2, ApprovalItemGuids(world.Path).Count);

        // After a reinstall this computer is new, and what it approved before stays readable and counted.
        using (InstallationMachine.Use(ourMachine with { SystemId = "machine-reinstalled" }))
        {
            Assert.NotEqual(ours, ScriptureLocalStore.ForInstallation().InstallationId());
            Stage(world, "MAT 2:1#2", world.LampA);
            Applied(world.Path);
        }
        Assert.Equal(["MAT 1:1#2", "MAT 1:2#2", "MAT 2:1#2"], Approvals(world)
            .Where(approval => approval.State == ApprovalState.Approved).Select(approval => approval.Anchor));
        Assert.Equal(3, ApprovalItemGuids(world.Path).Count);
    }

    [Fact]
    public void RepairingThisInstallationsUnreadableSubitemKeepsEveryApprovalOfTheBatchInIt()
    {
        var world = World_();
        Stage(world, "MAT 1:1#2", world.LampA);
        Applied(world.Path);
        Corrupt(world.Path, "MAT 1", ScriptureLocalStore.ForInstallation().InstallationId());
        Capture(world.Path);

        var bulk = Stage(world, "MAT 1:1#2", world.LampA, bulk: true);
        Assert.Equal(6, bulk.Approved);
        Applied(world.Path);

        var approvals = Approvals(world);
        Assert.Equal(["MAT 1:1#2", "MAT 1:2#2", "MAT 2:1#2", "MAT 2:2#2", "MAT 3:1#2", "MAT 3:2#2"],
            approvals.Where(approval => approval.State == ApprovalState.Approved).Select(approval => approval.Anchor));
        using var cache = new FwDataProjectLoader().LoadCache(world.Path);
        Assert.Empty(SourceApprovals.ReadReporting(cache, "WEB").Unreadable);
        Assert.Equal(3, ApprovalItems(cache).Count());
    }

    [Fact]
    public void ComposedDecomposedAndMixedWordsStayApprovedThroughSaveAndReopenAndBulk()
    {
        const string composed = "caf\u00e9", decomposed = "cafe\u0301", mixed = "re\u0301sum\u00e9";
        var world = World_(usfm: "\\id MAT Invented\n\\h MAT\n\\c 1\n\\p\n" +
            $"\\v 1 The {composed} is open.\n\\v 2 The {decomposed} is open.\n\\v 3 The {mixed} is long.\n" +
            $"\\c 2\n\\p\n\\v 1 The {composed} is open.\n\\v 2 The {decomposed} is open.\n",
            extras: [composed, "r\u00e9sum\u00e9"]);
        var cafe = world.Extras[composed];
        Stage(world, "MAT 1:1#2", cafe);
        Stage(world, "MAT 1:2#2", cafe);
        Stage(world, "MAT 1:3#2", world.Extras["r\u00e9sum\u00e9"]);
        Applied(world.Path);

        Assert.All(Approvals(world), approval => Assert.Equal(ApprovalState.Approved, approval.State));
        Assert.Equal(3, Approvals(world).Count);
        var bulk = Stage(world, "MAT 2:1#2", cafe, bulk: true);
        Assert.Equal((2, 2, 0), (bulk.Approved, bulk.AlreadyApproved, bulk.Stale));
        Applied(world.Path);
        Assert.Equal(5, Approvals(world).Count(approval => approval.State == ApprovalState.Approved));
        Assert.Equal(0, Stage(world, "MAT 1:2#2", cafe, bulk: true).Approved);
    }

    [Fact]
    public void AnUnreadableSubitemMakesOnlyItsPassageUncertainSoAnApprovalItOverruledIsNotCounted()
    {
        var world = World_();
        var two = new TwoInstallations(world);
        var original = File.ReadAllBytes(world.Path);
        var ours = two.Side(original, "ours", () => { Stage(world, "MAT 1:1#2", world.LampA); Stage(world, "MAT 2:1#2", world.LampA); });
        var theirs = two.Side(original, "theirs", () => two.AsTheirs(() => Stage(world, "MAT 1:1#2", world.LampB)));
        var merged = File.ReadAllBytes(two.Merge(original, ours, theirs));
        // Approving again settles the conflict: this installation's entry overrules the other's.
        two.Side(merged, "settled", () => Stage(world, "MAT 1:1#2", world.LampA));
        var settled = File.ReadAllBytes(world.Path);
        Assert.Equal(["MAT 1:1#2", "MAT 2:1#2"], Approvals(world).Select(approval => approval.Anchor));

        Corrupt(world.Path, "MAT 1", ScriptureLocalStore.ForInstallation().InstallationId());
        Capture(world.Path);
        using (var cache = new FwDataProjectLoader().LoadCache(world.Path))
        {
            var read = SourceApprovals.ReadReporting(cache, "WEB");
            Assert.Equal(["MAT 1"], read.Unreadable);
            Assert.Equal(["MAT 2"], read.Documents.Keys);
            Assert.Empty(read.Conflicts);
        }
        Assert.Equal(["MAT 2:1#2"], Approvals(world).Select(approval => approval.Anchor));
        Assert.Equal(((int?)1, "unreadable"), Progress(world));

        // The sub-items arrive without the field's definition, as from a copy that never had it.
        File.WriteAllBytes(world.Path, settled);
        WithoutApprovalsField(world.Path);
        Capture(world.Path);
        using (var cache = new FwDataProjectLoader().LoadCache(world.Path))
        {
            var read = SourceApprovals.ReadReporting(cache, "WEB");
            Assert.Equal(["MAT 1", "MAT 2"], read.Unreadable);
            Assert.Empty(read.Documents);
        }
        Assert.Equal(((int?)0, "unreadable"), Progress(world));
    }

    [Fact]
    public void AnEntryThatArrivesAfterTheClearThatOverruledItStaysOverruledAndApprovingAgainStillHolds()
    {
        var world = World_();
        var two = new TwoInstallations(world);
        var original = File.ReadAllBytes(world.Path);
        var ours = two.Side(original, "ours", () => Stage(world, "MAT 1:1#2", world.LampA));
        var theirs = two.Side(original, "theirs", () => two.AsTheirs(() => Stage(world, "MAT 1:1#2", world.LampB)));
        var merged = File.ReadAllBytes(two.Merge(original, ours, theirs));
        var settled = two.Side(merged, "settled", () => Stage(world, "MAT 1:1#2", world.LampA));

        // A copy that has the settling entry but not yet the entry it overrules.
        Guid source;
        using (var cache = new FwDataProjectLoader().LoadCache(settled)) source = ScriptureSourceLookup.FindItem(cache, "WEB")!.Guid;
        File.WriteAllBytes(world.Path, File.ReadAllBytes(settled));
        WithoutObject(world.Path, ScriptureApprovalFields.ItemGuid(source, "MAT 1", two.TheirsId));
        var partial = File.ReadAllBytes(world.Path);
        var cleared = two.Side(partial, "cleared", () => Assert.Equal(1, ProposalCommands.StageSourceClearApproval(
            new SourceClearApprovalRequest(world.Path, "WEB", "MAT 1:1#2")).Value!.Cleared));
        two.Merge(partial, cleared, settled);

        Assert.Empty(Approvals(world));
        using (var cache = new FwDataProjectLoader().LoadCache(world.Path))
        {
            var read = SourceApprovals.ReadReporting(cache, "WEB");
            Assert.Empty(read.Conflicts);
            Assert.Equal(2, ApprovalItems(cache).Count());
        }
        Stage(world, "MAT 1:1#2", world.LampA);
        Applied(world.Path);
        var again = Assert.Single(Approvals(world));
        Assert.Equal((world.LampA.ToGuid(), ApprovalState.Approved), (again.Analysis, again.State));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TwoInstallationsApprovingOneAnalysisCountAsApprovedWhenEitherApprovalStillHolds(bool oursIsStale)
    {
        var world = World_();
        var two = new TwoInstallations(world);
        var file = Path.Combine(world.Folder, "41MAT.SFM");
        var text = File.ReadAllText(file);
        void Approve(bool stale)
        {
            // An approval made against an older text of the verse, which is then put back.
            if (stale) File.WriteAllText(file, text.Replace("\\v 1 The lamp MAT 1 keeps", "\\v 1 The lamp MAT 1 once kept"));
            try { Stage(world, "MAT 1:1#2", world.LampA); }
            finally { File.WriteAllText(file, text); }
        }
        var original = File.ReadAllBytes(world.Path);
        var ours = two.Side(original, "ours", () => Approve(oursIsStale));
        var theirs = two.Side(original, "theirs", () => two.AsTheirs(() => Approve(!oursIsStale)));
        two.Merge(original, ours, theirs);

        using (var cache = new FwDataProjectLoader().LoadCache(world.Path))
            Assert.Equal(2, SourceApprovals.Read(cache, "WEB")["MAT 1"].Evidence["MAT 1:1#2"].Count);
        var approval = Assert.Single(Approvals(world));
        Assert.Equal((world.LampA.ToGuid(), ApprovalState.Approved), (approval.Analysis, approval.State));
    }

    [SlowScaleFact]
    [Trait("Category", "SlowScale")]
    public void TenThousandApprovalsInOneApplyTakeSeconds()
    {
        // 20 chapters of 10 verses of 50 words: one wordform 10,000 times, as a frequent word is across a Bible.
        var world = World_(usfm: Repeated("lamp", chapters: 20, verses: 10, words: 50));
        var staging = System.Diagnostics.Stopwatch.StartNew();
        var staged = Stage(world, "MAT 1:1#1", world.LampA, bulk: true);
        staging.Stop();
        Assert.Equal(10_000, staged.Approved);

        var applying = System.Diagnostics.Stopwatch.StartNew();
        Applied(world.Path);
        applying.Stop();

        using (var cache = new FwDataProjectLoader().LoadCache(world.Path))
        {
            Assert.Equal(20, ApprovalItems(cache).Count());
            Assert.Equal(10_000, SourceApprovals.Read(cache, "WEB").Sum(pair => pair.Value.Approvals.Count));
        }
        Console.WriteLine($"10,000 approvals: staged in {staging.Elapsed.TotalSeconds:F1} s, Dry Run and Apply in {applying.Elapsed.TotalSeconds:F1} s.");
        // The per-approval list took about an hour for this; the ceilings leave several times the measured time.
        Assert.True(applying.Elapsed < TimeSpan.FromSeconds(60), $"Dry Run and Apply took {applying.Elapsed}.");
        Assert.True(staging.Elapsed < TimeSpan.FromSeconds(60), $"Staging took {staging.Elapsed}.");
    }

    [Fact]
    public void EachChapterIsReadAndWrittenOnceAnApplyAlsoWhenItEditsChaptersThatHaveApprovals()
    {
        // A word that is not ASCII, so the stored text is too.
        var world = World_(usfm: Repeated("lámp", chapters: 3, verses: 4, words: 10), form: "lámp");
        Assert.Equal(120, Stage(world, "MAT 1:1#1", world.LampA, bulk: true).Approved);

        var first = ScriptureApprovalCounts.Start();
        Applied(world.Path);
        var (parsed, written) = first();

        // Dry Run and Apply each write a chapter once; new chapters have nothing stored to read.
        Assert.Equal(2 * 3, written);
        Assert.InRange(parsed, 0, 4 * 3);
        // Editing chapters that already have approvals: another analysis at one Occurrence in each.
        Stage(world, "MAT 1:1#1", world.LampB);
        Stage(world, "MAT 2:1#1", world.LampB);
        Stage(world, "MAT 3:4#10", world.LampB);
        var second = ScriptureApprovalCounts.Start();
        Applied(world.Path);
        (parsed, written) = second();
        // Each stored chapter is read a few times, not once per Occurrence it holds.
        Assert.Equal(2 * 3, written);
        Assert.InRange(parsed, 3, 4 * 3);
        Assert.Equal(117, Approvals(world).Count(approval => approval.Analysis == world.LampA.ToGuid() && approval.State == ApprovalState.Approved));
        Assert.Equal(3, Approvals(world).Count(approval => approval.Analysis == world.LampB.ToGuid() && approval.State == ApprovalState.Approved));
    }

    private static string Repeated(string word, int chapters, int verses, int words)
    {
        var usfm = new StringBuilder("\\id MAT Invented\n\\h MAT\n");
        for (var chapter = 1; chapter <= chapters; chapter++)
        {
            usfm.Append($"\\c {chapter}\n\\p\n");
            for (var verse = 1; verse <= verses; verse++) usfm.Append($"\\v {verse} ").AppendJoin(' ', Enumerable.Repeat(word, words)).Append('\n');
        }
        return usfm.ToString();
    }

    [Fact]
    public void ADisapprovedOpinionIsLeftAloneAndTheConflictIsReported()
    {
        var world = World_();
        SetOpinion(world.Path, world.LampA, Opinions.disapproves);

        var staged = Stage(world, "MAT 1:1#2", world.LampA);

        Assert.Equal(["MAT 1:1#2"], staged.Conflicts);
        Assert.DoesNotContain(staged.Operations, operation => operation.Kind == "analysis/wfiAnalysis/addRefEvaluations");
        Applied(world.Path);
        Assert.Equal(Opinions.disapproves, Opinion(world.Path, world.LampA));
        Assert.Single(Approvals(world));
    }

    [Fact]
    public void AGuessOnlyAddsTheAnalysisAndApprovesItInOneDraftAndRetryingConverges()
    {
        var world = World_(analyses: false);
        var reading = Reading(world.Path);

        var staged = ProposalCommands.StageSourceApprove(new SourceApproveRequest(world.Path, "WEB", "MAT 1:1#2",
            Wordform: world.LampWordform.Value, Reading: reading));
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        Assert.Contains(staged.Value!.Operations, operation => operation.Kind == "analysis/wfiWordform/createAnalyses");
        Assert.True(ProposalCommands.StageSourceApprove(new SourceApproveRequest(world.Path, "WEB", "MAT 1:1#2",
            Wordform: world.LampWordform.Value, Reading: reading)).Succeeded);
        Assert.Equal(0, Analyses(world.Path, world.LampWordform));

        Applied(world.Path);

        Assert.Equal(1, Analyses(world.Path, world.LampWordform));
        Assert.Single(Approvals(world));
        var again = ProposalCommands.StageSourceApprove(new SourceApproveRequest(world.Path, "WEB", "MAT 1:1#2",
            Wordform: world.LampWordform.Value, Reading: reading));
        Assert.True(again.Succeeded, again.Refusal?.Message);
        Assert.Equal(0, again.Value!.Approved);
                Assert.Equal(1, Analyses(world.Path, world.LampWordform));
    }

    [Fact]
    public void AGuessOnlyWritesBothOrNeitherWhenTheSourceIsGoneBeforeApply()
    {
        var world = World_(analyses: false);
        Assert.True(ProposalCommands.StageSourceApprove(new SourceApproveRequest(world.Path, "WEB", "MAT 1:1#2",
            Wordform: world.LampWordform.Value, Reading: Reading(world.Path))).Succeeded);
        using (var cache = new FwDataProjectLoader().LoadCache(world.Path))
        {
            UndoableUnitOfWorkHelper.Do("test", "test", cache.ServiceLocator.GetInstance<IActionHandler>(), () =>
                ScriptureSourceLookup.FindList(cache)!.PossibilitiesOS.Remove(ScriptureSourceLookup.FindItem(cache, "WEB")!));
            new FwDataProjectLoader().Save(cache);
        }
        Capture(world.Path);

        var applied = Apply(world.Path);

        Assert.False(applied.Succeeded && applied.Value!.Applied);
        Assert.Equal(0, Analyses(world.Path, world.LampWordform));
    }

    [Fact]
    public void TheNewestApprovalReplacesTheOldOneAndClearReturnsTheOccurrenceToAGuess()
    {
        var world = World_();
        Stage(world, "MAT 1:1#2", world.LampA);
        Applied(world.Path);

        var replaced = Stage(world, "MAT 1:1#2", world.LampB);
        Assert.Equal(1, replaced.Approved);
        Applied(world.Path);
        Assert.Equal(world.LampB.ToGuid(), Assert.Single(Approvals(world)).Analysis);

        var cleared = ProposalCommands.StageSourceClearApproval(new SourceClearApprovalRequest(world.Path, "WEB", "MAT 1:1#2"));
        Assert.True(cleared.Succeeded, cleared.Refusal?.Message);
        Assert.Equal(1, cleared.Value!.Cleared);
        Applied(world.Path);
        Assert.Empty(Approvals(world));
        Assert.Equal(Opinions.approves, Opinion(world.Path, world.LampB));

        var nothing = ProposalCommands.StageSourceClearApproval(new SourceClearApprovalRequest(world.Path, "WEB", "MAT 1:1#2"));
        Assert.True(nothing.Succeeded, nothing.Refusal?.Message);
        Assert.Equal(0, nothing.Value!.Cleared);
    }

    [Fact]
    public void AnApprovalGoesStaleWhenTheVerseTextChangesAndBulkNeverReapprovesIt()
    {
        var world = World_();
        Stage(world, "MAT 1:1#2", world.LampA);
        Applied(world.Path);
        Assert.Equal(ApprovalState.Approved, Assert.Single(Approvals(world)).State);

        var file = Path.Combine(world.Folder, "41MAT.SFM");
        File.WriteAllText(file, File.ReadAllText(file).Replace("\\v 1 The lamp MAT 1 keeps", "\\v 1 The lamp MAT 1 now keeps"));

        var approval = Assert.Single(Approvals(world));
        Assert.Equal((ApprovalState.Stale, "The verse text changed."), (approval.State, approval.Reason));

        var bulk = ProposalCommands.StageSourceApprove(new SourceApproveRequest(world.Path, "WEB", "MAT 1:2#2",
            Analysis: world.LampA.Value, Bulk: true));
        Assert.True(bulk.Succeeded, bulk.Refusal?.Message);
        Assert.Equal(1, bulk.Value!.Stale);
        Assert.DoesNotContain("MAT 1:1#2", bulk.Value.Operations.Select(operation => operation.Kind));
    }

    [Fact]
    public void BulkApprovesTheSameWordformAcrossChaptersSkipsWhatIsApprovedOtherwiseAndCountsBeforeApply()
    {
        var world = World_();
        Stage(world, "MAT 2:1#2", world.LampB);
        Applied(world.Path);
        // Both analyses approved, so either is a guess FieldWorks may offer at the other Occurrences.
        SetOpinion(world.Path, world.LampA, Opinions.approves);

        var bulk = ProposalCommands.StageSourceApprove(new SourceApproveRequest(world.Path, "WEB", "MAT 1:1#2",
            Analysis: world.LampA.Value, Bulk: true));

        Assert.True(bulk.Succeeded, bulk.Refusal?.Message);
        Assert.Equal(1, bulk.Value!.ApprovedOtherwise);
        Assert.Equal((5, 3), (bulk.Value.Approved, bulk.Value.Chapters));
        Assert.DoesNotContain(Approvals(world), approval => approval.Analysis == world.LampA.ToGuid());
        Applied(world.Path);
        var after = Approvals(world);
        Assert.Equal(6, after.Count);
        Assert.Equal(world.LampB.ToGuid(), after.Single(approval => approval.Anchor == "MAT 2:1#2").Analysis);
        Assert.Equal(5, after.Count(approval => approval.Analysis == world.LampA.ToGuid()));

        var again = ProposalCommands.StageSourceApprove(new SourceApproveRequest(world.Path, "WEB", "MAT 1:1#2",
            Analysis: world.LampA.Value, Bulk: true));
        Assert.Equal((0, 5, 1), (again.Value!.Approved, again.Value.AlreadyApproved, again.Value.ApprovedOtherwise));
    }

    [Fact]
    public void AnApprovalIsStoredAtTheWordNumberTheSourceIndexGivesThatWord()
    {
        var world = World_();
        Capture(world.Path);
        using (var database = ProjectMotifDatabase.Open(world.Path)) SIL.Motif.Worker.Sources.SourceIndexRefresh.Run(database);
        var indexed = SourcePassageQuery.Query(new SourcePassageRequest(world.Path, "WEB", "MAT 1", 200));
        Assert.True(indexed.Succeeded, indexed.Refusal?.Message);
        // A number stands before "keeps", so another tokeniser would number it differently.
        var keeps = indexed.Value!.Places.First(place => place.Anchor.StartsWith("MAT 1:1#", StringComparison.Ordinal) && place.Surface == "keeps");

        Assert.Equal(1, Stage(world, keeps.Anchor, world.Keeps).Approved);
        Applied(world.Path);
        Capture(world.Path);
        using (var database = ProjectMotifDatabase.Open(world.Path)) SIL.Motif.Worker.Sources.SourceIndexRefresh.Run(database);

        var read = SourcePassageQuery.Query(new SourcePassageRequest(world.Path, "WEB", "MAT 1", 200));
        var approved = Assert.Single(read.Value!.Places, place => place.State == "approved");
        Assert.Equal((keeps.Anchor, "keeps"), (approved.Anchor, approved.Surface));
    }

    [Fact]
    public void OnlyTheConfirmationOfTheFirstApplyThatNeedsTheFieldSaysItAddsTheField()
    {
        var world = World_();

        var first = Stage(world, "MAT 1:1#2", world.LampA);
        Assert.True(first.AddsApprovalsField);
        Applied(world.Path);

        var second = Stage(world, "MAT 2:1#2", world.LampA);
        Assert.False(second.AddsApprovalsField);
    }

    [Fact]
    public void ThePassageAndProgressReadsShowApprovedAndStaleOccurrencesAndCountOnlyTheApprovedOnes()
    {
        var world = World_();
        Stage(world, "MAT 1:1#2", world.LampA);
        Stage(world, "MAT 2:1#2", world.LampA);
        Applied(world.Path);
        var file = Path.Combine(world.Folder, "41MAT.SFM");
        File.WriteAllText(file, File.ReadAllText(file).Replace("\\v 1 The lamp MAT 2", "\\v 1 A lamp MAT 2"));
        Capture(world.Path);
        using (var database = ProjectMotifDatabase.Open(world.Path)) SIL.Motif.Worker.Sources.SourceIndexRefresh.Run(database);

        var first = SourcePassageQuery.Query(new SourcePassageRequest(world.Path, "WEB", "MAT 1", 200));
        Assert.True(first.Succeeded, first.Refusal?.Message);
        var approved = Assert.Single(first.Value!.Places, place => place.State == "approved");
        Assert.Equal(("MAT 1:1#2", world.LampA.ToGuid().ToString("D")), (approved.Anchor, approved.ReadingKey));
        var stale = SourcePassageQuery.Query(new SourcePassageRequest(world.Path, "WEB", "MAT 2", 200));
        Assert.Equal("MAT 2:1#2", Assert.Single(stale.Value!.Places, place => place.State == "stale").Anchor);

        var progress = SourceProgressQuery.Query(world.Path, "WEB", "passages", 10);
        Assert.True(progress.Succeeded, progress.Refusal?.Message);
        Assert.Equal(1, progress.Value!.ApprovedPlaces);
        Assert.Equal([1, 0, 0], progress.Value.Passages.Select(passage => passage.ApprovedPlaces));
    }

    [Fact]
    public void BulkTakesTheSameWordformNotTheSameSurfaceAndTheCountEqualsWhatIsStaged()
    {
        var world = World_();
        var file = Path.Combine(world.Folder, "41MAT.SFM");
        File.WriteAllText(file, File.ReadAllText(file).Replace("\\v 2 The lamp MAT 3", "\\v 2 Lamp MAT 3"));

        var bulk = Stage(world, "MAT 1:1#2", world.LampA, bulk: true);

        // "Lamp" is a different wordform from "lamp" although they differ only in case.
        Assert.Equal(5, bulk.Approved);
        Assert.Equal(bulk.Approved, bulk.Operations.Count(operation => operation.Kind == ScriptureApprovalOperationKinds.Approve));
    }

    [Fact]
    public void BulkLeavesOutOccurrencesWhoseGuessIsAnotherAnalysisOfTheWordform()
    {
        var world = World_();
        SetOpinion(world.Path, world.LampB, Opinions.approves);

        var bulk = Stage(world, "MAT 1:1#2", world.LampA, bulk: true);

        // Somebody approved the other analysis, so that is what FieldWorks guesses at the other Occurrences.
        Assert.Equal(1, bulk.Approved);
        Assert.Equal(1, bulk.Operations.Count(operation => operation.Kind == ScriptureApprovalOperationKinds.Approve));
        var both = Stage(world, "MAT 1:1#2", world.LampB, bulk: true);
        Assert.Equal(6, both.Approved);
    }

    [Fact]
    public void BulkByReadingLeavesOutOccurrencesWhoseGuessIsAnotherApprovedAnalysis()
    {
        var world = World_();
        SetOpinion(world.Path, world.LampB, Opinions.approves);
        SourceApprovalStageResponse Bulk() => ProposalCommands.StageSourceApprove(new SourceApproveRequest(world.Path, "WEB",
            "MAT 1:1#2", Wordform: world.LampWordform.Value, Reading: Reading(world.Path), Bulk: true)).Value!;

        // The reading is analysis A; FieldWorks guesses the approved B at the other Occurrences.
        Assert.Equal(1, Bulk().Approved);

        SetOpinion(world.Path, world.LampA, Opinions.approves);
        Assert.Equal(6, Bulk().Approved);
    }

    [Fact]
    public void UnreadableAndOrphanedApprovalsAreReportedAndNeverCounted()
    {
        var world = World_();
        Stage(world, "MAT 1:1#2", world.LampA);
        Stage(world, "MAT 2:1#2", world.LampA);
        Stage(world, "MAT 3:1#2", world.LampA);
        Applied(world.Path);
        var file = Path.Combine(world.Folder, "41MAT.SFM");
        var text = File.ReadAllText(file);
        File.WriteAllText(file, text[..text.IndexOf("\\c 3", StringComparison.Ordinal)]);
        using (var cache = new FwDataProjectLoader().LoadCache(world.Path))
        {
            var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
            var flid = metadata.GetFieldIds().Single(id => metadata.GetFieldName(id) == ScriptureApprovalFields.ApprovalsField);
            var item = ApprovalItems(cache).Single(each => ScriptureSourceLookup.IdOf(each) == "MAT 2");
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => cache.DomainDataByFlid.SetString(
                item.Hvo, flid, TsStringUtils.MakeString("this is not an approvals document", cache.DefaultAnalWs)));
            new FwDataProjectLoader().Save(cache);
        }
        Capture(world.Path);
        using (var database = ProjectMotifDatabase.Open(world.Path)) SIL.Motif.Worker.Sources.SourceIndexRefresh.Run(database);

        var progress = SourceProgressQuery.Query(world.Path, "WEB");
        Assert.True(progress.Succeeded, progress.Refusal?.Message);
        Assert.Equal("unreadable", progress.Value!.ApprovedState);
        Assert.Equal(["MAT 2"], progress.Value.UnreadableApprovals);
        Assert.Equal(["MAT 3"], progress.Value.OrphanedApprovals);
        Assert.Equal(1, progress.Value.ApprovedPlaces);
        var unreadable = SourcePassageQuery.Query(new SourcePassageRequest(world.Path, "WEB", "MAT 2", 200));
        Assert.Equal("unreadable", unreadable.Value!.ApprovedState);
        Assert.DoesNotContain(unreadable.Value.Places, place => place.State == "approved");
        var fine = SourcePassageQuery.Query(new SourcePassageRequest(world.Path, "WEB", "MAT 1", 200));
        Assert.Equal("available", fine.Value!.ApprovedState);
        Assert.Single(fine.Value.Places, place => place.State == "approved");
    }

    [Trait("Quarantine", "#514")]
    [Fact]
    public void AnApprovalWhoseWordChangedUnderTheSameVerseTextIsStaleBecauseTheTokenizerChanged()
    {
        var world = World_();
        var passage = ParatextPassages.Read(world.Folder).Single(each => each.Locator == "MAT 1");
        var verse = SourceApprovals.VerseFingerprints(passage)["MAT 1:1"];
        var document = new ScriptureApprovalDocument();
        // As an older tokenizer numbered the words: the verse is byte for byte the same, #2 was another word.
        document.Set("MAT 1:1#2", new(world.LampA.ToGuid(), verse, "The", "unused"));

        // Scratch and disposed, so no shared writing systems or open cache are left to load.
        using var cache = new FwDataProjectLoader().LoadScratchCache(world.Path);
        var evaluated = Assert.Single(SourceApprovals.Evaluate(cache, document, passage));

        Assert.Equal((ApprovalState.Stale, "The word at this number changed."), (evaluated.State, evaluated.Reason));
    }

    [Fact]
    public void ARemovedSourceRefusesApprovalWithItsOwnCodeAndStagesNothing()
    {
        var world = World_();
        Assert.True(ProposalCommands.StageSourceRemove(new SourceRemoveRequest(world.Path, "WEB")).Succeeded);
        Applied(world.Path);

        var refused = ProposalCommands.StageSourceApprove(new SourceApproveRequest(world.Path, "WEB", "MAT 1:1#2", Analysis: world.LampA.Value));

        Assert.Equal(RefusalCodes.SourceIdRemoved, refused.Refusal?.Code);
        Assert.Empty(Pending(world.Path).Changes);
    }

    [Fact]
    public void BadRequestsAreRefusedWithTheirOwnCodesAndStageNothing()
    {
        var world = World_();
        string? Code(SourceApproveRequest request) => ProposalCommands.StageSourceApprove(request).Refusal?.Code;

        Assert.Equal(RefusalCodes.SourceRefInvalid, Code(new(world.Path, "WEB", "MAT 1:1", Analysis: world.LampA.Value)));
        Assert.Equal(RefusalCodes.SourceOccurrenceNotFound, Code(new(world.Path, "WEB", "MAT 1:1#99", Analysis: world.LampA.Value)));
        Assert.Equal(RefusalCodes.SourceChapterNotInSource, Code(new(world.Path, "WEB", "MAT 9:1#1", Analysis: world.LampA.Value)));
        Assert.Equal(RefusalCodes.SourceAnalysisMismatch, Code(new(world.Path, "WEB", "MAT 1:1#2", Analysis: world.Keeps.Value)));
        Assert.Equal(RefusalCodes.SourceAnalysisNeeded, Code(new(world.Path, "WEB", "MAT 1:1#2")));
        Assert.Equal(RefusalCodes.SourceAnalysisNeeded, Code(new(world.Path, "WEB", "MAT 1:1#2", Analysis: CanonicalId.Mint().Value)));
        Assert.Equal(RefusalCodes.SourceNotFound, Code(new(world.Path, "NOPE", "MAT 1:1#2", Analysis: world.LampA.Value)));
        // Digits are words only once the writing system declares them; "1" in "MAT 1" is then word 4 and has no letter.
        Assert.True(ProjectWordFormingCommand.Add(new ProjectWordFormingRequest(world.Path, "0123456789")).Succeeded);
        Assert.Equal(RefusalCodes.SourceOccurrenceNotEligible, Code(new(world.Path, "WEB", "MAT 1:1#4", Analysis: world.LampA.Value)));
        Assert.Empty(Pending(world.Path).Changes);
    }

    // form is the word that has analyses A and B; each of extras gets a wordform with one analysis.
    private World World_(bool analyses = true, string? usfm = null, string form = "lamp", string[]? extras = null)
    {
        CanonicalId lampA = default, lampB = default, keeps = default, lampWordform = default;
        var extra = new Dictionary<string, CanonicalId>(StringComparer.Ordinal);
        using (var cache = pristine.NewScratch())
        {
            var ws = cache.DefaultVernWs;
            var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                IWfiAnalysis Analysis(string text, bool withBundle)
                {
                    var wordform = cache.ServiceLocator.GetInstance<IWfiWordformFactory>().Create(TsStringUtils.MakeString(text, ws));
                    if (text == form) lampWordform = CanonicalId.FromGuid(wordform.Guid);
                    if (!analyses && text == form) return null!;
                    var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
                    wordform.AnalysesOC.Add(analysis);
                    if (withBundle)
                    {
                        var bundle = cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
                        analysis.MorphBundlesOS.Add(bundle);
                        bundle.MorphRA = entry.LexemeFormOA;
                        bundle.MsaRA = entry.MorphoSyntaxAnalysesOC.First();
                    }
                    return analysis;
                }
                var a = Analysis(form, true);
                if (a is not null) lampA = CanonicalId.FromGuid(a.Guid);
                var lamp = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances().First(w => w.Guid == lampWordform.ToGuid());
                if (analyses)
                {
                    var b = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
                    lamp.AnalysesOC.Add(b);
                    lampB = CanonicalId.FromGuid(b.Guid);
                }
                keeps = CanonicalId.FromGuid(Analysis("keeps", false).Guid);
                foreach (var text in extras ?? []) extra[text] = CanonicalId.FromGuid(Analysis(text, false).Guid);
            });
            new FwDataProjectLoader().Save(cache);
            var path = cache.ProjectId.Path;
            Capture(path);
            var folder = Paratext(usfm);
            var added = ProposalCommands.StageSourceAdd(new SourceAddRequest(path, "WEB", "Test", "English", "MAT", Folder: folder));
            Assert.True(added.Succeeded, added.Refusal?.Message);
            Applied(path);
            return new(path, folder, lampA, lampB, keeps, lampWordform, extra);
        }
    }

    private static string Paratext(string? text = null)
    {
        var folder = Directory.CreateTempSubdirectory("source-approval-").FullName;
        File.WriteAllText(Path.Combine(folder, "Settings.xml"), """
            <ScriptureText>
              <StyleSheet>usfm.sty</StyleSheet>
              <Language>Invented</Language>
              <BiblicalTermsListSetting>Major::BiblicalTerms.xml</BiblicalTermsListSetting>
              <Encoding>65001</Encoding>
              <Name>TINY</Name>
              <Guid>00000000000000000000000000000000000000ab</Guid>
              <FullName>Tiny invented test project</FullName>
              <FileNameBookNameForm>41MAT</FileNameBookNameForm>
              <FileNamePrePart />
              <FileNamePostPart>.SFM</FileNamePostPart>
              <Versification>4</Versification>
            </ScriptureText>
            """, new UTF8Encoding(false));
        var usfm = new StringBuilder("\\id MAT Invented\n\\h MAT\n");
        for (var chapter = 1; chapter <= 3; chapter++)
        {
            usfm.Append($"\\c {chapter}\n\\p\n");
            for (var verse = 1; verse <= 2; verse++)
                usfm.Append($"\\v {verse} The lamp MAT {chapter} keeps watch over verse {verse}.\n");
        }
        File.WriteAllText(Path.Combine(folder, "41MAT.SFM"), text ?? usfm.ToString(), new UTF8Encoding(false));
        return folder;
    }

    // The reading of analysis A: the seed's first entry, which World_ builds A from.
    private string Reading(string path)
    {
        using var cache = new FwDataProjectLoader().LoadCache(path);
        var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
        return JsonSerializer.Serialize(new ParseAnalysis([new(entry.LexemeFormOA!.Guid.ToString("D"),
            entry.MorphoSyntaxAnalysesOC.First().Guid.ToString("D"), null, null)]),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static SourceApprovalStageResponse Stage(World world, string anchor, CanonicalId analysis, bool bulk = false)
    {
        var staged = ProposalCommands.StageSourceApprove(new SourceApproveRequest(world.Path, "WEB", anchor,
            Analysis: analysis.Value, Bulk: bulk));
        Assert.True(staged.Succeeded, staged.Refusal?.Code + ": " + staged.Refusal?.Message);
        return staged.Value!;
    }

    private static IReadOnlyList<OccurrenceApproval> Approvals(World world)
    {
        using var cache = new FwDataProjectLoader().LoadCache(world.Path);
        var passages = ParatextPassages.Read(world.Folder).ToDictionary(passage => passage.Locator);
        return SourceApprovals.Read(cache, "WEB").SelectMany(pair => SourceApprovals.Evaluate(cache, pair.Value, passages[pair.Key]))
            .OrderBy(approval => approval.Anchor, StringComparer.Ordinal).ToArray();
    }

    private static bool HasField(LcmCache cache)
    {
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        return metadata.GetFieldIds().Any(id => metadata.GetFieldName(id) == ScriptureApprovalFields.ApprovalsField);
    }

    private static Opinions Opinion(string path, CanonicalId analysis)
    {
        using var cache = new FwDataProjectLoader().LoadCache(path);
        return ((IWfiAnalysis)cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(analysis.ToGuid()))
            .GetAgentOpinion(cache.LangProject.DefaultUserAgent);
    }

    private static void SetOpinion(string path, CanonicalId analysis, Opinions opinion)
    {
        using var cache = new FwDataProjectLoader().LoadCache(path);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => cache.LangProject.DefaultUserAgent.SetEvaluation(
            (IWfiAnalysis)cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(analysis.ToGuid()), opinion));
        new FwDataProjectLoader().Save(cache);
        Capture(path);
    }

    private static int Analyses(string path, CanonicalId wordform)
    {
        using var cache = new FwDataProjectLoader().LoadCache(path);
        return ((IWfiWordform)cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(wordform.ToGuid())).AnalysesOC.Count;
    }

    private static PendingChangesSnapshot Pending(string path)
    {
        var loaded = PendingChanges.Load(new PendingChangesRequest(path, "1.0"));
        Assert.True(loaded.Succeeded, loaded.Refusal?.Message);
        return loaded.Value!;
    }

    // Every possibility whose approvals field holds text, wherever it is stored.
    private static IEnumerable<ICmPossibility> ApprovalItems(LcmCache cache)
    {
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        var flid = metadata.GetFieldIds().FirstOrDefault(id => metadata.GetFieldName(id) == ScriptureApprovalFields.ApprovalsField);
        return flid == 0 ? [] : cache.ServiceLocator.GetInstance<ICmPossibilityRepository>().AllInstances()
            .Where(item => cache.DomainDataByFlid.get_StringProp(item.Hvo, flid)?.Length > 0).ToArray();
    }

    private static HashSet<Guid> ApprovalItemGuids(string path) => XDocument.Load(path).Root!.Elements("rt")
        .Where(rt => rt.Elements("Custom").Any(custom => (string?)custom.Attribute("name") == ScriptureApprovalFields.ApprovalsField))
        .Select(rt => Guid.Parse((string)rt.Attribute("guid")!)).ToHashSet();

    // Makes the installation's approvals of the Passage unreadable.
    private static void Corrupt(string path, string passage, Guid installation)
    {
        using var cache = new FwDataProjectLoader().LoadCache(path);
        var flid = FieldId(cache);
        var item = ApprovalItems(cache).Single(each => ScriptureApprovalFields.TryRead(cache, each, flid, out _) is { } blob &&
            blob.Passage == passage && blob.Installation == installation);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => cache.DomainDataByFlid.SetString(
            item.Hvo, flid, TsStringUtils.MakeString("this is not an approvals document", cache.DefaultAnalWs)));
        new FwDataProjectLoader().Save(cache);
    }

    private static void WithoutObject(string path, Guid guid)
    {
        var document = XDocument.Load(path);
        var id = guid.ToString("D");
        document.Root!.Elements("rt").Where(rt => string.Equals((string?)rt.Attribute("guid"), id, StringComparison.OrdinalIgnoreCase)).Remove();
        document.Descendants("objsur").Where(sur => string.Equals((string?)sur.Attribute("guid"), id, StringComparison.OrdinalIgnoreCase)).Remove();
        document.Save(path);
    }

    private static void WithoutApprovalsField(string path)
    {
        var document = XDocument.Load(path);
        document.Descendants().Where(element => element.Name.LocalName is "CustomField" or "Custom" &&
            (string?)element.Attribute("name") == ScriptureApprovalFields.ApprovalsField).Remove();
        document.Save(path);
    }

    private static string? ProgressRefusal(World world)
    {
        using (var database = ProjectMotifDatabase.Open(world.Path)) SIL.Motif.Worker.Sources.SourceIndexRefresh.Run(database);
        return SourceProgressQuery.Query(world.Path, "WEB").Refusal?.Code;
    }

    private static (int? Approved, string State) Progress(World world)
    {
        using (var database = ProjectMotifDatabase.Open(world.Path)) SIL.Motif.Worker.Sources.SourceIndexRefresh.Run(database);
        var progress = SourceProgressQuery.Query(world.Path, "WEB");
        Assert.True(progress.Succeeded, progress.Refusal?.Message);
        return (progress.Value!.ApprovedPlaces, progress.Value.ApprovedState);
    }

    // Two Motif installations on copies of one project, merged as Send/Receive merges them.
    private sealed class TwoInstallations
    {
        private readonly World _world;
        private readonly string _theirsLocal;

        private readonly InstallationMachine? _theirMachine;

        public TwoInstallations(World world, Guid? copiedId = null, string? copiedIdFile = null, InstallationMachine? theirMachine = null)
        {
            _theirMachine = theirMachine;
            _world = world;
            _theirsLocal = Path.Combine(Path.GetDirectoryName(world.Path)!, "theirs-local-" + Guid.NewGuid().ToString("N")[..8], "scripture-local.json");
            Directory.CreateDirectory(Path.GetDirectoryName(_theirsLocal)!);
            // The other installation has the same Source folder but its own local file.
            File.Copy(ScriptureLocalStore.ForInstallation().FilePath, _theirsLocal);
            if (copiedId is { } copied)
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(_theirsLocal)!, "installation-id"), copied.ToString("D") + "\n");
            if (copiedIdFile is not null) File.Copy(copiedIdFile, Path.Combine(Path.GetDirectoryName(_theirsLocal)!, "installation-id"));
        }

        public Guid TheirsId
        {
            get
            {
                var id = Guid.Empty;
                AsTheirs(() => id = ScriptureLocalStore.ForInstallation().InstallationId());
                return id;
            }
        }

        public void AsTheirs(Action action)
        {
            using var machine = _theirMachine is null ? null : InstallationMachine.Use(_theirMachine);
            using (AmbientEnvironment.Push(new Dictionary<string, string?> { [ScriptureLocalStore.PathEnvironmentVariable] = _theirsLocal }))
                action();
        }

        public string Side(byte[] start, string name, Action work)
        {
            File.WriteAllBytes(_world.Path, start);
            Capture(_world.Path);
            work();
            Applied(_world.Path);
            var copy = Path.Combine(Path.GetDirectoryName(_world.Path)!, name + ".fwdata");
            File.Copy(_world.Path, copy, overwrite: true);
            return copy;
        }

        public string Merge(byte[] start, string ours, string theirs)
        {
            var ancestor = Path.Combine(Path.GetDirectoryName(_world.Path)!, "ancestor.fwdata");
            File.WriteAllBytes(ancestor, start);
            var clashes = MergeByGuid(ancestor, ours, theirs, _world.Path);
            Assert.DoesNotContain(clashes, clash => ApprovalItemGuids(_world.Path).Contains(Guid.Parse(clash.Guid)));
            Capture(_world.Path);
            return _world.Path;
        }
    }

    private sealed record Clash(string Guid, string Property);

    // A three-way merge by object Guid, as Chorus merges fwdata (FLEx Bridge FieldWorksMergeServices).
    private static IReadOnlyList<Clash> MergeByGuid(string ancestorPath, string oursPath, string theirsPath, string resultPath)
    {
        var ancestor = XDocument.Load(ancestorPath).Root!.Elements("rt")
            .ToDictionary(rt => (string)rt.Attribute("guid")!, StringComparer.OrdinalIgnoreCase);
        var ours = XDocument.Load(oursPath);
        var theirs = XDocument.Load(theirsPath);
        var clashes = new List<Clash>();
        var root = ours.Root!;
        var fields = root.Element("AdditionalFields");
        foreach (var field in theirs.Root!.Element("AdditionalFields")?.Elements() ?? [])
            if (fields is not null && !fields.Elements().Any(each =>
                    (string?)each.Attribute("class") == (string?)field.Attribute("class") && (string?)each.Attribute("name") == (string?)field.Attribute("name")))
                fields.Add(new XElement(field));
        var byGuid = root.Elements("rt").ToDictionary(rt => (string)rt.Attribute("guid")!, StringComparer.OrdinalIgnoreCase);
        static string Key(XElement property) => property.Name.LocalName + "|" + (string?)property.Attribute("name");
        static XElement? Property(XElement? rt, string key) => rt?.Elements().FirstOrDefault(each => Key(each) == key);
        static bool Same(XElement? left, XElement? right) => left is null ? right is null : right is not null && XNode.DeepEquals(left, right);
        foreach (var other in theirs.Root.Elements("rt"))
        {
            var guid = (string)other.Attribute("guid")!;
            ancestor.TryGetValue(guid, out var old);
            if (!byGuid.TryGetValue(guid, out var mine))
            {
                if (old is null) root.Add(new XElement(other));
                continue;
            }
            if (XNode.DeepEquals(mine, other)) continue;
            foreach (var key in mine.Elements().Concat(other.Elements()).Select(Key).Distinct().ToArray())
            {
                var (m, t, b) = (Property(mine, key), Property(other, key), Property(old, key));
                if (Same(m, t) || Same(t, b)) continue;
                if (Same(m, b))
                {
                    m?.Remove();
                    if (t is not null) mine.Add(new XElement(t));
                    continue;
                }
                if (m is not null && t is not null && t.Elements().Any() &&
                    t.Elements().All(child => child.Name == "objsur") && m.Elements().All(child => child.Name == "objsur"))
                {
                    var held = m.Elements().Select(child => (string)child.Attribute("guid")!).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    foreach (var child in t.Elements().Where(child => !held.Contains((string)child.Attribute("guid")!)))
                        m.Add(new XElement(child));
                    continue;
                }
                // DateModified takes the merge time and DateCreated is immutable, so neither is a clash in Chorus.
                if (key is not ("DateModified|" or "DateCreated|")) clashes.Add(new(guid, key));
            }
        }
        ours.Save(resultPath);
        return clashes;
    }

    private static int FieldId(LcmCache cache)
    {
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        return metadata.GetFieldIds().Single(id => metadata.GetFieldName(id) == ScriptureApprovalFields.ApprovalsField);
    }

    private static void Capture(string path)
    {
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), Root(path));
        Assert.True(captured.Succeeded, captured.Refusal?.Code + ": " + captured.Refusal?.Message);
    }

    private static void Applied(string path)
    {
        var applied = Apply(path);
        Assert.True(applied.Succeeded, applied.Refusal?.Code + ": " + applied.Refusal?.Message);
        Assert.True(applied.Value!.Applied, applied.Value.Summary);
    }

    private static CommandOutcome<ApplyPendingResult> Apply(string path)
    {
        var runner = new InProcessRunnerLauncher(IsolatedRunner.Options(Root(path)));
        CommandOutcome<ApplyPendingResult> applied;
        try
        {
            applied = PendingChangesWorkflow.Apply(new ApplyPendingRequest(path, null, null),
                dryRunTimeout: TimeSpan.FromMinutes(2), runnerLauncher: runner);
        }
        finally
        {
            runner.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        if (applied.Succeeded && applied.Value!.Applied)
            Capture(path);
        return applied;
    }

    private static string Root(string projectPath)
    {
        var root = Path.Combine(Path.GetDirectoryName(projectPath)!, "source-approval-root");
        Directory.CreateDirectory(root);
        return root;
    }
}
