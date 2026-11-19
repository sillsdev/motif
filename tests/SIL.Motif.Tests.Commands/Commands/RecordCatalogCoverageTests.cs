using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Records;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Generator;
using SIL.Motif.Generator.Tsv;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Project;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group0)]
public sealed class RecordCatalogCoverageTests(ITestOutputHelper output) : IDisposable
{
    private const string GoldenName = "record-catalog-coverage.tsv";
    private readonly string _root = Directory.CreateTempSubdirectory("motif-record-catalog-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void GoldenMatchesGeneratedSchemasAndExecutableCatalog()
    {
        using var cache = NewLangProjFixture.CreateCache(_root);
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        metadata.AddCustomField("LexEntry", "ExampleNumber", CellarPropertyType.Integer, 0);
        metadata.AddCustomField("LexSense", "ExampleNote", CellarPropertyType.MultiString, 0);
        metadata.AddCustomField("LexEntry", "ExampleLabel", CellarPropertyType.MultiUnicode, 0);
        metadata.AddCustomField("LexEntry", "ExampleTag", CellarPropertyType.String, 0);
        var possibility = metadata.GetClassId("CmPossibility");
        metadata.AddCustomField("LexEntry", "ExampleStatus", CellarPropertyType.ReferenceAtomic, possibility);
        metadata.AddCustomField("LexEntry", "ExampleSet", CellarPropertyType.ReferenceCollection, possibility);
        metadata.AddCustomField("LexEntry", "ExampleFlag", CellarPropertyType.Boolean, 0);
        metadata.AddCustomField("LexEntry", "ExampleDate", CellarPropertyType.GenDate, 0);
        var schemas = new[]
        {
            ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay),
            ProjectRecordSchema.Generate(cache, ProjectRecordSchema.NotebookRecordOverlay),
            ProjectRecordSchema.Generate(cache, ProjectRecordSchema.ListItemOverlay),
        };

        var schemaFields = schemas.SelectMany(schema => ReachableFields(schema.Schema)).ToHashSet();
        var lexiconEditFields = LexiconEditFields();
        var rows = schemas.SelectMany(schema => Rows(schema, lexiconEditFields)).Distinct().ToArray();
        var actual = Render(rows);
        using var golden = new StreamReader(typeof(RecordCatalogCoverageTests).Assembly
            .GetManifestResourceStream(GoldenName)!);
        var expected = golden.ReadToEnd().ReplaceLineEndings("\n");
        if (expected != actual)
        {
            var candidate = Path.Combine(Directory.CreateTempSubdirectory("motif-record-catalog-golden-").FullName, GoldenName);
            File.WriteAllText(candidate, actual);
            output.WriteLine($"Generated report saved to {candidate}");
        }

        var goldenRows = expected.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.StartsWith('#')).Select(line => line.Split('\t')).ToArray();
        var goldenFields = goldenRows.Select(columns => (columns[1], columns[2])).ToHashSet();
        Assert.Empty(schemaFields.Except(goldenFields).Order());
        Assert.Empty(goldenFields.Except(schemaFields).Order());
        Assert.Equal(rows.Length, rows.Select(row => (row.Class, row.Field, row.Verb)).Distinct().Count());
        Assert.Equal(expected, actual);
    }

    private static IEnumerable<(string Class, string Field)> ReachableFields(RecordSchema schema)
    {
        var reachable = schema.Kinds.SelectMany(kind => kind.Roots).ToHashSet();
        while (true)
        {
            var before = reachable.Count;
            reachable.UnionWith(reachable.SelectMany(type => type.Fields)
                .SelectMany(field => (field.Item ?? field).ObjectTypes).ToArray());
            if (reachable.Count == before)
                return reachable.SelectMany(type => type.Fields.Select(field => (type.Name, field.Name)));
        }
    }

    private static IEnumerable<Row> Rows(ProjectRecordSchemaResult schema,
        IReadOnlySet<(string Class, string Field)> lexiconEditFields)
    {
        var report = new RecordWritableFields(schema);
        var types = schema.Schema.ObjectTypes.ToDictionary(type => type.Name, StringComparer.Ordinal);
        foreach (var ((className, key, verb), result) in report.Entries)
        {
            var field = types[className].Field(key)!;
            var binding = schema.FieldBinding(className, key)!.Value;
            var classification = RecordFieldAuthorability.Classification(binding.DeclaringClass, binding.Field);
            var group = Group(className, field, binding.IsCustom, classification,
                lexiconEditFields.Contains((binding.DeclaringClass, binding.Field)), result);
            var modelField = binding.DeclaringClass + "." + binding.Field;
            yield return result switch
            {
                WritableFieldResolution.Covered covered => new(group, className, key, modelField, verb, covered.Kind, "-", "-"),
                WritableFieldResolution.Uncovered { Reason: UncoveredFieldReason.NotUserEditable } refused =>
                    new(group, className, key, modelField, verb, "none", "not-user-editable", refused.ManifestClassification!),
                WritableFieldResolution.Uncovered uncovered =>
                    new(group, className, key, modelField, verb, "none", uncovered.Reason.ToString(), "-"),
                _ => throw new InvalidOperationException($"Unexpected result for {className}.{key}"),
            };
        }
    }

    private static ReportGroup Group(string className, RecordField field, bool custom, string? classification,
        bool inLexiconEdit, WritableFieldResolution result)
    {
        if (result is WritableFieldResolution.Covered)
            return ReportGroup.Covered;
        if (result is WritableFieldResolution.Uncovered { Reason: UncoveredFieldReason.NotUserEditable })
            return ReportGroup.NotUserEditable;
        if (field.Computed)
            return ReportGroup.ComputedFields;
        if (custom)
            return ReportGroup.CustomFields;
        if (classification is not ("semantic-operation" or "supporting-detail"))
            return ReportGroup.OtherFields;
        if (className is "LexEntry" or "LexSense" && !field.IsList && !field.Paragraphs &&
            field.Kind is not (RecordFieldKind.Object or RecordFieldKind.Reference))
            return ReportGroup.LexSenseAndLexEntryScalars;
        if (inLexiconEdit && field.Item?.Kind == RecordFieldKind.Reference)
            return ReportGroup.LexiconEditReferenceSets;
        return ReportGroup.OtherFields;
    }

    private static HashSet<(string, string)> LexiconEditFields()
    {
        var path = Path.Combine(RepoPaths.FindRepoRoot(), "manifest", "fieldworks-labels.tsv");
        return QuotedTsv.Read(path, ["Class", "Field", "Label", "Tooltip", "Source", "SourceDetail", "Confidence"])
            .Where(row => row[4] == "slice" && row[5].Split(" | ").Any(source =>
                (source.StartsWith("LexEntryParts.xml, bin class=", StringComparison.Ordinal) ||
                 source.StartsWith("LexSenseParts.xml, bin class=", StringComparison.Ordinal)) &&
                source.Contains("-Detail-", StringComparison.Ordinal)))
            .Select(row => (row[0], row[1])).ToHashSet();
    }

    private static string Render(IEnumerable<Row> rows)
    {
        var header = new[]
        {
            "# Runner catalog coverage for record files",
            "# Generated in RecordCatalogCoverageTests from the lexicon entry, notebook record and list item schemas.",
            "# The Example* fields are project-local custom fields: one of each type a kind writes, a Boolean no kind writes, and a date the schema omits.",
            "# One row per reachable concrete schema class, record field key and verb.",
            "# Covered rows are subject to the placement, owner, child and shape in the Runner declarations.",
            "# Non-authorable fields cite their classification in manifest/liblcm-inventory.tsv.",
            "# LexiconEditReferenceSets require a Lexicon Edit detail slice in manifest/fieldworks-labels.tsv.",
            "# Group\tClass\tField key\tLibLCM field\tVerb\tRunner kind\tUncovered reason\tManifest classification",
        };
        var body = rows.OrderBy(row => row.Group).ThenBy(row => row.Class, StringComparer.Ordinal)
            .ThenBy(row => row.Field, StringComparer.Ordinal).ThenBy(row => row.Verb)
            .Select(row => $"{row.Group}\t{row.Class}\t{row.Field}\t{row.ModelField}\t{row.Verb}\t{row.Kind}\t{row.Reason}\t{row.Classification}");
        return string.Join('\n', header.Concat(body).Concat(CatalogDeclarations())) + "\n";
    }

    private static IEnumerable<string> CatalogDeclarations()
    {
        OperationRegistryBootstrap.Initialize();
        var handlers = OperationHandlerRegistry.RegisteredKinds.Order(StringComparer.Ordinal).ToArray();
        var parserKinds = OperationKindRegistry.KnownKinds.ToHashSet(StringComparer.Ordinal);
        var parserOnly = parserKinds.Except(handlers, StringComparer.Ordinal).Order(StringComparer.Ordinal);
        Assert.Equal(new[] { "lexical/entry/create", "sequence/move" }, parserOnly);
        Assert.Empty(handlers.Except(parserKinds, StringComparer.Ordinal));

        yield return $"# Parser-only kinds: {string.Join(", ", parserOnly)}";
        yield return "# Runner declarations\tKind\tDeclaring class\tField\tVerb\tPlacement\tOwner restriction\tChild classes\tShape\tFallback";
        foreach (var kind in handlers)
        {
            var declarations = OperationHandlerRegistry.FieldWrites(kind);
            if (declarations.Count == 0)
                yield return $"# Handler\t{kind}\t(no direct field edit)";
            foreach (var row in declarations.Select(Declaration).Order(StringComparer.Ordinal))
                yield return $"# Handler\t{kind}\t{row}";
        }
    }

    private static string Declaration(FieldWriteCapability write) =>
        $"{write.DeclaringClass}\t{write.Field}\t{write.Verb}\t{write.Placement}\t{write.OwnerClass ?? "-"}\t" +
        $"{string.Join(",", write.ChildClasses.Order(StringComparer.Ordinal))}\t{write.Shape}\t{write.Fallback}";

    private enum ReportGroup
    {
        Covered,
        CustomFields,
        LexSenseAndLexEntryScalars,
        LexiconEditReferenceSets,
        OtherFields,
        NotUserEditable,
        ComputedFields,
    }

    private sealed record Row(ReportGroup Group, string Class, string Field, string ModelField,
        RecordWriteVerb Verb, string Kind, string Reason, string Classification);
}
