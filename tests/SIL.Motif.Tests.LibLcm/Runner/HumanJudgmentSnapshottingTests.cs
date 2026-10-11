using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Runner.Snapshotting;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Runner;

[Collection(LcmCacheTestCollection.Name)]
public sealed class HumanJudgmentSnapshottingTests : IDisposable
{
    private static readonly Guid RecordId = Guid.Parse("00000000-0000-0000-0000-000000004201");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-judgment-snapshotting-" + Guid.NewGuid().ToString("N"));

    public HumanJudgmentSnapshottingTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void ARecordInAProjectWithoutTheFieldHasNoAlternatives()
    {
        using var cache = NewLangProjFixture.CreateCache(_root);
        var record = AddEmptyRecord(cache);

        Assert.Empty(HumanJudgmentSnapshotting.ReadRecord(cache, record));
    }

    [Fact]
    public void ARecordWithAJudgmentReadsItsWritingSystemAndNormalizedText()
    {
        using var cache = NewLangProjFixture.CreateCache(_root);
        var fieldId = NotebookJudgmentFixture.InitializeReservedField(cache);
        var record = NotebookJudgmentFixture.AddRecord(cache, RecordId, fieldId,
            TsStringUtils.MakeString("Keep é", cache.DefaultAnalWs));

        var alternatives = HumanJudgmentSnapshotting.ReadRecord(cache, record);

        Assert.Equal(cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs), alternatives["ws"]);
        Assert.Equal("Keep é", alternatives["text"].Normalize(System.Text.NormalizationForm.FormC));
    }

    [Fact]
    public void ARecordWhoseFieldIsEmptyHasNoAlternatives()
    {
        using var cache = NewLangProjFixture.CreateCache(_root);
        var fieldId = NotebookJudgmentFixture.InitializeReservedField(cache);
        var record = NotebookJudgmentFixture.AddRecord(cache, RecordId, fieldId,
            TsStringUtils.MakeString("", cache.DefaultAnalWs));

        Assert.Empty(HumanJudgmentSnapshotting.ReadRecord(cache, record));
    }

    [Fact]
    public void APassResolvesTheFieldOnceSoASchemaChangeIsSeenOnlyByTheNextSnapshot()
    {
        using var cache = NewLangProjFixture.CreateCache(_root);
        var record = AddEmptyRecord(cache);

        using (SnapshotPass.Begin(cache))
        {
            Assert.Empty(HumanJudgmentSnapshotting.ReadRecord(cache, record));
            var fieldId = NotebookJudgmentFixture.InitializeReservedField(cache);
            NotebookJudgmentFixture.SetValue(cache, record, TsStringUtils.MakeString("Keep", cache.DefaultAnalWs));
            Assert.NotEqual(0, fieldId);

            Assert.Empty(HumanJudgmentSnapshotting.ReadRecord(cache, record));
        }

        Assert.Equal("Keep", HumanJudgmentSnapshotting.ReadRecord(cache, record)["text"]);
    }

    [Fact]
    public void ARefusalInsideAPassIsNotRememberedSoEveryRecordStillThrows()
    {
        using var cache = NewLangProjFixture.CreateCache(_root);
        NotebookJudgmentFixture.InitializeReservedField(cache);
        AddField(cache, "LexEntry", "MotifHumanJudgment");
        var record = AddEmptyRecord(cache);

        using var pass = SnapshotPass.Begin(cache);

        Assert.Throws<InvalidOperationException>(() => HumanJudgmentSnapshotting.ReadRecord(cache, record));
        Assert.Throws<InvalidOperationException>(() => HumanJudgmentSnapshotting.ReadRecord(cache, record));
    }

    [Fact]
    public void APassOverOneCacheDoesNotAnswerForAnother()
    {
        using var passCache = NewLangProjFixture.CreateCache(Path.Combine(_root, "pass"));
        using var otherCache = NewLangProjFixture.CreateCache(Path.Combine(_root, "other"));
        var fieldId = NotebookJudgmentFixture.InitializeReservedField(otherCache);
        var record = NotebookJudgmentFixture.AddRecord(otherCache, RecordId, fieldId,
            TsStringUtils.MakeString("Keep", otherCache.DefaultAnalWs));

        using var pass = SnapshotPass.Begin(passCache);

        Assert.Equal("Keep", HumanJudgmentSnapshotting.ReadRecord(otherCache, record)["text"]);
    }

    [Fact]
    public void AnInnerPassEndsWithoutEndingTheOuterOne()
    {
        using var cache = NewLangProjFixture.CreateCache(_root);
        var record = AddEmptyRecord(cache);

        using (SnapshotPass.Begin(cache))
        {
            Assert.Empty(HumanJudgmentSnapshotting.ReadRecord(cache, record));
            using (SnapshotPass.Begin(cache))
            {
            }

            var fieldId = NotebookJudgmentFixture.InitializeReservedField(cache);
            NotebookJudgmentFixture.SetValue(cache, record, TsStringUtils.MakeString("Keep", cache.DefaultAnalWs));
            Assert.NotEqual(0, fieldId);
            // The outer pass resolved before the field existed and still holds that answer.
            Assert.Empty(HumanJudgmentSnapshotting.ReadRecord(cache, record));
        }

        Assert.Equal("Keep", HumanJudgmentSnapshotting.ReadRecord(cache, record)["text"]);
    }

    [Fact]
    public void BeginningAPassWithoutACacheIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => SnapshotPass.Begin(null!));
    }

    private static IRnGenericRec AddEmptyRecord(LcmCache cache)
    {
        IRnGenericRec record = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            record = cache.ServiceLocator.GetInstance<IRnGenericRecFactory>().Create(RecordId);
            cache.LangProject.ResearchNotebookOA.RecordsOC.Add(record);
        });
        return record;
    }

    private static void AddField(LcmCache cache, string ownerClass, string name)
    {
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            metadata.AddCustomField(ownerClass, name, CellarPropertyType.String, 0, "Second judgment",
                WritingSystemServices.kwsAnal, Guid.Empty));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
