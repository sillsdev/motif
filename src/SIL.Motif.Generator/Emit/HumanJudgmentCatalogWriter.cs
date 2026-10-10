using SIL.Motif.Generator.Checks;
using SIL.Motif.Generator.Derivation;
using SIL.Motif.Generator.Descriptions;
using SIL.Motif.Generator.Model;

namespace SIL.Motif.Generator.Emit;

public static class HumanJudgmentCatalogWriter
{
    private static readonly (string Class, string Field)[] ModelFields =
    [
        ("RnGenericRec", "Title"),
        ("RnGenericRec", "Type"),
        ("StTxtPara", "Contents"),
    ];

    public static IReadOnlyList<GeneratedCatalogWriter.WrittenFile> WriteAll(LoadedMotifModel model, string repoRoot)
    {
        var rows = ModelFields.Select(key => model.Rows.Single(row =>
            row.DeclaringClass == key.Class && row.FieldName == key.Field)).ToArray();
        DescriptionCheck.CheckEmittedKinds(rows, KindDescriptionTsvParser.Parse(RepoPaths.DefaultDescriptionsPath()));

        var written = new List<GeneratedCatalogWriter.WrittenFile>();
        var typeRow = rows.Single(row => row.FieldName == "Type");
        if (typeRow.Manifest.Scope != "in" || typeRow.Kind != FieldKind.Rel || typeRow.Card != FieldCard.Atomic || typeRow.Sig != "CmPossibility")
            throw new GeneratorException("RnGenericRec.Type no longer matches the human-judgment reference policy.");
        var typeSpec = RelationFieldSpecBuilder.BuildAtomic(typeRow);
        Write("src/SIL.Motif.Runner/Operations/Generated6/RnGenericRecType.g.cs",
            ReferenceAtomicFieldEmitter.Render(typeSpec));
        Write("src/SIL.Motif.Runner/Snapshotting/Generated6/RnGenericRecRelationsSnapshotter.g.cs",
            RelationsSnapshotterEmitter.Render("RnGenericRec", "IRnGenericRec", "rnGenericRec", new[]
            {
                new RelationsSnapshotterEmitter.PopulationLine("RnGenericRec.Type (CmPossibility, rel/atomic)",
                    "AddIfPopulated(fields, SnapshotFields.RnGenericRecType, ReferenceFieldSnapshotting.ReadAlternatives(rnGenericRec.TypeRA));"),
            }));

        var stringSpecs = rows.Where(row => row.Kind == FieldKind.Basic)
            .Select(BasicFieldSpecBuilder.Build).ToArray();
        foreach (var spec in stringSpecs)
        {
            Write($"src/SIL.Motif.Runner/Operations/Generated6/{spec.SnapshotFieldConstant}.g.cs",
                StringFieldEmitter.Render(spec));
        }
        foreach (var group in stringSpecs.GroupBy(spec => spec.DeclaringClass, StringComparer.Ordinal))
        {
            // An ambiguous judgment field throws here, pinned by `AnAmbiguousJudgmentFieldRefusesTheSnapshot`.
            var extra = group.Key == "RnGenericRec"
                ? new[] { "        AddIfPopulated(fields, SnapshotFields.RnGenericRecMotifHumanJudgment, " +
                          "HumanJudgmentSnapshotting.ReadRecord(cache, rnGenericRec));" }
                : Array.Empty<string>();
            Write($"src/SIL.Motif.Runner/Snapshotting/Generated6/{group.Key}Snapshotter.g.cs",
                StringFieldEmitter.RenderSnapshotter(group.Key, group.ToArray(), extra));
        }

        var constants = stringSpecs.Select(spec => new Slice3SnapshotFieldsEmitter.ConstantEntry(
                spec.DeclaringClass, spec.FieldName, spec.Group, spec.Construct, spec.SnapshotFieldConstant,
                $"basic {spec.Sig}"))
            .Append(new Slice3SnapshotFieldsEmitter.ConstantEntry(
                "RnGenericRec", "Type", typeSpec.Group, typeSpec.Construct, "RnGenericRecType", "rel/atomic CmPossibility"))
            .Append(new Slice3SnapshotFieldsEmitter.ConstantEntry(
                "RnGenericRec", "MotifHumanJudgment", "system", "rnGenericRec",
                "RnGenericRecMotifHumanJudgment", "reserved custom String"))
            .ToArray();
        Write("src/SIL.Motif.Model/Snapshot/SnapshotFields.Generated6.g.cs",
            Slice3SnapshotFieldsEmitter.Render(constants));
        return written;

        void Write(string relative, string content)
        {
            var path = Path.Combine(repoRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content.Replace("\r\n", "\n"));
            written.Add(new(relative));
        }
    }
}
