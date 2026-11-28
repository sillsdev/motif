using System.Collections.Immutable;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;
using SIL.Motif.RecordStore;
using SIL.Motif.RecordStore.HandAnalysis;
using SIL.Motif.RecordStore.Lcm;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.RecordStore;

[Collection(LcmCacheTestCollection.Name)]
public sealed class ImportVectorStalenessTests : IDisposable
{
    private const int Field = 1000;
    private readonly string root = Path.Combine(Path.GetTempPath(), "motif-record-vector-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Fact]
    public async Task WfiMembersFollowACommittedListEditInsteadOfTheImportVector()
    {
        Guid[] items = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        var shape = new GenericRecord { Id = Guid.NewGuid(), Values = { [Field.ToString()] = JsonSerializer.SerializeToElement(items) } };
        var seed = new GenericRecord
        {
            Id = Guid.NewGuid(),
            Provenance = new NativeProvenance(0, "test", ImmutableDictionary<int, ImmutableArray<Guid>>.Empty.Add(Field, [.. items]))
        };
        var layout = RecordInitialization.Layout([new(shape, new Dictionary<int, int> { [Field] = 27 })]);
        var record = RecordInitialization.Prepare(seed, layout);
        for (var i = 0; i < items.Length; i++)
            await new InsertRecordItem(record.Id, Field, items[i], i).ApplyChange(record, null!);
        Assert.Equal(items, WfiProjection.Members(record, Field));

        var move = ListAuthoring.Move(record, Field, Guid.NewGuid(), 0, 2)!;
        await move.ApplyChange(record, null!);

        Assert.Equal([items[1], items[2], items[0]], WfiProjection.Members(record, Field));
    }

    [Fact]
    public void TheLcmGraphReaderReadsTheCurrentListNotTheVectorCapturedAtConstruction()
    {
        using var cache = NewLangProjFixture.CreateCache(root);
        SeededProject.Seed(cache);
        var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances().First();
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            while (entry.SensesOS.Count < 2) entry.SensesOS.Add(cache.ServiceLocator.GetInstance<ILexSenseFactory>().Create());
        });
        var reader = new LcmGraphReader(cache);
        var first = entry.SensesOS[0].Guid;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => entry.SensesOS.MoveTo(0, 0, entry.SensesOS, entry.SensesOS.Count));
        Assert.NotEqual(first, entry.SensesOS[0].Guid);

        var sensesField = LexEntryTags.kflidSenses.ToString();
        var record = reader.Read().Single(r => r.Id == entry.Guid);
        Assert.Equal(entry.SensesOS.Select(s => s.Guid), record.Values[sensesField].EnumerateArray().Select(v => v.GetGuid()));
    }
}
