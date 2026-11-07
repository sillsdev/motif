using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Markdown.Tests.Conformance;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests.Records
{
    /// <summary>ADR 0033 (owner decision Q79) in the record schema: a rich field's block features, their defaults, the JSON Schema, and the reader's fix, which is
    /// the code that flattens a paste.</summary>
    public class RecordBlockFeaturesTests
    {
        static RecordSchema Blocks => RecordFixtures.Schema("blocks");

        static RecordField Field(string name) => Blocks.ObjectType("note")!.Field(name)!;

        [Fact]
        public void A_field_of_paragraphs_allows_every_block_unless_it_says_otherwise_and_a_one_paragraph_field_allows_none()
        {
            Assert.Equal(BlockFeatures.All, Field("anything").BlockFeatures);
            Assert.Equal(BlockFeatures.None, Field("plain").BlockFeatures);
            Assert.Equal(BlockFeatures.Lists, Field("lists-only").BlockFeatures);
            Assert.Equal(BlockFeatures.Lists | BlockFeatures.Headings, Field("no-tables").BlockFeatures);
            Assert.Equal(BlockFeatures.None, Field("remarks").BlockFeatures);
            Assert.Equal(BlockFeatures.None, Field("title").BlockFeatures);
            // The test schema's fields, none of which says anything.
            var test = RecordFixtures.Schema("test");
            Assert.Equal(BlockFeatures.All, test.ObjectType("notebook-record")!.Field("conclusions")!.BlockFeatures);
            Assert.Equal(BlockFeatures.None, test.ObjectType("sense")!.Field("definition")!.BlockFeatures);
            Assert.Equal(BlockFeatures.None, test.ObjectType("sense")!.Field("gloss")!.BlockFeatures);
        }

        [Fact]
        public void A_one_paragraph_field_cannot_be_given_a_block_kind_and_only_tables_lists_and_headings_are_kinds()
        {
            var builder = new RecordSchemaBuilder("x.schema.json");
            var type = builder.ObjectType("t");
            Assert.Throws<ArgumentException>(() => type.Rich("a", false, RecordWritingSystem.Analysis, BlockFeatures.Lists));
            Assert.Throws<ArgumentException>(() => type.MultiRich("b", false, BlockFeatures.Tables));
            Assert.Throws<ArgumentException>(() => type.Rich("c", true, RecordWritingSystem.Analysis, (BlockFeatures)8));
            type.Rich("d", false, RecordWritingSystem.Analysis, BlockFeatures.None);
            type.MultiRich("e", true, BlockFeatures.Tables);
            builder.Kind("k", "t", 1);
            var built = builder.Build().ObjectType("t")!;
            Assert.Equal(BlockFeatures.None, built.Field("d")!.BlockFeatures);
            Assert.Equal(BlockFeatures.Tables, built.Field("e")!.BlockFeatures);
        }

        [Fact]
        public void The_blocks_schema_publishes_the_expected_document_with_the_block_kinds_where_they_are_restricted()
        {
            string actual = Blocks.ToJsonSchema();
            string? write = Environment.GetEnvironmentVariable("TEXTEDITING_WRITE_SCHEMA");
            if (!string.IsNullOrEmpty(write)) File.WriteAllText(write, actual);
            Assert.Equal(ConformanceLoader.ReadText(Path.Combine(RecordFixtures.Directory, "blocks.schema.json")), actual);

            var defs = Json.Obj(Json.Obj(Json.Parse(actual))["definitions"]);
            Dictionary<string, object?> Prop(string field) => Json.Obj(Json.Obj(Json.Obj(defs["note"])["properties"])[field]);
            Assert.Equal(new string[0], Json.Arr(Prop("plain")["x-fieldworks-blocks"]).Select(Json.Str).ToArray());
            Assert.Equal(new[] { "lists" }, Json.Arr(Prop("lists-only")["x-fieldworks-blocks"]).Select(Json.Str).ToArray());
            Assert.Equal(new[] { "lists", "headings" }, Json.Arr(Prop("no-tables")["x-fieldworks-blocks"]).Select(Json.Str).ToArray());
            Assert.Equal(new[] { "tables" }, Json.Arr(Prop("description")["x-fieldworks-blocks"]).Select(Json.Str).ToArray());
            Assert.Equal(new string[0], Json.Arr(Json.Obj(Prop("remarks")["additionalProperties"])["x-fieldworks-blocks"]).Select(Json.Str).ToArray());
            // Not where nothing is restricted, and not on a one-paragraph field, whose media type already says it holds no block.
            Assert.False(Prop("anything").ContainsKey("x-fieldworks-blocks"));
            Assert.False(Prop("title").ContainsKey("x-fieldworks-blocks"));
        }

        [Fact]
        public void A_schema_that_restricts_nothing_publishes_no_block_annotation()
            => Assert.DoesNotContain("x-fieldworks-blocks", RecordFixtures.Schema("test").ToJsonSchema());

        public static IEnumerable<object[]> CasesWithFixes()
            => RecordFixtures.All().Where(c => c.SchemaName == "blocks" && c.IsRead && c.Findings.Any(f => f.Id == RecordDiagnosticIds.BlockNotAllowed && f.Fix != null))
                .Select(c => new object[] { c.Id });

        /// <summary>A finding's fix replaces the block's lines: applied, the file has no block-not-allowed finding left, and what it reads is what flattening the block gives, the very code a paste runs.</summary>
        [Theory]
        [MemberData(nameof(CasesWithFixes))]
        public void Applying_the_fixes_leaves_a_file_with_no_disallowed_block_that_reads_as_the_paste_would(string id)
        {
            var c = RecordFixtures.Find(id);
            var options = RecordFixtures.Options(c);
            var first = RecordFileReader.Read(c.Text!, c.Schema, options);
            var fixes = first.Diagnostics.Where(d => d.Id == RecordDiagnosticIds.BlockNotAllowed && d.Replacement != null).ToList();
            Assert.NotEmpty(fixes);

            // Apply from the last to the first, so the offsets of the earlier ones hold. Offsets are into the source the reader read.
            string fixedText = first.Source;
            foreach (var d in fixes.OrderByDescending(d => d.Start))
                fixedText = fixedText.Substring(0, d.Start) + d.Replacement + fixedText.Substring(d.End);
            var second = RecordFileReader.Read(fixedText, c.Schema, options);
            Assert.DoesNotContain(second.Diagnostics, d => d.Id == RecordDiagnosticIds.BlockNotAllowed);
            Assert.False(second.HasErrors, string.Join("\n", second.Diagnostics));

            // The paragraphs of every value, from the first read as flattened by the shared code, and from the fixed file.
            var recognizer = options.Markdown.StyleNames.ToBlockRecognizer();
            foreach (var value in Scalars(first.Document!.Data).Where(s => s.Paragraphs != null))
            {
                var field = FieldAt(c.Schema, value.Path);
                var expected = value.Paragraphs!.Select(p => p.Clone(false)).ToList();
                BlockFlattener.Flatten(expected, field.BlockFeatures, recognizer);
                var actual = ((RecordScalar)second.Document!.Find(value.Path)!).Paragraphs!;
                Assert.True(MarkdownConverter.AreEquivalent(expected, actual), "The value at " + value.Path + " does not read as the flattened block.");
            }
        }

        [Fact]
        public void The_fix_reads_as_the_editor_paste_of_the_same_block_does()
        {
            var c = RecordFixtures.All().First(x => x.SchemaName == "blocks" && x.Name.StartsWith("a table in a field that allows no blocks", StringComparison.Ordinal));
            var options = RecordFixtures.Options(c);
            var read = RecordFileReader.Read(c.Text!, c.Schema, options);
            var paragraphs = ((RecordScalar)read.Document!.Find("plain")!).Paragraphs!.ToList();   // Intro., then the table's header row and body row

            // The editor, in a field that allows no blocks, is pasted the same paragraphs.
            var document = new Document();
            document.Paragraphs.Add(new Paragraph { Style = "Normal", DefaultWs = "en" });
            var editor = new DocumentEditor(document) { BlockFeatures = BlockFeatures.None, BlockRecognizer = options.Markdown.StyleNames.ToBlockRecognizer() };
            editor.SetCaret(new TextPosition(0, 0));
            editor.Paste(paragraphs.Select(p => p.Clone(false)).ToList());

            var finding = read.Diagnostics.Single(d => d.Id == RecordDiagnosticIds.BlockNotAllowed);
            string fixedText = read.Source.Substring(0, finding.Start) + finding.Replacement + read.Source.Substring(finding.End);
            var again = RecordFileReader.Read(fixedText, c.Schema, options);
            var fixedParagraphs = ((RecordScalar)again.Document!.Find("plain")!).Paragraphs!;
            Assert.Equal(editor.Document.Paragraphs.Select(p => p.Text).ToArray(), fixedParagraphs.Select(p => p.Text).ToArray());
            Assert.Equal(new[] { "Intro.", "Name | Gloss", "palit | buy" }, fixedParagraphs.Select(p => p.Text).ToArray());
            Assert.All(editor.Document.Paragraphs.Skip(1), p => Assert.Equal("Normal", p.Style));
            Assert.All(fixedParagraphs.Skip(1), p => Assert.Equal("Normal", p.Style));
        }

        [Fact]
        public void A_file_with_a_disallowed_block_still_reads_and_the_block_is_kept_in_the_document()
        {
            var c = RecordFixtures.All().First(x => x.SchemaName == "blocks" && x.Name.StartsWith("a heading in a field", StringComparison.Ordinal));
            var read = RecordFileReader.Read(c.Text!, c.Schema, RecordFixtures.Options(c));
            Assert.True(read.HasErrors);
            var paragraphs = ((RecordScalar)read.Document!.Find("plain")!).Paragraphs!;
            Assert.Equal("Heading 1", paragraphs[0].Style);
        }

        [Fact]
        public void The_fix_keeps_the_files_line_ends_so_a_CRLF_file_gets_no_bare_LF()
        {
            var c = RecordFixtures.All().First(x => x.SchemaName == "blocks" && x.Name.StartsWith("a table in a field that allows no blocks", StringComparison.Ordinal));
            var options = RecordFixtures.Options(c);
            string crlf = c.Text!.Replace("\n", "\r\n");
            var read = RecordFileReader.Read(crlf, c.Schema, options);
            var finding = read.Diagnostics.Single(d => d.Id == RecordDiagnosticIds.BlockNotAllowed);
            Assert.Contains("\r\n", finding.Replacement);
            Assert.DoesNotContain("\n", finding.Replacement!.Replace("\r\n", string.Empty));
            string fixedText = read.Source.Substring(0, finding.Start) + finding.Replacement + read.Source.Substring(finding.End);
            var again = RecordFileReader.Read(fixedText, c.Schema, options);
            Assert.False(again.HasErrors, string.Join("\n", again.Diagnostics));
            Assert.Equal(new[] { "Intro.", "Name | Gloss", "palit | buy" }, ((RecordScalar)again.Document!.Find("plain")!).Paragraphs!.Select(p => p.Text).ToArray());
            // An LF file keeps LF.
            Assert.DoesNotContain("\r", RecordFileReader.Read(c.Text, c.Schema, options).Diagnostics.Single(d => d.Id == RecordDiagnosticIds.BlockNotAllowed).Replacement!);
        }

        static IEnumerable<RecordScalar> Scalars(RecordValue value)
        {
            switch (value)
            {
                case RecordScalar s: yield return s; break;
                case RecordAlternatives a: foreach (var i in a.Items) yield return i.Value; break;
                case RecordList l: foreach (var item in l.Items) foreach (var inner in Scalars(item)) yield return inner; break;
                case RecordObject o: foreach (var f in o.Fields) foreach (var inner in Scalars(f.Value)) yield return inner; break;
            }
        }

        /// <summary>The rich field a path such as <c>remarks.en</c> or <c>plain</c> names, in the blocks schema's one record type.</summary>
        static RecordField FieldAt(RecordSchema schema, string path)
        {
            string name = path.Split('.')[0];
            return schema.ObjectType("note")!.Field(name)!;
        }
    }
}
