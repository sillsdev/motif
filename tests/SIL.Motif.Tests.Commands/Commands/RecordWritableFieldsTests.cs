using SIL.Motif.Commands.Records;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.TextEditing.Project;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group0)]
public sealed class RecordWritableFieldsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("motif-record-writable-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void LookupUsesSchemaKeysAndOnlyReturnsExecutableKinds()
    {
        using var cache = NewLangProjFixture.CreateCache(_root);
        var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
        IWritableFields fields = new RecordWritableFields(schema);

        Assert.Equal(new WritableFieldResolution.Covered("lexical/lexSense/setGloss"),
            fields.Resolve("LexSense", "gloss", RecordWriteVerb.SetAlternative));
        Assert.Equal(new WritableFieldResolution.Covered("text/setRichString"),
            fields.Resolve("LexSense", "source", RecordWriteVerb.SetRich));
        Assert.Equal(new WritableFieldResolution.Covered("text/setRichString"),
            fields.Resolve("LexSense", "definition", RecordWriteVerb.ClearRich));
        Assert.Equal(new WritableFieldResolution.Covered("lexical/lexEntry/createAlternateForms"),
            fields.Resolve("LexEntry", "allomorphs", RecordWriteVerb.AddObject, "MoStemAllomorph"));
        Assert.Equal(new WritableFieldResolution.Covered("lexical/lexSense/remove"),
            fields.Resolve("LexSense", "senses", RecordWriteVerb.RemoveObject, "LexSense"));
        Assert.Equal(new WritableFieldResolution.Covered("lexical/lexEntryRef/addRefComponentLexemes"),
            fields.Resolve("LexEntryRef", "component-lexemes", RecordWriteVerb.AddReference));
        Assert.Equal(new WritableFieldResolution.Uncovered(UncoveredFieldReason.UnsupportedPlacement),
            fields.Resolve("LexEntryRef", "component-lexemes", RecordWriteVerb.InsertReference, placement: BeforeNeighbor));
        Assert.Equal(new WritableFieldResolution.Covered("grammar/moAffixAllomorph/addRefPosition"),
            fields.Resolve("MoAffixAllomorph", "position", RecordWriteVerb.InsertReference, placement: BeforeNeighbor));
        Assert.Equal(new WritableFieldResolution.Uncovered(UncoveredFieldReason.NoOperationKind),
            fields.Resolve("LexEntry", "senses", RecordWriteVerb.MoveObject, "LexSense", BeforeNeighbor));
        Assert.Equal(new WritableFieldResolution.Uncovered(UncoveredFieldReason.ComputedField),
            fields.Resolve("LexEntry", "head-word", RecordWriteVerb.SetRich));
        Assert.Equal(new WritableFieldResolution.Uncovered(UncoveredFieldReason.UnknownField),
            fields.Resolve("LexEntry", "AlternateForms", RecordWriteVerb.AddObject));
        Assert.Equal(new WritableFieldResolution.Uncovered(UncoveredFieldReason.VerbNotExpressible),
            fields.Resolve("LexSense", "gloss", RecordWriteVerb.MoveObject));
    }

    [Fact]
    public void LookupPreservesInheritedAndRichFieldRestrictions()
    {
        using var cache = NewLangProjFixture.CreateCache(_root);
        var listSchema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.ListItemOverlay);
        IWritableFields listFields = new RecordWritableFields(listSchema);
        var notebookSchema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.NotebookRecordOverlay);
        IWritableFields notebookFields = new RecordWritableFields(notebookSchema);

        Assert.Equal(new WritableFieldResolution.Covered("lists/cmPossibility/setName"),
            listFields.Resolve("CmSemanticDomain", "name", RecordWriteVerb.SetAlternative));
        Assert.Equal(new WritableFieldResolution.Uncovered(UncoveredFieldReason.UnsupportedOwnerClass),
            listFields.Resolve("CmSemanticDomain", "sub-possibilities", RecordWriteVerb.AddObject, "PartOfSpeech"));
        Assert.Equal(new WritableFieldResolution.Covered("lists/cmPossibility/createSubPossibilities"),
            listFields.Resolve("PartOfSpeech", "sub-possibilities", RecordWriteVerb.AddObject, "PartOfSpeech"));
        Assert.Equal(new WritableFieldResolution.Covered("system/rnGenericRec/setTitle"),
            notebookFields.Resolve("RnGenericRec", "title", RecordWriteVerb.SetPlainRich));
        Assert.Equal(new WritableFieldResolution.Uncovered(UncoveredFieldReason.NoOperationKind),
            notebookFields.Resolve("RnGenericRec", "title", RecordWriteVerb.SetRich));
        Assert.Equal(new WritableFieldResolution.Covered("text/setParagraphs"),
            notebookFields.Resolve("RnGenericRec", "description", RecordWriteVerb.SetRich));
    }

    [Fact]
    public void LookupIdentifiesRestrictedVariantCreatesAndRefusesNonAuthorableFields()
    {
        using var cache = NewLangProjFixture.CreateCache(_root);
        var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
        IWritableFields fields = new RecordWritableFields(schema);

        Assert.Equal(new WritableFieldResolution.Covered("lexical/lexEntry/createVariantRef"),
            fields.Resolve("LexEntry", "entry-refs", RecordWriteVerb.AddObject, "LexEntryRef",
                shape: FieldWriteShape.VariantReference));
        Assert.Equal(new WritableFieldResolution.Uncovered(UncoveredFieldReason.UnsupportedShape),
            fields.Resolve("LexEntry", "entry-refs", RecordWriteVerb.AddObject, "LexEntryRef"));
        Assert.Equal(new WritableFieldResolution.Uncovered(UncoveredFieldReason.UnsupportedPlacement),
            fields.Resolve("LexEntry", "entry-refs", RecordWriteVerb.AddObject, "LexEntryRef", BeforeNeighbor,
                FieldWriteShape.VariantReference));
        Assert.Equal(new WritableFieldResolution.Uncovered(UncoveredFieldReason.NotUserEditable, "derived-read-only"),
            fields.Resolve("LexEntry", "homograph-number", RecordWriteVerb.Set));
        Assert.Equal(new WritableFieldResolution.Uncovered(UncoveredFieldReason.NotUserEditable, "derived-read-only"),
            fields.Resolve("LexEntry", "main-entries-or-senses", RecordWriteVerb.AddReference));
        Assert.Equal(new WritableFieldResolution.Uncovered(UncoveredFieldReason.NotUserEditable, "internal"),
            fields.Resolve("LexSense", "import-residue", RecordWriteVerb.SetRich));
        Assert.Equal(new WritableFieldResolution.Uncovered(UncoveredFieldReason.NotUserEditable, "internal"),
            fields.Resolve("LexEntry", "lift-residue", RecordWriteVerb.Set));
    }

    [Fact]
    public void LookupRefusesOwnedEditsOutsideTheHandlersChildAndPlacementSupport()
    {
        using var cache = NewLangProjFixture.CreateCache(_root);
        IWritableFields lexicon = new RecordWritableFields(
            ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay));
        IWritableFields lists = new RecordWritableFields(
            ProjectRecordSchema.Generate(cache, ProjectRecordSchema.ListItemOverlay));

        Assert.Equal(new WritableFieldResolution.Covered("lexical/lexEntry/createLexemeForm"),
            lexicon.Resolve("LexEntry", "lexeme-form", RecordWriteVerb.AddObject, "MoAffixProcess"));
        Assert.Equal(new WritableFieldResolution.Uncovered(UncoveredFieldReason.UnsupportedChildClass),
            lexicon.Resolve("LexEntry", "lexeme-form", RecordWriteVerb.RemoveObject, "MoAffixProcess"));
        Assert.Equal(new WritableFieldResolution.Covered("lexical/lexEntry/deleteLexemeForm"),
            lexicon.Resolve("LexEntry", "lexeme-form", RecordWriteVerb.RemoveObject, "MoAffixAllomorph"));
        Assert.Equal(new WritableFieldResolution.Uncovered(UncoveredFieldReason.MissingChildClass),
            lexicon.Resolve("LexEntry", "lexeme-form", RecordWriteVerb.RemoveObject));
        Assert.Equal(new WritableFieldResolution.Covered("lexical/lexEntry/createSenses"),
            lexicon.Resolve("LexEntry", "senses", RecordWriteVerb.AddObject, "LexSense", BeforeNeighbor));
        Assert.Equal(new WritableFieldResolution.Uncovered(UncoveredFieldReason.UnsupportedPlacement),
            lists.Resolve("PartOfSpeech", "sub-possibilities", RecordWriteVerb.AddObject, "PartOfSpeech", BeforeNeighbor));
        Assert.Equal(new WritableFieldResolution.Uncovered(UncoveredFieldReason.UnsupportedChildClass),
            lists.Resolve("PartOfSpeech", "sub-possibilities", RecordWriteVerb.AddObject, "CmSemanticDomain"));
        Assert.Equal(new WritableFieldResolution.Uncovered(UncoveredFieldReason.MissingPlacement),
            lexicon.Resolve("LexEntry", "allomorphs", RecordWriteVerb.MoveObject, "MoStemAllomorph"));
    }

    private static readonly Placement BeforeNeighbor = new(after: null,
        before: CanonicalId.FromGuid(new Guid("e9a305d9-a245-4a0f-b865-baaf06ee1a32")));
}
