using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Snapshotting;

namespace SIL.Motif.Runner.Operations;

/// <summary>The operation that adds a Grammar Guide part or section (ADR 0005 decision 1).</summary>
public static class GdrSectionOperationKinds
{
    /// <summary>
    /// Adds a Guide item: {id, title, ws, parent?, after?}. The target is the language project; the entity is the new item.
    /// <c>id</c> is its permanent <c>GDR-n</c> number, <c>title</c> its Name in <c>ws</c>, <c>parent</c> the item it goes
    /// under (the top of the list when absent) and <c>after</c> the sibling it follows (the end when absent).
    /// The "Grammar Guide" list is created when the project has none.
    /// </summary>
    public const string Create = "guide/section/create";

#pragma warning disable CA2255 // Load-time registration makes this kind available before parsing and dispatch.
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(Create);
        OperationHandlerRegistry.Register(Create, GdrSectionCreateHandler.Instance);
        OperationSlotRegistry.RegisterOwningCreate(Create, SnapshotFields.GuideSections, false);
    }
#pragma warning restore CA2255
}

/// <summary>Finds the Grammar Guide list and numbers new sections, for the code that composes and applies them.</summary>
public static class GdrSectionSupport
{
    /// <summary>The name of the custom possibility list that holds the Guide.</summary>
    public const string ListName = "Grammar Guide";

    /// <summary>The name the list had before; it is read, never written.</summary>
    public const string OldListName = "Grammar Design Records";

    private static readonly Regex Number = new(@"^GDR-(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// The Guide list, or null when the project has none. Throws when two lists carry either name, or when the only list
    /// still has the old name: Motif reads that list but writes a Guide only after it is renamed in FieldWorks.
    /// </summary>
    public static ICmPossibilityList? FindList(LcmCache cache, string kind)
    {
        var all = cache.ServiceLocator.GetInstance<ICmPossibilityListRepository>().AllInstances().ToArray();
        bool Named(ICmPossibilityList list, string name) =>
            list.Name.AvailableWritingSystemIds.Any(ws => list.Name.get_String(ws).Text == name);
        var current = all.Where(list => Named(list, ListName)).ToArray();
        var old = all.Where(list => Named(list, OldListName)).ToArray();
        if (current.Length + old.Length > 1)
            throw new InvalidOperationException($"'{kind}': {current.Length + old.Length} possibility lists are named " +
                $"'{ListName}' or '{OldListName}'. Rename or remove all but one in FieldWorks.");
        if (old.Length == 1)
            throw new InvalidOperationException($"'{kind}': the Guide list is still named '{OldListName}'. " +
                $"Rename it '{ListName}' in FieldWorks before adding a section.");
        return current.Length == 1 ? current[0] : null;
    }

    /// <summary>The abbreviation text of <paramref name="item"/> in any writing system, or empty.</summary>
    public static string NumberOf(LcmCache cache, ICmPossibility item)
    {
        foreach (var ws in new[] { cache.DefaultAnalWs, cache.DefaultVernWs }.Concat(item.Abbreviation.AvailableWritingSystemIds))
        {
            var text = item.Abbreviation.get_String(ws).Text;
            if (!string.IsNullOrEmpty(text)) return text;
        }
        return "";
    }

    /// <summary>The next permanent <c>GDR-n</c>: one more than the largest number in the list, <c>GDR-1</c> for an empty list.</summary>
    public static string NextNumber(LcmCache cache, ICmPossibilityList? list)
    {
        var largest = 0L;
        if (list is not null)
            foreach (var item in list.ReallyReallyAllPossibilities)
                if (Number.Match(NumberOf(cache, item).Normalize(NormalizationForm.FormD)) is { Success: true } match &&
                    long.TryParse(match.Groups[1].Value, out var value) && value > largest)
                    largest = value;
        return "GDR-" + (largest + 1);
    }

    /// <summary>The item whose number or canonical id is <paramref name="id"/>, or null.</summary>
    public static ICmPossibility? Find(LcmCache cache, ICmPossibilityList list, string id)
    {
        var wanted = id.Normalize(NormalizationForm.FormD);
        return list.ReallyReallyAllPossibilities.FirstOrDefault(item =>
            string.Equals(NumberOf(cache, item).Normalize(NormalizationForm.FormD), wanted, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(CanonicalId.FromGuid(item.Guid).Value, id, StringComparison.Ordinal));
    }
}

/// <summary>Adds a Guide item: the list when the project has none, the item at its place, its number and title.</summary>
internal sealed class GdrSectionCreateHandler : IOperationHandler
{
    internal static readonly GdrSectionCreateHandler Instance = new();
    private GdrSectionCreateHandler() { }

    private const string Kind = GdrSectionOperationKinds.Create;

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        if (operation.StorageIdOverride is not null)
            throw new InvalidOperationException($"'{Kind}': storage GUID overrides are not supported for this create.");
        var entityId = operation.EntityId ?? throw new InvalidOperationException($"'{Kind}' requires entityId.");
        var after = operation.After ?? throw new InvalidOperationException($"'{Kind}' requires after.");
        ClosedPayloadParsing.RequireObject(after, Kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, ["id", "title", "ws", "parent", "after"], Kind);
        var number = ClosedPayloadParsing.GetRequiredString(after, "id", Kind).Normalize(NormalizationForm.FormD);
        var title = ClosedPayloadParsing.GetRequiredString(after, "title", Kind).Normalize(NormalizationForm.FormD);
        var wsTag = ClosedPayloadParsing.GetRequiredString(after, "ws", Kind);
        var parentId = Optional(after, "parent");
        var afterId = Optional(after, "after");
        if (number.Trim().Length == 0 || title.Trim().Length == 0)
            throw new InvalidOperationException($"'{Kind}': a Guide item needs a number and a title.");
        var ws = cache.WritingSystemFactory.GetWsFromStr(wsTag);
        if (ws == 0)
            throw new InvalidOperationException($"'{Kind}': writing system tag '{wsTag}' is not known to this project.");
        var (id, _) = TargetResolution.Resolve<ILangProject>(cache, operation, Kind);
        CreateIdentity.RequireFresh(cache, entityId, Kind, "CmPossibility");
        var list = GdrSectionSupport.FindList(cache, Kind);
        if (list is not null && GdrSectionSupport.Find(cache, list, number) is { } holder && holder.Guid != entityId.ToGuid())
            throw new InvalidOperationException($"'{Kind}': the number '{number}' is already in use; add the section again to take the next one.");

        var before = Read(cache, entityId, number);
        var analysis = cache.DefaultAnalWs;
        list ??= cache.ServiceLocator.GetInstance<ICmPossibilityListFactory>().CreateUnowned(GdrSectionSupport.ListName, analysis);
        ICmPossibility? parent = null;
        if (parentId is not null)
            parent = FindIn(cache, list, parentId) ?? throw new InvalidOperationException(
                $"'{Kind}': the parent '{parentId}' is not in the Grammar Guide.");
        var siblings = parent is null ? list.PossibilitiesOS : parent.SubPossibilitiesOS;
        var index = siblings.Count;
        if (afterId is not null)
        {
            var sibling = FindIn(cache, list, afterId) ?? throw new InvalidOperationException(
                $"'{Kind}': the section '{afterId}' to follow is not in the Grammar Guide.");
            if (!siblings.Contains(sibling))
                throw new InvalidOperationException($"'{Kind}': '{afterId}' is not under the same parent, so a section cannot follow it there.");
            index = siblings.IndexOf(sibling) + 1;
        }
        var item = parent is null
            ? cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create(entityId.ToGuid(), list)
            : cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create(entityId.ToGuid(), parent);
        if (index < siblings.Count - 1) siblings.MoveTo(siblings.Count - 1, siblings.Count - 1, siblings, index);
        item.Abbreviation.set_String(analysis, TsStringUtils.MakeString(number, analysis));
        item.Name.set_String(ws, TsStringUtils.MakeString(title, ws));
        touchedTargets.Add(id);
        return new ExpectedEffect(id, SnapshotFields.GuideSections, before, Read(cache, entityId, number));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var entityId = operation.EntityId ?? throw new InvalidOperationException($"'{Kind}' requires entityId.");
        var (id, _) = TargetResolution.Resolve<ILangProject>(cache, operation, Kind);
        var number = operation.After is { ValueKind: System.Text.Json.JsonValueKind.Object } after &&
            after.TryGetProperty("id", out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString() ?? "" : "";
        var current = Read(cache, entityId, number.Normalize(NormalizationForm.FormD));
        return new ExpectedEffect(id, SnapshotFields.GuideSections, current, current);
    }

    private static string? Optional(System.Text.Json.JsonElement after, string name) =>
        after.TryGetProperty(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() : null;

    private static ICmPossibility? FindIn(LcmCache cache, ICmPossibilityList list, string id) => GdrSectionSupport.Find(cache, list, id);

    // The footprint: whether the item exists, the identity names something else, or another item holds the number.
    private static Dictionary<string, string> Read(LcmCache cache, CanonicalId entityId, string number)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        ICmPossibilityList? list;
        try { list = GdrSectionSupport.FindList(cache, Kind); }
        catch (InvalidOperationException) { list = null; }
        var inList = list?.ReallyReallyAllPossibilities.Any(item => item.Guid == entityId.ToGuid()) == true;
        if (inList) result["ref"] = entityId.Value;
        else if (cache.ServiceLocator.GetInstance<ICmObjectRepository>().IsValidObjectId(entityId.ToGuid()))
            result["collision"] = entityId.Value;
        if (list is not null && number.Length > 0 && GdrSectionSupport.Find(cache, list, number) is { } holder && holder.Guid != entityId.ToGuid())
            result["numberHeldBy"] = CanonicalId.FromGuid(holder.Guid).Value;
        return result;
    }
}
