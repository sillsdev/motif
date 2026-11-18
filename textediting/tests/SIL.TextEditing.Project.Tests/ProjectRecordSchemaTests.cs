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
        public void NotebookSchemaIncludesSubrecordsAndParticipants()
        {
            using var fixture = new ProjectFixture();
            var schema = ProjectRecordSchema.Generate(fixture.Cache, ProjectRecordSchema.NotebookRecordOverlay);
            var kind = schema.Kind("notebook/record")!;
            Assert.Equal(2, kind.PathSegments);
            Assert.Equal("description", kind.BodyField!.Name);
            Assert.Equal(RecordFieldKind.Sequence, kind.Root.Field("sub-records")!.Kind);
            Assert.Equal("RnGenericRec", kind.Root.Field("sub-records")!.Item!.ObjectType!.Name);
            Assert.Equal(RecordFieldKind.Collection, kind.Root.Field("participants")!.Kind);
            Assert.NotNull(schema.ObjectType("RnRoledPartic")!.Field("role"));
            Assert.NotNull(schema.ObjectType("RnRoledPartic")!.Field("participants"));
            Assert.Equal(schema.ToJson(), RecordSchema.FromJson(schema.ToJson()).ToJson());
        }

        [Fact]
        public void ListItemSchemaSelectsConcreteRootsAndKeepsTheOutline()
        {
            using var fixture = new ProjectFixture();
            var schema = ProjectRecordSchema.Generate(fixture.Cache, ProjectRecordSchema.ListItemOverlay);
            var kind = schema.Kind("lists/item")!;
            Assert.Equal(3, kind.PathSegments);
            Assert.Contains(kind.Roots, t => t.Name == "CmSemanticDomain");
            Assert.Contains(kind.Roots, t => t.Name == "PartOfSpeech");
            var subitems = schema.ObjectType("CmSemanticDomain")!.Field("sub-possibilities")!;
            Assert.Equal(RecordFieldKind.Sequence, subitems.Kind);
            Assert.Contains("CmPerson", subitems.Item!.ObjectTypeNames);
            Assert.NotNull(schema.ObjectType("CmSemanticDomain")!.Field("questions"));
            schema = RecordSchema.FromJson(schema.ToJson());
            var record = new RecordDocument("lists/item") { Root = "/lists/semantic-domains/12345678-1234-1234-1234-123456789abc" };
            record.Data.Class = "CmSemanticDomain";
            record.Data.Set("louw-nida-codes", new RecordScalar("code"));
            record.Data.Set("sub-possibilities", new RecordList().Add(new RecordObject { Class = "CmPerson" }.Set("gender", new RecordScalar("1"))));
            string text = RecordFileWriter.Write(record, schema);
            var read = RecordFileReader.Read(text, schema);
            Assert.False(read.HasErrors, string.Join("\n", read.Diagnostics));
            Assert.Equal("CmSemanticDomain", read.Document!.Data.Class);
            Assert.Contains("class", schema.ToJsonSchema());
        }

        [Fact]
        public void WritingSystemOverrideIsValidatedAndUsed()
        {
            using var fixture = new ProjectFixture();
            var schema = ProjectRecordSchema.Generate(fixture.Cache, ProjectRecordSchema.LexiconEntryOverlay);
            Assert.Equal(RecordWritingSystem.Vernacular, schema.ObjectType("LexPronunciation")!.Field("tone")!.WritingSystem);
            Assert.Equal(RecordWritingSystem.Analysis, schema.Kind("lexicon/entry")!.Root.Field("lift-residue")!.WritingSystem);
            string invalid = ChangeOverlay(root => Field(root, "LexEntry", "HomographNumber").SetAttributeValue("ws", "vernacular"));
            Assert.Throws<ArgumentException>(() => ProjectRecordSchema.Generate(fixture.Cache, invalid));
            invalid = ChangeOverlay(root => Field(root, "LexEntry", "LiftResidue").SetAttributeValue("ws", "unknown"));
            Assert.Throws<ArgumentException>(() => ProjectRecordSchema.Generate(fixture.Cache, invalid));
        }

        [Fact]
        public void OverlayCannotUseReservedCustomKeyOrExcludeCustomFields()
        {
            using var fixture = new ProjectFixture();
            string collision = ChangeOverlay(root => Field(root, "LexEntry", "LiftResidue").SetAttributeValue("key", "custom-0041"));
            Assert.Throws<ArgumentException>(() => ProjectRecordSchema.Generate(fixture.Cache, collision));
            var metadata = (IFwMetaDataCacheManaged)fixture.Cache.MetaDataCacheAccessor;
            metadata.AddCustomField("LexEntry", "Added", CellarPropertyType.Integer, 0);
            string excluded = ChangeOverlay(root => Class(root, "LexEntry").Add(new XElement("exclude", new XAttribute("name", "Added"))));
            Assert.Throws<ArgumentException>(() => ProjectRecordSchema.Generate(fixture.Cache, excluded));
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
        public void CustomFieldsAreIncludedAfterOverlayFieldsOnEachSubclass()
        {
            using var fixture = new ProjectFixture();
            var metadata = (IFwMetaDataCacheManaged)fixture.Cache.MetaDataCacheAccessor;
            metadata.AddCustomField("MoForm", "AddedField", CellarPropertyType.Unicode, 0);
            metadata.AddCustomField("MoStemAllomorph", "SecondField", CellarPropertyType.Integer, 0);
            var cache = (IFwMetaDataCache)metadata;
            var expected = metadata.GetFields(cache.GetClassId("MoStemAllomorph"), true, -1).Where(metadata.IsCustom)
                .Select(f => "custom-" + string.Concat((cache.GetOwnClsName(f) + "\0" + cache.GetFieldName(f)).Select(c => ((int)c).ToString("x4")))).ToArray();
            var type = ProjectRecordSchema.Generate(fixture.Cache, ProjectRecordSchema.LexiconEntryOverlay).ObjectType("MoStemAllomorph")!;
            Assert.Equal(2, expected.Length);
            Assert.Equal(expected, type.Fields.Skip(type.Fields.Count - 2).Select(f => f.Name));
            Assert.Equal(RecordFieldKind.Text, type.Field("custom-004d006f0046006f0072006d000000410064006400650064004600690065006c0064")!.Kind);
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
            var field = ProjectRecordSchema.Generate(fixture.Cache, ProjectRecordSchema.LexiconEntryOverlay).Kind("lexicon/entry")!.Root.Fields.Last();
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
            var error = Assert.Throws<NotSupportedException>(() => ProjectRecordSchema.Generate(fixture.Cache, ProjectRecordSchema.LexiconEntryOverlay));
            Assert.Contains("writing-system hint " + hint, error.Message);
        }

        [Fact]
        public void GeneralDatesMustBeExcludedUntilTheRecordFormatRepresentsThem()
        {
            using var fixture = new ProjectFixture();
            var metadata = (IFwMetaDataCacheManaged)fixture.Cache.MetaDataCacheAccessor;
            metadata.AddCustomField("LexEntry", "DateField", CellarPropertyType.GenDate, 0);
            var error = Assert.Throws<NotSupportedException>(() => ProjectRecordSchema.Generate(fixture.Cache, ProjectRecordSchema.LexiconEntryOverlay));
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
