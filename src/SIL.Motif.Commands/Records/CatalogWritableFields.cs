using System.Collections.Frozen;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Operations;
using SIL.TextEditing.Project;

namespace SIL.Motif.Commands.Records;

/// <summary>
/// What the record importer may plan, read from the Runner's registered field-write declarations, which are the
/// generated catalog the coverage report is made from. Names are FieldWorks metadata names: the class that declares
/// the field, and the LibLCM field name. A custom field is writable when the schema passed to the constructor has it and a kind writes its type.
/// </summary>
/// <remarks>
/// <see cref="CanWrite"/> is true when some kind sets, clears, adds to, removes from or reorders the field; it does not
/// know the owner's concrete class, so a field narrowed to one owner class is writable here and the lowerer declines the
/// others by name. <see cref="CanCreate"/> and <see cref="CanDelete"/> are the owning create and delete kinds.
/// Only declarations the lowerer authors count (<see cref="RecordLoweringRules"/>), so a true answer is a promise that the
/// lowerer produces an operation for it; a kind with a payload only its own composer builds is false here.
/// A field the field inventory classifies as not user-editable is never writable, as in the coverage report.
/// </remarks>
public sealed class CatalogWritableFields : WritableFields
{
    private static readonly FrozenSet<RecordWriteVerb> WriteVerbs = new[]
    {
        RecordWriteVerb.Set, RecordWriteVerb.Clear, RecordWriteVerb.SetAlternative, RecordWriteVerb.ClearAlternative,
        RecordWriteVerb.SetPlainRich, RecordWriteVerb.SetRich, RecordWriteVerb.ClearRich, RecordWriteVerb.MoveObject,
        RecordWriteVerb.AddReference, RecordWriteVerb.InsertReference, RecordWriteVerb.RemoveReference,
        RecordWriteVerb.MoveReference, RecordWriteVerb.AddItem, RecordWriteVerb.RemoveItem, RecordWriteVerb.MoveItem,
    }.ToFrozenSet();

    private readonly FrozenSet<(string Class, string Field)> _writable;
    private readonly ILookup<(string Class, string Field), FieldWriteCapability> _creates;
    private readonly ILookup<(string Class, string Field), FieldWriteCapability> _deletes;

    /// <summary>Reads every registered declaration; <paramref name="schema"/> supplies the project's custom fields.</summary>
    public CatalogWritableFields(ProjectRecordSchemaResult schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        OperationRegistryBootstrap.Initialize();
        var all = OperationHandlerRegistry.RegisteredKinds.Where(OperationKindRegistry.IsKnown)
            .SelectMany(kind => OperationHandlerRegistry.FieldWrites(kind).Where(write => RecordLoweringRules.Lowers(kind, write)))
            .Where(write => !RecordFieldAuthorability.IsReadOnly(
                RecordFieldAuthorability.Classification(write.DeclaringClass, write.Field)))
            .ToArray();
        var customTypes = all.Where(write => write.DeclaringClass == CustomFieldOperationKinds.DeclaringClass &&
            WriteVerbs.Contains(write.Verb)).Select(write => write.Field).ToHashSet(StringComparer.Ordinal);
        var customs = schema.Schema.ObjectTypes.SelectMany(type => type.Fields.Select(field =>
                (Field: field, Binding: schema.FieldBinding(type.Name, field.Name))))
            .Where(item => item.Binding is { IsCustom: true } && !item.Field.Computed &&
                CustomFieldCatalog.TypeOf(item.Field) is { } kind && customTypes.Contains(kind))
            .Select(item => (item.Binding!.Value.DeclaringClass, item.Binding.Value.Field));
        var structures = schema.Schema.ObjectTypes.SelectMany(type => type.Fields.Where(RecordWritableFields.IsFeatureStructure)
                .Select(field => schema.FieldBinding(type.Name, field.Name)))
            .Where(binding => binding is { IsCustom: false }).Select(binding => (binding!.Value.DeclaringClass, binding.Value.Field))
            .Where(slot => OperationHandlerRegistry.RegisteredKinds.SelectMany(OperationHandlerRegistry.FieldWrites).Any(write =>
                write.Verb == RecordWriteVerb.AddObject && write.DeclaringClass == slot.DeclaringClass && write.Field == slot.Field &&
                write.ChildClasses.Contains(RecordWritableFields.FeatureStructureClass)));
        var formulas = schema.Schema.ObjectTypes.SelectMany(type => type.Fields.Where(RecordWritableFields.IsRuleFormula)
                .Select(field => schema.FieldBinding(type.Name, field.Name)))
            .Where(binding => binding is { IsCustom: false }).Select(binding => (binding!.Value.DeclaringClass, binding.Value.Field))
            .Where(slot => all.Any(write => write.Verb == RecordWriteVerb.AddObject && write.DeclaringClass == slot.DeclaringClass &&
                write.Field == slot.Field));
        _writable = all.Where(write => WriteVerbs.Contains(write.Verb))
            .Select(write => (write.DeclaringClass, write.Field)).Concat(customs).Concat(structures).Concat(formulas)
            .Where(slot => !RecordWritableFields.IsFormulaOwned(slot.DeclaringClass, slot.Field)).ToFrozenSet();
        _creates = all.Where(write => write.Verb == RecordWriteVerb.AddObject)
            .ToLookup(write => (write.DeclaringClass, write.Field));
        _deletes = all.Where(write => write.Verb == RecordWriteVerb.RemoveObject)
            .ToLookup(write => (write.DeclaringClass, write.Field));
    }

    /// <inheritdoc />
    public override bool CanWrite(string className, string fieldName) => _writable.Contains((className, fieldName));

    /// <inheritdoc />
    public override bool CanCreate(string ownerClassName, string ownerFieldName, string className) =>
        Allows(_creates, ownerClassName, ownerFieldName, className);

    /// <inheritdoc />
    public override bool CanDelete(string ownerClassName, string ownerFieldName, string className) =>
        Allows(_deletes, ownerClassName, ownerFieldName, className);

    private static bool Allows(ILookup<(string Class, string Field), FieldWriteCapability> kinds, string owner, string field,
        string child) => !RecordWritableFields.IsTypedOnlyMembership(owner, field) && kinds[(owner, field)].Any(write =>
        write.ChildClasses.Count == 0 || write.ChildClasses.Contains(child, StringComparer.Ordinal));
}
