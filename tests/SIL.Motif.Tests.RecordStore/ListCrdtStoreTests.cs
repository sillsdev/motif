using System.Text.Json;
using SIL.ListCrdt;
using SIL.Motif.RecordStore;
using Xunit;
using Store = SIL.Motif.RecordStore.RecordStore;

namespace SIL.Motif.Tests.RecordStore;

public sealed class ListCrdtStoreTests : IDisposable
{
    private const int Field = 1000;
    private readonly string root = Path.Combine(Path.GetTempPath(), "motif-record-list-" + Guid.NewGuid().ToString("N"));
    private string Database
    {
        get { Directory.CreateDirectory(root); return Path.Combine(root, "records.sqlite"); }
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    private static GenericRecord Base(Guid[] items, int type = 27)
    {
        var layout = RecordInitialization.Layout([new(new GenericRecord { Id = Guid.NewGuid(), Values = { [Field.ToString()] = JsonSerializer.SerializeToElement(items) } },
            new Dictionary<int, int> { [Field] = type })]);
        var record = RecordInitialization.Prepare(new GenericRecord { Id = Guid.NewGuid() }, layout);
        if (items.Length > 1000)
        {
            record.HistoryFor(Field).Initial.AddRange(items);
            record.Values[Field.ToString()] = JsonSerializer.SerializeToElement(items);
            return record;
        }
        for (var i = 0; i < items.Length; i++) new InsertRecordItem(record.Id, Field, items[i], i).ApplyChange(record, null!).AsTask().Wait();
        return record;
    }

    private static void Apply(ListRecordOp change, GenericRecord record) =>
        Assert.True(change.ApplyChange(record, null!).IsCompletedSuccessfully);

    private static GenericRecord Fold(GenericRecord start, IEnumerable<ListRecordOp> operations)
    {
        var record = (GenericRecord)start.Copy();
        foreach (var operation in operations) operation.ApplyChange(record, null!).AsTask().Wait();
        return record;
    }

    private static IReadOnlyList<Guid> Visible(GenericRecord record) =>
        record.Values[Field.ToString()].EnumerateArray().Select(x => x.GetGuid()).ToArray();

    private static List<ListRecordOp> Author(GenericRecord start, Guid client, int count, Random random, List<Guid> inserted, HashSet<Guid> deleted)
    {
        var local = (GenericRecord)start.Copy();
        var authored = new List<ListRecordOp>();
        for (var i = 0; i < count; i++)
        {
            var live = Visible(local).Count;
            ListRecordOp? change;
            var roll = random.Next(3);
            if (roll == 0 || live < 2)
            {
                var item = Guid.NewGuid();
                change = ListAuthoring.Insert(local, Field, client, item, random.Next(live + 1));
                inserted.Add(item);
            }
            else if (roll == 1) change = ListAuthoring.Move(local, Field, client, random.Next(live), random.Next(live));
            else
            {
                var index = random.Next(live);
                deleted.Add(Visible(local)[index]);
                change = ListAuthoring.Delete(local, Field, client, index);
            }
            if (change is null) continue;
            change.ApplyChange(local, null!).AsTask().Wait();
            authored.Add(change);
        }
        return authored;
    }

    private static string[] ConflictKeys(GenericRecord record) =>
        new CrdtRecordList().Conflicts(record, Field).Select(c => c.ToString()).Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void ConcurrentReorderInsertAndDeleteConvergeWithoutLossOrDuplicates()
    {
        for (var seed = 0; seed < 60; seed++)
        {
            var random = new Random(seed);
            var items = Enumerable.Range(0, 6 + seed % 5).Select(_ => Guid.NewGuid()).ToArray();
            var start = Base(items);
            var inserted = new List<Guid>();
            var deleted = new HashSet<Guid>();
            var a = Author(start, Guid.NewGuid(), 6, random, inserted, deleted);
            var b = Author(start, Guid.NewGuid(), 6, random, inserted, deleted);
            var all = a.Concat(b).ToList();
            var first = Fold(start, a.Concat(b));
            var second = Fold(start, b.Concat(a));
            var shuffled = Fold(start, all.OrderBy(_ => random.Next()));
            var expected = items.Concat(inserted).Where(x => !deleted.Contains(x)).Order().ToArray();
            foreach (var replica in new[] { first, second, shuffled })
            {
                Assert.Equal(Visible(first), Visible(replica));
                Assert.Equal(expected, Visible(replica).Order().ToArray());
                Assert.Equal(ConflictKeys(first), ConflictKeys(replica));
            }
        }
    }

    [Fact]
    public void ConcurrentMovesOfOneItemKeepOnePlaceAndReportTheLosingCopy()
    {
        var items = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        var start = Base(items);
        var moveToFront = ListAuthoring.Move(start, Field, Guid.NewGuid(), 2, 0)!;
        var moveToEnd = ListAuthoring.Move(start, Field, Guid.NewGuid(), 2, 4)!;
        var one = Fold(start, [moveToFront, moveToEnd]);
        var two = Fold(start, [moveToEnd, moveToFront]);
        Assert.Equal(Visible(one), Visible(two));
        Assert.Equal(items.Order(), Visible(one).Order());
        Assert.Equal(5, Visible(one).Count);
        var conflict = Assert.Single(new CrdtRecordList().Conflicts(one, Field));
        Assert.Equal(items[2], conflict.Item);
        Assert.Equal(ConflictKeys(one), ConflictKeys(two));
    }

    [Fact]
    public void DeleteWinsOverConcurrentMoveAndInsertAfterTheDeletedItem()
    {
        var items = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var start = Base(items);
        var delete = ListAuthoring.Delete(start, Field, Guid.NewGuid(), 1);
        var move = ListAuthoring.Move(start, Field, Guid.NewGuid(), 1, 3)!;
        var insert = ListAuthoring.Insert(start, Field, Guid.NewGuid(), Guid.NewGuid(), 2);
        var one = Fold(start, [delete, move, insert]);
        var two = Fold(start, [insert, move, delete]);
        Assert.Equal(Visible(one), Visible(two));
        Assert.DoesNotContain(items[1], Visible(one));
        Assert.Equal(4, Visible(one).Count);
    }

    [Fact]
    public void ConcurrentAddsOfOneTargetToACollectionShowItOnce()
    {
        var target = Guid.NewGuid();
        var start = Base([Guid.NewGuid()], 26);
        var a = ListAuthoring.Insert(start, Field, Guid.NewGuid(), target, 1);
        var b = ListAuthoring.Insert(start, Field, Guid.NewGuid(), target, 1);
        var folded = Fold(start, [a, b]);
        Assert.Single(Visible(folded), target);
        var sequenceStart = Base([Guid.NewGuid()], 28);
        var twice = Fold(sequenceStart, [ListAuthoring.Insert(sequenceStart, Field, Guid.NewGuid(), target, 0),
            ListAuthoring.Insert(sequenceStart, Field, Guid.NewGuid(), target, 0)]);
        Assert.Equal(2, Visible(twice).Count(x => x == target));
    }

    [Fact]
    public async Task StoreRoundTripKeepsEditedListsThroughReopenAndReplay()
    {
        var items = Enumerable.Range(0, 12).Select(_ => Guid.NewGuid()).ToArray();
        var record = new GenericRecord { Id = Guid.NewGuid() };
        record.Values[Field.ToString()] = JsonSerializer.SerializeToElement(items);
        var types = new Dictionary<int, int> { [Field] = 27 };
        var clientA = Guid.NewGuid();
        var clientB = Guid.NewGuid();
        IReadOnlyList<Guid> expected;
        string[] conflicts;
        await using (var store = new Store(Database))
        {
            await store.ImportAsync([new(record, types)], Guid.NewGuid());
            var start = Assert.Single(await store.ReadAsync());
            var moveToFront = ListAuthoring.Move(start, Field, Guid.NewGuid(), 5, 0)!;
            var moveToEnd = ListAuthoring.Move(start, Field, Guid.NewGuid(), 5, 11)!;
            var insert = ListAuthoring.Insert(start, Field, Guid.NewGuid(), Guid.NewGuid(), 3);
            var delete = ListAuthoring.Delete(start, Field, Guid.NewGuid(), 8);
            var oracle = Fold(start, [moveToFront, delete, moveToEnd, insert]);
            await store.CommitListOperationsAsync([moveToFront, delete], clientA);
            await store.CommitListOperationsAsync([moveToEnd, insert], clientB);
            var stored = Assert.Single(await store.ReadAsync());
            expected = Visible(oracle);
            conflicts = ConflictKeys(oracle);
            Assert.Equal(expected, store.Lists.Read(stored, Field));
            Assert.Equal(conflicts, ConflictKeys(stored));
            Assert.Equal(Store.SnapshotItem(oracle), Store.SnapshotItem(stored));
            Assert.NotEmpty(conflicts);
            await store.RegenerateAsync();
            Assert.Equal(expected, store.Lists.Read(Assert.Single(await store.ReadAsync()), Field));
            AssertRowsRestoreTheList(store, Assert.Single(await store.ReadAsync()), expected);
        }
        await using var reopened = new Store(Database);
        var after = Assert.Single(await reopened.ReadAsync());
        Assert.Equal(expected, reopened.Lists.Read(after, Field));
        Assert.Equal(conflicts, ConflictKeys(after));
        var rows = reopened.Lists.CheckpointRows(after, Field);
        Assert.Equal(12 + 4, rows.Count(r => r.Kind == "node"));
        Assert.Equal(conflicts.Length, rows.Count(r => r.Kind == "conflict"));
    }

    private static void AssertRowsRestoreTheList(Store store, GenericRecord record, IReadOnlyList<Guid> expected)
    {
        var rows = store.Lists.CheckpointRows(record, Field);
        Assert.Equal(rows.Count, rows.Select(r => (r.Kind, r.Key)).Distinct().Count());
        Assert.All(rows, r => Assert.Matches("^[0-9a-f]+$", r.Key));
        var restored = NodeRowEncoding.Restore(rows.Where(r => r.Kind != "conflict")
            .Select(r => new ListNodeRow(NodeRowEncoding.Version, KeyOf(r.Json), r.Json)));
        Assert.Equal(expected, restored.Elements().Select(e => Guid.ParseExact(e[..36], "D")).ToArray());
        Assert.Equal(0, restored.PendingCount);
    }

    private static string KeyOf(string json) => NodeRowEncoding.Encode(OpJson.FromJson(json)).Key;

    [Fact]
    public void StoreLimitsCoverTheLargestM1ListAndTheIdentitiesTheStoreAuthors()
    {
        var limits = Store.ListLimits;
        Assert.True(limits.MaxPendingOperations >= 7 * 34_383);
        Assert.True(limits.MaxOperationJsonLength >= 4 * 512);
        var start = Base(Enumerable.Range(0, 34_383).Select(_ => Guid.NewGuid()).ToArray());
        var change = ListAuthoring.Insert(start, Field, Guid.NewGuid(), Guid.NewGuid(), 34_383);
        var json = OpJson.ToJson(change.Operation);
        Assert.InRange(change.Operation.Element.Length, 1, limits.MaxIdentifierLength);
        Assert.InRange(change.Operation.Id.Replica.Length, 1, limits.MaxIdentifierLength);
        Assert.InRange(json.Length, 1, limits.MaxOperationJsonLength / 4);
        Assert.Equal(34_384, Visible(Fold(start, [change])).Count);
        var tooLong = ListOp.Insert(new NodeId(Guid.NewGuid().ToString("D"), 1), 1, new string('a', limits.MaxIdentifierLength + 1), null, null);
        Assert.Throws<ListLimitException>(() => new ListState(limits).Apply(tooLong));
    }

    [Fact]
    public void SnapshotCopiesCarryTheFoldedListInsteadOfRefoldingIt()
    {
        var items = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToArray();
        var record = Fold(Base(items), [ListAuthoring.Delete(Base(items), Field, Guid.NewGuid(), 0)]);
        Assert.Equal(49, Visible(record).Count);
        ListHistory.StateBuilds = 0;
        for (var i = 0; i < 20; i++)
        {
            var copy = (GenericRecord)record.Copy();
            Apply(ListAuthoring.Insert(copy, Field, Guid.NewGuid(), Guid.NewGuid(), 0), copy);
            record = copy;
        }
        Assert.Equal(69, Visible(record).Count);
        Assert.Equal(0, ListHistory.StateBuilds);
    }

    [Fact]
    public void ASequentialSupersedingMoveIsNotAConflictButAConcurrentOneIs()
    {
        var items = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        var start = Base(items);
        var first = ListAuthoring.Move(start, Field, Guid.NewGuid(), 2, 0)!;
        var afterFirst = Fold(start, [first]);
        var second = ListAuthoring.Move(afterFirst, Field, Guid.NewGuid(), 0, 4)!;
        var sequential = Fold(start, [first, second]);
        Assert.Empty(new CrdtRecordList().Conflicts(sequential, Field));
        Assert.DoesNotContain(new CrdtRecordList().CheckpointRows(sequential, Field), r => r.Kind == "conflict");
        var concurrent = ListAuthoring.Move(start, Field, Guid.NewGuid(), 2, 3)!;
        Assert.Single(new CrdtRecordList().Conflicts(Fold(start, [first, concurrent]), Field));
    }

    [Fact]
    public async Task AWholeValueWriteToAListFieldIsRefusedAndNeverDiscardsHistory()
    {
        var start = Base(Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray());
        var edited = Fold(start, [ListAuthoring.Delete(start, Field, Guid.NewGuid(), 1)]);
        var before = Visible(edited);
        await new SetRecordValue(edited.Id, Field.ToString(), JsonSerializer.SerializeToElement(new[] { Guid.NewGuid() }))
            .ApplyChange(edited, null!);
        Assert.Equal(before, Visible(edited));
        Assert.Single(edited.Lists, p => p.Value.Ops.Count == 1);

        var record = new GenericRecord { Id = Guid.NewGuid() };
        record.Values[Field.ToString()] = JsonSerializer.SerializeToElement(new[] { Guid.NewGuid() });
        await using var store = new Store(Database);
        await store.ImportAsync([new(record, new Dictionary<int, int> { [Field] = 27 })], Guid.NewGuid());
        var stored = Assert.Single(await store.ReadAsync());
        var revision = (await store.ReferencesAsync()).Revision;
        await Assert.ThrowsAsync<ListFieldWriteException>(() => store.SetFieldAsync(stored.Id, Field,
            stored.Values[Field.ToString()], JsonSerializer.SerializeToElement(new[] { Guid.NewGuid() }), revision, Guid.NewGuid()));
    }

    [Fact]
    public async Task AMalformedListOperationIsRefusedBeforeStorageAndReplayNeverThrows()
    {
        var items = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        var record = new GenericRecord { Id = Guid.NewGuid() };
        record.Values[Field.ToString()] = JsonSerializer.SerializeToElement(items);
        await using var store = new Store(Database);
        await store.ImportAsync([new(record, new Dictionary<int, int> { [Field] = 27 })], Guid.NewGuid());
        var start = Assert.Single(await store.ReadAsync());
        var commits = (await store.CommitsAsync()).Count;
        var replica = new NodeId(Guid.NewGuid().ToString("D"), 1);
        var bad = new[]
        {
            new ListRecordOp(start.Id, Field, ListOp.Insert(replica, 1, "not-a-guid", null, null)),
            new ListRecordOp(start.Id, Field, ListOp.Insert(replica, 1, Guid.NewGuid().ToString("D") + "~" + new string('x', 200), null, null)),
        };
        foreach (var operation in bad.Take(2))
        {
            await Assert.ThrowsAsync<ListOperationRefusedException>(() => store.CommitListOperationsAsync([operation], Guid.NewGuid()));
            Assert.Equal(commits, (await store.CommitsAsync()).Count);
            Assert.Equal(items, store.Lists.Read(Assert.Single(await store.ReadAsync()), Field));
            var copy = (GenericRecord)start.Copy();
            Apply(operation, copy);
            Assert.Equal(items, Visible(copy));
        }
        await store.RegenerateAsync();
        Assert.Equal(items, store.Lists.Read(Assert.Single(await store.ReadAsync()), Field));
    }

    [Fact]
    public async Task ALargeListImportedThroughTheStoreIsEditedAndSurvivesReopen()
    {
        var items = Enumerable.Range(0, 1500).Select(_ => Guid.NewGuid()).ToArray();
        var record = new GenericRecord { Id = Guid.NewGuid() };
        record.Values[Field.ToString()] = JsonSerializer.SerializeToElement(items);
        IReadOnlyList<Guid> expected;
        await using (var store = new Store(Database))
        {
            await store.ImportAsync([new(record, new Dictionary<int, int> { [Field] = 27 })], Guid.NewGuid());
            var start = Assert.Single(await store.ReadAsync());
            var move = ListAuthoring.Move(start, Field, Guid.NewGuid(), 1200, 3)!;
            var delete = ListAuthoring.Delete(start, Field, Guid.NewGuid(), 10);
            var insert = ListAuthoring.Insert(start, Field, Guid.NewGuid(), Guid.NewGuid(), 1500);
            expected = Visible(Fold(start, [move, delete, insert]));
            await store.CommitListOperationsAsync([move, delete, insert], Guid.NewGuid());
            Assert.Equal(expected, store.Lists.Read(Assert.Single(await store.ReadAsync()), Field));
        }
        await using var reopened = new Store(Database);
        Assert.Equal(expected, reopened.Lists.Read(Assert.Single(await reopened.ReadAsync()), Field));
        Assert.Equal(1500, expected.Count);
    }

    private sealed class FixedClock(DateTimeOffset time) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => time;
    }

    [Fact]
    public void AMoveViewIsACausalPrefixSoAGappedDeliveryStillReportsTheConflict()
    {
        var items = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        var start = Base(items);
        var b = Guid.NewGuid();
        var localB = (GenericRecord)start.Copy();
        var lost = ListAuthoring.Move(localB, Field, b, 2, 0)!;
        Apply(lost, localB);
        var later = ListAuthoring.Insert(localB, Field, b, Guid.NewGuid(), 6);
        Apply(later, localB);
        var localA = (GenericRecord)start.Copy();
        Apply(later, localA);
        var winner = ListAuthoring.Move(localA, Field, Guid.NewGuid(), 2, 5)!;
        Apply(winner, localA);
        foreach (var order in new[] { new[] { later, winner, lost }, new[] { lost, later, winner }, new[] { winner, lost, later } })
        {
            var folded = Fold(start, order);
            var conflict = Assert.Single(new CrdtRecordList().Conflicts(folded, Field));
            Assert.Equal(b.ToString("D"), conflict.Author);
            Assert.Equal(1, conflict.Counter);
        }
    }

    private static List<ListRecordOp> AuthorOn(GenericRecord local, Guid client, int count, Random random,
        Dictionary<NodeId, HashSet<NodeId>> known)
    {
        var authored = new List<ListRecordOp>();
        for (var i = 0; i < count; i++)
        {
            var live = Visible(local).Count;
            var before = local.HistoryFor(Field).State().AppliedOperations.Select(o => o.Id).ToHashSet();
            ListRecordOp? change;
            var roll = random.Next(3);
            if (roll == 0 || live < 2) change = ListAuthoring.Insert(local, Field, client, Guid.NewGuid(), random.Next(live + 1));
            else if (roll == 1) change = ListAuthoring.Move(local, Field, client, random.Next(live), random.Next(live));
            else change = ListAuthoring.Delete(local, Field, client, random.Next(live));
            if (change is null) continue;
            known[change.Operation.Id] = before;
            Apply(change, local);
            authored.Add(change);
        }
        return authored;
    }

    [Fact]
    public void ConflictsNeverMissAnUnseenMoveUnderGappedAndShuffledDelivery()
    {
        for (var seed = 0; seed < 60; seed++)
        {
            var random = new Random(1000 + seed);
            var start = Base(Enumerable.Range(0, 7).Select(_ => Guid.NewGuid()).ToArray());
            var known = new Dictionary<NodeId, HashSet<NodeId>>();
            var clientA = Guid.NewGuid();
            var clientB = Guid.NewGuid();
            var localA = (GenericRecord)start.Copy();
            var localB = (GenericRecord)start.Copy();
            var firstA = AuthorOn(localA, clientA, 4, random, known);
            var firstB = AuthorOn(localB, clientB, 4, random, known);
            foreach (var change in firstB.Where(_ => random.Next(2) == 0).OrderBy(_ => random.Next())) Apply(change, localA);
            foreach (var change in firstA.Where(_ => random.Next(2) == 0).OrderBy(_ => random.Next())) Apply(change, localB);
            var secondA = AuthorOn(localA, clientA, 4, random, known);
            var secondB = AuthorOn(localB, clientB, 4, random, known);
            var all = firstA.Concat(firstB).Concat(secondA).Concat(secondB).ToList();
            var one = Fold(start, all);
            var two = Fold(start, all.AsEnumerable().Reverse());
            var three = Fold(start, all.OrderBy(_ => random.Next()));
            Assert.Equal(Visible(one), Visible(two));
            Assert.Equal(Visible(one), Visible(three));
            Assert.Equal(ConflictKeys(one), ConflictKeys(two));
            Assert.Equal(ConflictKeys(one), ConflictKeys(three));
            var state = one.Lists[Field].State();
            var reported = new CrdtRecordList().Conflicts(one, Field).Select(c => (c.Author, c.Counter)).ToHashSet();
            foreach (var loser in state.LosingMoves)
            {
                var winner = all.Single(c => c.Operation.Id == state.PositionOf(loser.Element)).Operation;
                if (winner.Kind == ListOpKind.Move && !known[winner.Id].Contains(loser.Id))
                    Assert.Contains((loser.Id.Replica, loser.Id.Counter), reported);
            }
        }
    }

    [Fact]
    public async Task ANonEmptyViewSurvivesCommitRegenerateAndReopenWithEqualDigests()
    {
        var items = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        var record = new GenericRecord { Id = Guid.NewGuid() };
        record.Values[Field.ToString()] = JsonSerializer.SerializeToElement(items);
        GenericRecord oracle;
        await using (var store = new Store(Database))
        {
            await store.ImportAsync([new(record, new Dictionary<int, int> { [Field] = 27 })], Guid.NewGuid());
            var start = Assert.Single(await store.ReadAsync());
            var first = ListAuthoring.Move(start, Field, Guid.NewGuid(), 2, 0)!;
            await store.CommitListOperationsAsync([first], Guid.NewGuid());
            var afterFirst = Assert.Single(await store.ReadAsync());
            var second = ListAuthoring.Move(afterFirst, Field, Guid.NewGuid(), 0, 5)!;
            Assert.NotEmpty(second.Seen);
            await store.CommitListOperationsAsync([second], Guid.NewGuid());
            oracle = Fold(start, [first, second]);
            await store.RegenerateAsync();
        }
        await using var reopened = new Store(Database);
        var after = Assert.Single(await reopened.ReadAsync());
        Assert.Empty(reopened.Lists.Conflicts(after, Field));
        Assert.Single(after.Lists[Field].Seen);
        Assert.Equal(Store.SnapshotItem(oracle), Store.SnapshotItem(after));
        Assert.Equal(Visible(oracle), Visible(after));
    }

    [Fact]
    public void TheViewEncodingHasItsOwnChangeKindAndSnapshotVersionAndOthersAreRefused()
    {
        var codec = new RecordCodec(new RecordContent());
        var start = Base(Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray());
        var move = ListAuthoring.Move(Fold(start, [ListAuthoring.Move(start, Field, Guid.NewGuid(), 1, 3)!]), Field, Guid.NewGuid(), 0, 2)!;
        var bytes = codec.Change(move);
        Assert.Equal(6, bytes[1]);
        var decoded = Assert.IsType<ListRecordOp>(codec.Change(bytes));
        Assert.Equal(move.Seen, decoded.Seen);
        var unknown = (byte[])bytes.Clone();
        unknown[1] = byte.MaxValue;
        Assert.Contains("Unknown record change kind", Assert.Throws<InvalidDataException>(() => codec.Change(unknown)).Message);
        var edited = Fold(start, [move]);
        var snapshot = codec.Snapshot(edited);
        Assert.Equal(3, snapshot[0]);
        var round = Assert.IsType<GenericRecord>(codec.Snapshot(snapshot));
        Assert.Equal(Store.SnapshotItem(edited), Store.SnapshotItem(round));
        snapshot[0] = 4;
        Assert.Contains("Unknown record codec version", Assert.Throws<InvalidDataException>(() => codec.Snapshot(snapshot)).Message);
    }

    [Fact]
    public async Task ALateCommitThatSortsEarlierIsReplayedFromItsPlaceSoARefusedOperationIsRefusedOnEveryReplica()
    {
        var items = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
        var record = new GenericRecord { Id = Guid.NewGuid() };
        record.Values[Field.ToString()] = JsonSerializer.SerializeToElement(items);
        var pathA = Path.Combine(root, "a", "records.sqlite");
        var pathB = Path.Combine(root, "b", "records.sqlite");
        Directory.CreateDirectory(Path.GetDirectoryName(pathA)!);
        Directory.CreateDirectory(Path.GetDirectoryName(pathB)!);
        await using (var seed = new Store(pathA, new FixedClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))))
            await seed.ImportAsync([new(record, new Dictionary<int, int> { [Field] = 27 })], Guid.NewGuid());
        File.Copy(pathA, pathB);
        var sameId = new NodeId(Guid.NewGuid().ToString("D"), 1);
        var early = Guid.NewGuid();
        var late = Guid.NewGuid();
        await using var a = new Store(pathA, new FixedClock(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)));
        await using var b = new Store(pathB, new FixedClock(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)));
        await a.CommitListOperationsAsync([new(record.Id, Field, ListOp.Insert(sameId, 1, late.ToString("D"), null, null))], Guid.NewGuid());
        await b.CommitListOperationsAsync([new(record.Id, Field, ListOp.Insert(sameId, 1, early.ToString("D"), null, null))], Guid.NewGuid());
        await a.Model.SyncWith(b.Model);
        var onA = await a.Model.GetLatest<GenericRecord>(record.Id);
        var onB = await b.Model.GetLatest<GenericRecord>(record.Id);
        Assert.NotNull(onA);
        Assert.NotNull(onB);
        Assert.Equal(Visible(onA), Visible(onB));
        Assert.Contains(early, Visible(onA));
        Assert.DoesNotContain(late, Visible(onA));
    }
}
