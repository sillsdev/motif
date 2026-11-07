using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Markdown.Tests.Conformance;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace SIL.TextEditing.Markdown.Tests.Records
{
    /// <summary>Random record documents over the fixtures' test schema: plain text full of YAML's traps and of characters the profile cannot quote, rich text from
    /// <see cref="ModelGenerator"/>, references, numbers, Booleans and GUIDs, whole records and partial files. The same seed gives the same document.</summary>
    internal sealed class RecordGenerator
    {
        static readonly string[] Plain =
        {
            "no", "yes", "on", "off", "true", "~", "null", "1.10", "012", "0x1F", "2026-11-04", "*palit", "&a", "!b", "-an", "an-", "=an", "~a~", "- x", "? x", "# x", "1. first",
            "a: b", "a #b", "\"quoted\"", "back\\slash", "'single'", "{a}", "[b]", "|", ">", "%x", "@x", "`x`", " leading", "trailing ", "  ", "caf\u00E9", "cafe\u0301", "k\u0301a",
            "\u0645\u0631\u062D\u0628\u0627", "\uD83D\uDE00", "a\tb", "a\nb", "a\u2028b", "a\u2029b", "a\u0085b", "\u0000", "\u0001x", "x\u007F", "\uFEFFx", "\uD800", "palit", "buy",
            "to [pay]{.v} for", "a *b* c", "[x](/y){guid=1}", "\\*", "&#x20;", "\r", "x\r\ny",
        };

        static readonly string[] Writing = { "en", "fr", "ceb", "no", "ar" };

        readonly Random _random;
        readonly RecordSchema _schema;
        readonly ModelGenerator _rich;
        public readonly TestObjects Objects = new TestObjects();

        public RecordGenerator(int seed, RecordSchema schema)
        {
            _random = new Random(seed);
            _schema = schema;
            _rich = new ModelGenerator(seed, Objects);
        }

        /// <summary>The options rich values are written and read with: the test host, with tables.</summary>
        public MarkdownOptions Markdown(string analysis = "en") => new MarkdownOptions
        {
            WritingSystems = new FixedWritingSystems(analysis, "ceb"),
            Objects = Objects,
            WriteFrontMatter = false,
            StyleNames = Hosts.Tables(),
        };

        public RecordReadOptions ReadOptions() => new RecordReadOptions { Markdown = new MarkdownOptions { WritingSystems = new TestWritingSystems(), Objects = Objects, StyleNames = Hosts.Tables() } };

        bool Chance(int percent) => _random.Next(100) < percent;
        T Pick<T>(IReadOnlyList<T> items) => items[_random.Next(items.Count)];

        Guid NextGuid()
        {
            var bytes = new byte[16];
            _random.NextBytes(bytes);
            return new Guid(bytes);
        }

        public RecordDocument Next()
        {
            var kind = Pick(_schema.Kinds);
            var document = new RecordDocument(kind.Name) { Schema = _schema.Id };
            if (Chance(70)) { document.Analysis = "en"; document.Vernacular = "ceb"; }
            if (Chance(50)) foreach (string ws in Writing.Take(_random.Next(1, Writing.Length + 1))) document.WritingSystems.Add(new KeyValuePair<string, WritingSystemInfo>(ws, WritingSystemInfo.Default(ws)));
            if (Chance(30)) document.Base = "sha256:" + NextGuid().ToString("N");
            string recordPath = kind.Name == "lists/item" ? "/lists/GDR/GDR-" + _random.Next(1, 99) : "/" + kind.Name.Split('/')[0] + "/x" + _random.Next(100);
            var type = kind.Root;
            if (Chance(40)) document.Root = recordPath;
            // A partial file: an owned object, or a field of one.
            if (Chance(25))
            {
                string path = recordPath;
                for (int step = 0; step < 2; step++)
                {
                    var owned = type.Fields.Where(f => f.Kind == RecordFieldKind.Object || (f.Kind == RecordFieldKind.Sequence && f.Item!.Kind == RecordFieldKind.Object)).ToList();
                    if (owned.Count == 0 || Chance(40)) break;
                    var f = Pick(owned);
                    path += "/" + f.Name + (f.Kind == RecordFieldKind.Sequence ? "/" + _random.Next(1, 4).ToString(CultureInfo.InvariantCulture) : string.Empty);
                    type = f.Kind == RecordFieldKind.Sequence ? f.Item!.ObjectType! : f.ObjectType!;
                }
                document.Root = path;
                if (Chance(40))
                {
                    var field = Pick(type.Fields);
                    string fragment = field.Name;
                    var value = Value(field, 0);
                    if (field.MultiWritingSystem && Chance(50) && value is RecordAlternatives alts && alts.Items.Count > 0)
                    {
                        var one = alts.Items[0];
                        fragment += "." + one.Key;
                        value = new RecordAlternatives().Add(one.Key, one.Value);
                    }
                    document.Root += "#" + fragment;
                    document.Data = new RecordObject().Set(field.Name, value);
                    return document;
                }
            }
            document.Data = Object(type, 0);
            return document;
        }

        RecordObject Object(RecordObjectType type, int depth)
        {
            var o = new RecordObject();
            if (type.HasGuid && Chance(85)) o.Guid = NextGuid();
            foreach (var field in type.Fields)
            {
                if (!Chance(depth == 0 ? 60 : 40)) continue;
                o[field.Name] = Value(field, depth);
            }
            if (o.IsEmpty) o.Guid = type.HasGuid ? NextGuid() : o.Guid;
            if (o.IsEmpty) o[type.Fields[0].Name] = Value(type.Fields[0], depth + 99);
            return o;
        }

        RecordValue Value(RecordField field, int depth)
        {
            if (field.MultiWritingSystem)
            {
                var alts = new RecordAlternatives();
                foreach (string ws in Writing.Where(_ => Chance(40))) alts.Add(ws, Leaf(field, ws));
                if (alts.Items.Count == 0) alts.Add("en", Leaf(field, "en"));
                return alts;
            }
            switch (field.Kind)
            {
                case RecordFieldKind.Sequence:
                {
                    var list = new RecordList();
                    int count = depth > 2 ? 1 : _random.Next(1, 4);
                    for (int i = 0; i < count; i++)
                        list.Add(field.Item!.Kind == RecordFieldKind.Object ? Object(field.Item.ObjectType!, depth + 1) : Leaf(field.Item, null));
                    return list;
                }
                case RecordFieldKind.Object: return Object(field.ObjectType!, depth + 1);
                default: return Leaf(field, null);
            }
        }

        RecordScalar Leaf(RecordField field, string? ws)
        {
            switch (field.Kind)
            {
                case RecordFieldKind.Integer: return RecordScalar.Of(Chance(20) ? -_random.Next() : (long)_random.Next() * (Chance(10) ? 1000003 : 1));
                case RecordFieldKind.Boolean: return RecordScalar.Of(Chance(50));
                case RecordFieldKind.Guid: return RecordScalar.Of(NextGuid());
                case RecordFieldKind.Reference: return new RecordScalar("[" + Pick(new[] { "stem", "Verb", "6.8.4.2 Buy", "a b" }) + "](/lists/x/" + _random.Next(100) + "){guid=" + NextGuid().ToString("D") + "}");
                case RecordFieldKind.Rich:
                {
                    var options = Markdown(ws ?? "en");
                    RecordScalar value;
                    if (field.Paragraphs) value = RecordScalar.Rich(_rich.NextDocument().Paragraphs, options);
                    else value = RecordScalar.RichInline(_rich.NextParagraph().Runs, options);
                    return value.IsEmpty ? new RecordScalar("x") : value;
                }
                default:
                {
                    var sb = new StringBuilder();
                    int parts = _random.Next(1, 4);
                    for (int i = 0; i < parts; i++) sb.Append(Pick(Plain));
                    return new RecordScalar(sb.ToString());
                }
            }
        }
    }

    /// <summary>The gates of the record frame (ADR 0032): model to file to model is lossless; a canonical file read and written again is the same bytes; YamlDotNet's
    /// representation model reads every canonical file to the tree our reader does, in the profile's styles; Pandoc reads every canonical file, its rich values
    /// and its body as the FieldWorks Markdown reader does.</summary>
    public class RecordGateTests
    {
        static RecordSchema Schema => RecordFixtures.Schema("test");

        static int Seeds(string variable, int fallback) => int.TryParse(Environment.GetEnvironmentVariable(variable), out int n) ? n : fallback;

        public static IEnumerable<object[]> GeneratedSeeds()
        {
            for (int seed = 1; seed <= Seeds("TEXTEDITING_RECORD_SEEDS", 400); seed++) yield return new object[] { seed };
        }

        public static IEnumerable<object[]> PandocSeeds()
        {
            for (int seed = 1; seed <= Seeds("TEXTEDITING_PANDOC_SEEDS", 150) / 3; seed++) yield return new object[] { seed };
        }

        public static IEnumerable<object[]> CanonicalCases() => RecordFixtures.All().Where(c => c.CanonicalText != null).Select(c => new object[] { c.Id });

        public static IEnumerable<object[]> PandocCases() => RecordFixtures.All().Where(c => c.CanonicalText != null && c.Pandoc).Select(c => new object[] { c.Id });

        static (RecordDocument document, string text, RecordReadOptions options) Generate(int seed)
        {
            var generator = new RecordGenerator(seed, Schema);
            var document = generator.Next();
            return (document, RecordFileWriter.Write(document, Schema), generator.ReadOptions());
        }

        [Theory]
        [MemberData(nameof(GeneratedSeeds))]
        public void ModelToFileToModelIsLosslessAndTheFileIsStable(int seed)
        {
            var (document, text, options) = Generate(seed);
            var read = RecordFileReader.Read(text, Schema, options);
            // The frame of a canonical file has no finding; the generated rich text may hold properties the reader only notes (fwmd.prop.unknown-number).
            var frame = read.Diagnostics.Where(d => d.Severity == MarkdownSeverity.Error || d.Id.StartsWith("fwmd.yaml.", StringComparison.Ordinal) || d.Id.StartsWith("fwmd.record.", StringComparison.Ordinal)).ToList();
            Assert.True(frame.Count == 0, "seed " + seed + ": the canonical file has findings:\n" + string.Join("\n", frame) + "\n" + text);
            RecordConformanceTests.AssertDocuments(document, read.Document!, Schema, "seed " + seed + "\n" + text);
            Assert.Equal(text, RecordFileWriter.Write(read.Document!, Schema));
        }

        [Theory]
        [MemberData(nameof(CanonicalCases))]
        public void ACanonicalFixtureReadAndWrittenIsTheSameBytes(string id)
        {
            var c = RecordFixtures.Find(id);
            var read = RecordFileReader.Read(c.CanonicalText!, c.Schema, RecordFixtures.Options(c));
            Assert.False(read.HasErrors);
            Assert.Equal(c.CanonicalText, RecordFileWriter.Write(read.Document!, c.Schema));
        }

        [Theory]
        [MemberData(nameof(CanonicalCases))]
        public void ACanonicalFixtureCheckedOutWithCrlfReadsTheSame(string id)
        {
            var c = RecordFixtures.Find(id);
            var lf = RecordFileReader.Read(c.CanonicalText!, c.Schema, RecordFixtures.Options(c));
            var crlf = RecordFileReader.Read(c.CanonicalText!.Replace("\n", "\r\n"), c.Schema, RecordFixtures.Options(c));
            Assert.True(crlf.Diagnostics.Count == 0, string.Join("\n", crlf.Diagnostics));
            RecordConformanceTests.AssertDocuments(lf.Document!, crlf.Document!, c.Schema, "CR LF");
            Assert.Equal(c.CanonicalText, RecordFileWriter.Write(crlf.Document!, c.Schema));
        }

        public static IEnumerable<object[]> CleanReadCases() => RecordFixtures.All().Where(c => c.IsRead && !c.HasErrors).Select(c => new object[] { c.Id });

        /// <summary>Reading then writing is total: a file the reader finds no error in can be written (review of PR 147, P2-1).</summary>
        [Theory]
        [MemberData(nameof(CleanReadCases))]
        public void AFixtureThatReadsWithoutAnErrorCanBeWritten(string id)
        {
            var c = RecordFixtures.Find(id);
            var read = RecordFileReader.Read(c.Text!, c.Schema, RecordFixtures.Options(c));
            Assert.False(read.HasErrors);
            string written = RecordFileWriter.Write(read.Document!, c.Schema);
            var again = RecordFileReader.Read(written, c.Schema, RecordFixtures.Options(c));
            Assert.False(again.HasErrors, string.Join("\n", again.Diagnostics));
            RecordConformanceTests.AssertDocuments(read.Document!, again.Document!, c.Schema, "written and read again");
        }

        [Theory]
        [InlineData("a\uFFFEb")]
        [InlineData("a\uFFFFb")]
        [InlineData("\uD800")]
        public void PlainTextTheProfileCannotSpellIsRefusedWithARecordWriteExceptionOrWrittenLosslessly(string text)
        {
            var document = new RecordDocument("lexicon/entry");
            document.Data.Set("citation-form", new RecordAlternatives().Add("ceb", new RecordScalar(text)));
            string written;
            try { written = RecordFileWriter.Write(document, Schema); }
            catch (RecordWriteException) { return; }
            var read = RecordFileReader.Read(written, Schema);
            Assert.False(read.HasErrors, string.Join("\n", read.Diagnostics));
            RecordConformanceTests.AssertDocuments(document, read.Document!, Schema, written);
        }

        [Fact]
        public void TheWriterRefusesWhatTheSchemaDoesNotHave()
        {
            var document = new RecordDocument("lexicon/entry");
            document.Data.Set("no-such-field", new RecordScalar("x"));
            Assert.Throws<RecordWriteException>(() => RecordFileWriter.Write(document, Schema));
            document = new RecordDocument("lexicon/entry");
            document.Data.Set("citation-form", new RecordScalar("not alternatives"));
            Assert.Throws<RecordWriteException>(() => RecordFileWriter.Write(document, Schema));
            document = new RecordDocument("lexicon/entry");
            document.Data.Set("senses", new RecordList().Add(new RecordObject()));
            Assert.Throws<RecordWriteException>(() => RecordFileWriter.Write(document, Schema));
            document = new RecordDocument("lexicon/entry");
            document.Data.Set("comment", new RecordAlternatives().Add("en", new RecordScalar(" leading space")));
            Assert.Throws<RecordWriteException>(() => RecordFileWriter.Write(document, Schema));
            Assert.Throws<RecordWriteException>(() => RecordFileWriter.Write(new RecordDocument("no/kind"), Schema));
        }

        // ---------------------------------------------------------------- YamlDotNet's representation model as an oracle

        [Theory]
        [MemberData(nameof(CanonicalCases))]
        public void YamlDotNetReadsACanonicalFixtureAsWeDo(string id)
        {
            var c = RecordFixtures.Find(id);
            CompareWithRepresentationModel(c.CanonicalText!, c.Schema, RecordFixtures.Options(c), id);
        }

        [Theory]
        [MemberData(nameof(GeneratedSeeds))]
        public void YamlDotNetReadsAGeneratedFileAsWeDo(int seed)
        {
            var (_, text, options) = Generate(seed);
            CompareWithRepresentationModel(text, Schema, options, "seed " + seed);
        }

        static void CompareWithRepresentationModel(string text, RecordSchema schema, RecordReadOptions options, string what)
        {
            var read = RecordFileReader.Read(text, schema, options);
            Assert.False(read.HasErrors, what);
            int close = text.IndexOf("\n---\n", 3, StringComparison.Ordinal);
            var stream = new YamlStream();
            stream.Load(new StringReader(text.Substring(4, close + 1 - 4)));
            Assert.Single(stream.Documents);
            var expected = Tree(read.Document!, schema);
            var actual = Tree(stream.Documents[0].RootNode);
            Assert.True(expected == actual, what + ": YamlDotNet's tree differs from ours.\nOurs:\n" + expected + "\nYamlDotNet's:\n" + actual + "\n" + text);
        }

        /// <summary>A YAML tree as text, one node per line, with each scalar's style: the profile's (double-quoted text, literal blocks, plain numbers and
        /// Booleans) is part of what is compared.</summary>
        static string Tree(YamlDotNet.RepresentationModel.YamlNode node)
        {
            var sb = new StringBuilder();
            Walk(node, 0);
            return sb.ToString();

            void Walk(YamlDotNet.RepresentationModel.YamlNode n, int indent)
            {
                switch (n)
                {
                    case YamlScalarNode s:
                        sb.Append(Style(s.Style)).Append(' ').Append(RecordConformanceTests.JsonString(s.Value ?? string.Empty)).Append('\n');
                        break;
                    case YamlMappingNode m:
                        sb.Append("map\n");
                        foreach (var kv in m.Children)
                        {
                            sb.Append(' ', indent + 2).Append(((YamlScalarNode)kv.Key).Value).Append(": ");
                            Walk(kv.Value, indent + 2);
                        }
                        break;
                    case YamlSequenceNode q:
                        sb.Append("list\n");
                        foreach (var item in q.Children) { sb.Append(' ', indent + 2).Append("- "); Walk(item, indent + 2); }
                        break;
                }
            }
        }

        static string Style(YamlDotNet.Core.ScalarStyle style)
            => style == YamlDotNet.Core.ScalarStyle.DoubleQuoted ? "quoted" : style == YamlDotNet.Core.ScalarStyle.Literal ? "block" : style == YamlDotNet.Core.ScalarStyle.Plain ? "plain" : style.ToString();

        /// <summary>Our document as the YAML tree a canonical file spells it: front-matter keys in their order, objects with guid first and the schema's order,
        /// text quoted unless it holds a character only a block of inline FieldWorks Markdown carries.</summary>
        static string Tree(RecordDocument d, RecordSchema schema)
        {
            var sb = new StringBuilder("map\n");
            void Scalar(string key, string style, string value) => sb.Append("  ").Append(key).Append(": ").Append(style).Append(' ').Append(RecordConformanceTests.JsonString(value)).Append('\n');
            Scalar("fwmd", "plain", "1");
            Scalar("record", "quoted", d.Kind);
            Scalar("schema", "quoted", schema.Id);
            if (d.Root != null) Scalar("root", "quoted", d.Root);
            if (d.Base != null) Scalar("base", "quoted", d.Base);
            if (d.Analysis != null) Scalar("analysis", "quoted", d.Analysis);
            if (d.Vernacular != null) Scalar("vernacular", "quoted", d.Vernacular);
            if (d.WritingSystems.Count > 0)
            {
                sb.Append("  writingSystems: map\n");
                foreach (var w in d.WritingSystems)
                {
                    sb.Append("    ").Append(w.Key).Append(": map\n");
                    sb.Append("      lang: quoted ").Append(RecordConformanceTests.JsonString(w.Value.Language)).Append('\n');
                    sb.Append("      dir: quoted ").Append(RecordConformanceTests.JsonString(w.Value.Direction)).Append('\n');
                    sb.Append("      font: quoted ").Append(RecordConformanceTests.JsonString(w.Value.Font)).Append('\n');
                }
            }
            var kind = schema.Kind(d.Kind)!;
            var (type, body, fragment) = Layout(kind, d.Root);
            var frame = new StringBuilder();
            ObjectTree(frame, d.Data, type, 4, body);
            if (frame.Length > 0) sb.Append("  record-data: map\n").Append(frame);
            _ = fragment;
            return sb.ToString();
        }

        /// <summary>The type of the data a root names, and the field that is the body (as the reader resolves it).</summary>
        static (RecordObjectType type, string? body, string? fragment) Layout(RecordKind kind, string? root)
        {
            var type = kind.Root;
            if (root == null) return (type, kind.BodyField?.Name, null);
            var path = ProjectPath.Parse(root)!;
            bool record = path.Segments.Count == kind.PathSegments;
            for (int i = kind.PathSegments; i < path.Segments.Count; i++)
            {
                var field = type.Field(path.Segments[i].Name)!;
                if (field.Kind == RecordFieldKind.Sequence) { type = field.Item!.ObjectType!; i++; }
                else type = field.ObjectType!;
            }
            if (path.Field != null)
            {
                var field = type.Field(path.Field)!;
                bool isBody = field.Kind == RecordFieldKind.Rich && (path.FieldWritingSystem != null || !field.MultiWritingSystem);
                return (type, isBody ? field.Name : null, path.Field);
            }
            return (type, record ? kind.BodyField?.Name : null, null);
        }

        static void ObjectTree(StringBuilder sb, RecordObject o, RecordObjectType type, int indent, string? skip)
        {
            if (o.Guid != null) sb.Append(' ', indent).Append("guid: quoted ").Append(RecordConformanceTests.JsonString(o.Guid.Value.ToString("D"))).Append('\n');
            foreach (var field in type.Fields)
            {
                if (field.Name == skip) continue;
                var value = o[field.Name];
                if (value == null || value.IsEmpty) continue;
                sb.Append(' ', indent).Append(field.Name).Append(": ");
                ValueTree(sb, field, value, indent);
            }
        }

        static void ValueTree(StringBuilder sb, RecordField field, RecordValue value, int indent)
        {
            if (field.MultiWritingSystem)
            {
                sb.Append("map\n");
                foreach (var alt in ((RecordAlternatives)value).Items.Where(a => !a.Value.IsEmpty))
                {
                    sb.Append(' ', indent + 2).Append(alt.Key).Append(": ");
                    LeafTree(sb, field, alt.Value);
                }
                return;
            }
            switch (field.Kind)
            {
                case RecordFieldKind.Sequence:
                    sb.Append("list\n");
                    foreach (var item in ((RecordList)value).Items)
                    {
                        sb.Append(' ', indent + 2).Append("- ");
                        if (field.Item!.Kind == RecordFieldKind.Object) { sb.Append("map\n"); ObjectTree(sb, (RecordObject)item, field.Item.ObjectType!, indent + 4, null); }
                        else LeafTree(sb, field.Item, (RecordScalar)item);
                    }
                    return;
                case RecordFieldKind.Object:
                    sb.Append("map\n");
                    ObjectTree(sb, (RecordObject)value, field.ObjectType!, indent + 2, null);
                    return;
                default:
                    LeafTree(sb, field, (RecordScalar)value);
                    return;
            }
        }

        static void LeafTree(StringBuilder sb, RecordField field, RecordScalar value)
        {
            string style, text = value.Text;
            switch (field.Kind)
            {
                case RecordFieldKind.Integer:
                case RecordFieldKind.Boolean: style = "plain"; break;
                // A value of paragraphs is a clip block (|, owner decision R-8), which keeps one final line end; one paragraph is |-.
                case RecordFieldKind.Rich: style = "block"; if (field.Paragraphs) text += "\n"; break;
                case RecordFieldKind.Text:
                case RecordFieldKind.Notation:
                    if (Quotable(text)) style = "quoted";
                    else
                    {
                        // The one place the YAML holds something else than the value: inline FieldWorks Markdown that spells it.
                        style = "block";
                        text = RecordProfile.ForBlock(MarkdownConverter.ToInlineMarkdown(new[] { new Run { Text = text, Ws = "x" } }, new MarkdownOptions { WritingSystems = new FixedWritingSystems("x", "x") }));
                    }
                    break;
                default: style = "quoted"; break;
            }
            sb.Append(style).Append(' ').Append(RecordConformanceTests.JsonString(text)).Append('\n');
        }

        static bool Quotable(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c < ' ' || (c >= '\x7F' && c <= '\x9F') || c == '\u2028' || c == '\u2029' || c == '\uFEFF' || c == '\uFFFE' || c == '\uFFFF') return false;
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) { i++; continue; }
                if (char.IsSurrogate(c)) return false;
            }
            return true;
        }

        // ---------------------------------------------------------------- Pandoc

        [PandocTheory]
        [MemberData(nameof(PandocCases))]
        public void PandocReadsACanonicalFixtureAsWeDo(string id)
        {
            Assert.True(PandocRunner.Available, "Pandoc is not on the PATH, and this run says it must be (CI)");
            var c = RecordFixtures.Find(id);
            ComparePandoc(c.CanonicalText!, c.Schema, RecordFixtures.Options(c), id);
        }

        [PandocTheory]
        [MemberData(nameof(PandocSeeds))]
        public void PandocReadsAGeneratedFileAsWeDo(int seed)
        {
            Assert.True(PandocRunner.Available, "Pandoc is not on the PATH, and this run says it must be (CI)");
            var (_, text, options) = Generate(seed);
            ComparePandoc(text, Schema, options, "seed " + seed);
        }

        /// <summary>Pandoc reads the file (its metadata view is not authoritative for the frame, ADR 0032, so the frame is compared by shape), and reads every rich
        /// value and the body as the FieldWorks Markdown reader does: a value of paragraphs (a <c>|</c> block) as blocks, one paragraph (<c>|-</c>) as inlines.</summary>
        static void ComparePandoc(string text, RecordSchema schema, RecordReadOptions options, string what)
        {
            var read = RecordFileReader.Read(text, schema, options);
            Assert.False(read.HasErrors, what);
            var document = read.Document!;
            var root = Json.Obj(Json.Parse(PandocRunner.ToJson(text)));
            var meta = Json.Obj(root["meta"]);
            var kind = schema.Kind(document.Kind)!;
            var (type, body, _) = Layout(kind, document.Root);
            string analysis = document.Analysis ?? options.Markdown.WritingSystems.DefaultAnalysis;
            string vernacular = document.Vernacular ?? options.Markdown.WritingSystems.DefaultVernacular;
            var frame = new StringBuilder();
            if (meta.TryGetValue("record-data", out var data)) MetaObject(Json.Obj(data), document.Data, type, body, options, analysis, vernacular, what, document.Data.Path);
            else Assert.True(document.Data.Fields.All(f => f.Value.IsEmpty || f.Key == body) && document.Data.Guid == null, what + ": Pandoc has no record-data");
            if (body != null)
            {
                var value = document.Data[body];
                var scalar = value as RecordScalar ?? (value as RecordAlternatives)?.Items.FirstOrDefault().Value;
                var bodyField = type.Field(body)!;
                string ws = value is RecordAlternatives alts && alts.Items.Count > 0 ? alts.Items[0].Key : bodyField.WritingSystem == RecordWritingSystem.Vernacular ? vernacular : analysis;
                var blocks = Json.Arr(root["blocks"]);
                if (scalar == null || scalar.IsEmpty) Assert.Empty(blocks);
                else
                {
                    var actual = PandocModel.Convert(Document(blocks, ws, vernacular), LeafOptions(options, ws, vernacular));
                    ConformanceTests.AssertParagraphs(scalar.Paragraphs!, actual, what + ": Pandoc reading the body\n" + text);
                }
            }
        }

        static MarkdownOptions LeafOptions(RecordReadOptions options, string analysis, string vernacular) => new MarkdownOptions
        {
            WritingSystems = new FixedWritingSystems(analysis, vernacular),
            Objects = options.Markdown.Objects,
            StyleNames = options.Markdown.StyleNames,
        };

        static void MetaObject(Dictionary<string, object?> meta, RecordObject o, RecordObjectType type, string? skip, RecordReadOptions options, string analysis, string vernacular, string what, string path)
        {
            Assert.Equal("MetaMap", Json.Str(meta["t"]));
            var map = Json.Obj(meta["c"]);
            var keys = new SortedSet<string>(StringComparer.Ordinal);
            if (o.Guid != null) keys.Add("guid");
            foreach (var f in o.Fields) if (!f.Value.IsEmpty && f.Key != skip) keys.Add(f.Key);
            Assert.True(keys.SetEquals(map.Keys), what + " at " + path + ": Pandoc's keys " + string.Join(",", map.Keys) + ", ours " + string.Join(",", keys));
            foreach (var field in type.Fields)
            {
                if (field.Name == skip || !map.TryGetValue(field.Name, out var m)) continue;
                MetaValue(Json.Obj(m), field, o[field.Name]!, options, analysis, vernacular, what, path + "/" + field.Name);
            }
        }

        static void MetaValue(Dictionary<string, object?> meta, RecordField field, RecordValue value, RecordReadOptions options, string analysis, string vernacular, string what, string path)
        {
            string t = Json.Str(meta["t"]);
            if (field.MultiWritingSystem)
            {
                Assert.Equal("MetaMap", t);
                var map = Json.Obj(meta["c"]);
                var alts = (RecordAlternatives)value;
                Assert.True(new HashSet<string>(alts.Items.Where(a => !a.Value.IsEmpty).Select(a => a.Key)).SetEquals(map.Keys), what + " at " + path + ": the writing systems differ");
                foreach (var alt in alts.Items.Where(a => !a.Value.IsEmpty)) MetaLeaf(Json.Obj(map[alt.Key]), field, alt.Value, options, alt.Key, vernacular, what, path + "." + alt.Key);
                return;
            }
            switch (field.Kind)
            {
                case RecordFieldKind.Sequence:
                {
                    Assert.Equal("MetaList", t);
                    var items = Json.Arr(meta["c"]);
                    var list = (RecordList)value;
                    Assert.Equal(list.Items.Count, items.Count);
                    for (int i = 0; i < items.Count; i++)
                    {
                        string itemPath = path + "/" + (i + 1);
                        if (field.Item!.Kind == RecordFieldKind.Object) MetaObject(Json.Obj(items[i]), (RecordObject)list.Items[i], field.Item.ObjectType!, null, options, analysis, vernacular, what, itemPath);
                        else MetaLeaf(Json.Obj(items[i]), field.Item, (RecordScalar)list.Items[i], options, analysis, vernacular, what, itemPath);
                    }
                    return;
                }
                case RecordFieldKind.Object:
                    MetaObject(meta, (RecordObject)value, field.ObjectType!, null, options, analysis, vernacular, what, path);
                    return;
                default:
                    MetaLeaf(meta, field, (RecordScalar)value, options, field.WritingSystem == RecordWritingSystem.Vernacular ? vernacular : analysis, vernacular, what, path);
                    return;
            }
        }

        static void MetaLeaf(Dictionary<string, object?> meta, RecordField field, RecordScalar value, RecordReadOptions options, string ws, string vernacular, string what, string path)
        {
            string t = Json.Str(meta["t"]);
            Assert.True(t == "MetaInlines" || t == "MetaString" || t == "MetaBool" || t == "MetaBlocks", what + " at " + path + ": Pandoc reads " + t);
            if (field.Kind != RecordFieldKind.Rich) return;
            var paragraphs = value.Paragraphs!;
            if (field.Paragraphs)
            {
                // A clip block (|) ends in a line end, so Pandoc reads it as blocks: every paragraph, heading, list and table, as the reader does (R-8).
                Assert.True(t == "MetaBlocks", what + " at " + path + ": Pandoc reads a value of paragraphs as " + t);
                var blocks = PandocModel.Convert(Document(Json.Arr(meta["c"]), ws, vernacular), LeafOptions(options, ws, vernacular));
                ConformanceTests.AssertParagraphs(paragraphs, blocks, what + ": Pandoc reading the rich value at " + path);
                return;
            }
            // One paragraph is a |- block, with no final line end, which Pandoc reads as inlines.
            Assert.Equal("MetaInlines", t);
            var actual = PandocModel.Convert(Document(new List<object?> { new Dictionary<string, object?> { ["t"] = "Para", ["c"] = meta["c"] } }, ws, vernacular), LeafOptions(options, ws, vernacular));
            ConformanceTests.AssertParagraphs(paragraphs.Select(p => Plain(p)).ToList(), actual.Select(p => Plain(p)).ToList(), what + ": Pandoc reading the rich value at " + path);
        }

        /// <summary>A paragraph with its style taken off: Pandoc reads a metadata value's paragraph as inlines, which have no block to carry a heading or list style.</summary>
        static Paragraph Plain(Paragraph p)
        {
            var copy = p.Clone();
            copy.Style = "Normal";
            copy.Props.StrProps[TextPropertyNumbers.NamedStyle] = "Normal";
            return copy;
        }

        /// <summary>A Pandoc JSON document of the given blocks whose metadata names the defaults, for <see cref="PandocModel"/>.</summary>
        static string Document(List<object?> blocks, string analysis, string vernacular)
        {
            var sb = new StringBuilder("{\"pandoc-api-version\":[1,23,1],\"meta\":{\"analysis\":{\"t\":\"MetaString\",\"c\":");
            sb.Append(RecordConformanceTests.JsonString(analysis)).Append("},\"vernacular\":{\"t\":\"MetaString\",\"c\":").Append(RecordConformanceTests.JsonString(vernacular)).Append("}},\"blocks\":");
            JsonOut(sb, blocks);
            return sb.Append('}').ToString();
        }

        static void JsonOut(StringBuilder sb, object? value)
        {
            switch (value)
            {
                case null: sb.Append("null"); break;
                case string s: sb.Append(RecordConformanceTests.JsonString(s)); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
                case List<object?> list:
                    sb.Append('[');
                    for (int i = 0; i < list.Count; i++) { if (i > 0) sb.Append(','); JsonOut(sb, list[i]); }
                    sb.Append(']');
                    break;
                case Dictionary<string, object?> map:
                    sb.Append('{');
                    bool first = true;
                    foreach (var kv in map) { if (!first) sb.Append(','); first = false; sb.Append(RecordConformanceTests.JsonString(kv.Key)).Append(':'); JsonOut(sb, kv.Value); }
                    sb.Append('}');
                    break;
                default: throw new InvalidOperationException("JSON of " + value.GetType().Name);
            }
        }
    }
}
