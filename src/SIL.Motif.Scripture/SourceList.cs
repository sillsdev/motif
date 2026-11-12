using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;

namespace SIL.Motif.Scripture;

/// <summary>
/// Reads the "Motif Sources" list from an open project copy, as <c>GdrReader</c> reads the Grammar Design Records.
/// It never opens a unit of work. See <see cref="SourceListFormat"/> for where each field lives.
/// </summary>
public static class SourceList
{
    /// <summary>
    /// The Sources in list order; empty when the project has no such list. A malformed item comes back with its
    /// <see cref="SourceListEntry.Error"/> set. Throws <see cref="SourceListAmbiguousException"/> when two lists carry the name.
    /// </summary>
    public static IReadOnlyList<SourceListEntry> Read(LcmCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        var lists = cache.ServiceLocator.GetInstance<ICmPossibilityListRepository>().AllInstances()
            .Where(candidate => Texts(candidate.Name).Any(name => name == SourceListFormat.ListName)).ToArray();
        if (lists.Length > 1)
            throw new SourceListAmbiguousException(
                $"{lists.Length} possibility lists are named '{SourceListFormat.ListName}'. Rename or remove all but one in FieldWorks.");
        if (lists.Length == 0) return [];

        var kindField = FindField(cache, SourceListFormat.KindField);
        var versificationField = FindField(cache, SourceListFormat.VersificationField);
        var entries = lists[0].PossibilitiesOS.Select(item => ReadItem(cache, item, kindField, versificationField)).ToArray();

        // A repeated id would make "which Source?" a guess, so every item that repeats one is unusable.
        var repeated = entries.Where(entry => entry.Id.Length > 0).GroupBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return entries.Select(entry => entry.IsValid && repeated.Contains(entry.Id)
            ? entry with { Context = null, Error = $"The id '{entry.Id}' is used by more than one Source." }
            : entry).ToArray();
    }

    private static SourceListEntry ReadItem(LcmCache cache, ICmPossibility item, int kindField, int versificationField)
    {
        var id = Real(item.Abbreviation).Trim();
        var name = Real(item.Name);
        var contextText = Real(item.Description).Trim();
        var kindText = Custom(cache, item, kindField);
        var versificationName = Custom(cache, item, versificationField);

        SourceListEntry Bad(SourceKind kind, string error) =>
            new(id, name, kind, contextText, versificationName, null, error);

        SourceKind kind;
        if (kindText.Length == 0) kind = SourceKind.Paratext;
        else if (!Enum.TryParse(kindText, ignoreCase: true, out kind) || !Enum.IsDefined(kind))
            return Bad(SourceKind.Paratext, $"The Source kind '{kindText}' is not known. Use Paratext or Corpus.");

        if (id.Length == 0) return Bad(kind, "The Source has no id. Write one in its Abbreviation.");
        if (kind == SourceKind.Corpus)
            return new(id, name, kind, contextText, versificationName, null, "A corpus Source is not supported yet.");
        if (versificationName.Length == 0)
            return Bad(kind, $"The Source '{id}' has no versification. Use one of: {string.Join(", ", SourceListFormat.Versifications.Keys)}.");
        if (!SourceListFormat.Versifications.TryGetValue(versificationName, out var versification))
            return Bad(kind, $"The versification '{versificationName}' is not known. Use one of: {string.Join(", ", SourceListFormat.Versifications.Keys)}.");
        if (!ScriptureContext.TryParse(contextText, versification, out var context, out var error))
            return Bad(kind, $"The Context of '{id}' is not valid. {error}");
        return new(id, name, kind, contextText, versificationName, context, null);
    }

    private static IEnumerable<string> Texts(IMultiAccessorBase text) =>
        text.AvailableWritingSystemIds.Select(ws => text.get_String(ws).Text ?? "");

    // The first writing system with text wins.
    private static string Real(IMultiAccessorBase text) =>
        Texts(text).FirstOrDefault(value => value.Length > 0) ?? "";

    private static int FindField(LcmCache cache, string name)
    {
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        foreach (var flid in metadata.GetFieldIds())
            if (metadata.GetFieldName(flid) == name && metadata.GetOwnClsName(flid) == SourceListFormat.FieldOwnerClass)
                return flid;
        return 0;
    }

    private static string Custom(LcmCache cache, ICmPossibility item, int flid) =>
        flid == 0 ? "" : cache.DomainDataByFlid.get_StringProp(item.Hvo, flid)?.Text?.Trim() ?? "";
}
