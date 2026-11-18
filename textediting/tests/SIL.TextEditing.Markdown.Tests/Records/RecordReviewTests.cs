using System;
using System.Collections.Generic;
using System.Linq;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Markdown.Tests.Conformance;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests.Records
{
    public class RecordReviewTests
    {
        static RecordSchema Schema => RecordFixtures.Schema("test");
        static string File(string data) => "---\nfwmd: 1\nrecord: \"lexicon/entry\"\n" + data + "---\n";

        [Theory]
        [InlineData("\t")]
        [InlineData("  ")]
        [InlineData(" \t ")]
        public void NoncanonicalSpaceAfterAColonHasAnExactStyleFix(string space)
        {
            string text = File("record-data:\n  citation-form:\n    ceb:" + space + "\"buy\"\n");
            var read = RecordFileReader.Read(text, Schema);
            var finding = Assert.Single(read.Diagnostics, d => d.Id == RecordDiagnosticIds.ScalarStyle);
            Assert.Equal(space, text.Substring(finding.Start, finding.End - finding.Start));
            Assert.Equal(" ", finding.Replacement);
            Assert.Equal(MarkdownSeverity.Suggestion, finding.Severity);
            Assert.Empty(RecordFileReader.Read(text.Substring(0, finding.Start) + finding.Replacement + text.Substring(finding.End), Schema).Diagnostics);
        }

        [Fact]
        public void ARawTabInAPlainBlockIsAnErrorWithoutALossyFix()
        {
            string text = File("record-data:\n  citation-form:\n    ceb: |-\n      a\tb\n");
            var read = RecordFileReader.Read(text, Schema);
            var finding = Assert.Single(read.Diagnostics);
            Assert.Equal(RecordDiagnosticIds.TabInPlainBlock, finding.Id);
            Assert.Equal(MarkdownSeverity.Error, finding.Severity);
            Assert.Null(finding.Replacement);
            Assert.Equal("\t", text.Substring(finding.Start, finding.End - finding.Start));
            Assert.Equal("a b", Assert.IsType<RecordScalar>(read.Document!.Find("citation-form.ceb")).Text);
        }

        [Theory]
        [InlineData("/objects/7d2c9a10-5c3e-4b1a-9e1f-0a2b3c4d5e6f")]
        [InlineData("/notebook/x")]
        public void ARootInAnotherFolderIsRefused(string root)
        {
            var read = RecordFileReader.Read(File("root: \"" + root + "\"\n"), Schema);
            Assert.Contains(read.Diagnostics, d => d.Id == RecordDiagnosticIds.BadRoot);
        }

        [Fact]
        public void FrameDefinitionsAvoidTypeNamesAndDifferentBodyFields()
        {
            var builder = new RecordSchemaBuilder("collision");
            builder.ObjectType("thing").Rich("first", paragraphs: true).Rich("second", paragraphs: true);
            builder.ObjectType("thing--frame").Text("other");
            builder.ObjectType("thing--frame-2").Text("another");
            builder.Kind("one/record", "thing", 2, "first").Kind("two/record", "thing", 2, "second").Kind("three/record", "thing", 2, "first");
            var schema = builder.Build();
            string json = schema.ToJsonSchema();
            Assert.Equal(json, schema.ToJsonSchema());
            var root = Json.Obj(Json.Parse(json));
            var defs = Json.Obj(root["definitions"]);
            Assert.Equal(5, defs.Count);
            Assert.Contains("other", Json.Obj(Json.Obj(defs["thing--frame"])["properties"]).Keys);
            var branches = Json.Arr(root["allOf"]);
            string Target(object? branch) => Json.Str(Json.Obj(Json.Obj(Json.Obj(Json.Obj(branch)["then"])["properties"])["record-data"])["$ref"]).Substring("#/definitions/".Length);
            string first = Target(branches[0]), second = Target(branches[1]);
            Assert.NotEqual(first, second);
            Assert.Equal(first, Target(branches[2]));
            Assert.DoesNotContain("first", Json.Obj(Json.Obj(defs[first])["properties"]).Keys);
            Assert.Contains("second", Json.Obj(Json.Obj(defs[first])["properties"]).Keys);
            Assert.DoesNotContain("second", Json.Obj(Json.Obj(defs[second])["properties"]).Keys);
        }

        sealed class MultipleEdits : INotationChecker
        {
            public IReadOnlyList<LanguageDiagnostic> Check(string notation, string text, string path)
                => new[] { new LanguageDiagnostic(new LanguageRange(0, text.Length), DiagnosticSeverity.Error, "Replace both classes.",
                    fix: new LanguageFix("Both", new[] { new LanguageTextEdit(new LanguageRange(0, 1), "V"), new LanguageTextEdit(new LanguageRange(2, 3), "C") })) };
        }

        [Fact]
        public void AMultipleEditNotationFixIsExplicitlyReported()
        {
            var read = RecordFileReader.Read(File("record-data:\n  allomorphs:\n    - environment-strings:\n        - \"X Y\"\n"), Schema,
                new RecordReadOptions { Notations = new MultipleEdits() });
            var finding = Assert.Single(read.Diagnostics, d => d.Id == RecordDiagnosticIds.Notation);
            Assert.Null(finding.Replacement);
            Assert.Contains("2 edits", finding.Message);
            Assert.Contains("together", finding.Message);
        }

        [Fact]
        public void TheSchemaUsesTheCurrentMarkdownVersion()
        {
            var root = Json.Obj(Json.Parse(Schema.ToJsonSchema()));
            Assert.Equal((long)MarkdownFrontMatter.CurrentVersion, Json.Obj(Json.Obj(root["properties"])["fwmd"])["const"]);
        }

        [Fact]
        public void BracketsInQuotedScalarsAndLiteralBlocksAreNotNesting()
        {
            string brackets = new string('[', 20_000);
            Assert.Null(YamlTree.Parse("value: \"" + brackets + "\"\n").TooDeepAt);
            Assert.Null(YamlTree.Parse("value: |-\n  " + brackets + "\n").TooDeepAt);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void TheDepthLimitIncludesExactlyTheAllowedLevels(int excess)
        {
            int sequences = YamlTree.MaxDepth - 1 + excess;
            var parse = YamlTree.Parse("value: " + new string('[', sequences) + "x" + new string(']', sequences));
            Assert.Equal(excess > 0, parse.TooDeepAt != null);
            Assert.Null(parse.Error);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DeepUnclosedYamlStopsAtTheDepthLimit(bool laterDocument)
        {
            RecordWork.Steps = 0;
            var parse = YamlTree.Parse((laterDocument ? "first: 1\n---\n" : "") + "value: " + new string('[', 20_000));
            Assert.NotNull(parse.TooDeepAt);
            Assert.True(RecordWork.Steps < 4 * YamlTree.MaxDepth, "events: " + RecordWork.Steps);
            var read = RecordFileReader.Read(File("record-data: " + new string('[', 20_000) + "\n"), Schema);
            Assert.Contains(read.Diagnostics, d => d.Id == RecordDiagnosticIds.TooDeep && d.Severity == MarkdownSeverity.Error);
        }
    }
}
