using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;
using SIL.Motif.LiveHost.HumanJudgments;

namespace SIL.Motif.Tests.TestFixtures;

public static class NotebookJudgmentFixture
{
    public static int InitializeReservedField(LcmCache cache)
    {
        HumanJudgmentFieldDefinition.CreateDefinition(cache);
        return FindReservedField(cache);
    }

    public static int FindReservedField(LcmCache cache)
    {
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        return metadata.GetFieldIds()
            .Single(flid => metadata.GetFieldName(flid) == HumanJudgmentFieldDefinition.InternalName);
    }

    public static IRnGenericRec AddRecord(LcmCache cache, Guid recordId, int fieldId, ITsString value)
    {
        IRnGenericRec record = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            record = cache.ServiceLocator.GetInstance<IRnGenericRecFactory>().Create(recordId);
            cache.LangProject.ResearchNotebookOA.RecordsOC.Add(record);
            cache.DomainDataByFlid.SetString(record.Hvo, fieldId, value);
        });
        return record;
    }

    public static void SetValue(LcmCache cache, IRnGenericRec record, ITsString value)
    {
        var fieldId = FindReservedField(cache);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            cache.DomainDataByFlid.SetString(record.Hvo, fieldId, value));
    }
}
