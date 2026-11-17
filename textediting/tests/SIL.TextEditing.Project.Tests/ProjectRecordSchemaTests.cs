using System;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Infrastructure;
using SIL.TextEditing.Markdown.Records;
using Xunit;

namespace SIL.TextEditing.Project.Tests
{
    public class ProjectRecordSchemaTests
    {
        [Fact]
        public void LexiconSchemaRoundTripsAndDerivesFieldShapes()
        {
            using var fixture = new ProjectFixture();
            var schema = ProjectRecordSchema.Generate(fixture.Cache, ProjectRecordSchema.LexiconEntryOverlay);
            Assert.Equal(schema.ToJson(), RecordSchema.FromJson(schema.ToJson()).ToJson());
            var entry = schema.Kind("lexicon/entry")!.Root;
            Assert.Equal(2, schema.Kind("lexicon/entry")!.PathSegments);
            Assert.Equal(RecordFieldKind.Text, entry.Field("citation-form")!.Kind);
            Assert.True(entry.Field("citation-form")!.MultiWritingSystem);
            Assert.Equal(RecordFieldKind.Rich, entry.Field("comment")!.Kind);
            Assert.True(entry.Field("comment")!.MultiWritingSystem);
            Assert.Equal(RecordFieldKind.Rich, entry.Field("head-word")!.Kind);
            Assert.True(entry.Field("head-word")!.Computed);
            Assert.Equal(RecordFieldKind.Integer, entry.Field("homograph-number")!.Kind);
            Assert.Equal(RecordFieldKind.Boolean, entry.Field("do-not-use-for-parsing")!.Kind);
            Assert.Equal(RecordFieldKind.Sequence, entry.Field("senses")!.Kind);
            Assert.Equal(RecordFieldKind.Collection, entry.Field("morpho-syntax-analyses")!.Kind);
            Assert.Equal(RecordFieldKind.Reference, entry.Field("dialect-labels")!.Item!.Kind);
            Assert.Equal(RecordFieldKind.Collection, entry.Field("do-not-publish-in")!.Kind);
            Assert.Equal(new[] { "MoAffixAllomorph", "MoAffixProcess", "MoStemAllomorph" },
                entry.Field("lexeme-form")!.ObjectTypeNames);
            Assert.Equal(RecordFieldKind.Rich, schema.ObjectType("PhPhonContext")!.Field("description")!.Kind);
            Assert.True(schema.ObjectType("PhPhonContext")!.Field("description")!.Paragraphs);
        }

        [Fact]
        public void GeneratedSchemaWritesAndReadsARecord()
        {
            using var fixture = new ProjectFixture();
            var schema = ProjectRecordSchema.Generate(fixture.Cache, ProjectRecordSchema.LexiconEntryOverlay);
            var record = new RecordDocument("lexicon/entry");
            record.Data.Set("citation-form", new RecordAlternatives().Add("en", new RecordScalar("example")));
            record.Data.Set("lexeme-form", new RecordObject { Class = "MoStemAllomorph" }
                .Set("form", new RecordAlternatives().Add("en", new RecordScalar("example"))));
            record.Data.Set("senses", new RecordList().Add(new RecordObject()
                .Set("gloss", new RecordAlternatives().Add("en", new RecordScalar("meaning")))));
            string text = RecordFileWriter.Write(record, schema);
            var read = RecordFileReader.Read(text, schema);
            Assert.False(read.HasErrors, string.Join("\n", read.Diagnostics));
            Assert.Equal("example", ((RecordAlternatives)read.Document!.Data["citation-form"]!)["en"]!.Text);
            Assert.Equal("MoStemAllomorph", ((RecordObject)read.Document.Data["lexeme-form"]!).Class);
        }

        [Fact]
        public void UnmentionedFieldIsRejected()
        {
            using var fixture = new ProjectFixture();
            string overlay = ProjectRecordSchema.LexiconEntryOverlay.Replace("    <field name=\"CitationForm\" key=\"citation-form\" />\n", "");
            var error = Assert.Throws<ArgumentException>(() => ProjectRecordSchema.Generate(fixture.Cache, overlay));
            Assert.Contains("LexEntry.CitationForm", error.Message);
        }

        [Fact]
        public void NewCustomFieldMustBeExplicitlyAccountedFor()
        {
            using var fixture = new ProjectFixture();
            var metadata = (IFwMetaDataCacheManaged)fixture.Cache.MetaDataCacheAccessor;
            metadata.AddCustomField("LexEntry", "AddedField", CellarPropertyType.Unicode, 0);
            var error = Assert.Throws<ArgumentException>(() => ProjectRecordSchema.Generate(fixture.Cache, ProjectRecordSchema.LexiconEntryOverlay));
            Assert.Contains("LexEntry.AddedField", error.Message);
        }

        [Fact]
        public void IncludedCustomFieldUsesItsWritingSystemHint()
        {
            using var fixture = new ProjectFixture();
            var metadata = (IFwMetaDataCacheManaged)fixture.Cache.MetaDataCacheAccessor;
            metadata.AddCustomField("LexEntry", "VernacularField", CellarPropertyType.Unicode, 0, "", -2, Guid.Empty);
            string overlay = ProjectRecordSchema.LexiconEntryOverlay.Replace("<class name=\"LexEntry\">", "<class name=\"LexEntry\"><field name=\"VernacularField\" key=\"custom\" />");
            var field = ProjectRecordSchema.Generate(fixture.Cache, overlay).Kind("lexicon/entry")!.Root.Field("custom")!;
            Assert.Equal(RecordWritingSystem.Vernacular, field.WritingSystem);
        }

        [Fact]
        public void GeneralDatesMustBeExcludedUntilTheRecordFormatRepresentsThem()
        {
            using var fixture = new ProjectFixture();
            var metadata = (IFwMetaDataCacheManaged)fixture.Cache.MetaDataCacheAccessor;
            metadata.AddCustomField("LexEntry", "DateField", CellarPropertyType.GenDate, 0);
            string overlay = ProjectRecordSchema.LexiconEntryOverlay.Replace("<class name=\"LexEntry\">", "<class name=\"LexEntry\"><field name=\"DateField\" key=\"date\" />");
            var error = Assert.Throws<NotSupportedException>(() => ProjectRecordSchema.Generate(fixture.Cache, overlay));
            Assert.Contains("GenDate", error.Message);
        }

        [Fact]
        public void MetadataGuidHasGuidKindWhenIncluded()
        {
            using var fixture = new ProjectFixture();
            string overlay = ProjectRecordSchema.LexiconEntryOverlay.Replace("<exclude name=\"Guid\" />", "<field name=\"Guid\" key=\"identity\" />");
            Assert.Equal(RecordFieldKind.Guid, ProjectRecordSchema.Generate(fixture.Cache, overlay).Kind("lexicon/entry")!.Root.Field("identity")!.Kind);
        }

        [Fact]
        public void OverlayCanNameNotationAndLimitBlocks()
        {
            using var fixture = new ProjectFixture();
            var context = "<class name=\"PhPhonContext\">";
            var original = ProjectRecordSchema.LexiconEntryOverlay;
            int start = original.IndexOf(context, StringComparison.Ordinal);
            int end = original.IndexOf("</class>", start, StringComparison.Ordinal);
            string section = original.Substring(start, end - start).Replace("key=\"description\" />", "key=\"description\" blocks=\"lists\" />");
            string overlay = original.Substring(0, start) + section + original.Substring(end);
            overlay = overlay.Replace("<field name=\"LiftResidue\" key=\"lift-residue\" />", "<field name=\"LiftResidue\" key=\"lift-residue\" notation=\"environment\" />");
            var schema = ProjectRecordSchema.Generate(fixture.Cache, overlay);
            Assert.Equal("environment", schema.Kind("lexicon/entry")!.Root.Field("lift-residue")!.Notation);
            Assert.Equal(BlockFeatures.Lists, schema.ObjectType("PhPhonContext")!.Field("description")!.BlockFeatures);
        }
    }
}
