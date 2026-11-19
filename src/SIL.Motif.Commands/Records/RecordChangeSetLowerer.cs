using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.TextEditing;
using SIL.TextEditing.Markdown;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Project;

namespace SIL.Motif.Commands.Records;

/// <summary>A record-file change that was not lowered, with the registered code and the (class, field) it is about.</summary>
/// <param name="Code">A <see cref="RefusalCodes"/> value in the <c>record-import</c> family.</param>
/// <param name="Class">The record schema's object type of the change.</param>
/// <param name="Field">The schema key of the field, with its path.</param>
/// <param name="ObjectGuid">The object the change is about, when known.</param>
/// <param name="Reason">What stopped the change.</param>
public sealed record RecordLoweringRefusal(string Code, string Class, string Field, Guid? ObjectGuid, string Reason)
{
    /// <summary>The refusal as a command outcome carries it.</summary>
    public Refusal ToRefusal()
    {
        var facts = new Dictionary<string, string> { ["class"] = Class, ["field"] = Field };
        if (ObjectGuid is { } guid)
            facts["object"] = CanonicalId.FromGuid(guid).Value;
        return new Refusal(Code, FailureReason.Refused, Reason, facts);
    }
}

/// <summary>What a record plan lowers to: operations in the order to apply them, and every change that was refused.</summary>
/// <param name="Operations">The operations, each with an id derived from the plan, so lowering one plan twice gives one intent digest.</param>
/// <param name="Refusals">The plan's changes that have no operation, each named by (class, field).</param>
/// <param name="PlanRefusals">The importer's own refusals, passed through untouched.</param>
public sealed record RecordLowering(IReadOnlyList<OperationEnvelope> Operations,
    IReadOnlyList<RecordLoweringRefusal> Refusals, IReadOnlyList<RecordRefusal> PlanRefusals);

/// <summary>
/// Lowers a planned record-file import to operations through the kinds the Runner catalog declares for each field
/// (<see cref="RecordWritableFields"/>). It reads the project and writes nothing. A change that no kind expresses, or whose
/// project value is no longer the one the file started from, is refused by code, never dropped and never approximated.
/// A whole-file refusal in the plan lowers nothing.
/// </summary>
public sealed class RecordChangeSetLowerer(ProjectRecordSchemaResult schema, RecordWritableFields catalog)
{
    private const string RichKind = TextRichStringOperationKinds.Set;

    /// <summary>Lowers <paramref name="plan"/> against the project in <paramref name="cache"/>, which the caller owns and serializes.</summary>
    public RecordLowering Lower(LcmCache cache, ProjectRecordPlan plan)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(plan);
        var set = plan.ChangeSet;
        if (set.IsRefusedWhole)
        {
            var refusal = set.Refusals.First(item => item.WholeFile);
            return new([], [new(RefusalCodes.RecordImportPlanRefused, plan.Kind ?? "", refusal.Field,
                refusal.ObjectGuid, $"The plan refuses the whole file ({refusal.Kind}): {refusal.Reason}")], set.Refusals);
        }
        var seed = plan.Export?.Base ?? plan.RecordGuid?.ToString("D") ?? "";
        var lowered = LowerChanges(cache, seed, set.Changes);
        return new(lowered.Operations, lowered.Refusals, set.Refusals);
    }

    /// <summary>Lowers individual changes; <paramref name="seed"/> makes the operation ids, so one seed and one list give one intent digest.</summary>
    public RecordLowering LowerChanges(LcmCache cache, string seed, IReadOnlyList<RecordChange> changes)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(changes);
        var run = new Run(schema, catalog, cache, seed, new ProjectRecordExporter(cache, schema).CreateReadOptions().Markdown);
        for (var index = 0; index < changes.Count; index++)
            run.Lower(index, changes[index]);
        return new(run.Operations, run.Refusals, []);
    }

    private sealed class Stop(RecordLoweringRefusal refusal) : Exception(refusal.Reason)
    {
        internal RecordLoweringRefusal Refusal { get; } = refusal;
    }

    private sealed class Run(ProjectRecordSchemaResult schema, RecordWritableFields catalog, LcmCache cache,
        string seed, MarkdownOptions markdown)
    {
        private readonly string _seed = seed;
        private readonly Dictionary<int, CanonicalId> _added = [];
        private readonly HashSet<int> _refused = [];
        private readonly Dictionary<(Guid Owner, string Field), List<Guid>> _lists = [];
        private List<OperationEnvelope> _pending = [];

        internal List<OperationEnvelope> Operations { get; } = [];
        internal List<RecordLoweringRefusal> Refusals { get; } = [];

        internal void Lower(int index, RecordChange change)
        {
            _pending = [];
            // A declined change leaves no trace in the caches, so nothing anchors on an object that never existed.
            var added = new Dictionary<int, CanonicalId>(_added);
            var lists = _lists.ToDictionary(pair => pair.Key, pair => pair.Value.ToList());
            try
            {
                LowerChange(index, change);
                Operations.AddRange(_pending);
            }
            catch (Stop stop)
            {
                _added.Clear();
                foreach (var pair in added)
                    _added[pair.Key] = pair.Value;
                _lists.Clear();
                foreach (var pair in lists)
                    _lists[pair.Key] = pair.Value;
                _refused.Add(index);
                Refusals.Add(stop.Refusal);
            }
        }

        private CanonicalId Id(string role) => CanonicalId.FromGuid(new Guid(
            SHA256.HashData(Encoding.UTF8.GetBytes(_seed + "|" + role))[..16]));

        private static Stop Refuse(string code, RecordChange change, string reason) =>
            new(new(code, change.ObjectType ?? "", change.Field, change.ObjectGuid, reason));

        private void LowerChange(int index, RecordChange change)
        {
            if (change.ObjectGuid is not { } owner || change.ObjectType is not { } type || change.Field.Contains('/'))
                throw Refuse(RefusalCodes.RecordImportShapeUnsupported, change,
                    "The change is addressed through an object that has no GUID, which no operation names.");
            var key = change.Field;
            var binding = schema.FieldBinding(type, key) ?? throw Refuse(RefusalCodes.RecordImportFieldUncovered, change,
                $"The schema has no field '{key}' on {type}.");
            var target = CanonicalId.FromGuid(owner);
            var role = $"{index}:{change.Kind}";
            switch (change.Kind)
            {
                case RecordChangeKind.SetAlternative or RecordChangeKind.ClearAlternative:
                    LowerAlternative(change, type, key, target, role, change.Kind == RecordChangeKind.SetAlternative);
                    break;
                case RecordChangeKind.Set or RecordChangeKind.Clear:
                    LowerScalar(change, type, key, target, role);
                    break;
                case RecordChangeKind.SetRich or RecordChangeKind.ClearRich:
                    LowerRich(change, type, key, binding, target, role, owner, change.Old, change.New, change.WritingSystem, fresh: false);
                    break;
                case RecordChangeKind.AddReference or RecordChangeKind.RemoveReference or RecordChangeKind.MoveReference:
                    LowerReference(change, type, key, binding, target, role);
                    break;
                case RecordChangeKind.AddObject:
                    LowerCreate(index, change, type, key, binding, target, role);
                    break;
                case RecordChangeKind.RemoveObject:
                    LowerDelete(change, type, key, target, role);
                    break;
                case RecordChangeKind.MoveObject:
                    LowerMove(change, type, key, binding, target, role, RecordWriteVerb.MoveObject);
                    break;
                default:
                    throw Refuse(RefusalCodes.RecordImportShapeUnsupported, change,
                        $"No payload shape lowers a {change.Kind} change to an operation.");
            }
        }

        private string KindFor(RecordChange change, string type, string key, RecordWriteVerb verb,
            string? child = null, Placement? placement = null)
        {
            if (catalog.Resolve(type, key, verb, child, placement) is WritableFieldResolution.Covered covered)
            {
                var binding = schema.FieldBinding(type, key);
                var write = binding is null ? null : OperationHandlerRegistry.FieldWrites(covered.Kind).FirstOrDefault(item =>
                    item.DeclaringClass == binding.Value.DeclaringClass && item.Field == binding.Value.Field &&
                    (item.Verb == verb || verb == RecordWriteVerb.InsertReference && item.Verb == RecordWriteVerb.AddReference));
                if (write is null || RecordLoweringRules.Lowers(covered.Kind, write))
                    return covered.Kind;
                throw Refuse(RefusalCodes.RecordImportFieldUncovered, change,
                    $"{covered.Kind} covers {verb} of this field with a payload this lowering does not author.");
            }
            var why = ((WritableFieldResolution.Uncovered)catalog.Resolve(type, key, verb, child, placement)).Reason;
            throw Refuse(RefusalCodes.RecordImportFieldUncovered, change, $"No Runner kind covers {verb} of this field ({why}).");
        }

        private static OperationEnvelope Operation(CanonicalId id, string kind, CanonicalId target, object? after,
            Placement? placement = null, CanonicalId? entity = null) => new(id, kind, entityId: entity, target: target,
            after: after is null ? null : after is JsonElement json ? json : JsonSerializer.SerializeToElement(after), placement: placement);

        private void LowerScalar(RecordChange change, string type, string key, CanonicalId target, string role)
        {
            var field = schema.Schema.ObjectType(type)?.Field(key);
            var set = change.Kind == RecordChangeKind.Set;
            var kind = KindFor(change, type, key, set ? RecordWriteVerb.Set : RecordWriteVerb.Clear);
            object? after = new { };
            if (set)
                after = field?.Kind switch
                {
                    RecordFieldKind.Boolean when bool.TryParse(change.New, out var flag) => new { value = flag },
                    RecordFieldKind.Integer when int.TryParse(change.New, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out var number) => new { value = number },
                    RecordFieldKind.Reference when change.NewTarget is { } to => new { @ref = CanonicalId.FromGuid(to).Value },
                    _ => throw Refuse(RefusalCodes.RecordImportShapeUnsupported, change,
                        $"A {field?.Kind.ToString() ?? "missing"} value has no payload shape for {kind}."),
                };
            else if (field?.Kind is not (RecordFieldKind.Boolean or RecordFieldKind.Integer or RecordFieldKind.Reference))
                throw Refuse(RefusalCodes.RecordImportShapeUnsupported, change,
                    $"A {field?.Kind.ToString() ?? "missing"} value has no payload shape for {kind}.");
            _pending.Add(Operation(Id(role + ":" + kind), kind, target, after));
        }

        private void LowerAlternative(RecordChange change, string type, string key, CanonicalId target, string role, bool set)
        {
            var ws = change.WritingSystem ?? throw Refuse(RefusalCodes.RecordImportShapeUnsupported, change,
                "An alternative change names no writing system.");
            var verb = set ? RecordWriteVerb.SetAlternative : RecordWriteVerb.ClearAlternative;
            var kind = KindFor(change, type, key, verb);
            var binding = schema.FieldBinding(type, key)!.Value;
            if (change.Old is { } old && Current(change, target, binding, ws) is { } current &&
                !SameText(current.Text, old))
                throw Refuse(RefusalCodes.RecordImportValueStale, change, "The project's value is no longer the one the file started from.");
            _pending.Add(Operation(Id(role + ":" + kind), kind, target, set ? new { ws, text = change.New } : new { ws }));
        }

        private void LowerRich(RecordChange change, string type, string key,
            (string DeclaringClass, string Field, bool IsCustom) binding, CanonicalId target, string role, Guid owner,
            string? old, string? text, string? ws, bool fresh)
        {
            var field = schema.Schema.ObjectType(type)?.Field(key);
            if (field is null)
                throw Refuse(RefusalCodes.RecordImportFieldUncovered, change, $"The schema has no field '{key}' on {type}.");
            var tsOld = fresh ? null : Current(change, target, binding, ws);
            if (!fresh && old is not null && tsOld is { } held && !SameText(held.Text, PlainText(change, old, ws)))
                throw Refuse(RefusalCodes.RecordImportValueStale, change, "The project's value is no longer the one the file started from.");
            var runs = text is null ? [] : ParseRuns(change, text, ws ?? DefaultWs(field));
            var plain = runs.All(run => run.Ws == runs[0].Ws && run.CharacterStyle is null && run.ObjData is null &&
                run.Props is not { Ints.Count: > 0 } && run.Props is not { Strings.Count: > 0 });
            var verb = text is null ? RecordWriteVerb.ClearRich
                : plain && (fresh || tsOld is null || tsOld.Value.Plain) ? RecordWriteVerb.SetPlainRich : RecordWriteVerb.SetRich;
            var kind = verb == RecordWriteVerb.SetPlainRich &&
                catalog.Resolve(type, key, verb) is not WritableFieldResolution.Covered
                    ? KindFor(change, type, key, verb = RecordWriteVerb.SetRich) : KindFor(change, type, key, verb);
            if (kind == RichKind)
            {
                var name = binding.DeclaringClass + "." + binding.Field;
                if (!TextRichStringFields.Names.Contains(name))
                    throw Refuse(RefusalCodes.RecordImportShapeUnsupported, change, $"{name} is not an editable rich field.");
                var multi = TextRichStringFields.IsMultiString(name);
                var expected = fresh || tsOld is null ? new TextRichStringExpected("", TextRunsDigest.Of([])) :
                    new TextRichStringExpected(tsOld.Value.Text, tsOld.Value.Digest);
                var payload = new TextRichStringPayload(name, multi ? ws : null, runs, expected);
                _pending.Add(Operation(Id(role + ":" + kind), kind, target, payload.ToJson()));
            }
            else if (kind is "text/setParagraphs")
                throw Refuse(RefusalCodes.RecordImportShapeUnsupported, change,
                    "A field of paragraphs is replaced by paragraph keys and digests, which this lowering does not author.");
            else
            {
                var tag = ws ?? (runs.Count > 0 ? runs[0].Ws : DefaultWs(field));
                _pending.Add(Operation(Id(role + ":" + kind), kind, target,
                    text is null ? new { ws = tag } : new { ws = tag, text = string.Concat(runs.Select(run => run.Text)) }));
            }
        }

        private void LowerReference(RecordChange change, string type, string key,
            (string DeclaringClass, string Field, bool IsCustom) binding, CanonicalId target, string role)
        {
            var owner = change.ObjectGuid!.Value;
            var list = List(owner, key, binding);
            if (change.Kind == RecordChangeKind.RemoveReference)
            {
                var gone = change.OldTarget ?? throw Refuse(RefusalCodes.RecordImportShapeUnsupported, change, "The removed reference names no target.");
                if (list is not null && !list.Contains(gone))
                    throw Refuse(RefusalCodes.RecordImportValueStale, change, "The project no longer holds the reference the file removes.");
                var removeKind = KindFor(change, type, key, RecordWriteVerb.RemoveReference);
                _pending.Add(Operation(Id(role + ":" + removeKind), removeKind, target, new { member = CanonicalId.FromGuid(gone).Value }));
                list?.Remove(gone);
                return;
            }
            LowerMove(change, type, key, binding, target, role, change.Kind == RecordChangeKind.MoveReference
                ? RecordWriteVerb.MoveReference : RecordWriteVerb.AddReference);
        }

        private void LowerMove(RecordChange change, string type, string key,
            (string DeclaringClass, string Field, bool IsCustom) binding, CanonicalId target, string role, RecordWriteVerb verb)
        {
            var owner = change.ObjectGuid!.Value;
            var member = verb == RecordWriteVerb.MoveObject ? change.ItemGuid : change.NewTarget ?? change.OldTarget;
            if (member is null)
                throw Refuse(RefusalCodes.RecordImportShapeUnsupported, change, "The change names no member.");
            var list = List(owner, key, binding);
            // The project's list is the base the file started from; a changed list is stale.
            if (list is not null && verb == RecordWriteVerb.AddReference && list.Contains(member.Value))
                throw Refuse(RefusalCodes.RecordImportValueStale, change, "The project already holds the reference the file adds.");
            if (list is not null && verb != RecordWriteVerb.AddReference && !list.Contains(member.Value))
                throw Refuse(RefusalCodes.RecordImportValueStale, change, "The project no longer holds the member the file moves.");
            var placement = list is null ? null : Place(change, list, change.Anchor, verb == RecordWriteVerb.AddReference ? null : member);
            var effective = verb == RecordWriteVerb.AddReference && placement is not null ? RecordWriteVerb.InsertReference : verb;
            var kind = KindFor(change, type, key, effective, child: change.Subtree?.Class ?? ChildOf(type, key) ?? ClassOf(member.Value), placement);
            _pending.Add(Operation(Id(role + ":" + kind), kind, target, new { member = CanonicalId.FromGuid(member.Value).Value }, placement));
            if (list is not null)
            {
                list.Remove(member.Value);
                var at = placement?.After is { } after ? list.IndexOf(after.ToGuid()) + 1 : 0;
                list.Insert(Math.Max(0, at), member.Value);
            }
        }

        private void LowerCreate(int index, RecordChange change, string type, string key,
            (string DeclaringClass, string Field, bool IsCustom) binding, CanonicalId target, string role)
        {
            var subtree = change.Subtree ?? throw Refuse(RefusalCodes.RecordImportShapeUnsupported, change, "The added object is not in the change.");
            var child = subtree.Class ?? ChildOf(type, key) ?? throw Refuse(RefusalCodes.RecordImportShapeUnsupported, change,
                "The added object's class is not named.");
            var owner = change.ObjectGuid!.Value;
            var list = List(owner, key, binding);
            var placement = list is null ? null : Place(change, list, change.Anchor, null);
            var kind = KindFor(change, type, key, RecordWriteVerb.AddObject, child, placement);
            var write = OperationHandlerRegistry.FieldWrites(kind).FirstOrDefault(item => item.Verb == RecordWriteVerb.AddObject &&
                item.DeclaringClass == binding.DeclaringClass && item.Field == binding.Field &&
                (item.OwnerClass is null || item.OwnerClass == type));
            if (write is null || write.CreatePayload == OwningCreatePayload.Unspecified)
                throw Refuse(RefusalCodes.RecordImportShapeUnsupported, change,
                    $"{kind} takes its own payload, which this lowering does not author.");
            var entity = subtree.Guid is { } given ? CanonicalId.FromGuid(given) : Id(role + ":entity");
            object after = write.CreatePayload == OwningCreatePayload.ClassProperty ? new { @class = child } : new { };
            _pending.Add(Operation(Id(role + ":" + kind), kind, target, after, placement, entity));
            _added[index] = entity;
            list?.Insert(placement?.After is { } anchor ? list.IndexOf(anchor.ToGuid()) + 1 : 0, entity.ToGuid());
            foreach (var (name, value) in subtree.Fields)
                LowerContent(change, child, name, value, entity, role + ":" + name);
        }

        private void LowerContent(RecordChange change, string type, string key, RecordValue value, CanonicalId entity, string role)
        {
            if (value.IsEmpty)
                return;
            var field = schema.Schema.ObjectType(type)?.Field(key);
            var binding = schema.FieldBinding(type, key);
            if (field is null || binding is null)
                throw Refuse(RefusalCodes.RecordImportFieldUncovered, change, $"The added {type} has no field '{key}'.");
            switch (value)
            {
                case RecordAlternatives alternatives when field.Kind != RecordFieldKind.Rich:
                    foreach (var (ws, text) in alternatives.Items.Where(item => !item.Value.IsEmpty))
                        _pending.Add(Operation(Id(role + ":" + ws), KindFor(change, type, key, RecordWriteVerb.SetAlternative),
                            entity, new { ws, text = text.Text }));
                    break;
                case RecordAlternatives alternatives:
                    foreach (var (ws, text) in alternatives.Items.Where(item => !item.Value.IsEmpty))
                        LowerRich(change, type, key, binding.Value, entity, role + ":" + ws, entity.ToGuid(), null, text.Text, ws, fresh: true);
                    break;
                case RecordScalar scalar when field.Kind == RecordFieldKind.Rich && !field.MultiWritingSystem:
                    LowerRich(change, type, key, binding.Value, entity, role, entity.ToGuid(), null, scalar.Text, null, fresh: true);
                    break;
                default:
                    throw Refuse(RefusalCodes.RecordImportShapeUnsupported, change,
                        $"The added {type} has a value in '{key}' that no operation sets on a new object.");
            }
        }

        private void LowerDelete(RecordChange change, string type, string key, CanonicalId target, string role)
        {
            var child = change.Subtree?.Class ?? ChildOf(type, key);
            var kind = KindFor(change, type, key, RecordWriteVerb.RemoveObject, child);
            if (kind != LexSenseRemoveOperationKinds.Remove || change.ItemGuid is not { } item)
                throw Refuse(RefusalCodes.RecordImportShapeUnsupported, change,
                    $"{kind} takes a payload computed from the project, which this lowering authors only for senses.");
            try
            {
                var built = DeleteSenseComposer.Build(cache, new DeleteSenseIntent(CanonicalId.FromGuid(item), target));
                foreach (var operation in built)
                    _pending.Add(new OperationEnvelope(Id(role + ":" + kind), operation.Kind, target: operation.Target, after: operation.After));
            }
            catch (InvalidOperationException exception)
            {
                throw Refuse(RefusalCodes.RecordImportNotExecutable, change, exception.Message);
            }
        }

        private string? ClassOf(Guid item) =>
            cache.ServiceLocator.GetInstance<ICmObjectRepository>().TryGetObject(item, out var found) ? found.ClassName : null;

        private string? ChildOf(string type, string key)
        {
            var field = schema.Schema.ObjectType(type)?.Field(key);
            var types = (field?.Item ?? field)?.ObjectTypes;
            return types is { Count: 1 } ? types[0].Name : null;
        }

        private Placement? Place(RecordChange change, List<Guid> list, RecordAnchor? anchor, Guid? moving)
        {
            if (anchor is null)
                return null;
            var rest = list.Where(item => item != moving).ToList();
            int at;
            switch (anchor.Kind)
            {
                case RecordAnchorKind.Start: at = -1; break;
                case RecordAnchorKind.Object: at = rest.IndexOf(anchor.ItemGuid!.Value); break;
                case RecordAnchorKind.Item when anchor is { Target: { } to, Occurrence: <= 1 }: at = rest.IndexOf(to); break;
                case RecordAnchorKind.Added when _added.TryGetValue(anchor.ChangeIndex, out var added): at = rest.IndexOf(added.ToGuid()); break;
                case RecordAnchorKind.Added when _refused.Contains(anchor.ChangeIndex):
                    throw Refuse(RefusalCodes.RecordImportNotExecutable, change,
                        "The placement is anchored on a change that was refused, so the object it follows does not exist.");
                default:
                    throw Refuse(RefusalCodes.RecordImportShapeUnsupported, change, $"A placement {anchor} cannot be named by identity.");
            }
            if (anchor.Kind != RecordAnchorKind.Start && at < 0)
                throw Refuse(RefusalCodes.RecordImportNotExecutable, change, "The placement names an item the list does not hold.");
            if (rest.Count == 0)
                return null;
            CanonicalId? left = at >= 0 ? CanonicalId.FromGuid(rest[at]) : null;
            CanonicalId? right = at + 1 < rest.Count ? CanonicalId.FromGuid(rest[at + 1]) : null;
            return left is null && right is null ? null : new Placement(left, right);
        }

        private static bool IsVector(CellarPropertyType type) => type is CellarPropertyType.OwningCollection
            or CellarPropertyType.OwningSequence or CellarPropertyType.ReferenceCollection or CellarPropertyType.ReferenceSequence;

        private List<Guid>? List(Guid owner, string key, (string DeclaringClass, string Field, bool IsCustom) binding)
        {
            if (_lists.TryGetValue((owner, key), out var known))
                return known;
            var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
            if (!repository.TryGetObject(owner, out var instance))
                return null;
            var flid = cache.MetaDataCacheAccessor.GetFieldId(binding.DeclaringClass, binding.Field, true);
            var data = cache.DomainDataByFlid;
            var items = IsVector((CellarPropertyType)cache.MetaDataCacheAccessor.GetFieldType(flid))
                ? Enumerable.Range(0, data.get_VecSize(instance.Hvo, flid))
                    .Select(position => repository.GetObject(data.get_VecItem(instance.Hvo, flid, position)).Guid).ToList()
                : data.get_ObjectProp(instance.Hvo, flid) is var held and not 0 ? [repository.GetObject(held).Guid] : [];
            return _lists[(owner, key)] = items;
        }

        private (string Text, string Digest, bool Plain)? Current(RecordChange change, CanonicalId target,
            (string DeclaringClass, string Field, bool IsCustom) binding, string? ws)
        {
            if (!cache.ServiceLocator.GetInstance<ICmObjectRepository>().TryGetObject(target.ToGuid(), out var instance))
                throw Refuse(RefusalCodes.RecordImportNotExecutable, change, "The object is not in the project.");
            var flid = cache.MetaDataCacheAccessor.GetFieldId(binding.DeclaringClass, binding.Field, true);
            var handle = ws is null ? 0 : cache.WritingSystemFactory.GetWsFromStr(ws);
            var value = handle == 0 ? cache.DomainDataByFlid.get_StringProp(instance.Hvo, flid)
                : cache.DomainDataByFlid.get_MultiStringAlt(instance.Hvo, flid, handle);
            if (value is null || value.Length == 0)
                return ("", TextRunsDigest.Of([]), true);
            return (value.Text, Digest(value), IsPlain(value));
        }

        private bool IsPlain(ITsString value)
        {
            for (var run = 0; run < value.RunCount; run++)
            {
                var props = value.get_Properties(run);
                for (var i = 0; i < props.IntPropCount; i++)
                {
                    props.GetIntProp(i, out var property, out _);
                    if (property != (int)FwTextPropType.ktptWs)
                        return false;
                }
                if (props.StrPropCount > 0)
                    return false;
            }
            return true;
        }

        private string Digest(ITsString value) => TextRunsDigest.Of(Enumerable.Range(0, value.RunCount).Select(index =>
        {
            var props = value.get_Properties(index);
            var ints = new List<KeyValuePair<int, (int, int)>>();
            for (var i = 0; i < props.IntPropCount; i++)
            {
                var number = props.GetIntProp(i, out var property, out var variation);
                if (property != (int)FwTextPropType.ktptWs)
                    ints.Add(KeyValuePair.Create(property, (variation, number)));
            }
            var strings = new List<KeyValuePair<int, string>>();
            string? objData = null;
            for (var i = 0; i < props.StrPropCount; i++)
            {
                var text = props.GetStrProp(i, out var property) ?? "";
                if (property == (int)FwTextPropType.ktptObjData)
                    objData = text;
                else
                    strings.Add(KeyValuePair.Create(property, text));
            }
            var ws = props.GetIntPropValues((int)FwTextPropType.ktptWs, out _);
            return new TextRunsDigest.RunFacts(value.get_RunText(index) ?? "",
                ws > 0 ? cache.WritingSystemFactory.GetStrFromWs(ws) : "", ints, strings, objData);
        }));

        private static bool SameText(string held, string file) =>
            held.Normalize(NormalizationForm.FormC) == file.Normalize(NormalizationForm.FormC);

        private string DefaultWs(RecordField field) => field.WritingSystem == RecordWritingSystem.Vernacular
            ? markdown.WritingSystems.DefaultVernacular : markdown.WritingSystems.DefaultAnalysis;

        private string PlainText(RecordChange change, string text, string? ws) =>
            string.Concat(ParseRuns(change, text, ws ?? markdown.WritingSystems.DefaultAnalysis).Select(run => run.Text));

        private List<TextRun> ParseRuns(RecordChange change, string text, string defaultWs)
        {
            var options = new MarkdownOptions
            {
                WritingSystems = new Defaults(markdown.WritingSystems, defaultWs),
                StyleNames = markdown.StyleNames, Styles = markdown.Styles, NormalizeText = markdown.NormalizeText,
                Objects = markdown.Objects, Paths = markdown.Paths, WriteFrontMatter = false, LinkForm = markdown.LinkForm,
                LinkBase = markdown.LinkBase, ProjectName = markdown.ProjectName, SilfwDatabase = markdown.SilfwDatabase,
                UnknownGuids = markdown.UnknownGuids,
            };
            var result = MarkdownConverter.ParseInline(text, options);
            if (result.HasErrors)
                throw Refuse(RefusalCodes.RecordImportNotExecutable, change, "The value does not read as FieldWorks Markdown.");
            return result.Runs.Select(run => new TextRun(run.Text, run.Ws,
                run.Props.StrProps.TryGetValue(TextPropertyNumbers.NamedStyle, out var style) && style.Length > 0 ? style : null,
                new TextProps(run.Props.IntProps.ToDictionary(pair => pair.Key, pair => (pair.Value.Variation, pair.Value.Value)),
                    run.Props.StrProps.Where(pair => pair.Key != TextPropertyNumbers.NamedStyle).ToDictionary(pair => pair.Key, pair => pair.Value)),
                run.ObjData)).ToList();
        }

        private sealed class Defaults(IWritingSystemMapper inner, string analysis) : IWritingSystemMapper
        {
            public string DefaultAnalysis => analysis;
            public string DefaultVernacular => inner.DefaultVernacular;
            public bool IsKnown(string tag) => inner.IsKnown(tag);
        }
    }
}

/// <summary>
/// Which declared field edits the lowerer authors. The catalog answers <c>CanWrite</c>, <c>CanCreate</c> and
/// <c>CanDelete</c> from this, so no field is offered that the lowerer then declines. A declaration whose kind takes
/// a payload only its own composer builds is not lowered, and so is not offered.
/// </summary>
public static class RecordLoweringRules
{
    /// <summary>Whether the lowerer authors <paramref name="write"/> as declared by <paramref name="kind"/>.</summary>
    public static bool Lowers(string kind, FieldWriteCapability write) => write.Verb switch
    {
        RecordWriteVerb.Set or RecordWriteVerb.Clear or RecordWriteVerb.SetRich or RecordWriteVerb.ClearRich =>
            write.Payload == FieldWritePayload.Generic,
        RecordWriteVerb.SetAlternative or RecordWriteVerb.ClearAlternative or RecordWriteVerb.SetPlainRich or
            RecordWriteVerb.AddReference or RecordWriteVerb.InsertReference or RecordWriteVerb.RemoveReference or
            RecordWriteVerb.MoveReference or RecordWriteVerb.MoveObject => true,
        RecordWriteVerb.AddObject => write.CreatePayload != OwningCreatePayload.Unspecified,
        RecordWriteVerb.RemoveObject => write.Payload == FieldWritePayload.Composer,
        _ => false,
    };
}
