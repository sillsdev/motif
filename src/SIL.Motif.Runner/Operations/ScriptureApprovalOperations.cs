using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Model;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Resolution;

namespace SIL.Motif.Runner.Operations;

/// <summary>
/// Where the approved Occurrences of Sources live in the project: under each Source's own item in the Motif Sources list,
/// one sub-item for each Passage of the Source that each installation of Motif approved something in. The sub-item's
/// Abbreviation is the Passage, its Name reads <c>WEB MAT 5 1a2b3c4d</c> (the Source, the Passage and the start of the
/// installation), its Description warns that Motif manages it, and one custom String field on CmPossibility holds that
/// installation's approvals in the Passage as a <see cref="ScriptureApprovalBlob"/>.
/// </summary>
/// <remarks>
/// ADR 0003 paths. A sub-item is what FieldWorks makes when a person inserts a subitem under a list item, and LibLCM
/// makes with <c>ICmPossibilityFactory.Create(Guid, ICmPossibility)</c>; the list is sorted as its properties dialog
/// sets it, <c>ICmPossibilityList.IsSorted</c>. The field is what FieldWorks makes under Tools, Configure, Custom Fields
/// for a list item, and LibLCM makes with <c>IFwMetaDataCacheManaged.AddCustomField</c> on class CmPossibility, type
/// String, writing-system selector analysis, no destination class.
/// <para>
/// An installation edits only its own sub-items, so Send/Receive, which merges objects by Guid, only ever adds or keeps
/// whole sub-items from each side and never sees one edited on both. A sub-item's Guid is derived from the Source, the
/// Passage and the installation, so an installation finds its own again. The sub-items travel in the Sources list's
/// own file, which every shared copy already has once a Source was added, and the list is sorted, so Chorus does not
/// report two people adding sub-items as a conflict.
/// </para>
/// </remarks>
public static class ScriptureApprovalFields
{
    /// <summary>The internal name of the custom String field that holds one installation's approvals in one Passage.</summary>
    public const string ApprovalsField = "MotifApprovals";

    /// <summary>The description of every approvals sub-item, which warns that edits there can be overwritten.</summary>
    public const string ItemDescription =
        "Motif keeps the approved Occurrences of this Source here, one subitem per chapter for each computer that approves. " +
        "Motif can overwrite or remove edits made here.";

    /// <summary>The fingerprint of the morphology an analysis has, which changes when its morphs change.</summary>
    public static string Signature(IWfiAnalysis analysis) => ScriptureApprovalDocument.Fingerprint(string.Join("|",
        analysis.MorphBundlesOS.Select(bundle => string.Join(",", bundle.MorphRA?.Guid, bundle.MsaRA?.Guid, bundle.InflTypeRA?.Guid))));

    /// <summary>The Guid of the sub-item that holds one installation's approvals in one Passage of one Source.</summary>
    public static Guid ItemGuid(Guid sourceItem, string passage, Guid installation)
    {
        var bytes = SHA256.HashData([.. sourceItem.ToByteArray(), .. Encoding.UTF8.GetBytes("approvals\0" + passage + "\0"),
            .. installation.ToByteArray()]);
        var guid = bytes[..16];
        guid[7] = (byte)((guid[7] & 0x0F) | 0x50);
        guid[8] = (byte)((guid[8] & 0x3F) | 0x80);
        return new Guid(guid);
    }

    /// <summary>
    /// The approvals one sub-item of a Source holds; null with the reason when the field is missing or is not approvals
    /// for this Source.
    /// </summary>
    public static ScriptureApprovalBlob? TryRead(LcmCache cache, ICmPossibility item, int flid, out string? error)
    {
        error = null;
        if (flid == 0)
        {
            error = "The project has no approvals field.";
            return null;
        }
        try
        {
            var blob = ScriptureApprovalBlob.Parse(cache.DomainDataByFlid.get_StringProp(item.Hvo, flid)?.Text);
            if (item.Owner is ICmPossibility source && blob.Source != source.Guid)
                throw new FormatException("The approvals name another Source.");
            return blob;
        }
        catch (FormatException exception)
        {
            error = exception.Message;
            return null;
        }
    }

    /// <summary>The Passage an item names in its Abbreviation, or its Name when the Abbreviation is not one.</summary>
    public static string LabelOf(ICmPossibility item)
    {
        var abbreviation = ScriptureSourceSupport.IdOf(item);
        if (ScriptureApprovalDocument.IsPassage(abbreviation)) return abbreviation;
        var name = ScriptureSourceSupport.TextsOf(item.Name).FirstOrDefault(text => text.Length > 0) ?? "";
        return name.Length > 0 ? name : item.Guid.ToString("D");
    }

    /// <summary>
    /// Makes the Sources list sorted, as its properties dialog in FieldWorks does, so that Chorus does not report two
    /// people adding sub-items at once as a conflict. Adding a Source makes the list sorted; the first Apply that adds an
    /// approvals sub-item sorts a list made before that.
    /// </summary>
    public static void RequireSorted(ICmPossibilityList sources)
    {
        if (!sources.IsSorted) sources.IsSorted = true;
    }
}

/// <summary>
/// The decisions about approvals that are not made yet, in one place so that deciding them changes this type only.
/// </summary>
public interface IScriptureApprovalPolicy
{
    /// <summary>Whether Apply may add the approvals field to a project that does not have it.</summary>
    bool MayCreateField(LcmCache cache);
}

/// <summary>The policy in force: Apply adds the field when absent. Hiding the items and detecting approvals a merge lost are open, and not built.</summary>
public static class ScriptureApprovalPolicy
{
    private sealed class Default : IScriptureApprovalPolicy
    {
        public bool MayCreateField(LcmCache cache) => true;
    }

    /// <summary>The policy every approval operation consults.</summary>
    public static IScriptureApprovalPolicy Current { get; set; } = new Default();
}

/// <summary>The two operations that write approved Occurrences of a Source.</summary>
public static class ScriptureApprovalOperationKinds
{
    /// <summary>Approves an Occurrence: {passage, anchor, installation, analysis, verse, word}; the target is the Source's item.</summary>
    public const string Approve = "scripture/source/approveOccurrence";

    /// <summary>Clears an Occurrence's approval: {passage, anchor, installation}; the target is the Source's item.</summary>
    public const string Clear = "scripture/source/clearOccurrence";

#pragma warning disable CA2255 // Load-time registration makes these kinds available before parsing and dispatch.
    [ModuleInitializer]
    internal static void Register()
    {
        foreach (var kind in new[] { Approve, Clear }) OperationKindRegistry.Register(kind);
        OperationHandlerRegistry.Register(Approve, new ScriptureApprovalHandler(Approve));
        OperationHandlerRegistry.Register(Clear, new ScriptureApprovalHandler(Clear));
        foreach (var kind in new[] { Approve, Clear })
            OperationSlotRegistry.RegisterMany(kind, operation =>
            [
                new OperationSlot(OperationSlotRegistry.Target(operation), SnapshotFields.ScriptureOccurrenceApproval,
                    ClosedPayloadParsing.GetRequiredString(OperationSlotRegistry.After(operation), "anchor", Approve)),
            ]);
    }
#pragma warning restore CA2255
}


internal sealed class ScriptureApprovalHandler(string kind) : IOperationHandler
{
    private bool IsApprove => kind == ScriptureApprovalOperationKinds.Approve;

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        var request = Parse(operation);
        var (id, source) = TargetResolution.Resolve<ICmPossibility>(cache, operation, kind);
        var store = ScriptureApprovalStore.For(cache);
        store.RequireSource(source);
        var before = Footprint(store, source, request);

        ScriptureApproval? approval = null;
        if (IsApprove)
        {
            var analysis = Analysis(cache, request.Analysis!.Value);
            store.RequireField();
            approval = new(analysis.Guid, request.Verse!, request.Word!.Normalize(NormalizationForm.FormD),
                ScriptureApprovalFields.Signature(analysis));
        }
        var others = store.Chapter(source, request.Passage).Where(blob => blob.Installation != request.Installation);
        var own = store.Own(source, request.Passage, request.Installation, create: false);
        var ownBlob = own?.Blob ?? new ScriptureApprovalBlob(source.Guid, request.Passage, request.Installation);
        var entry = Settle([.. others, ownBlob], ownBlob, request.Anchor, approval);
        var previous = ownBlob.Entries.GetValueOrDefault(request.Anchor);
        if (!ReferenceEquals(entry, previous) && !(entry is null && previous is null))
        {
            own ??= store.Own(source, request.Passage, request.Installation, create: true);
            if (entry is null) own!.Value.Blob.Remove(request.Anchor);
            else own!.Value.Blob.Set(entry);
            store.Write(own.Value.Item, own.Value.Blob);
        }
        touchedTargets.Add(id);
        return new ExpectedEffect(id, SnapshotFields.ScriptureOccurrenceApproval, before, Footprint(store, source, request),
            Occurrence(request));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var request = Parse(operation);
        var (id, source) = TargetResolution.Resolve<ICmPossibility>(cache, operation, kind);
        var store = ScriptureApprovalStore.For(cache);
        var current = Footprint(store, source, request);
        return new ExpectedEffect(id, SnapshotFields.ScriptureOccurrenceApproval, current, current, Occurrence(request));
    }

    // Names the Occurrence, so a report links the effect to the operation at that location.
    private static JsonElement Occurrence(Request request) =>
        JsonSerializer.SerializeToElement(new { anchor = request.Anchor });

    /// <summary>
    /// The installation's new entry at the Occurrence, or null to remove its entry. It overrules every other
    /// installation's entry standing there and keeps every mark of its previous entry, also one whose entry this copy of
    /// the project does not have, since that entry may still arrive; so a settled conflict stays settled. The previous
    /// entry is returned unchanged when it already says this and stands.
    /// </summary>
    internal static ScriptureApprovalEntry? Settle(IReadOnlyList<ScriptureApprovalBlob> all, ScriptureApprovalBlob own,
        string anchor, ScriptureApproval? approval)
    {
        var standing = ScriptureApprovalBlob.Standing(all, anchor);
        var previous = own.Entries.GetValueOrDefault(anchor);
        var marks = (previous?.Overrules ?? []).Concat(standing.Where(holding => holding.Installation != own.Installation)
                .Select(holding => holding.Mark))
            .Distinct()
            .OrderBy(mark => mark.Installation.ToString("D"), StringComparer.Ordinal).ThenBy(mark => mark.Stamp, StringComparer.Ordinal)
            .ToArray();
        if (approval is null && marks.Length == 0) return null;
        var candidate = new ScriptureApprovalEntry(anchor, approval, previous?.Stamp ?? "", marks);
        if (previous is not null && previous.SaysTheSame(candidate) && standing.Any(holding => holding.Installation == own.Installation))
            return previous;
        return candidate with { Stamp = ScriptureApprovalBlob.NewStamp() };
    }

    private sealed record Request(string Passage, string Anchor, Guid Installation, CanonicalId? Analysis, string? Verse, string? Word);

    private Request Parse(OperationEnvelope operation)
    {
        var after = operation.After ?? throw new InvalidOperationException($"'{kind}' requires after.");
        ClosedPayloadParsing.RequireObject(after, kind);
        string[] allowed = IsApprove ? ["passage", "anchor", "installation", "analysis", "verse", "word"] : ["passage", "anchor", "installation"];
        ClosedPayloadParsing.RejectUnknownProperties(after, allowed, kind);
        var passage = ClosedPayloadParsing.GetRequiredString(after, "passage", kind);
        var anchor = ClosedPayloadParsing.GetRequiredString(after, "anchor", kind);
        if (!ScriptureApprovalDocument.IsAnchor(anchor))
            throw new InvalidOperationException($"'{kind}': '{anchor}' is not an Occurrence location such as MAT 5:3#2.");
        if (ScriptureApprovalDocument.PassageOf(anchor) != passage)
            throw new InvalidOperationException($"'{kind}': the location {anchor} is not in the Passage {passage}.");
        if (!Guid.TryParse(ClosedPayloadParsing.GetRequiredString(after, "installation", kind), out var installation) ||
            installation == Guid.Empty)
            throw new InvalidOperationException($"'{kind}': the installation is not an identity.");
        if (!IsApprove) return new(passage, anchor, installation, null, null, null);
        return new(passage, anchor, installation, ClosedPayloadParsing.GetRequiredCanonicalId(after, "analysis", kind),
            ClosedPayloadParsing.GetRequiredString(after, "verse", kind),
            ClosedPayloadParsing.GetRequiredString(after, "word", kind));
    }

    // The standing analyses at the location, in Guid order, and the verse fingerprints they were approved against.
    private static Dictionary<string, string> Footprint(ScriptureApprovalStore store, ICmPossibility source, Request request)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var approvals = ScriptureApprovalBlob.Standing(store.Chapter(source, request.Passage), request.Anchor)
            .Select(holding => holding.Entry.Approval).OfType<ScriptureApproval>()
            .OrderBy(approval => approval.Analysis.ToString("D"), StringComparer.Ordinal).ToArray();
        if (approvals.Length == 0) return result;
        result["analysis"] = string.Join(",", approvals.Select(approval => CanonicalId.FromGuid(approval.Analysis).Value).Distinct());
        result["verse"] = string.Join(",", approvals.Select(approval => approval.Verse).Distinct());
        return result;
    }

    private static IWfiAnalysis Analysis(LcmCache cache, CanonicalId id) =>
        CanonicalIdResolver.Resolve(cache, id) as IWfiAnalysis
        ?? throw new InvalidOperationException($"The analysis '{id.Value}' is not a stored analysis.");
}

/// <summary>
/// The approvals sub-items of one cache, read once: the sub-items of each Passage of each Source, and each sub-item's
/// approvals, read again only when the field text changed. Within a <see cref="DeferredWrites"/> batch the changed
/// approvals are held here and each sub-item is written once when the batch completes.
/// </summary>
internal sealed class ScriptureApprovalStore
{
    private static readonly ConditionalWeakTable<LcmCache, ScriptureApprovalStore> Stores = new();
    private readonly LcmCache _cache;
    private readonly Dictionary<(Guid Source, string Passage), List<ICmPossibility>> _items = new();
    private readonly Dictionary<ICmPossibility, int> _indexed = new();
    private readonly Dictionary<ICmPossibility, (string? Text, ScriptureApprovalBlob? Blob)> _read = new();
    private readonly Dictionary<ICmPossibility, ScriptureApprovalBlob> _held = new();

    private int _flid;
    private ICmPossibilityList? _sources;
    private bool _compatible;

    private ScriptureApprovalStore(LcmCache cache) => _cache = cache;

    /// <summary>Throws unless the target is a Source in the Motif Sources list and the Source fields are usable.</summary>
    public void RequireSource(ICmPossibility source)
    {
        if (_sources is not { IsValidObject: true }) _sources = ScriptureSourceSupport.FindList(_cache);
        if (_sources is null || source.Owner != _sources)
            throw new InvalidOperationException("The target is not a Source in the Motif Sources list.");
        if (_compatible) return;
        ScriptureSourceSupport.RequireCompatible(_cache);
        _compatible = true;
    }

    /// <summary>Adds the approvals field when the project has none and the policy allows it.</summary>
    public void RequireField()
    {
        if (Field() != 0) return;
        if (!ScriptureApprovalPolicy.Current.MayCreateField(_cache))
            throw new InvalidOperationException("The project has no approvals field and it may not be added.");
        _flid = ScriptureSourceSupport.FindField(_cache, ScriptureApprovalFields.ApprovalsField, create: true);
    }

    // The field id once the project has the field; looked up again while it has none.
    private int Field() => _flid != 0 ? _flid : _flid = ScriptureSourceSupport.FindField(_cache, ScriptureApprovalFields.ApprovalsField, create: false);

    public static ScriptureApprovalStore For(LcmCache cache) => Stores.GetValue(cache, each => new ScriptureApprovalStore(each));

    /// <summary>Every readable approvals blob for the Passage of the Source.</summary>
    public IReadOnlyList<ScriptureApprovalBlob> Chapter(ICmPossibility source, string passage)
    {
        Index(source);
        return _items.TryGetValue((source.Guid, passage), out var items)
            ? items.Select(Blob).OfType<ScriptureApprovalBlob>().Where(blob => blob.Source == source.Guid && blob.Passage == passage).ToArray()
            : [];
    }

    /// <summary>
    /// The installation's sub-item for the Passage and the approvals to change, made when asked to. A sub-item with this
    /// identity whose field cannot be read, or names another Source, Passage or installation, is this installation's and
    /// is started again empty; from then on it is in the Passage like any other, so every later write in a batch adds to it.
    /// </summary>
    public (ICmPossibility Item, ScriptureApprovalBlob Blob)? Own(ICmPossibility source, string passage, Guid installation, bool create)
    {
        Index(source);
        var guid = ScriptureApprovalFields.ItemGuid(source.Guid, passage, installation);
        var objects = _cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        ICmPossibility? item = null;
        if (objects.TryGetObject(guid, out var found))
        {
            if (found is not ICmPossibility same || same.Owner != source)
                throw new InvalidOperationException($"The identity of the approvals subitem for {passage} is already used by another object.");
            item = same;
        }
        if (item is not null && Blob(item) is { } blob && blob.Installation == installation && blob.Source == source.Guid &&
            blob.Passage == passage)
            return (item, _held.ContainsKey(item) || DeferredWrites.Current(_cache) is not null ? blob : ScriptureApprovalBlob.Parse(blob.Serialize()));
        if (!create) return null;
        item ??= CreateItem(source, passage, installation, guid);
        var group = Group(source.Guid, passage);
        if (!group.Contains(item)) group.Add(item);
        return (item, new ScriptureApprovalBlob(source.Guid, passage, installation));
    }

    /// <summary>Stores the sub-item's approvals, or removes the sub-item when it holds none; held back while a batch is open.</summary>
    public void Write(ICmPossibility item, ScriptureApprovalBlob blob)
    {
        if (DeferredWrites.Current(_cache) is { } batch)
        {
            _held[item] = blob;
            batch.Hold(this, Flush, () => Stores.Remove(_cache));
            return;
        }
        WriteNow(item, blob);
    }

    private void Flush()
    {
        foreach (var (item, blob) in _held.ToArray()) WriteNow(item, blob);
        _held.Clear();
    }

    private void WriteNow(ICmPossibility item, ScriptureApprovalBlob blob)
    {
        _held.Remove(item);
        if (blob.Entries.Count == 0)
        {
            _read.Remove(item);
            _items.GetValueOrDefault((blob.Source, blob.Passage))?.Remove(item);
            if (item.Owner is ICmPossibility source)
            {
                source.SubPossibilitiesOS.Remove(item);
                _indexed[source] = source.SubPossibilitiesOS.Count;
            }
            return;
        }
        var flid = Field();
        var text = blob.Serialize();
        var value = TsStringUtils.MakeString(text, _cache.DefaultAnalWs);
        // A checked value is remembered as normalized, so LCM does not rebuild a long one code point at a time on save.
        value.get_IsNormalizedForm(FwNormalizationMode.knmNFD);
        value.get_IsNormalizedForm(FwNormalizationMode.knmNFC);
        _cache.DomainDataByFlid.SetString(item.Hvo, flid, value);
        _read[item] = (text, blob);
        ScriptureApprovalCounts.Wrote();
    }

    // A Source's sub-items are grouped once, and again only when its number of sub-items changes.
    private void Index(ICmPossibility source)
    {
        var count = source.SubPossibilitiesOS.Count;
        if (_indexed.TryGetValue(source, out var indexed) && indexed == count) return;
        foreach (var key in _items.Keys.Where(key => key.Source == source.Guid).ToArray()) _items.Remove(key);
        _indexed[source] = count;
        foreach (var item in source.SubPossibilitiesOS)
            if (Blob(item) is { } blob) Group(blob.Source, blob.Passage).Add(item);
    }

    private List<ICmPossibility> Group(Guid source, string passage)
    {
        if (!_items.TryGetValue((source, passage), out var items)) _items[(source, passage)] = items = new();
        return items;
    }

    private ScriptureApprovalBlob? Blob(ICmPossibility item)
    {
        if (_held.TryGetValue(item, out var held)) return held;
        if (!item.IsValidObject) return null;
        var flid = Field();
        if (flid == 0) return null;
        var text = _cache.DomainDataByFlid.get_StringProp(item.Hvo, flid)?.Text;
        if (_read.TryGetValue(item, out var read) && string.Equals(read.Text, text, StringComparison.Ordinal)) return read.Blob;
        var blob = ScriptureApprovalFields.TryRead(_cache, item, flid, out _);
        _read[item] = (text, blob);
        return blob;
    }

    private ICmPossibility CreateItem(ICmPossibility source, string passage, Guid installation, Guid guid)
    {
        if (_cache.ServiceLocator.GetInstance<ICmObjectRepository>().IsValidObjectId(guid))
            throw new InvalidOperationException($"The identity of the approvals subitem for {passage} is already used by another object.");
        if (source.Owner is ICmPossibilityList sources) ScriptureApprovalFields.RequireSorted(sources);
        var item = _cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create(guid, source);
        var ws = _cache.DefaultAnalWs;
        item.Abbreviation.set_String(ws, TsStringUtils.MakeString(passage, ws));
        item.Name.set_String(ws, TsStringUtils.MakeString(
            ScriptureSourceSupport.IdOf(source) + " " + passage + " " + installation.ToString("N")[..8], ws));
        item.Description.set_String(ws, TsStringUtils.MakeString(ScriptureApprovalFields.ItemDescription, ws));
        _indexed[source] = source.SubPossibilitiesOS.Count;
        return item;
    }
}

/// <summary>Counts approvals blobs read and written within a scope, so a test can hold each chapter to one of each.</summary>
internal static class ScriptureApprovalCounts
{
    private sealed class Counter
    {
        public int Parsed;
        public int Written;
    }

    private static readonly AsyncLocal<Counter?> Current = new();

    /// <summary>Starts counting in this flow; the returned function gives the counts so far.</summary>
    public static Func<(int Parsed, int Written)> Start()
    {
        var counter = new Counter();
        Current.Value = counter;
        return () => (Volatile.Read(ref counter.Parsed), Volatile.Read(ref counter.Written));
    }

    public static void Parsed()
    {
        if (Current.Value is { } counter) Interlocked.Increment(ref counter.Parsed);
    }

    public static void Wrote()
    {
        if (Current.Value is { } counter) Interlocked.Increment(ref counter.Written);
    }
}
