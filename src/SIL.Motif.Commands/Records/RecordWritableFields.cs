using System.Collections.Frozen;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Operations;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Project;

namespace SIL.Motif.Commands.Records;

/// <summary>Joins one generated project record schema to the executable operation catalog.</summary>
public sealed class RecordWritableFields : IWritableFields
{
    private readonly FrozenSet<(string Class, string Field)> _fields;
    private readonly FrozenDictionary<(string Class, string Field, RecordWriteVerb Verb), FieldEntry> _entries;

    /// <summary>
    /// Catalog results for every field and expressible verb on types reachable from a record root.
    /// Computed fields appear with refusals so the report accounts for their keys too.
    /// </summary>
    public IReadOnlyDictionary<(string Class, string Field, RecordWriteVerb Verb), WritableFieldResolution> Entries { get; }

    public RecordWritableFields(ProjectRecordSchemaResult schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        OperationRegistryBootstrap.Initialize();
        var catalog = OperationHandlerRegistry.RegisteredKinds.Where(OperationKindRegistry.IsKnown)
            .SelectMany(kind => OperationHandlerRegistry.FieldWrites(kind).Select(write => new Candidate(kind, write)))
            .ToLookup(candidate => (candidate.Write.DeclaringClass, candidate.Write.Field, candidate.Write.Verb));
        var entries = new Dictionary<(string, string, RecordWriteVerb), FieldEntry>();
        var fields = new HashSet<(string, string)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<RecordObjectType>(schema.Schema.Kinds.SelectMany(kind => kind.Roots));
        while (pending.TryDequeue(out var type))
        {
            if (!seen.Add(type.Name))
                continue;

            foreach (var field in type.Fields)
            {
                var binding = schema.FieldBinding(type.Name, field.Name) ??
                    throw new InvalidOperationException($"No model binding for record field '{type.Name}.{field.Name}'.");
                fields.Add((type.Name, field.Name));
                var refusal = FieldRefusal(field, binding);
                var owned = IsFormulaOwned(binding.DeclaringClass, binding.Field);
                var parts = IsRuleParts(type, field, schema) || owned;
                foreach (var verb in Verbs(field))
                {
                    var structure = IsFeatureStructure(field) || IsRuleFormula(field);
                    var handlerVerb = structure ? (verb == RecordWriteVerb.Set ? RecordWriteVerb.AddObject : RecordWriteVerb.RemoveObject)
                        : verb == RecordWriteVerb.InsertReference ? RecordWriteVerb.AddReference : verb;
                    var candidates = (IsTypedOnlyMembership(binding.DeclaringClass, binding.Field) ? Array.Empty<Candidate>() : (binding.IsCustom
                        ? catalog[(CustomFieldOperationKinds.DeclaringClass, CustomFieldCatalog.TypeOf(field) ?? "", handlerVerb)]
                        : parts || (IsRuleFormula(field) && verb == RecordWriteVerb.Clear) ? [] : catalog[(binding.DeclaringClass, binding.Field, handlerVerb)])).ToArray();
                    var formula = owned && verb is RecordWriteVerb.Set or RecordWriteVerb.AddReference
                        ? catalog[(binding.DeclaringClass, binding.Field, handlerVerb)].ToArray() : [];
                    entries.Add((type.Name, field.Name, verb), new FieldEntry(type.Name, field, structure ? handlerVerb : verb, candidates, refusal,
                        IsFeatureStructure(field) ? [FeatureStructureClass] : IsRuleFormula(field) ? RuleFormulaChildren(binding.Field) : null,
                        formula, binding.DeclaringClass.StartsWith("PhSimpleContext", StringComparison.Ordinal) || binding.DeclaringClass == "PhSequenceContext" ? "input" : "output"));
                }
                foreach (var child in (field.Item ?? field).ObjectTypes)
                    pending.Enqueue(child);
            }
        }

        _entries = entries.ToFrozenDictionary();
        Entries = entries.ToFrozenDictionary(entry => entry.Key, entry => entry.Value.Summarize());
        _fields = fields.ToFrozenSet();
    }

    public WritableFieldResolution Resolve(string className, string field, RecordWriteVerb verb,
        string? childClass = null, Placement? placement = null, FieldWriteShape shape = FieldWriteShape.Unspecified)
    {
        if (_entries.TryGetValue((className, field, verb), out var entry))
            return entry.Resolve(childClass, placement, shape);
        return new WritableFieldResolution.Uncovered(_fields.Contains((className, field))
            ? UncoveredFieldReason.VerbNotExpressible : UncoveredFieldReason.UnknownField);
    }

    /// <summary>The class of the owned structure a feature-structure notation field writes.</summary>
    internal const string FeatureStructureClass = "FsFeatStruc";

    /// <summary>Whether the field is a feature structure written as text; its set and clear are the create and delete of the owned structure.</summary>
    internal static bool IsFeatureStructure(RecordField field) =>
        field is { Kind: RecordFieldKind.Notation, Notation: SIL.TextEditing.Project.FeatureStructureNotation.Name };

    /// <summary>Whether the field is rule-formula text; its set and clear are the creates and deletes of the owned objects it describes.</summary>
    internal static bool IsRuleFormula(RecordField field) =>
        field is { Kind: RecordFieldKind.Notation, Notation: SIL.TextEditing.Project.RuleNotation.Name };

    /// <summary>A list of owned objects the file shows but never adds to or removes from; the typed composers do (a rule's right-hand sides).</summary>
    internal static bool IsTypedOnlyMembership(string declaringClass, string field) => (declaringClass, field) is ("PhRegularRule", "RightHandSides");

    // The parts key reaches only a member's own fields.
    internal static bool IsRuleParts(RecordObjectType type, RecordField field, ProjectRecordSchemaResult schema) =>
        field is { Kind: RecordFieldKind.Sequence } && type.Fields.Any(other => IsRuleFormula(other) &&
            schema.FieldBinding(type.Name, other.Name) == schema.FieldBinding(type.Name, field.Name));

    /// <summary>A member field the formula text already says, so only the formula writes it and the parts key shows it read-only.</summary>
    internal static bool IsFormulaOwned(string declaringClass, string field) => (declaringClass, field) is
        ("MoCopyFromInput", "Content") or ("MoModifyFromInput", "Content") or ("MoModifyFromInput", "Modification") or
        ("MoInsertPhones", "Content") or ("MoInsertNC", "Content") or ("PhSequenceContext", "Members") or
        ("PhSimpleContextSeg", "FeatureStructure") or ("PhSimpleContextNC", "FeatureStructure") or ("PhSimpleContextBdry", "FeatureStructure");

    private static string[] RuleFormulaChildren(string field) => field == "Input"
        ? ["PhVariable", "PhSimpleContextSeg", "PhSimpleContextNC", "PhSimpleContextBdry", "PhSequenceContext"]
        : ["MoCopyFromInput", "MoInsertPhones", "MoInsertNC", "MoModifyFromInput"];

    private static RecordWriteVerb[] Verbs(RecordField field) => (field.Kind, field.Item?.Kind) switch
    {
        (RecordFieldKind.Rich, _) when field.Paragraphs => [RecordWriteVerb.SetRich, RecordWriteVerb.ClearRich],
        (RecordFieldKind.Rich, _) => [RecordWriteVerb.SetPlainRich, RecordWriteVerb.SetRich, RecordWriteVerb.ClearRich],
        (RecordFieldKind.Object, _) or (RecordFieldKind.Collection, RecordFieldKind.Object) =>
            [RecordWriteVerb.AddObject, RecordWriteVerb.RemoveObject],
        (RecordFieldKind.Sequence, RecordFieldKind.Object) =>
            [RecordWriteVerb.AddObject, RecordWriteVerb.RemoveObject, RecordWriteVerb.MoveObject],
        (RecordFieldKind.Collection, RecordFieldKind.Reference) =>
            [RecordWriteVerb.AddReference, RecordWriteVerb.RemoveReference],
        (RecordFieldKind.Sequence, RecordFieldKind.Reference) =>
            [RecordWriteVerb.AddReference, RecordWriteVerb.InsertReference, RecordWriteVerb.RemoveReference, RecordWriteVerb.MoveReference],
        (RecordFieldKind.Collection, _) => [RecordWriteVerb.AddItem, RecordWriteVerb.RemoveItem],
        (RecordFieldKind.Sequence, _) => [RecordWriteVerb.AddItem, RecordWriteVerb.RemoveItem, RecordWriteVerb.MoveItem],
        _ when field.MultiWritingSystem => [RecordWriteVerb.SetAlternative, RecordWriteVerb.ClearAlternative],
        _ => [RecordWriteVerb.Set, RecordWriteVerb.Clear],
    };

    private static WritableFieldResolution.Uncovered? FieldRefusal(RecordField field,
        (string DeclaringClass, string Field, bool IsCustom) binding)
    {
        var classification = RecordFieldAuthorability.Classification(binding.DeclaringClass, binding.Field);
        if (!binding.IsCustom && RecordFieldAuthorability.IsReadOnly(classification))
            return new(UncoveredFieldReason.NotUserEditable, classification);
        if (field.Computed)
            return new(UncoveredFieldReason.ComputedField);
        if (binding.IsCustom && CustomFieldCatalog.TypeOf(field) is null)
            return new(field.Kind == RecordFieldKind.Rich && field.Paragraphs ? UncoveredFieldReason.CustomStText : UncoveredFieldReason.CustomField);
        return null;
    }

    private sealed record Candidate(string Kind, FieldWriteCapability Write);

    private sealed class FieldEntry
    {
        private readonly string _owner;
        private readonly RecordWriteVerb _verb;
        private readonly string[] _children;
        private readonly Candidate[] _candidates;
        private readonly WritableFieldResolution.Uncovered? _refusal;
        private readonly Candidate[] _formulaCandidates;
        private readonly string _formulaKey;

        internal FieldEntry(string owner, RecordField field, RecordWriteVerb verb, Candidate[] candidates,
            WritableFieldResolution.Uncovered? refusal, string[]? children, Candidate[] formulaCandidates, string formulaKey)
        {
            _formulaCandidates = formulaCandidates;
            _formulaKey = formulaKey;
            _owner = owner;
            _verb = verb;
            _children = children ?? (field.Item ?? field).ObjectTypes.Select(type => type.Name).ToArray();
            _candidates = candidates;
            _refusal = refusal;
        }

        internal WritableFieldResolution Summarize()
        {
            if (_refusal is not null)
                return _refusal;

            var placed = _verb is RecordWriteVerb.MoveObject or RecordWriteVerb.MoveReference or RecordWriteVerb.InsertReference;
            if (_formulaCandidates.Length > 0 && Select(null, placed, FieldWriteShape.Unspecified, inventory: true, _formulaCandidates) is WritableFieldResolution.Covered formula)
                return formula with { Owner = _formulaKey };
            return Select(null, placed, FieldWriteShape.Unspecified, inventory: true, _candidates);
        }

        internal WritableFieldResolution Resolve(string? child, Placement? placement, FieldWriteShape shape)
        {
            if (_refusal is not null)
                return _refusal;

            return Select(child, placement is not null, shape, inventory: false, _candidates);
        }

        private WritableFieldResolution Select(string? child, bool hasPlacement, FieldWriteShape shape, bool inventory, Candidate[] candidates)
        {
            var owned = _verb is RecordWriteVerb.AddObject or RecordWriteVerb.RemoveObject or RecordWriteVerb.MoveObject;
            if (candidates.Length == 0)
                return new WritableFieldResolution.Uncovered(UncoveredFieldReason.NoOperationKind);

            var eligible = candidates.Where(candidate => candidate.Write.OwnerClass is null ||
                candidate.Write.OwnerClass == _owner).ToArray();
            if (eligible.Length == 0)
                return new WritableFieldResolution.Uncovered(UncoveredFieldReason.UnsupportedOwnerClass);

            if (owned)
            {
                if (!inventory && child is null)
                    return new WritableFieldResolution.Uncovered(UncoveredFieldReason.MissingChildClass);
                eligible = eligible.Where(candidate => child is null
                    ? _children.Any(allowed => AllowsChild(candidate, allowed))
                    : _children.Contains(child, StringComparer.Ordinal) && AllowsChild(candidate, child)).ToArray();
                if (eligible.Length == 0)
                    return new WritableFieldResolution.Uncovered(UncoveredFieldReason.UnsupportedChildClass);
            }

            if (_verb == RecordWriteVerb.InsertReference && !hasPlacement)
                return new WritableFieldResolution.Uncovered(UncoveredFieldReason.MissingPlacement);
            eligible = eligible.Where(candidate => hasPlacement
                ? candidate.Write.Placement != FieldWritePlacement.Forbidden
                : candidate.Write.Placement != FieldWritePlacement.Required).ToArray();
            if (eligible.Length == 0)
                return new WritableFieldResolution.Uncovered(hasPlacement
                    ? UncoveredFieldReason.UnsupportedPlacement : UncoveredFieldReason.MissingPlacement);

            if (!inventory)
            {
                eligible = eligible.Where(candidate => candidate.Write.Shape == shape).ToArray();
                if (eligible.Length == 0)
                    return new WritableFieldResolution.Uncovered(UncoveredFieldReason.UnsupportedShape);
            }

            var preferFallback = eligible.All(candidate => candidate.Write.Fallback);
            var kinds = eligible.Where(candidate => candidate.Write.Fallback == preferFallback)
                .Select(candidate => candidate.Kind).Distinct(StringComparer.Ordinal).ToArray();
            return kinds.Length == 1
                ? new WritableFieldResolution.Covered(kinds[0])
                : new WritableFieldResolution.Uncovered(UncoveredFieldReason.AmbiguousOperationKind);
        }

        private static bool AllowsChild(Candidate candidate, string child) =>
            candidate.Write.ChildClasses.Count == 0 || candidate.Write.ChildClasses.Contains(child, StringComparer.Ordinal);
    }
}

/// <summary>Which LibLCM property type a custom field of the record schema has, as the custom-field kinds declare it.</summary>
internal static class CustomFieldCatalog
{
    /// <summary>The declared type name, or null for a type no kind writes (booleans, GUIDs, owned objects, reference sequences, single Unicode).</summary>
    internal static string? TypeOf(RecordField field) => (field.Kind, field.Item?.Kind, field.IsList, field.MultiWritingSystem) switch
    {
        (RecordFieldKind.Text, _, false, true) => "MultiUnicode",
        (RecordFieldKind.Rich, _, false, true) when !field.Paragraphs => "MultiString",
        (RecordFieldKind.Rich, _, false, false) when !field.Paragraphs => "String",
        (RecordFieldKind.Integer, _, false, _) => "Integer",
        (RecordFieldKind.Reference, _, false, _) => "ReferenceAtomic",
        (RecordFieldKind.Collection, RecordFieldKind.Reference, _, _) => "ReferenceCollection",
        _ => null,
    };
}
