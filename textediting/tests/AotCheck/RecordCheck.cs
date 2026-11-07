using System;
using System.Collections.Generic;
using System.Linq;
using SIL.TextEditing;
using SIL.TextEditing.Markdown;
using SIL.TextEditing.Markdown.Records;

// Exercises the record frame under AOT with the public API only: a schema built as data, a document written and read back through YamlDotNet's parser,
// the traps the reader types by the schema, and the JSON Schema the schema publishes.
internal static class RecordCheck
{
    static readonly List<string> Failures = new List<string>();

    static void Check(bool condition, string what)
    {
        if (!condition) Failures.Add(what);
    }

    public static IReadOnlyList<string> Run()
    {
        var builder = new RecordSchemaBuilder("aot.schema.json");
        builder.ObjectType("entry").MultiText("citation-form").Objects("senses", "sense").Integer("homograph-number").Boolean("do-not-use-for-parsing");
        builder.ObjectType("sense").MultiText("gloss").MultiRich("definition").Notation("features", "feature-structure").References("semantic-domains");
        builder.ObjectType("record").Rich("title").Rich("description", paragraphs: true).Rich("notes", paragraphs: true, blocks: BlockFeatures.None);
        builder.Kind("lexicon/entry", "entry", 2).Kind("notebook/record", "record", 2, "description");
        var schema = builder.Build();

        var entry = new RecordDocument("lexicon/entry") { Root = "/lexicon/palit(2)", Analysis = "en", Vernacular = "ceb" };
        entry.Data.Guid = Guid.Parse("7d2c9a10-5c3e-4b1a-9e1f-0a2b3c4d5e6f");
        entry.Data.Set("citation-form", new RecordAlternatives().Add("ceb", new RecordScalar("palit")).Add("no", new RecordScalar("a\tb")));
        entry.Data.Set("homograph-number", RecordScalar.Of(2)).Set("do-not-use-for-parsing", RecordScalar.Of(false));
        var sense = new RecordObject { Guid = Guid.Parse("1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d") };
        sense.Set("gloss", new RecordAlternatives().Add("en", new RecordScalar("no")));
        sense.Set("definition", new RecordAlternatives().Add("en", new RecordScalar("to [pay]{.v} for")));
        sense.Set("features", new RecordScalar("[tense:past]"));
        sense.Set("semantic-domains", new RecordList().Add(new RecordScalar("[6.8.4.2 Buy](/lists/semantic-domains/6.8.4.2){guid=4bf411b7-2b5b-4673-b116-0e6c31fbd08a}")));
        entry.Data.Set("senses", new RecordList().Add(sense));
        string text = RecordFileWriter.Write(entry, schema);
        var read = RecordFileReader.Read(text, schema, new RecordReadOptions { Notations = new Features() });
        Check(read.Diagnostics.Count == 1 && read.Diagnostics[0].Id == "fwmd.test.feature", "record: " + string.Join("; ", read.Diagnostics.Select(d => d.ToString())));
        Check(read.Document != null && RecordDocument.AreEquivalent(entry, read.Document), "record: the entry reads back");
        Check(read.Document?.Find("senses/1/definition.en") is RecordScalar d && d.Paragraphs != null && d.Paragraphs[0].Runs.Count == 3, "record: rich text is read");
        Check(read.Document != null && RecordFileWriter.Write(read.Document, schema) == text, "record: the file is stable");

        var trap = RecordFileReader.Read("---\nfwmd: 1\nrecord: \"lexicon/entry\"\nrecord-data:\n  citation-form:\n    ceb: no\n  homograph-number: 012\n  # a comment\n---\n", schema);
        Check(!trap.HasErrors && trap.Document?.Find("citation-form.ceb") is RecordScalar no && no.Text == "no", "record: no is text");
        Check(trap.Document?.Find("homograph-number") is RecordScalar n && n.AsInteger() == 12, "record: 012 is twelve");
        Check(trap.Diagnostics.Any(x => x.Id == RecordDiagnosticIds.CommentDropped), "record: the comment is reported");

        var notebook = new RecordDocument("notebook/record");
        notebook.Data.Set("title", new RecordScalar("Kaon")).Set("description", RecordScalar.Rich(new[] { Para("A [b]") }, new MarkdownOptions()));
        string withBody = RecordFileWriter.Write(notebook, schema);
        Check(withBody.Contains("---\n\nA "), "record: the description is the body: " + withBody);
        var back = RecordFileReader.Read(withBody, schema);
        Check(!back.HasErrors && back.Document != null && RecordDocument.AreEquivalent(notebook, back.Document), "record: the body reads back");

        // Block features (ADR 0033): a heading in a field that allows none is found and fixed, and a paste flattens a table the same way.
        var blocked = RecordFileReader.Read("---\nfwmd: 1\nrecord: \"notebook/record\"\nrecord-data:\n  notes: |\n    # Title\n---\n", schema);
        Check(blocked.Diagnostics.Any(x => x.Id == RecordDiagnosticIds.BlockNotAllowed && x.Replacement == "Title"), "record: a heading in a field that allows no blocks: " + string.Join("; ", blocked.Diagnostics.Select(x => x.ToString())));
        var pasted = new Document();
        pasted.Paragraphs.Add(Para("x"));
        var editor = new DocumentEditor(pasted) { BlockFeatures = BlockFeatures.None, BlockRecognizer = new StyleBlockRecognizer(null, null, new[] { "Table Row" }) };
        var row = Para("a\tb");
        row.Style = "Table Row";
        editor.Paste(new List<Paragraph> { Para("lead"), row });
        Check(pasted.Paragraphs.Count == 2 && pasted.Paragraphs[1].Text == "a | bx" && pasted.Paragraphs[1].Style == "Normal", "record: a pasted table row becomes a plain paragraph");

        string json = schema.ToJsonSchema();
        Check(json.Contains("\"contentMediaType\": \"text/x-fieldworks-feature-structure\"") && json.Contains("\"$id\": \"aot.schema.json\""), "record: the JSON Schema");
        return Failures;
    }

    static Paragraph Para(string text)
    {
        var p = new Paragraph { Style = "Normal" };
        p.Runs.Add(new Run { Text = text, Ws = "en" });
        return p;
    }

    sealed class Features : INotationChecker
    {
        public IReadOnlyList<LanguageDiagnostic> Check(string notation, string text, string path)
            => notation == "feature-structure" && text.Contains("past")
                ? new[] { new LanguageDiagnostic(new LanguageRange(text.IndexOf("past", StringComparison.Ordinal), text.IndexOf("past", StringComparison.Ordinal) + 4), DiagnosticSeverity.Information, "past is noted.", "fwmd.test.feature") }
                : Array.Empty<LanguageDiagnostic>();
    }
}
