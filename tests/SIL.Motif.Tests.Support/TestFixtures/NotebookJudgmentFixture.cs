using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.Motif.Contract.HumanJudgments;
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

    public static int FindIdField(LcmCache cache)
    {
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        return metadata.GetFieldIds()
            .Single(flid => metadata.GetFieldName(flid) == HumanJudgmentFieldDefinition.IdInternalName);
    }

    /// <summary>Adds a record shaped as the composers shape it: title, sentence and reason paragraphs, data, ID.</summary>
    public static IRnGenericRec AddRecord(LcmCache cache, Guid recordId, int fieldId, FormattedJudgment judgment)
    {
        var record = AddRecord(cache, recordId, fieldId, TsStringUtils.MakeString(judgment.Data, cache.DefaultAnalWs));
        var idField = FindIdField(cache);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            cache.DomainDataByFlid.SetString(record.Hvo, idField,
                TsStringUtils.MakeString(judgment.VisibleId, cache.DefaultAnalWs));
            record.Title = TsStringUtils.MakeString(
                judgment.Statement + " (" + judgment.VisibleId + ")", cache.DefaultAnalWs);
            record.DescriptionOA = cache.ServiceLocator.GetInstance<IStTextFactory>().Create();
            foreach (var text in new[] { judgment.Statement }.Concat(HumanJudgmentCodec.ReasonParagraphs(judgment.Reason)))
            {
                var paragraph = cache.ServiceLocator.GetInstance<IStTxtParaFactory>().Create();
                record.DescriptionOA.ParagraphsOS.Add(paragraph);
                paragraph.Contents = TsStringUtils.MakeString(text, cache.DefaultAnalWs);
            }
        });
        return record;
    }

    /// <summary>Replaces every Description paragraph, as a person editing the record in FieldWorks would.</summary>
    public static void SetParagraphs(LcmCache cache, IRnGenericRec record, params string[] texts)
    {
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            record.DescriptionOA ??= cache.ServiceLocator.GetInstance<IStTextFactory>().Create();
            record.DescriptionOA.ParagraphsOS.Clear();
            foreach (var text in texts)
            {
                var paragraph = cache.ServiceLocator.GetInstance<IStTxtParaFactory>().Create();
                record.DescriptionOA.ParagraphsOS.Add(paragraph);
                paragraph.Contents = TsStringUtils.MakeString(text, cache.DefaultAnalWs);
            }
        });
    }

    /// <summary>Overwrites the visible ID field, as a person editing the record in FieldWorks would.</summary>
    public static void SetVisibleId(LcmCache cache, IRnGenericRec record, string text)
    {
        var idField = FindIdField(cache);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            cache.DomainDataByFlid.SetString(record.Hvo, idField, TsStringUtils.MakeString(text, cache.DefaultAnalWs)));
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
