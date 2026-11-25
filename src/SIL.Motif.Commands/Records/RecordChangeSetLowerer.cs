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
/// <param name="OperationChanges">For each operation, the index in the plan's changes of the change it came from; null when not tracked.</param>
/// <param name="RefusedChanges">The refusal code of each plan change, by its index, that has no operation; null when not tracked.</param>
public sealed record RecordLowering(IReadOnlyList<OperationEnvelope> Operations,
    IReadOnlyList<RecordLoweringRefusal> Refusals, IReadOnlyList<RecordRefusal> PlanRefusals,
    IReadOnlyList<int>? OperationChanges = null, IReadOnlyDictionary<int, string>? RefusedChanges = null);

/// <summary>
/// Lowers a planned record-file import to operations through the kinds the Runner catalog declares for each field
/// (<see cref="RecordWritableFields"/>). It reads the project and writes nothing. A change that no kind expresses, or whose
/// project value is no longer the one the file started from, is refused by code, never dropped and never approximated.
/// A whole-file refusal in the plan lowers nothing.
/// </summary>
public sealed partial class RecordChangeSetLowerer(ProjectRecordSchemaResult schema, RecordWritableFields catalog)
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
        return new(lowered.Operations, lowered.Refusals, set.Refusals, lowered.OperationChanges, lowered.RefusedChanges);
    }

    /// <summary>Lowers individual changes; <paramref name="seed"/> makes the operation ids, so one seed and one list give one intent digest.</summary>
    public RecordLowering LowerChanges(LcmCache cache, string seed, IReadOnlyList<RecordChange> changes)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(changes);
        var run = new Run(schema, catalog, cache, seed, new ProjectRecordExporter(cache, schema).CreateReadOptions().Markdown) { Changes = changes };
        for (var index = 0; index < changes.Count; index++)
            run.Lower(index, changes[index]);
        return new(run.Operations, run.Refusals, [], run.OperationChanges, run.RefusedCodes);
    }

    private sealed class Stop(RecordLoweringRefusal refusal) : Exception(refusal.Reason)
    {
        internal RecordLoweringRefusal Refusal { get; } = refusal;
    }

    private sealed partial class Run(ProjectRecordSchemaResult schema, RecordWritableFields catalog, LcmCache cache,
        string seed, MarkdownOptions markdown)
    {
        private readonly string _seed = seed;
        private readonly Dictionary<int, CanonicalId> _added = [];
        private readonly HashSet<int> _refused = [];
        private readonly Dictionary<(Guid Owner, string Field), List<Guid>> _lists = [];
        private List<OperationEnvelope> _pending = [];

        internal List<OperationEnvelope> Operations { get; } = [];
        internal List<int> OperationChanges { get; } = [];
        internal Dictionary<int, string> RefusedCodes { get; } = [];
        internal List<RecordLoweringRefusal> Refusals { get; } = [];

        internal void Lower(int index, RecordChange change)
        {
            _pending = [];
            _siblingRefusals.Clear();
            // A declined change leaves no trace in the caches, so nothing anchors on an object that never existed.
            var added = new Dictionary<int, CanonicalId>(_added);
            var lists = _lists.ToDictionary(pair => pair.Key, pair => pair.Value.ToList());
            try
            {
                LowerChange(index, change);
                Operations.AddRange(_pending);
                OperationChanges.AddRange(_pending.Select(operation => _attributed.TryGetValue(operation.OperationId, out var owner) ? owner : index));
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
                RefusedCodes[index] = stop.Refusal.Code;
                Refusals.Add(stop.Refusal);
                foreach (var (sibling, refusal) in _siblingRefusals)
                {
                    _refused.Add(sibling);
                    RefusedCodes[sibling] = refusal.Code;
                    Refusals.Add(refusal);
                }
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

        /// <summary>Adds the field name that a custom-field kind needs.</summary>
        private object? Named(string type, string key, object? after)
        {
            if (schema.FieldBinding(type, key) is not { IsCustom: true } binding || after is null)
                return after;
            var json = after is JsonElement element ? element : JsonSerializer.SerializeToElement(after);
            var fields = new Dictionary<string, JsonElement> { ["field"] = JsonSerializer.SerializeToElement(binding.Field) };
            foreach (var property in json.EnumerateObject())
                fields[property.Name] = property.Value;
            return JsonSerializer.SerializeToElement(fields);
        }

        private static OperationEnvelope Operation(CanonicalId id, string kind, CanonicalId target, object? after,
            Placement? placement = null, CanonicalId? entity = null) => new(id, kind, entityId: entity, target: target,
            after: after is null ? null : after is JsonElement json ? json : JsonSerializer.SerializeToElement(after), placement: placement);

        private void LowerScalar(RecordChange change, string type, string key, CanonicalId target, string role)
        {
            var field = schema.Schema.ObjectType(type)?.Field(key);
            if (field is not null && RecordWritableFields.IsFeatureStructure(field))
            {
                LowerFeatureStructure(change, type, key, target, role);
                return;
            }
            if (field is not null && RecordWritableFields.IsRuleFormula(field))
            {
                if (schema.FieldBinding(type, key)!.Value.DeclaringClass == "MoAffixProcess")
                    LowerAffixFormula(change, type, key);
                else
                    LowerRuleFormula(change, type);
                return;
            }
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
            _pending.Add(Operation(Id(role + ":" + kind), kind, target, Named(type, key, after)));
        }

        private void LowerFeatureStructure(RecordChange change, string type, string key, CanonicalId target, string role)
        {
            var binding = schema.FieldBinding(type, key)!.Value;
            var set = change.Kind == RecordChangeKind.Set;
            var kind = KindFor(change, type, key, set ? RecordWriteVerb.Set : RecordWriteVerb.Clear,
                RecordWritableFields.FeatureStructureClass);
            var owner = cache.ServiceLocator.GetObject(change.ObjectGuid!.Value);
            var flid = cache.MetaDataCacheAccessor.GetFieldId(binding.DeclaringClass, binding.Field, true);
            var heldHvo = cache.DomainDataByFlid.get_ObjectProp(owner.Hvo, flid);
            var held = heldHvo == 0 ? null : (IFsFeatStruc)cache.ServiceLocator.GetObject(heldHvo);
            string? current;
            try
            {
                current = held is null ? null : FeatureStructureNotation.Render(held, cache);
            }
            catch (FeatureStructureNotationException exception)
            {
                throw Refuse(exception.Code, change, exception.Message);
            }
            if (!string.Equals(current, change.Old, StringComparison.Ordinal))
                throw Refuse(RefusalCodes.RecordImportValueStale, change, "The project's value is no longer the one the file started from.");
            if (!set)
            {
                if (held is null)
                    throw Refuse(RefusalCodes.RecordImportValueStale, change, "The project holds no feature structure to clear.");
                if (kind != MoStemMsaMsFeaturesOperationKinds.DeleteMsFeatures)
                    throw Refuse(RefusalCodes.RecordImportFieldUncovered, change,
                        "No operation deletes this feature structure; write [] to leave it with no features.");
                _pending.Add(Operation(Id(role + ":" + kind), kind, target, new { }));
                return;
            }
            IReadOnlyList<FeatureStructureSelection> wanted;
            try
            {
                var system = cache.LangProject.MsFeatureSystemOA ?? throw Refuse(RefusalCodes.RecordImportNotExecutable, change,
                    "The project has no morphosyntactic feature system.");
                wanted = FeatureStructureNotation.Parse(change.New ?? "", system, cache);
            }
            catch (FeatureStructureNotationException exception)
            {
                throw Refuse(exception.Code, change, exception.Message);
            }
            CanonicalId structure;
            if (held is null)
            {
                structure = Id(role + ":structure");
                _pending.Add(Operation(Id(role + ":" + kind), kind, target, new { }, entity: structure));
            }
            else
                structure = CanonicalId.FromGuid(held.Guid);
            var existing = held?.FeatureSpecsOC.OfType<IFsClosedValue>().Where(spec => spec.FeatureRA is not null)
                .ToDictionary(spec => spec.FeatureRA.Guid) ?? [];
            var keep = wanted.Select(selection => selection.Feature.Guid).ToHashSet();
            foreach (var spec in existing.Values.Where(spec => !keep.Contains(spec.FeatureRA.Guid)).OrderBy(spec => spec.Guid))
                _pending.Add(Operation(Id(role + ":delete:" + spec.Guid.ToString("N")), FsFeatStrucFeatureSpecsOperationKinds.DeleteFeatureSpecs,
                    CanonicalId.FromGuid(spec.Guid), new { }));
            foreach (var selection in wanted)
            {
                var feature = selection.Feature.Guid.ToString("N");
                if (existing.TryGetValue(selection.Feature.Guid, out var spec))
                {
                    if (spec.ValueRA?.Guid != selection.Value.Guid)
                        _pending.Add(Operation(Id(role + ":value:" + feature), FsClosedValueValueOperationKinds.SetValue,
                            CanonicalId.FromGuid(spec.Guid), new { @ref = CanonicalId.FromGuid(selection.Value.Guid).Value }));
                    continue;
                }
                var created = Id(role + ":spec:" + feature);
                _pending.Add(Operation(Id(role + ":create:" + feature), FsFeatStrucFeatureSpecsOperationKinds.CreateFeatureSpecs,
                    structure, new { }, entity: created));
                _pending.Add(Operation(Id(role + ":feature:" + feature), FsFeatureSpecificationFeatureOperationKinds.SetFeature,
                    created, new { @ref = CanonicalId.FromGuid(selection.Feature.Guid).Value }));
                _pending.Add(Operation(Id(role + ":value:" + feature), FsClosedValueValueOperationKinds.SetValue,
                    created, new { @ref = CanonicalId.FromGuid(selection.Value.Guid).Value }));
            }
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
            _pending.Add(Operation(Id(role + ":" + kind), kind, target,
                Named(type, key, set ? new { ws, text = change.New } : new { ws })));
        }

        private void LowerRich(RecordChange change, string type, string key,
            (string DeclaringClass, string Field, bool IsCustom) binding, CanonicalId target, string role, Guid owner,
            string? old, string? text, string? ws, bool fresh)
        {
            var field = schema.Schema.ObjectType(type)?.Field(key);
            if (field is null)
                throw Refuse(RefusalCodes.RecordImportFieldUncovered, change, $"The schema has no field '{key}' on {type}.");
            if (field.Paragraphs)
            {
                LowerParagraphs(change, type, key, binding, target, role, owner, text);
                return;
            }
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
            else
            {
                var tag = ws ?? (runs.Count > 0 ? runs[0].Ws : DefaultWs(field));
                _pending.Add(Operation(Id(role + ":" + kind), kind, target, Named(type, key,
                    text is null ? new { ws = tag } : new { ws = tag, text = string.Concat(runs.Select(run => run.Text)) })));
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
                _pending.Add(Operation(Id(role + ":" + removeKind), removeKind, target,
                    Named(type, key, new { member = CanonicalId.FromGuid(gone).Value })));
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
            var memberClass = change.Subtree?.Class ?? ChildOf(type, key) ?? ClassOf(member.Value);
            var appended = verb == RecordWriteVerb.AddReference && AppendsToTheEnd(type, key, memberClass, placement);
            if (appended)
                placement = null;
            var effective = verb == RecordWriteVerb.AddReference && placement is not null ? RecordWriteVerb.InsertReference : verb;
            var kind = KindFor(change, type, key, effective, child: memberClass, placement);
            _pending.Add(Operation(Id(role + ":" + kind), kind, target,
                Named(type, key, new { member = CanonicalId.FromGuid(member.Value).Value }), placement));
            if (list is not null)
            {
                list.Remove(member.Value);
                var at = appended ? list.Count : placement?.After is { } after ? list.IndexOf(after.ToGuid()) + 1 : 0;
                list.Insert(Math.Max(0, at), member.Value);
            }
        }

        /// <summary>Appends after the last member (FieldWorks VectorReferenceLauncher.cs:287, ADR 0003).</summary>
        private bool AppendsToTheEnd(string type, string key, string? member, Placement? placement)
        {
            if (placement is not { After: not null, Before: null } ||
                catalog.Resolve(type, key, RecordWriteVerb.InsertReference, member, placement) is WritableFieldResolution.Covered ||
                catalog.Resolve(type, key, RecordWriteVerb.AddReference, member) is not WritableFieldResolution.Covered covered ||
                schema.FieldBinding(type, key) is not { } binding)
                return false;
            return OperationHandlerRegistry.FieldWrites(covered.Kind).Any(write => write.Verb == RecordWriteVerb.AddReference &&
                write.DeclaringClass == binding.DeclaringClass && write.Field == binding.Field &&
                write.Placement == FieldWritePlacement.Forbidden);
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
                            entity, Named(type, key, new { ws, text = text.Text })));
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
            if (rest.Count == 0 && moving is not null)
                throw Refuse(RefusalCodes.RecordImportNotExecutable, change, "The list holds no other member to place the moved item against.");
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
            if (handle == 0 && (CellarPropertyType)cache.MetaDataCacheAccessor.GetFieldType(flid) is not (CellarPropertyType.String))
                throw Refuse(RefusalCodes.RecordImportShapeUnsupported, change, "The field holds text per writing system and the change names none.");
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

        // The file has no paragraph keys, so paragraphs pair with the stored ones by position.
        private void LowerParagraphs(RecordChange change, string type, string key,
            (string DeclaringClass, string Field, bool IsCustom) binding, CanonicalId target, string role, Guid owner, string? text)
        {
            var verb = text is null ? RecordWriteVerb.ClearRich : RecordWriteVerb.SetRich;
            var kind = KindFor(change, type, key, verb);
            var flid = cache.MetaDataCacheAccessor.GetFieldId(binding.DeclaringClass, binding.Field, true);
            var data = cache.DomainDataByFlid;
            var hvo = data.get_ObjectProp(cache.ServiceLocator.GetObject(owner).Hvo, flid);
            var held = hvo == 0 ? [] : cache.ServiceLocator.GetObject(hvo) is IStText stText
                ? stText.ParagraphsOS.OfType<IStTxtPara>().ToArray() : [];
            var field = schema.Schema.ObjectType(type)!.Field(key)!;
            var oldParagraphs = change.Old is null ? [] : ParseParagraphs(change, change.Old, DefaultWs(field));
            if (oldParagraphs.Count != held.Length || oldParagraphs.Zip(held).Any(pair =>
                    !SameText(held: pair.Second.Contents?.Text ?? "", file: pair.First.Text)))
                throw Refuse(RefusalCodes.RecordImportValueStale, change, "The project's value is no longer the one the file started from.");
            var order = new List<string?>();
            var changed = new List<TextParagraphChange>();
            var removed = new List<string>();
            var wanted = new HashSet<string>(StringComparer.Ordinal);
            string KeyOf(int index) => CanonicalId.FromGuid(held[index].Guid).Value;
            if (text is null)
            {
                if (held.Length > 0)
                {
                    order.Add(KeyOf(0));
                    changed.Add(new TextParagraphChange(0, KeyOf(0), held[0].StyleName is { Length: > 0 } style ? style : null, [], null));
                    wanted.Add(KeyOf(0));
                }
                for (var i = 1; i < held.Length; i++) { removed.Add(KeyOf(i)); wanted.Add(KeyOf(i)); }
            }
            else
            {
                var read = ParseParagraphs(change, text, DefaultWs(field));
                var pairs = GdrMarkdownImport.Match(oldParagraphs.Count, read.Count,
                    (o, n) => MarkdownConverter.AreEquivalent(oldParagraphs[o], read[n]));
                var keyFor = new string?[read.Count];
                var unchanged = new bool[read.Count];
                foreach (var (o, n) in pairs) { keyFor[n] = KeyOf(o); unchanged[n] = true; }
                int previousOld = -1, previousNew = -1;
                foreach (var (o, n) in pairs.Append((oldParagraphs.Count, read.Count)))
                {
                    var oldGap = Enumerable.Range(previousOld + 1, o - previousOld - 1).ToArray();
                    var newGap = Enumerable.Range(previousNew + 1, n - previousNew - 1).ToArray();
                    for (var i = 0; i < Math.Max(oldGap.Length, newGap.Length); i++)
                    {
                        if (i < oldGap.Length && i < newGap.Length) keyFor[newGap[i]] = KeyOf(oldGap[i]);
                        else if (i < oldGap.Length) removed.Add(KeyOf(oldGap[i]));
                    }
                    previousOld = o;
                    previousNew = n;
                }
                order.AddRange(keyFor);
                for (var n = 0; n < read.Count; n++)
                    if (!unchanged[n])
                        changed.Add(new TextParagraphChange(n, keyFor[n], read[n].Style.Length > 0 ? read[n].Style : null,
                            GdrMarkdownImport.Runs(read[n]), GdrMarkdownImport.ParagraphProps(read[n])));
                if (changed.Count == 0 && removed.Count == 0)
                    return;
                foreach (var name in changed.Select(item => item.Key).Concat(removed).OfType<string>()) wanted.Add(name);
            }
            var expected = new Dictionary<string, TextParagraphExpected>(StringComparer.Ordinal);
            for (var i = 0; i < held.Length; i++)
                if (wanted.Contains(KeyOf(i)))
                    expected[KeyOf(i)] = new TextParagraphExpected(held[i].Contents?.Text ?? "", held[i].StyleName ?? "",
                        held[i].Contents is { Length: > 0 } contents ? Digest(contents) : TextRunsDigest.Of([]));
            var payload = new TextParagraphsPayload((binding.IsCustom ? TextParagraphFields.CustomPrefix : "") + binding.DeclaringClass + "." + binding.Field, order, changed, removed, expected);
            _pending.Add(Operation(Id(role + ":" + kind), kind, target, payload.ToJson()));
        }

        private List<Paragraph> ParseParagraphs(RecordChange change, string text, string defaultWs)
        {
            var options = new MarkdownOptions
            {
                WritingSystems = new Defaults(markdown.WritingSystems, defaultWs),
                StyleNames = markdown.StyleNames, Styles = markdown.Styles, NormalizeText = markdown.NormalizeText,
                Objects = markdown.Objects, Paths = markdown.Paths, WriteFrontMatter = false, LinkForm = markdown.LinkForm,
                LinkBase = markdown.LinkBase, ProjectName = markdown.ProjectName, SilfwDatabase = markdown.SilfwDatabase,
                UnknownGuids = markdown.UnknownGuids,
            };
            var result = MarkdownConverter.Parse(text, options);
            if (result.HasErrors)
                throw Refuse(RefusalCodes.RecordImportNotExecutable, change, "The value does not read as FieldWorks Markdown.");
            return result.Document.Paragraphs.ToList();
        }

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
        RecordWriteVerb.SetRich or RecordWriteVerb.ClearRich when kind == TextParagraphsOperationKinds.Set => true,
        RecordWriteVerb.Set or RecordWriteVerb.Clear or RecordWriteVerb.SetRich or RecordWriteVerb.ClearRich =>
            write.Payload == FieldWritePayload.Generic,
        RecordWriteVerb.SetAlternative or RecordWriteVerb.ClearAlternative or RecordWriteVerb.SetPlainRich or
            RecordWriteVerb.AddReference or RecordWriteVerb.InsertReference or RecordWriteVerb.RemoveReference or
            RecordWriteVerb.MoveReference or RecordWriteVerb.MoveObject => true,
        RecordWriteVerb.AddObject => write.CreatePayload != OwningCreatePayload.Unspecified,
        RecordWriteVerb.RemoveObject => write.Payload == FieldWritePayload.Composer,
        _ => false,
    };

    /// <summary>
    /// The reasons a declaration is covered by a kind and still not offered, keyed by the code <see cref="NotLowered"/> returns.
    /// </summary>
    public static IReadOnlyDictionary<string, string> NotLoweredReasons { get; } = new Dictionary<string, string>
    {
        ["bespoke-payload"] = "the kind's payload carries more than the verb and field (digests, paragraph keys, a census), so only its own composer authors it",
        ["create-composer"] = "the create kind takes its own payload (morph type, reference, forms), which only its composer authors",
        ["delete-composer"] = "the delete takes a payload only its own kind builds (a child id or none, by kind), and only the sense delete is authored",
    };

    /// <summary>The <see cref="NotLoweredReasons"/> code for a declaration the lowerer does not author, or null when it does or no code explains it.</summary>
    public static string? NotLowered(string kind, FieldWriteCapability write) => Lowers(kind, write) ? null : write.Verb switch
    {
        RecordWriteVerb.AddObject => "create-composer",
        RecordWriteVerb.RemoveObject => "delete-composer",
        _ when write.Payload == FieldWritePayload.Bespoke => "bespoke-payload",
        _ => null,
    };

    /// <summary>
    /// Why a reference-set field of Lexicon Edit has an uncovered row: an insert at a position, or a reorder, has no kind,
    /// and a field whose add and remove have no kind is not built.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ExclusionReasons { get; } = new Dictionary<string, string>
    {
        ["append-only-kind"] = "the add kind appends to the end of the sequence and takes no placement, so an insert elsewhere is refused",
        ["no-kind-built"] = "no operation kind writes this edit of the field",
    };
}
