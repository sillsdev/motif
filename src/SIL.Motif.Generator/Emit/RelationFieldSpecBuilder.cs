using SIL.Motif.Generator.Derivation;
using SIL.Motif.Generator.Join;
using SIL.Motif.Generator.Model;

namespace SIL.Motif.Generator.Emit;

/// <summary>
/// Assembles a <see cref="ReferenceAtomicFieldSpec"/>/<see cref="ReferenceCollectionFieldSpec"/> from
/// one <see cref="JoinedRow"/>, calling the same <see cref="GroupDerivation"/>/
/// <see cref="ConstructDerivation"/>/<see cref="KindNameDerivation"/> <see cref="BasicFieldSpecBuilder"/>
/// calls for slice 1 — glue, not a second place that decides group/construct/kind.
/// </summary>
public static class RelationFieldSpecBuilder
{
    /// <summary>The possibility list FieldWorks's chooser offers for a field, as <c>LexDb</c> owns it.</summary>
    private static readonly Dictionary<FieldKey, string> PossibilityLists = new()
    {
        // FieldWorks LexSenseParts.xml:457-462, possAtomicReference for SenseType, chooses from LexDb.SenseTypesOA.
        [new FieldKey("LexSense", "SenseType")] = "c => c.LangProject.LexDbOA.SenseTypesOA",
        // FieldWorks LexSenseParts.xml:509-514, possAtomicReference for Status, chooses from LangProject.StatusOA.
        [new FieldKey("LexSense", "Status")] = "c => c.LangProject.StatusOA",
        // FieldWorks LexSenseParts.xml:444-451, UsageTypes chooses from LexDb.UsageTypesOA.
        [new FieldKey("LexSense", "UsageTypes")] = "c => c.LangProject.LexDbOA.UsageTypesOA",
        // FieldWorks LexSenseParts.xml:465-472, DialectLabels chooses from LexDb.DialectLabelsOA.
        [new FieldKey("LexSense", "DialectLabels")] = "c => c.LangProject.LexDbOA.DialectLabelsOA",
        // FieldWorks LexSenseParts.xml:473-480, DomainTypes chooses from LexDb.DomainTypesOA.
        [new FieldKey("LexSense", "DomainTypes")] = "c => c.LangProject.LexDbOA.DomainTypesOA",
        // FieldWorks LexSenseParts.xml:481-489, SemanticDomains chooses from LangProject.SemanticDomainListOA.
        [new FieldKey("LexSense", "SemanticDomains")] = "c => c.LangProject.SemanticDomainListOA",
        // FieldWorks LexSenseParts.xml:499-507, AnthroCodes chooses from LangProject.AnthroListOA.
        [new FieldKey("LexSense", "AnthroCodes")] = "c => c.LangProject.AnthroListOA",
    };

    public static ReferenceAtomicFieldSpec BuildAtomic(JoinedRow row)
    {
        var group = GroupDerivation.Derive(row.DeclaringClass);
        var construct = ConstructDerivation.Derive(row.DeclaringClass);

        return new ReferenceAtomicFieldSpec(
            DeclaringClass: row.DeclaringClass,
            FieldName: row.FieldName,
            Sig: row.Sig,
            Group: group,
            Construct: construct,
            SetKind: KindNameDerivation.DeriveOne(group, construct, "set", row.FieldName),
            ClearKind: KindNameDerivation.DeriveOne(group, construct, "clear", row.FieldName),
            TargetInterface: "I" + row.DeclaringClass,
            RefInterface: "I" + row.Sig,
            // LibLCM's own accessor-property suffix convention (SIL.LCModel's IMoForm): "RA" for a reference atomic.
            AccessorPropertyName: row.FieldName + "RA",
            SnapshotFieldConstant: row.DeclaringClass + row.FieldName,
            PossibilityListExpression: PossibilityLists.GetValueOrDefault(row.Key));
    }

    public static ReferenceCollectionFieldSpec BuildCollection(JoinedRow row, bool supportsPlacement = false)
    {
        var group = GroupDerivation.Derive(row.DeclaringClass);
        var construct = ConstructDerivation.Derive(row.DeclaringClass);

        if (row.Card is not { } card)
        {
            throw new GeneratorException(
                $"{row.DeclaringClass}.{row.FieldName}: a rel/col or rel/seq field must carry a Card.");
        }
        if (supportsPlacement && card != FieldCard.Seq)
        {
            throw new GeneratorException(
                $"{row.DeclaringClass}.{row.FieldName}: identity-relative placement requires rel/seq.");
        }

        // LibLCM's accessor suffixes (SIL.LCModel's ILexEntry): RC reference collection, RS reference sequence.
        var accessorSuffix = card == FieldCard.Seq ? "RS" : "RC";

        return new ReferenceCollectionFieldSpec(
            DeclaringClass: row.DeclaringClass,
            FieldName: row.FieldName,
            Sig: row.Sig,
            Card: card,
            Group: group,
            Construct: construct,
            AddRefKind: KindNameDerivation.DeriveOne(group, construct, "addRef", row.FieldName),
            RemoveRefKind: KindNameDerivation.DeriveOne(group, construct, "removeRef", row.FieldName),
            TargetInterface: "I" + row.DeclaringClass,
            RefInterface: "I" + row.Sig,
            AccessorPropertyName: row.FieldName + accessorSuffix,
            SnapshotFieldConstant: row.DeclaringClass + row.FieldName,
            MoveKind: supportsPlacement ? KindNameDerivation.DeriveOne(group, construct, "move", row.FieldName) : null,
            SupportsPlacement: supportsPlacement,
            PossibilityListExpression: PossibilityLists.GetValueOrDefault(row.Key));
    }

    public static IReadOnlyList<ReferenceAtomicFieldSpec> BuildAllAtomic(IReadOnlyList<JoinedRow> rows) =>
        rows.Select(BuildAtomic).ToList();

    public static IReadOnlyList<ReferenceCollectionFieldSpec> BuildAllCollection(
        IReadOnlyList<JoinedRow> rows,
        IReadOnlySet<FieldKey>? placementFields = null) =>
        rows.Select(row => BuildCollection(row, placementFields?.Contains(row.Key) == true)).ToList();
}
