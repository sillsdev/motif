using System;
using System.Linq;
using System.Xml.Linq;
using SIL.LCModel.Core.KernelInterfaces;
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
            string overlay = ChangeOverlay(root => Field(root, "LexEntry", "CitationForm").Remove());
            var error = Assert.Throws<ArgumentException>(() => ProjectRecordSchema.Generate(fixture.Cache, overlay));
            Assert.Contains("LexEntry.CitationForm", error.Message);
        }

        [Fact]
        public void NewCustomFieldMustBeExplicitlyAccountedFor()
        {
            using var fixture = new ProjectFixture();
            var metadata = (IFwMetaDataCacheManaged)fixture.Cache.MetaDataCacheAccessor;
            metadata.AddCustomField("MoForm", "AddedField", CellarPropertyType.Unicode, 0);
            var error = Assert.Throws<ArgumentException>(() => ProjectRecordSchema.Generate(fixture.Cache, ProjectRecordSchema.LexiconEntryOverlay));
            Assert.Contains("MoForm.AddedField on MoAffixAllomorph", error.Message);
        }

        [Fact]
        public void UnknownClassIsRejectedBeforeResolvingItsId()
        {
            using var fixture = new ProjectFixture();
            string overlay = ChangeOverlay(root => root.Add(new XElement("class", new XAttribute("name", "AbsentClass"))));
            var error = Assert.Throws<ArgumentException>(() => ProjectRecordSchema.Generate(fixture.Cache, overlay));
            Assert.Contains("Unknown metadata class AbsentClass", error.Message);
        }

        [Fact]
        public void UnknownDeclaredFieldIsRejected()
        {
            using var fixture = new ProjectFixture();
            string overlay = ChangeOverlay(root => Class(root, "LexEntry").Add(new XElement("exclude", new XAttribute("name", "AbsentField"))));
            var error = Assert.Throws<ArgumentException>(() => ProjectRecordSchema.Generate(fixture.Cache, overlay));
            Assert.Contains("Unknown declared field LexEntry.AbsentField", error.Message);
        }

        [Fact]
        public void InheritedFieldCannotBeDeclaredOnTheSubclass()
        {
            using var fixture = new ProjectFixture();
            string overlay = ChangeOverlay(root => Class(root, "MoStemAllomorph").Add(new XElement("exclude", new XAttribute("name", "Form"))));
            var error = Assert.Throws<ArgumentException>(() => ProjectRecordSchema.Generate(fixture.Cache, overlay));
            Assert.Contains("Unknown declared field MoStemAllomorph.Form", error.Message);
        }

        [Fact]
        public void DuplicateFieldDecisionIsRejected()
        {
            using var fixture = new ProjectFixture();
            string overlay = ChangeOverlay(root => Class(root, "LexEntry").Add(new XElement("exclude", new XAttribute("name", "CitationForm"))));
            var error = Assert.Throws<ArgumentException>(() => ProjectRecordSchema.Generate(fixture.Cache, overlay));
            Assert.Contains("Duplicate field decision LexEntry.CitationForm", error.Message);
        }

        [Fact]
        public void UnknownBlockKindIsRejected()
        {
            using var fixture = new ProjectFixture();
            string overlay = ChangeOverlay(root => Field(root, "PhPhonContext", "Description").SetAttributeValue("blocks", "tables,unknown"));
            var error = Assert.Throws<ArgumentException>(() => ProjectRecordSchema.Generate(fixture.Cache, overlay));
            Assert.Contains("Unknown block kind unknown", error.Message);
        }

        [Fact]
        public void NotationOnNonStringIsRejected()
        {
            using var fixture = new ProjectFixture();
            string overlay = ChangeOverlay(root => Field(root, "LexEntry", "HomographNumber").SetAttributeValue("notation", "environment"));
            var error = Assert.Throws<ArgumentException>(() => ProjectRecordSchema.Generate(fixture.Cache, overlay));
            Assert.Contains("Notation requires a string field: homograph-number", error.Message);
        }

        [Fact]
        public void InvalidNotationNameIsRejected()
        {
            using var fixture = new ProjectFixture();
            string overlay = ChangeOverlay(root => Field(root, "LexEntry", "LiftResidue").SetAttributeValue("notation", "Bad Notation"));
            var error = Assert.Throws<ArgumentException>(() => ProjectRecordSchema.Generate(fixture.Cache, overlay));
            Assert.Contains("notation name is lowercase", error.Message);
        }

        [Fact]
        public void BlocksOnNonParagraphFieldAreRejected()
        {
            using var fixture = new ProjectFixture();
            string overlay = ChangeOverlay(root => Field(root, "LexEntry", "CitationForm").SetAttributeValue("blocks", "lists"));
            var error = Assert.Throws<ArgumentException>(() => ProjectRecordSchema.Generate(fixture.Cache, overlay));
            Assert.Contains("Blocks require a paragraph field: citation-form", error.Message);
        }

        [Fact]
        public void UnknownKindRootIsRejected()
        {
            using var fixture = new ProjectFixture();
            string overlay = ChangeOverlay(root => root.Element("kind")!.SetAttributeValue("root", "AbsentRoot"));
            var error = Assert.Throws<ArgumentException>(() => ProjectRecordSchema.Generate(fixture.Cache, overlay));
            Assert.Contains("Unknown kind root AbsentRoot", error.Message);
        }

        [Fact]
        public void UnreachableOverlayClassesAreRejected()
        {
            using var fixture = new ProjectFixture();
            string overlay = ChangeOverlay(root => root.Element("kind")!.SetAttributeValue("root", "CmTranslation"));
            var error = Assert.Throws<ArgumentException>(() => ProjectRecordSchema.Generate(fixture.Cache, overlay));
            Assert.Contains("Unreachable overlay class", error.Message);
        }

        [Fact]
        public void MissingConcreteOwnedAlternativeIsRejected()
        {
            using var fixture = new ProjectFixture();
            string overlay = ChangeOverlay(root => Class(root, "MoStemAllomorph").Remove());
            var error = Assert.Throws<ArgumentException>(() => ProjectRecordSchema.Generate(fixture.Cache, overlay));
            Assert.Contains("Missing concrete overlay class for allomorphs", error.Message);
        }

        [Fact]
        public void SeveralKindRootsShareTheReachabilityCheck()
        {
            using var fixture = new ProjectFixture();
            string overlay = ChangeOverlay(root => root.Add(new XElement("kind", new XAttribute("name", "example/translation"),
                new XAttribute("root", "CmTranslation"), new XAttribute("pathSegments", "1"))));
            var schema = ProjectRecordSchema.Generate(fixture.Cache, overlay);
            Assert.NotNull(schema.Kind("lexicon/entry"));
            Assert.NotNull(schema.Kind("example/translation"));
        }

        [Fact]
        public void AllOwnedObjectAlternativesAreConcreteMetadataClasses()
        {
            using var fixture = new ProjectFixture();
            var metadata = (IFwMetaDataCache)fixture.Cache.MetaDataCacheAccessor;
            var schema = ProjectRecordSchema.Generate(fixture.Cache, ProjectRecordSchema.LexiconEntryOverlay);
            var alternatives = schema.ObjectTypes.SelectMany(t => t.Fields)
                .Select(f => f.IsList ? f.Item! : f)
                .Where(f => f.Kind == RecordFieldKind.Object).SelectMany(f => f.ObjectTypeNames).Distinct().ToArray();
            Assert.Contains("MoStemAllomorph", alternatives);
            Assert.Contains("LexSense", alternatives);
            Assert.All(alternatives, name => Assert.False(metadata.GetAbstract(metadata.GetClassId(name)), name));
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

        [Theory]
        [InlineData(-5)]
        [InlineData(-6)]
        public void MixedWritingSystemHintsAreRejectedWithoutLosingTheirOrder(int hint)
        {
            using var fixture = new ProjectFixture();
            var metadata = (IFwMetaDataCacheManaged)fixture.Cache.MetaDataCacheAccessor;
            metadata.AddCustomField("LexEntry", "MixedField", CellarPropertyType.Unicode, 0, "", hint, Guid.Empty);
            string overlay = ChangeOverlay(root => Class(root, "LexEntry").Add(
                new XElement("field", new XAttribute("name", "MixedField"), new XAttribute("key", "mixed"))));
            var error = Assert.Throws<NotSupportedException>(() => ProjectRecordSchema.Generate(fixture.Cache, overlay));
            Assert.Contains("writing-system hint " + hint + " for mixed", error.Message);
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
        static string ChangeOverlay(Action<XElement> change)
        {
            var document = XDocument.Parse(ProjectRecordSchema.LexiconEntryOverlay);
            change(document.Root!);
            return document.ToString();
        }

        static XElement Class(XElement root, string name)
            => root.Elements("class").Single(e => (string?)e.Attribute("name") == name);

        static XElement Field(XElement root, string className, string name)
            => Class(root, className).Elements("field").Single(e => (string?)e.Attribute("name") == name);
    }
}
