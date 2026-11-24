using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HandAnalysis;
using System.Collections.Immutable;
using System.Text.Json.Nodes;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Core.WritingSystems;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.RecordStore;
using SIL.Motif.RecordStore.HandAnalysis;
using SIL.Motif.RecordStore.Lcm;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Store = SIL.Motif.RecordStore.RecordStore;

namespace SIL.Motif.Tests.RecordStore.HandAnalyses;

[Collection(LcmCacheTestCollection.Name)]
public sealed class HandAnalysisSeededTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "motif-hand-seeded-" + Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(root, "records.sqlite");

    private sealed record Seed(SeededProject Project, SeededText Text);

    /// <summary>Imports a runtime project whose approved analysis uses the first entry.</summary>
    private Task<Seed> Import(params Func<Seed, GenericRecord>[] extras) => Import(null, extras);

    private async Task<Seed> Import(Action<LcmCache, Seed>? configure, params Func<Seed, GenericRecord>[] extras)
    {
        using var cache = NewLangProjFixture.CreateCache(root);
        var seed = SeededProject.Seed(cache);
        var text = SeededProject.SeedText(cache, seed);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(seed.FirstEntryId);
            var bundle = cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
            cache.ServiceLocator.GetInstance<IWfiAnalysisRepository>().GetObject(text.ApprovedAnalysisId).MorphBundlesOS.Add(bundle);
            bundle.MorphRA = entry.LexemeFormOA;
            bundle.SenseRA = entry.SensesOS[0];
            bundle.MsaRA = entry.MorphoSyntaxAnalysesOC.First();
        });
        var result = new Seed(seed, text);
        configure?.Invoke(cache, result);
        new FwDataProjectLoader().Save(cache);
        var graph = new LcmGraphReader(cache);
        var types = graph.FieldTypes;
        var imports = graph.Read().Select(r => new RecordImport(r, types)).ToList();
        var manifest = new GenericRecord { Id = LcmRecordBridge.ManifestId, Kind = "manifest" };
        manifest.Values["schema"] = graph.Schema();
        manifest.Values["nativeProject"] = JsonSerializer.SerializeToElement(cache.ProjectId.Name);
        imports.Add(new RecordImport(manifest, new Dictionary<int, int>()));
        imports.AddRange(extras.Select(e => new RecordImport(e(result), new Dictionary<int, int>())));
        await using var store = new Store(Database);
        await store.ImportAsync(imports, Guid.NewGuid(), graph.Coverage());
        return result;
    }

    private static GenericRecord Synthetic(string kind, Guid? target = null, string text = "MAT 5:3#2")
    {
        var record = new GenericRecord { Id = Guid.NewGuid(), Kind = kind };
        if (target.HasValue) record.Values["target"] = JsonSerializer.SerializeToElement(target.Value);
        record.Values["anchor"] = JsonSerializer.SerializeToElement(text);
        return record;
    }

    [Fact]
    public async Task DeletionClosureLeavesCountsUnknownWhenExternalCoverageIsMissing()
    {
        var seed = await Import();
        await using var host = await HandAnalysisHost.OpenAsync(Database);
        var pin = await host.PinAsync(new ExternalBasis(), null);
        var page = await pin.References(new ReferenceQuery.DeleteClosure(new(DeletionKind.Entry, seed.Project.FirstEntryId)), new PageRequest());
        Assert.False(page.Counts.CountComplete);
        Assert.Null(page.Counts.Analyses);
        Assert.Null(page.Rows.Total);
        Assert.Contains(page.Rows.Limitations, l => l.Reason.Contains("Conflicting-head", StringComparison.Ordinal));
        Assert.Contains(page.Rows.Limitations, l => l.Reason.Contains("Parser-reference", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AReferenceCursorRefusesAnotherSnapshotOfTheSameHome()
    {
        var seed = await Import();
        await using var host = await HandAnalysisHost.OpenAsync(Database);
        await using var first = await host.PinAsync(new ExternalBasis(), null);
        await using var other = await host.PinAsync(new ExternalBasis("different"), null);
        var query = new ReferenceQuery.DeleteClosure(new(DeletionKind.Entry, seed.Project.FirstEntryId));
        var page = await first.References(query, new(1));
        Assert.NotNull(page.Rows.NextCursor);
        Assert.False(page.Rows.Complete);
        Assert.Equal(first.Stamp, page.Stamp);
        var next = await first.References(query, new(1, page.Rows.NextCursor));
        Assert.False(next.Rows.Complete);
        Assert.NotEqual(Assert.Single(page.Rows.Items), Assert.Single(next.Rows.Items));
        Assert.Equal(RefusalCodes.HandAnalysisInvalidCursor, (await other.ReferencesAsync(query, new(1, page.Rows.NextCursor))).Refusal?.Code);
        var wrongKind = new ReferenceQuery.DeleteClosure(new(DeletionKind.Sense, seed.Project.FirstEntryId));
        Assert.Equal(RefusalCodes.HandAnalysisInvalidCursor, (await first.ReferencesAsync(wrongKind, new(1, page.Rows.NextCursor))).Refusal?.Code);
        Assert.Equal(RefusalCodes.HandAnalysisTypeMismatch, (await first.ReferencesAsync(wrongKind, new())).Refusal?.Code);
        Assert.Equal(page.Counts, next.Counts);
        Assert.Null(page.Counts.PlanToken);
        Assert.NotNull(page.Counts.ClosureDigest);
        Assert.Equal(first.Stamp, Assert.Single(page.Rows.Items).Stamp);
    }

    [Fact]
    public async Task AnAnchorDirectlyTargetingAnAnalysisIsReportedAsAUse()
    {
        var seed = await Import(s => Synthetic("hand-anchor", s.Text.ApprovedAnalysisId));
        await using var host = await HandAnalysisHost.OpenAsync(Database);
        var pin = await host.PinAsync(new ExternalBasis(), null);
        var page = await pin.References(new ReferenceQuery.Uses([new(ReferenceKind.Analysis, seed.Text.ApprovedAnalysisId)], true), new PageRequest());
        var row = Assert.Single(page.Rows.Items, r => !r.OccurrenceWitnesses.IsEmpty);
        var witness = Assert.Single(row.OccurrenceWitnesses);
        Assert.Equal("MAT 5:3#2", witness.RawLocation);
        Assert.Equal(seed.Text.ApprovedAnalysisId, witness.Target.Id);
        Assert.Equal(new ReferenceResultTarget(seed.Text.ApprovedAnalysisId, "WfiAnalysis"), row.Referrer);
        Assert.Equal(row.Referrer, witness.NativeSource);
        Assert.Null(row.Location);
        Assert.Equal(pin.Stamp, row.Stamp);
    }

    [Fact]
    public async Task UsesRetainsTheLinkBetweenTwoRequestedTargets()
    {
        Guid bundle = default;
        var seed = await Import((cache, s) => bundle = cache.ServiceLocator.GetInstance<IWfiAnalysisRepository>()
            .GetObject(s.Text.ApprovedAnalysisId).MorphBundlesOS.Last().Guid);
        await using var host = await HandAnalysisHost.OpenAsync(Database);
        await using var pin = await host.PinAsync(new ExternalBasis(), null);
        var query = new ReferenceQuery.Uses([new(ReferenceKind.Sense, seed.Project.FirstSenseId), new(ReferenceKind.Bundle, bundle)], true);
        var page = await pin.References(query, new PageRequest());
        var use = Assert.Single(page.Rows.Items, row => row.Target.Id == seed.Project.FirstSenseId && row.Referrer?.Id == bundle);
        Assert.Equal("Sense", use.Location?.Field);
    }

    [Fact]
    public async Task DeleteClosureRetainsEveryAnchorRelationshipAndCountsOneOccurrence()
    {
        Guid[] targets = [];
        var seed = await Import((cache, s) =>
        {
            var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(s.Project.FirstEntryId);
            targets = [entry.LexemeFormOA.Guid, entry.SensesOS[0].Guid, entry.MorphoSyntaxAnalysesOC.Single().Guid];
        }, s => Synthetic("hand-anchor", s.Text.ApprovedAnalysisId), _ => Synthetic("hand-request"));
        await using var host = await HandAnalysisHost.OpenAsync(Database);
        await using var pin = await host.PinAsync(new ExternalBasis(), null);
        var impact = pin.PreviewDeletion(seed.Project.FirstEntryId);
        Assert.True(impact.NativeComplete, string.Join("; ", impact.Limitations));
        Assert.Equal(targets.Order(), impact.Witnesses.Select(w => w.Target));
        Assert.Single(impact.Witnesses.Select(w => w.Identity).Distinct());
        Assert.All(impact.Witnesses, witness =>
        {
            Assert.Equal(ReferenceWitnessKind.Anchor, witness.Kind);
            Assert.Equal(seed.Text.ApprovedAnalysisId, witness.NativeSource);
            Assert.Equal("MAT 5:3#2", witness.Location);
            Assert.Contains(witness.Target, impact.Targets);
        });
        Assert.DoesNotContain(impact.Limitations, l => l.Contains("namespace", StringComparison.Ordinal));
        var rows = await pin.References(new ReferenceQuery.DeleteClosure(new(DeletionKind.Entry, seed.Project.FirstEntryId)), new PageRequest());
        Assert.Null(rows.Counts.Occurrences);
        var anchorRows = rows.Rows.Items.Where(r => !r.OccurrenceWitnesses.IsEmpty).ToArray();
        Assert.Equal(targets.Order(), anchorRows.Select(r => r.Target.Id));
        Assert.All(anchorRows, r => Assert.Equal(seed.Text.ApprovedAnalysisId, r.AnalysisId));
        var complete = new DeletionImpact(new ReferenceResult(impact.Revision, impact.Targets, [], impact.Witnesses, true, []),
            impact.Links, impact.Analyses, impact.Bundles, []);
        Assert.Equal(impact.Links.Length + 1, complete.LinkCount);
        await using var writer = new Store(Database);
        await Assert.ThrowsAsync<IncompleteDeletionClosureException>(() =>
            writer.PrepareDeletionAsync(seed.Project.FirstEntryId, pin.Stamp.HomePoint, impact.PlanToken));
        await Assert.ThrowsAsync<DeletionPlanMismatchException>(() =>
            writer.PrepareDeletionAsync(seed.Project.FirstEntryId, pin.Stamp.HomePoint, impact.PlanToken + "-different"));
    }

    [Fact]
    public async Task ACertifiedPointAnswersEqualAFullPointOnAnAnchoredStoreIncludingWitnessesAndBundles()
    {
        var seed = await Import(s => Synthetic("hand-anchor", s.Text.ApprovedAnalysisId));
        await using var full = new Store(Database) { CertifiedAcquisitionAllowed = false };
        await using var certified = new Store(Database);
        var records = await full.ReadAsync();
        var eager = await full.ReferencesAsync();
        var lazy = await certified.ReferencesAsync();
        Assert.Equal(1, certified.CertifiedAcquisitions);
        Assert.Equal(0, full.CertifiedAcquisitions);
        var targets = records.Where(r => r.Kind == "lcm" && eager.UsageSchema!.IsDeletable(r.ClassId)).Select(r => r.Id)
            .Append(seed.Text.ApprovedAnalysisId).Distinct().ToArray();
        foreach (var id in targets)
        {
            var expected = eager.DeletionImpact(id);
            var actual = lazy.DeletionImpact(id);
            Assert.Equal(expected.PlanToken, actual.PlanToken);
            Assert.Equal(expected.Links.ToArray(), actual.Links.ToArray());
            Assert.Equal(expected.Analyses.ToArray(), actual.Analyses.ToArray());
            Assert.Equal(expected.Bundles.ToArray(), actual.Bundles.ToArray());
            Assert.Equal(expected.Witnesses.ToArray(), actual.Witnesses.ToArray());
        }
        var all = records.Where(r => r.Kind == "lcm").Select(r => r.Id).ToArray();
        Assert.Equal(eager.Incoming(all).Witnesses.ToArray(), lazy.Incoming(all).Witnesses.ToArray());
        Assert.NotEmpty(lazy.Incoming([seed.Text.ApprovedAnalysisId]).Witnesses);
    }

    [Fact]
    public async Task ExplicitObservationsPreserveStoredAnchorWitnesses()
    {
        var seed = await Import(s => Synthetic("hand-anchor", s.Text.ApprovedAnalysisId));
        await using var store = new Store(Database);
        var index = await store.ReferencesAsync();
        var target = seed.Text.ApprovedAnalysisId;
        var stored = Assert.Single(index.Incoming([target]).Witnesses);
        var evidence = new ReferenceEvidence(index.Revision, "caller", "heads", null, []);
        var empty = (await store.ReferencesAsync(evidence)).Incoming([target]);
        Assert.Equal(stored, Assert.Single(empty.Witnesses));
        Assert.DoesNotContain("Anchor coverage is unavailable.", empty.Limitations);

        var head = new SIL.Motif.RecordStore.ReferenceWitness(ReferenceWitnessKind.ConflictingHead, "observed-head", "heads", target);
        var matching = stored with { EvidenceRevision = "caller" };
        var combined = (await store.ReferencesAsync(evidence with { Witnesses = [matching, head] })).Incoming([target]);
        Assert.Equal(new[] { stored, head }, combined.Witnesses);
        Assert.DoesNotContain("Anchor coverage is unavailable.", combined.Limitations);

        var conflicting = matching with { Location = "MAT 5:4#1" };
        var conflict = (await store.ReferencesAsync(evidence with { Witnesses = [conflicting] })).Incoming([target]);
        Assert.Contains(stored, conflict.Witnesses);
        Assert.Contains(conflicting, conflict.Witnesses);
        Assert.Contains("Anchor coverage is unavailable.", conflict.Limitations);
        Assert.Null(conflict.Total);

        var omitted = matching with { Identity = "omitted-anchor" };
        var expanded = (await store.ReferencesAsync(evidence with { Witnesses = [omitted] })).Incoming([target]);
        Assert.Contains(stored, expanded.Witnesses);
        Assert.Contains(omitted, expanded.Witnesses);
        Assert.Contains("Anchor coverage is unavailable.", expanded.Limitations);
        Assert.Null(expanded.Total);
    }

    [Fact]
    public async Task OpeningAMissingHomeCreatesNoFilesOrDirectories()
    {
        Directory.CreateDirectory(root);
        var refusal = await Assert.ThrowsAsync<IndexUnavailableException>(() => HandAnalysisHost.OpenAsync(Database));
        Assert.IsType<SqliteException>(refusal.InnerException);
        await Assert.ThrowsAsync<IndexUnavailableException>(() => HandAnalysisHost.OpenAsync(Path.Combine(root, "absent", "store.sqlite")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AReadWithAnEmptyOrMissingWalLeavesDatabaseAndWalContentUnchanged(bool removeSidecars)
    {
        await Import();
        var wal = Database + "-wal";
        var walBytes = File.ReadAllBytes(wal);
        Assert.Empty(walBytes);
        if (removeSidecars) RemoveSidecars();
        var before = File.ReadAllBytes(Database);
        using (File.Open(Database + ".write.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await using var host = await HandAnalysisHost.OpenAsync(Database);
            if (removeSidecars) RemoveSidecars();
            await using var pin = await host.PinAsync(new ExternalBasis(), null);
            if (removeSidecars) RemoveSidecars();
            Assert.True(pin.IsCurrent);
            if (removeSidecars) RemoveSidecars();
            await host.CheckPointAsync();
        }
        Assert.Equal(before, File.ReadAllBytes(Database));
        if (!removeSidecars || File.Exists(wal)) Assert.Equal(walBytes, File.ReadAllBytes(wal));

        void RemoveSidecars()
        {
            if (File.Exists(wal)) Assert.Equal(walBytes, File.ReadAllBytes(wal));
            File.Delete(wal);
            File.Delete(Database + "-shm");
        }
    }

    [Fact]
    public async Task PinsBindTheOpaqueBasisProjectAndStoreScope()
    {
        await Import();
        var copy = Path.Combine(root, "copy.sqlite");
        CopyDatabase(Database, copy);
        await using var host = await HandAnalysisHost.OpenAsync(Database);
        await using var otherHost = await HandAnalysisHost.OpenAsync(copy);
        await using var first = await host.PinAsync(new ExternalBasis("a\nb", "c"), null);
        await using var second = await host.PinAsync(new ExternalBasis("a", "b\nc"), null);
        Assert.NotEqual(first.Stamp.SnapshotId, second.Stamp.SnapshotId);
        Assert.Equal("a", second.Stamp.SourceRevision);
        Assert.Equal("b\nc", second.Stamp.ParserRevision);

        await using var other = await otherHost.PinAsync(new ExternalBasis("a\nb", "c"), null);
        Assert.Equal(first.Stamp.ProjectId, other.Stamp.ProjectId);
        Assert.Equal(first.Stamp.HomePoint, other.Stamp.HomePoint);
        Assert.NotEqual(first.Stamp.SnapshotId, other.Stamp.SnapshotId);
        await Assert.ThrowsAsync<SnapshotExpiredException>(() => otherHost.PinAsync(new ExternalBasis(), first.Stamp.SnapshotId));

        var index = (IHandAnalysisIndex)host;
        await using var typed = (await index.PinAsync(first.Stamp.ProjectId, first.Stamp.SnapshotId, default)).Value();
        Assert.Equal(first.Stamp, typed.Stamp);
        var wrongProject = await index.PinAsync(Guid.NewGuid(), null, default);
        Assert.Null(wrongProject.Value);
        Assert.Equal(RefusalCodes.HandAnalysisIndexUnavailable, wrongProject.Refusal?.Code);
        Assert.Equal(HandAnalysisRefusalFacts.Unavailable, wrongProject.Refusal!.Facts[HandAnalysisRefusalFacts.Cause]);
        Assert.Equal(typed.Stamp, Assert.IsType<ProjectRead.Available>(typed.Project).Facts.Stamp);
        var unsupported = (await typed.FormsAsync(new("", "", [], FormMatch.Equal, FormOrder.Encounter), new(), default)).Value();
        Assert.Equal(typed.Stamp, unsupported.Stamp);
        Assert.Equal(0, unsupported.Total);
        Assert.True(unsupported.Complete);
    }

    [Fact]
    public async Task FourCompositePointsAreRetainedAndAnEvictedPinExpiresOnDisposal()
    {
        var seed = await Import();
        await using var host = await HandAnalysisHost.OpenAsync(Database);
        await using var store = new Store(Database);
        var basis = new ExternalBasis();
        var oldest = await host.PinAsync(basis, null);
        var oldId = oldest.Stamp.SnapshotId;
        var entry = (await store.ReadAsync()).Single(r => r.Id == seed.Project.FirstEntryId);
        var field = LexEntryTags.kflidHomographNumber;
        var value = entry.Values[field.ToString()].GetInt32();
        var revision = oldest.Stamp.HomePoint;
        var ids = new List<string>();
        for (var i = 0; i < HandAnalysisHost.RetainedPoints; i++)
        {
            var next = await store.SetFieldAsync(entry.Id, field, JsonSerializer.SerializeToElement(value), JsonSerializer.SerializeToElement(value + 1),
                revision, Guid.NewGuid());
            Assert.NotEqual(revision, next);
            value++;
            revision = next;
            await using var pin = await host.PinAsync(basis, null);
            ids.Add(pin.Stamp.SnapshotId);
        }
        foreach (var id in ids)
        {
            await using var retained = await host.PinAsync(basis, id);
            Assert.Equal(id, retained.Stamp.SnapshotId);
        }
        ids.Clear();
        for (var i = 0; i <= HandAnalysisHost.RetainedPoints; i++)
        {
            await using var sameHome = await host.PinAsync(basis with { SourceRevision = i.ToString() }, null);
            Assert.Equal(revision, sameHome.Stamp.HomePoint);
            ids.Add(sameHome.Stamp.SnapshotId);
        }
        var index = (IHandAnalysisIndex)host;
        var expired = await index.PinAsync(oldest.Stamp.ProjectId, ids[0], default);
        Assert.Null(expired.Value);
        Assert.Equal(RefusalCodes.HandAnalysisSnapshotExpired, expired.Refusal?.Code);
        Assert.Equal(ids[0], expired.Refusal!.Facts[HandAnalysisRefusalFacts.SnapshotId]);
        foreach (var id in ids.Skip(1))
        {
            await using var retained = (await index.PinAsync(oldest.Stamp.ProjectId, id, default)).Value();
            Assert.Equal(id, retained.Stamp.SnapshotId);
        }
        await using (var live = await host.PinAsync(basis, oldId)) Assert.Equal(oldId, live.Stamp.SnapshotId);
        await oldest.DisposeAsync();
        await Assert.ThrowsAsync<SnapshotExpiredException>(() => host.PinAsync(basis, oldId));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await oldest.WordsAsync(TestWord.Reading(seed.Text.ApprovedAnalysisId), new()));
    }

    [Fact]
    public async Task AReadOverlappingPublicationSeesOneWholeHomePointAndLeavesTheRetainedWalUnchanged()
    {
        Guid category = default;
        var seed = await Import((cache, _) =>
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var part = cache.ServiceLocator.GetInstance<IPartOfSpeechFactory>().Create();
                cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(part);
                category = part.Guid;
            });
        });
        using var pause = new PublicationReadPause(Database);
        await using var host = await HandAnalysisHost.OpenAsync(Database);
        var field = WfiAnalysisTags.kflidCategory;
        string oldRevision;
        JsonElement prior;
        await using (var observer = new Store(Database))
        {
            oldRevision = (await observer.ReferencesAsync()).Revision;
            var record = (await observer.ReadAsync()).Single(r => r.Id == seed.Text.ApprovedAnalysisId);
            prior = record.Values[field.ToString()];
        }

        Task<StoreSnapshot> read;
        string revision;
        try
        {
            await using var writer = new Store(Database);
            pause.Arm();
            read = Task.Run(() => host.PinAsync(new ExternalBasis(), null));
            await Wait.UntilAsync(() => pause.Paused.IsCompleted || read.IsCompleted, "Pin reached its revision query",
                detail: () => pause.Detail);
            if (read.IsFaulted) await read;
            Assert.True(pause.Paused.IsCompleted, "The pin finished before the revision query was paused. " + pause.Detail);
            revision = await writer.SetFieldAsync(seed.Text.ApprovedAnalysisId, field, prior,
                JsonSerializer.SerializeToElement(category), oldRevision, Guid.NewGuid());
        }
        finally { pause.Release(); }
        await using var overlapping = await read;
        Assert.Equal(oldRevision, overlapping.Stamp.HomePoint);
        Assert.False(overlapping.IsCurrent);
        var reading = TestWord.Reading(seed.Text.ApprovedAnalysisId);
        var page = await overlapping.Words(reading, new());
        Assert.Equal(overlapping.Stamp, page.Stamp);
        Assert.Equal(prior.ValueKind == JsonValueKind.String ? prior.GetGuid() : (Guid?)null,
            Assert.IsType<WordFact.Reading>(Assert.Single(page.Items)).CategoryId);

        var databaseBytes = File.ReadAllBytes(Database);
        var walBytes = File.ReadAllBytes(Database + "-wal");
        Assert.NotEmpty(walBytes);
        await using var reopened = await HandAnalysisHost.OpenAsync(Database);
        await using var current = await reopened.PinAsync(new ExternalBasis(), null);
        Assert.Equal(revision, current.Stamp.HomePoint);
        Assert.True(current.IsCurrent);
        Assert.Equal(category, Assert.IsType<WordFact.Reading>(Assert.Single((await current.Words(reading, new())).Items)).CategoryId);
        Assert.Equal(databaseBytes, File.ReadAllBytes(Database));
        Assert.Equal(walBytes, File.ReadAllBytes(Database + "-wal"));
    }

    [Theory]
    [InlineData("RecordStoreState", "RecordContent")]
    [InlineData("RecordContent", "Commits")]
    public async Task CancellationDuringAcquisitionSkipsTheNextVerificationRead(string pausedTable, string nextTable)
    {
        Guid project = default;
        await Import((cache, _) => project = cache.LangProject.Guid);
        using var pause = new PublicationReadPause(Database, pausedTable);
        await using var host = await HandAnalysisHost.OpenAsync(Database);
        IHandAnalysisIndex index = host;
        using var cancel = new CancellationTokenSource();
        pause.Arm();
        var read = Task.Run(async () => await index.PinAsync(project, null, cancel.Token));
        var queriesBeforePause = 0;
        try
        {
            await Wait.UntilAsync(() => pause.Paused.IsCompleted || read.IsCompleted, "Pin reached its verification query",
                detail: () => pause.Detail);
            if (read.IsFaulted) await read;
            Assert.True(pause.Paused.IsCompleted, "The pin finished before the verification query was paused. " + pause.Detail);
            queriesBeforePause = pause.Detail.Length;
            cancel.Cancel();
        }
        finally { pause.Release(); }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        Assert.DoesNotContain($"FROM \"{nextTable}\"", pause.Detail[queriesBeforePause..], StringComparison.Ordinal);
        await using var retry = (await index.PinAsync(project, null, default)).Value();
        Assert.Equal(project, retry.Stamp.ProjectId);
    }

    [Fact]
    public async Task CompleteUnusedEvidenceAllowsDeletionWhileMissingExternalEvidenceRefuses()
    {
        Guid target = default;
        await Import((cache, _) => NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            target = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create().Guid));
        await using var host = await HandAnalysisHost.OpenAsync(Database);
        await using var pin = await host.PinAsync(new ExternalBasis(), null);
        var incomplete = pin.PreviewDeletion(target);
        Assert.True(incomplete.NativeComplete);
        Assert.Empty(incomplete.Links);
        Assert.Empty(incomplete.Analyses);
        Assert.False(incomplete.DeleteAllowed);
        var completeReferences = new ReferenceResult(incomplete.Revision, [target], [], [], true, []);
        var complete = new DeletionImpact(completeReferences, [], [], [], []);
        Assert.True(complete.DeleteAllowed);
        Assert.Equal(0, complete.AnalysisCount);
        complete.RequireDeletionAllowed(complete.PlanToken);
    }

    private static void CopyDatabase(string from, string to)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            if (File.Exists(from + suffix)) File.Copy(from + suffix, to + suffix);
    }

    [Fact]
    public async Task ExactCaseRemainsDistinctAndTheCommandCanLinkTheLowerCaseReading()
    {
        Guid upperId = default, lowerId = default, lowerAnalysis = default;
        string ws;
        string lower;
        const string upper = "Word";
        using (var cache = NewLangProjFixture.CreateCache(root))
        {
            var definition = cache.ServiceLocator.WritingSystemManager.Get(cache.DefaultVernWs);
            ws = definition.Id;
            lower = new CaseFunctions(definition).ToLower(upper);
            Assert.NotEqual(upper, lower);
            var services = cache.ServiceLocator;
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var factory = services.GetInstance<IWfiWordformFactory>();
                upperId = factory.Create(TsStringUtils.MakeString(upper, cache.DefaultVernWs)).Guid;
                var word = factory.Create(TsStringUtils.MakeString(lower, cache.DefaultVernWs));
                lowerId = word.Guid;
                var analysis = services.GetInstance<IWfiAnalysisFactory>().Create();
                word.AnalysesOC.Add(analysis);
                lowerAnalysis = analysis.Guid;
            });
            var graph = new LcmGraphReader(cache);
            var imports = graph.Read().Select(r => new RecordImport(r, graph.FieldTypes)).ToList();
            var manifest = new GenericRecord { Id = LcmRecordBridge.ManifestId, Kind = "manifest" };
            manifest.Values["schema"] = graph.Schema();
            imports.Add(new RecordImport(manifest, new Dictionary<int, int>()));
            await using var store = new Store(Database);
            await store.ImportAsync(imports, Guid.NewGuid(), graph.Coverage());
        }
        await using var host = await HandAnalysisHost.OpenAsync(Database);
        await using var pin = await host.PinAsync(new ExternalBasis(), null);
        var exact = await pin.Words(new WordQuery.Identity(TestWord.Key(ws, upper)), new());
        var header = Assert.IsType<WordFact.Header>(Assert.Single(exact.Items));
        Assert.Equal(upperId, Assert.Single(header.ExactMatches));
        Assert.Equal(0, header.Counts.Analyses);

        var casePair = new WordformKey(ws, lower);
        var linked = await pin.Words(new WordQuery.Identity(new WordIdentity.Native(upperId), casePair), new());
        var other = Assert.Single(Assert.IsType<WordFact.Header>(Assert.Single(linked.Items)).OtherCases);
        Assert.Equal(lowerId, Assert.Single(other.WordformIds));
        Assert.Equal(casePair, other.Key);
        var fallback = await pin.Words(new WordQuery.Alternatives(new WordIdentity.Native(lowerId), WordOrder.Encounter), new());
        Assert.Equal(lowerAnalysis, TestWord.AnalysisId(Assert.IsType<WordFact.Reading>(Assert.Single(fallback.Items))));
        Assert.All(new[] { exact, linked, fallback }, page => Assert.Equal(pin.Stamp, page.Stamp));
    }

    [Fact]
    public async Task AReadingPreservesNativeBundleOrderEvenWhenGuidsSortDifferently()
    {
        Guid[] expected = [];
        var seed = await Import((cache, seed) =>
        {
            var services = cache.ServiceLocator;
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var analysis = services.GetInstance<IWfiAnalysisRepository>().GetObject(seed.Text.ApprovedAnalysisId);
                var factory = services.GetInstance<IWfiMorphBundleFactory>();
                foreach (var bundle in new[] { factory.Create(), factory.Create() }.OrderByDescending(b => b.Guid))
                    analysis.MorphBundlesOS.Add(bundle);
                expected = analysis.MorphBundlesOS.Select(b => b.Guid).ToArray();
            });
        });
        Assert.NotEqual(expected.Order(), expected);
        await using var host = await HandAnalysisHost.OpenAsync(Database);
        await using var pin = await host.PinAsync(new ExternalBasis(), null);
        var page = await pin.Words(TestWord.Reading(seed.Text.ApprovedAnalysisId), new());
        Assert.Equal(expected.Select(id => (Guid?)id), Assert.IsType<WordFact.Reading>(Assert.Single(page.Items)).Bundles.Select(b => b.Id));
    }

    [Fact]
    public async Task WordformsAndAnalysesComeBackInCreationOrderNotGuidOrder()
    {
        var created = new List<(Guid Wordform, Guid[] Analyses)>();
        using (var cache = NewLangProjFixture.CreateCache(root))
        {
            var services = cache.ServiceLocator;
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                for (var n = 0; n < 12; n++)
                {
                    var word = services.GetInstance<IWfiWordformFactory>().Create(TsStringUtils.MakeString("w" + n, cache.DefaultVernWs));
                    var analyses = Enumerable.Range(0, 3).Select(_ =>
                    {
                        var analysis = services.GetInstance<IWfiAnalysisFactory>().Create();
                        word.AnalysesOC.Add(analysis);
                        return analysis.Guid;
                    }).ToArray();
                    created.Add((word.Guid, analyses));
                }
            });
            new FwDataProjectLoader().Save(cache);
            var native = services.GetInstance<IWfiWordformRepository>().AllInstances().Select(w => w.Guid).ToArray();
            Assert.NotEqual(native.Order(), native);
            var graph = new LcmGraphReader(cache);
            var imports = graph.Read().Select(r => new RecordImport(r, graph.FieldTypes)).ToList();
            var manifest = new GenericRecord { Id = LcmRecordBridge.ManifestId, Kind = "manifest" };
            manifest.Values["schema"] = graph.Schema();
            manifest.Values["nativeProject"] = JsonSerializer.SerializeToElement(cache.ProjectId.Name);
            imports.Add(new RecordImport(manifest, new Dictionary<int, int>()));
            await using var store = new Store(Database);
            await store.ImportAsync(imports, Guid.NewGuid(), graph.Coverage());
            await using var host = await HandAnalysisHost.OpenAsync(Database);
            IHandAnalysisIndex index = host;
            await using var pin = (await index.PinAsync(cache.LangProject.Guid, null, default)).Value();
            var headers = (await Assert.IsType<StoreSnapshot>(pin).Inventory(new())).Items;
            Assert.Equal(native, headers.Select(h => h.Id));
            var key = TestWord.Key(headers[0].Forms.Keys.Single(), headers[0].Forms.Values.Single());
            var alternatives = new WordQuery.Alternatives(key, WordOrder.Encounter);
            var first = await pin.Words(alternatives, new(1), default);
            Assert.True(first.Complete);
            Assert.Equal(3, first.Total);
            Assert.NotNull(first.NextCursor);
            Assert.Equal(pin.Stamp, first.Stamp);
            var rest = await pin.Words(alternatives, new(2, first.NextCursor), default);
            Assert.True(rest.Complete);
            Assert.Equal(first.Total, rest.Total);
            Assert.Equal(pin.Stamp, rest.Stamp);
            Assert.Null(rest.NextCursor);
            var whole = await pin.Words(alternatives, new(3), default);
            Assert.Equal(whole.Items.AsEnumerable(), first.Items.Concat(rest.Items));
            Assert.Equal(whole.Items.AsEnumerable(), await Pages.All(p => pin.Words(alternatives, p, default), 1));
            Assert.Equal(RefusalCodes.HandAnalysisInvalidCursor, (await pin.WordsAsync(
                new WordQuery.Alternatives(key, WordOrder.DefaultRank), new(1, first.NextCursor), default)).Refusal?.Code);
            await using var other = await host.PinAsync(new ExternalBasis("different"), null);
            Assert.Equal(RefusalCodes.HandAnalysisInvalidCursor,
                (await other.WordsAsync(alternatives, new(1, first.NextCursor))).Refusal?.Code);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await pin.WordsAsync(alternatives, new(), new CancellationToken(canceled: true)));
            var analysisOrder = services.GetInstance<IWfiAnalysisRepository>().AllInstances().Select(a => a.Guid).ToArray();
            Assert.NotEqual(analysisOrder.Order(), analysisOrder);
            foreach (var header in headers)
            {
                var readings = (await pin.Words(new WordQuery.Alternatives(new WordIdentity.Native(header.Id), WordOrder.Encounter),
                    new PageRequest(), default)).Items.Cast<WordFact.Reading>().Select(TestWord.AnalysisId).ToArray();
                Assert.Equal(created.Single(c => c.Wordform == header.Id).Analyses.OrderBy(a => Array.IndexOf(analysisOrder, a)), readings);
            }
        }
    }

    [Fact]
    public async Task AnUnregisteredRecordKindStillMakesTheClosureIncomplete()
    {
        var seed = await Import(_ => Synthetic("mystery-kind"));
        await using var store = new Store(Database);
        Assert.False((await store.ReferencesAsync()).DeletionImpact(seed.Project.FirstEntryId).NativeComplete);
        await using var host = await HandAnalysisHost.OpenAsync(Database);
        await Assert.ThrowsAsync<IndexUnavailableException>(() => host.PinAsync(new ExternalBasis(), null));
    }

    [Fact]
    public async Task AMalformedAnchorMakesTheClosureIncomplete()
    {
        var seed = await Import(_ => Synthetic("hand-anchor", Guid.NewGuid()));
        await using var store = new Store(Database);
        Assert.False((await store.ReferencesAsync()).DeletionImpact(seed.Project.FirstEntryId).NativeComplete);
    }

    [Fact]
    public async Task TamperedSchemaOrMembershipRefusesInsteadOfReturningAnEmptyIndex()
    {
        await Import();
        await using var store = new Store(Database);
        var records = (await store.ReadAsync()).ToList();
        Assert.Single(WfiProjection.FromRecords(records).Readings);

        var tampered = records.ToList();
        var manifest = tampered.Single(r => r.Kind == "manifest");
        tampered[tampered.IndexOf(manifest)] = Rebuilt(manifest, v => v["schema"] =
            JsonSerializer.SerializeToElement(JsonSerializer.Serialize(manifest.Values["schema"]).Replace("MorphBundles", "MorphBundlez")));
        Assert.Throws<IndexUnavailableException>(() => WfiProjection.FromRecords(tampered));

        var analysis = records.First(r => r.ClassId == WfiAnalysisTags.kClassId);
        var dropped = records.ToList();
        dropped[dropped.IndexOf(analysis)] = Rebuilt(analysis, _ => { }, analysis.Provenance! with
        { RepositoryOrdinals = ImmutableDictionary<string, long>.Empty });
        Assert.Throws<IndexUnavailableException>(() => WfiProjection.FromRecords(dropped));

        Assert.Throws<IndexUnavailableException>(() => WfiProjection.FromRecords(records.Where(r => r.Kind != "native-coverage").ToList()));
    }

    [Fact]
    public async Task ABindingByNameRefusesWhenAFieldIsMissingFromAnOtherwiseConsistentSchema()
    {
        await Import();
        await using var store = new Store(Database);
        var records = (await store.ReadAsync()).ToList();
        var manifest = records.Single(r => r.Kind == "manifest");
        var schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(manifest.Values["schema"])!;
        schema["fields"] = JsonSerializer.SerializeToElement(schema["fields"].EnumerateArray().Where(f =>
            !(f.GetProperty("owner").GetString() == "WfiMorphBundle" && f.GetProperty("name").GetString() == "Morph")).ToArray());
        var rewritten = JsonSerializer.SerializeToElement(schema);
        records[records.IndexOf(manifest)] = Rebuilt(manifest, v => v["schema"] = rewritten);
        var coverage = records.Single(r => r.Kind == "native-coverage");
        var basis = coverage.Values["basis"].Deserialize<NativeImportCoverage>()! with { SchemaDigest = NativeImportCoverage.ComputeSchemaDigest(rewritten) };
        records[records.IndexOf(coverage)] = Rebuilt(coverage, v => v["basis"] = JsonSerializer.SerializeToElement(basis));
        var failure = Assert.Throws<IndexUnavailableException>(() => WfiProjection.FromRecords(records));
        Assert.Contains("WfiMorphBundle.Morph", failure.Message);
    }

    [Fact]
    public async Task MorphBindsByNameInAConsistentlyRemappedSchema()
    {
        await Import();
        await using var store = new Store(Database);
        var records = (await store.ReadAsync()).ToList();
        var original = WfiProjection.FromRecords(records);
        var oldId = WfiMorphBundleTags.kflidMorph;
        const int newId = 95112002;
        var manifest = records.Single(r => r.Kind == "manifest");
        var schema = JsonNode.Parse(manifest.Values["schema"].GetRawText())!;
        var field = schema["fields"]!.AsArray().Single(f => f!["id"]!.GetValue<int>() == oldId)!;
        Assert.Equal("Morph", field["name"]!.GetValue<string>());
        field["id"] = newId;
        var rewritten = JsonSerializer.SerializeToElement(schema);
        records[records.IndexOf(manifest)] = Rebuilt(manifest, v => v["schema"] = rewritten);
        var coverage = records.Single(r => r.Kind == "native-coverage");
        var basis = coverage.Values["basis"].Deserialize<NativeImportCoverage>()! with
        {
            SchemaDigest = NativeImportCoverage.ComputeSchemaDigest(rewritten)
        };
        records[records.IndexOf(coverage)] = Rebuilt(coverage, v => v["basis"] = JsonSerializer.SerializeToElement(basis));
        var bundles = records.Where(r => r.ClassId == WfiMorphBundleTags.kClassId).ToArray();
        foreach (var bundle in bundles)
        {
            var values = Rebuilt(bundle, v =>
            {
                Assert.True(v.Remove(oldId.ToString(), out var morph));
                v.Add(newId.ToString(), morph);
            });
            var types = bundle.Values.Keys.ToDictionary(int.Parse, key => RecordInitialization.Type(bundle, key));
            Assert.True(types.Remove(oldId, out var type));
            types.Add(newId, type);
            records[records.IndexOf(bundle)] = RecordInitialization.Prepare(values,
                RecordInitialization.Layout([new RecordImport(values, types)]));
        }
        var remapped = WfiProjection.FromRecords(records);
        Assert.Equal(newId, new WfiSchema(NativeUsageSchema.Read(records)!).Morph);
        Assert.Equal(Assert.Single(original.Readings.Values).Fact.Bundles.Select(b => b.MorphId),
            Assert.Single(remapped.Readings.Values).Fact.Bundles.Select(b => b.MorphId));
        Assert.Equal(original.FactsDigest, remapped.FactsDigest);
    }

    [Theory]
    [InlineData("destination")]
    [InlineData("utf16")]
    [InlineData("ordinal")]
    public async Task InvalidWfiFactsAreRefused(string failure)
    {
        await Import();
        await using var store = new Store(Database);
        var records = (await store.ReadAsync()).ToList();
        if (failure == "destination")
        {
            var manifest = records.Single(r => r.Kind == "manifest");
            var schema = JsonNode.Parse(manifest.Values["schema"].GetRawText())!;
            var field = schema["fields"]!.AsArray().Single(f => f!["id"]!.GetValue<int>() == WfiAnalysisTags.kflidCategory)!;
            field["destination"] = LexEntryTags.kClassId;
            var rewritten = JsonSerializer.SerializeToElement(schema);
            records[records.IndexOf(manifest)] = Rebuilt(manifest, v => v["schema"] = rewritten);
            var coverage = records.Single(r => r.Kind == "native-coverage");
            var basis = coverage.Values["basis"].Deserialize<NativeImportCoverage>()! with
            {
                SchemaDigest = NativeImportCoverage.ComputeSchemaDigest(rewritten)
            };
            records[records.IndexOf(coverage)] = Rebuilt(coverage, v => v["basis"] = JsonSerializer.SerializeToElement(basis));
        }
        else if (failure == "utf16")
        {
            var word = records.First(r => r.ClassId == WfiWordformTags.kClassId);
            var malformed = JsonSerializer.SerializeToElement(new Dictionary<string, object>
            {
                ["en"] = new { text = new { utf16le = "AA==" } }
            });
            records[records.IndexOf(word)] = Rebuilt(word, v => v[WfiWordformTags.kflidForm.ToString()] = malformed);
        }
        else
        {
            var words = records.Where(r => r.ClassId == WfiWordformTags.kClassId).Take(2).ToArray();
            var provenance = words[1].Provenance! with
            {
                RepositoryOrdinals = words[1].Provenance!.RepositoryOrdinals.SetItem("WfiWordform",
                    words[0].Provenance!.RepositoryOrdinals["WfiWordform"])
            };
            records[records.IndexOf(words[1])] = Rebuilt(words[1], _ => { }, provenance);
        }
        Assert.Throws<IndexUnavailableException>(() => WfiProjection.FromRecords(records));
    }

    [Fact]
    public async Task AChangedDigestIsCheckedBeforeReturningARingHit()
    {
        await Import();
        await using var host = await HandAnalysisHost.OpenAsync(Database);
        var basis = new ExternalBasis();
        await using var first = await host.PinAsync(basis, null);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE RecordStoreState SET IndexDigest = 'incorrect'";
        command.ExecuteNonQuery();
        var latest = await host.PinAsync(first.Stamp.ProjectId, null, default);
        Assert.Null(latest.Value);
        Assert.Equal(RefusalCodes.HandAnalysisIndexUnavailable, latest.Refusal?.Code);
        Assert.Equal(HandAnalysisRefusalFacts.Corrupt, latest.Refusal!.Facts[HandAnalysisRefusalFacts.Cause]);
        Assert.False(first.IsCurrent);
        await using var historical = await host.PinAsync(basis, first.Stamp.SnapshotId);
        Assert.Equal(first.Stamp, historical.Stamp);
    }

    [Fact]
    public async Task ACorruptReferenceIndexRefusesThePin()
    {
        await Import();
        await using var host = await HandAnalysisHost.OpenAsync(Database);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM IncomingReferences";
            command.ExecuteNonQuery();
            await Assert.ThrowsAsync<IndexUnavailableException>(() => host.PinAsync(new ExternalBasis(), null));
        }
    }

    [Theory]
    [InlineData("Source")]
    [InlineData("Target")]
    public async Task AColdPinRefusesAMalformedPersistedReference(string column)
    {
        Guid project = default;
        await Import((cache, _) => project = cache.LangProject.Guid);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                UPDATE IncomingReferences SET {column} = X'00'
                WHERE (Target, Source, Field, Position, Occurrence) =
                    (SELECT Target, Source, Field, Position, Occurrence FROM IncomingReferences LIMIT 1)
                """;
            Assert.Equal(1, command.ExecuteNonQuery());
        }

        await using var host = await HandAnalysisHost.OpenAsync(Database);
        IHandAnalysisIndex index = host;
        var pin = await index.PinAsync(project, null, default);
        Assert.Null(pin.Value);
        Assert.Equal(RefusalCodes.HandAnalysisIndexUnavailable, pin.Refusal?.Code);
        Assert.Equal(HandAnalysisRefusalFacts.Corrupt, pin.Refusal!.Facts[HandAnalysisRefusalFacts.Cause]);
    }

    [Fact]
    public async Task AnUnrelatedInflectionalTypeLeavesTheObservedReadingWithoutASignature()
    {
        Guid type = default;
        var seed = await Import((cache, s) => NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var services = cache.ServiceLocator;
            var lexDb = cache.LangProject.LexDbOA;
            lexDb.VariantEntryTypesOA ??= services.GetInstance<ICmPossibilityListFactory>().Create();
            var infl = services.GetInstance<ILexEntryInflTypeFactory>().Create();
            lexDb.VariantEntryTypesOA.PossibilitiesOS.Add(infl);
            type = infl.Guid;
            services.GetInstance<IWfiAnalysisRepository>().GetObject(s.Text.ApprovedAnalysisId).MorphBundlesOS.Last().InflTypeRA = infl;
        }));
        await using var host = await HandAnalysisHost.OpenAsync(Database);
        await using var pin = await host.PinAsync(new ExternalBasis(), null);
        var page = await pin.Words(TestWord.Reading(seed.Text.ApprovedAnalysisId), new());
        var reading = Assert.IsType<WordFact.Reading>(Assert.Single(page.Items));
        Assert.Equal(type, Assert.Single(reading.Bundles, bundle => bundle.InflTypeId is not null).InflTypeId);
        Assert.Null(reading.Signature);
        Assert.Contains(reading.Limitations, limitation => limitation.Code == "reading-signature-unavailable");
    }

    private static GenericRecord Rebuilt(GenericRecord source, Action<Dictionary<string, JsonElement>> change, NativeProvenance? provenance = null)
    {
        var copy = new GenericRecord
        {
            Id = source.Id, Kind = source.Kind, ClassId = source.ClassId, Owner = source.Owner, OwningField = source.OwningField,
            Values = new(source.Values, StringComparer.Ordinal), Provenance = provenance ?? source.Provenance
        };
        change(copy.Values);
        return copy;
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
