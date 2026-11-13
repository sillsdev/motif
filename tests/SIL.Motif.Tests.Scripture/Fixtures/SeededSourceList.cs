using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Scripture;

namespace SIL.Motif.Tests.Scripture;

/// <summary>
/// Test code only: writes a "Motif Sources" list straight into an open project, the way the GDR fixtures write theirs.
/// The product never writes this list from here; its writes are Motif operations.
/// </summary>
internal static class SeededSourceList
{
    /// <summary>One item to write; null kind or versification leaves that custom field empty.</summary>
    public sealed record Item(string Id, string Name, string Context, string? Kind, string? Versification, string? Status = null);

    /// <summary>Adds the two custom fields once.</summary>
    public static void AddFields(LcmCache cache)
    {
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        foreach (var name in new[] { SourceListFormat.KindField, SourceListFormat.VersificationField, SourceListFormat.StatusField })
        {
            if (metadata.GetFieldIds().Any(flid => metadata.GetFieldName(flid) == name)) continue;
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                metadata.AddCustomField(SourceListFormat.FieldOwnerClass, name, CellarPropertyType.String, 0, name,
                    WritingSystemServices.kwsAnal, Guid.Empty));
        }
    }

    /// <summary>Adds a list named "Motif Sources" holding the items; with no items it is an empty list.</summary>
    public static void AddList(LcmCache cache, params Item[] items) => Write(cache, withFields: true, items);

    /// <summary>As <see cref="AddList"/> but the project gets no custom fields, as when a person made the list in FieldWorks.</summary>
    public static void AddListWithoutFields(LcmCache cache, params Item[] items) => Write(cache, withFields: false, items);

    private static void Write(LcmCache cache, bool withFields, Item[] items)
    {
        if (withFields) AddFields(cache);
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        var kindField = metadata.GetFieldIds().FirstOrDefault(flid => metadata.GetFieldName(flid) == SourceListFormat.KindField);
        var versificationField = metadata.GetFieldIds().FirstOrDefault(flid => metadata.GetFieldName(flid) == SourceListFormat.VersificationField);
        var statusField = metadata.GetFieldIds().FirstOrDefault(flid => metadata.GetFieldName(flid) == SourceListFormat.StatusField);
        var analysis = cache.DefaultAnalWs;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var list = cache.ServiceLocator.GetInstance<ICmPossibilityListFactory>().Create();
            cache.LangProject.CheckListsOC.Add(list);
            list.Name.set_String(analysis, SourceListFormat.ListName);
            foreach (var item in items)
            {
                var possibility = cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create();
                list.PossibilitiesOS.Add(possibility);
                possibility.Name.set_String(analysis, item.Name);
                if (item.Id.Length > 0) possibility.Abbreviation.set_String(analysis, item.Id);
                possibility.Description.set_String(analysis, TsStringUtils.MakeString(item.Context, analysis));
                if (item.Kind is not null && kindField != 0)
                    cache.DomainDataByFlid.SetString(possibility.Hvo, kindField, TsStringUtils.MakeString(item.Kind, analysis));
                if (item.Versification is not null && versificationField != 0)
                    cache.DomainDataByFlid.SetString(possibility.Hvo, versificationField, TsStringUtils.MakeString(item.Versification, analysis));
                if (item.Status is not null && statusField != 0)
                    cache.DomainDataByFlid.SetString(possibility.Hvo, statusField, TsStringUtils.MakeString(item.Status, analysis));
            }
        });
    }
}
