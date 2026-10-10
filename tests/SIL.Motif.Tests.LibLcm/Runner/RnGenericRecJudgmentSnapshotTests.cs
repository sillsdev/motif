using System;
using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Core.WritingSystems;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.LiveHost.Baselines;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Snapshotting;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Runner;

[Collection(LcmCacheTestCollection.Name)]
public sealed class RnGenericRecJudgmentSnapshotTests : IDisposable
{
    private static readonly Guid RecordId = Guid.Parse("00000000-0000-0000-0000-000000004101");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-rn-judgment-snapshot-" + Guid.NewGuid().ToString("N"));

    public RnGenericRecJudgmentSnapshotTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void ChangingOnlyAJudgmentReasonChangesTheSemanticDigestAndTheRecordSnapshot()
    {
        using var cache = NewLangProjFixture.CreateCache(_root);
        var fieldId = NotebookJudgmentFixture.InitializeReservedField(cache);
        var projectId = CanonicalId.FromGuid(cache.LangProject.Guid).Value;
        var first = HumanJudgmentCodec.Format(Disposition(projectId, "Alternation family"));
        var second = HumanJudgmentCodec.Format(Disposition(projectId, "Allomorph family"));
        var record = NotebookJudgmentFixture.AddRecord(cache, RecordId, fieldId,
            TsStringUtils.MakeString(first, cache.DefaultAnalWs));
        var digestBefore = BaselineSemanticDigest.Compute(cache);

        NotebookJudgmentFixture.SetValue(cache, record, TsStringUtils.MakeString(second, cache.DefaultAnalWs));

        Assert.NotEqual(digestBefore, BaselineSemanticDigest.Compute(cache));
        var snapshot = RnGenericRecSnapshotter.Snapshot(cache, record);
        var value = snapshot.AlternativesFields[SnapshotFields.RnGenericRecMotifHumanJudgment];
        Assert.Equal(
            second.Normalize(System.Text.NormalizationForm.FormD),
            value["text"].Normalize(System.Text.NormalizationForm.FormD));
    }

    [Fact]
    public void ProjectWithoutTheJudgmentFieldEmitsNoJudgmentAlternatives()
    {
        using var cache = NewLangProjFixture.CreateCache(_root);
        IRnGenericRec record = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            record = cache.ServiceLocator.GetInstance<IRnGenericRecFactory>().Create(RecordId);
            cache.LangProject.ResearchNotebookOA.RecordsOC.Add(record);
        });

        var snapshot = RnGenericRecSnapshotter.Snapshot(cache, record);

        Assert.DoesNotContain(SnapshotFields.RnGenericRecMotifHumanJudgment, snapshot.AlternativesFields.Keys);
    }

    [Fact]
    public void AnAmbiguousJudgmentFieldRefusesTheSnapshot()
    {
        using var cache = NewLangProjFixture.CreateCache(_root);
        NotebookJudgmentFixture.InitializeReservedField(cache);
        AddField(cache, "LexEntry", "MotifHumanJudgment", CellarPropertyType.String,
            WritingSystemServices.kwsAnal, "Second judgment");
        var record = AddEmptyRecord(cache);

        Assert.Throws<InvalidOperationException>(() => RnGenericRecSnapshotter.Snapshot(cache, record));
    }

    [Fact]
    public void AnIncompatibleJudgmentFieldRefusesTheSnapshot()
    {
        using var cache = NewLangProjFixture.CreateCache(_root);
        AddField(cache, "RnGenericRec", "MotifHumanJudgment", CellarPropertyType.Integer,
            WritingSystemServices.kwsAnal, "Wrong type");
        var record = AddEmptyRecord(cache);

        Assert.Throws<InvalidOperationException>(() => RnGenericRecSnapshotter.Snapshot(cache, record));
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

    private static void AddField(
        LcmCache cache, string ownerClass, string name, CellarPropertyType type, int selector, string label)
    {
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            metadata.AddCustomField(ownerClass, name, type, 0, label, selector, Guid.Empty));
    }

    private static HumanJudgment Disposition(string projectId, string reason)
    {
        var subject = new ObjectJudgmentSubject(new("LexEntry", CanonicalId.FromGuid(Guid.Parse("00000000-0000-0000-0000-000000008001")).Value, null));
        return new HumanJudgment(projectId, CanonicalId.FromGuid(Guid.Parse("00000000-0000-0000-0000-000000009100")).Value,
            CanonicalId.FromGuid(RecordId).Value, [],
            new DispositionJudgment(subject, "M-family", ParsimonyDispositionKind.Keep,
                "sha256:" + new string('a', 64), "test-evidence-v1", "LexEntry", reason));
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
