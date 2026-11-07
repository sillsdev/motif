using System;
using System.Collections.Generic;
using System.Linq;

namespace SIL.TextEditing.Markdown.Records
{
    /// <summary>The ids of the findings the record-file reader reports (ADR 0032). <c>fwmd.yaml.*</c> are about the YAML of the frame,
    /// <c>fwmd.record.*</c> about what it says against the record schema. Findings inside a rich value keep the FieldWorks Markdown reader's own ids
    /// (<see cref="MarkdownDiagnosticIds"/>), and the reader also reports <c>fwmd.version.unsupported</c> and <c>fwmd.ws.unknown</c>. "Information" is
    /// <see cref="MarkdownSeverity.Suggestion"/>: the file is read as it means, and the writer spells it canonically next time (decision 9).</summary>
    public static class RecordDiagnosticIds
    {
        /// <summary>Error: YAML that does not parse. The rest of the front matter is not read.</summary>
        public const string YamlSyntax = "fwmd.yaml.syntax";
        /// <summary>Error: a tab in the indentation of a front-matter line. Fix: the indentation with two spaces for each tab.</summary>
        public const string Tab = "fwmd.yaml.tab";
        /// <summary>Error: a raw U+2028, U+2029 or U+0085, which one YAML reader takes as a line break and another does not. Fix in a rich value: its
        /// character reference (<c>&amp;#x2028;</c>); elsewhere none.</summary>
        public const string LineSeparator = "fwmd.yaml.line-separator";
        /// <summary>Error: a key a mapping already has. The first is read; the fix removes the later pair's lines (none when the pair shares a line).</summary>
        public const string DuplicateKey = "fwmd.yaml.duplicate-key";
        /// <summary>Error: an anchor (<c>&amp;x</c>). Fix: remove it.</summary>
        public const string Anchor = "fwmd.yaml.anchor";
        /// <summary>Error: an alias (<c>*x</c>), usually a bound-root form written without quotes. Fix: the text as a double-quoted string.</summary>
        public const string Alias = "fwmd.yaml.alias";
        /// <summary>Error: a tag (<c>!!str</c>, <c>!x</c>). Fix: remove it; the schema types every value.</summary>
        public const string Tag = "fwmd.yaml.tag";
        /// <summary>Error: a merge key (<c>&lt;&lt;</c>). It is not read; the fix removes its lines (none when it shares a line).</summary>
        public const string MergeKey = "fwmd.yaml.merge-key";
        /// <summary>Error: a folded scalar (<c>&gt;</c>), which rewrites line ends. Fix: the value it reads as, as a literal block.</summary>
        public const string Folded = "fwmd.yaml.folded";
        /// <summary>Error: a second YAML document: a <c>---</c> line with text after it in the front matter, or a YAML block in the body, which Pandoc
        /// merges into the metadata. It is not read; the fix removes it.</summary>
        public const string SecondDocument = "fwmd.yaml.second-document";
        /// <summary>Error: YAML nested deeper than the reader builds (64 levels).</summary>
        public const string TooDeep = "fwmd.yaml.too-deep";
        /// <summary>Information: a comment. It is dropped, because the project has nowhere to keep it. Fix: remove it.</summary>
        public const string CommentDropped = "fwmd.yaml.comment-dropped";
        /// <summary>Information: a plain (unquoted) scalar where the schema expects text. It is read as the text it spells. Fix: the value double-quoted.</summary>
        public const string Unquoted = "fwmd.yaml.unquoted";
        /// <summary>Information: a value in a style that is not canonical for its field (single quotes, a quoted number, a rich value that is not
        /// <c>|-</c>, an uppercase GUID). Fix: the canonical spelling.</summary>
        public const string ScalarStyle = "fwmd.yaml.scalar-style";
        /// <summary>Information: a flow collection (<c>{...}</c>, <c>[...]</c>, JSON). It is read; the writer writes block style.</summary>
        public const string Flow = "fwmd.yaml.flow";
        /// <summary>Information: an empty value (<c>key:</c>, <c>~</c>, <c>""</c>, <c>[]</c>, <c>{}</c>). It means the field is empty, which a canonical file
        /// says by leaving the key out. Fix: remove the line, when the key has a line of its own.</summary>
        public const string Empty = "fwmd.yaml.empty";
        /// <summary>Information: keys out of the schema's order (<c>guid</c> not first, fields out of order).</summary>
        public const string KeyOrder = "fwmd.yaml.key-order";

        /// <summary>Error: the text is not a record file: no front matter, or no <c>fwmd</c> or <c>record</c> key.</summary>
        public const string NotARecord = "fwmd.record.not-a-record";
        /// <summary>Error: a front-matter key a record file does not have (owner decision R-5).</summary>
        public const string UnknownKey = "fwmd.record.unknown-key";
        /// <summary>Error: a record kind the schema does not have.</summary>
        public const string UnknownKind = "fwmd.record.unknown-kind";
        /// <summary>Error: <c>schema:</c> names another schema than the reader's. Fix: the reader's schema id.</summary>
        public const string SchemaMismatch = "fwmd.record.schema-mismatch";
        /// <summary>Error: a key an object of its type does not have.</summary>
        public const string UnknownField = "fwmd.record.unknown-field";
        /// <summary>Error: a value of the wrong shape for its field: a mapping, list or scalar where the schema has another.</summary>
        public const string WrongShape = "fwmd.record.wrong-shape";
        /// <summary>Error: not a whole number in an integer field.</summary>
        public const string NotAnInteger = "fwmd.record.not-an-integer";
        /// <summary>Error: not <c>true</c> or <c>false</c> in a Boolean field. Fix for <c>yes</c>, <c>no</c>, <c>on</c>, <c>off</c> and other spellings YAML 1.1 reads as Booleans.</summary>
        public const string NotABoolean = "fwmd.record.not-a-boolean";
        /// <summary>Error: not a GUID in a GUID field.</summary>
        public const string BadGuid = "fwmd.record.bad-guid";
        /// <summary>Error: one GUID on two objects of the file.</summary>
        public const string DuplicateGuid = "fwmd.record.duplicate-guid";
        /// <summary>Error: a reference that is not one FieldWorks Markdown link.</summary>
        public const string NotAReference = "fwmd.record.not-a-reference";
        /// <summary>Error: formatting or another writing system in a plain-text value written as a block (a block holds plain text as FieldWorks Markdown so
        /// that control characters can be carried).</summary>
        public const string PlainFieldMarkup = "fwmd.record.plain-field-markup";
        /// <summary>Error: a value or body the reader can read but the writer cannot spell in the profile (a rich value whose first line starts with a space, or
        /// that holds a tab, a control character or a line separator). Fix: what was read, written again, where that reads back the same.</summary>
        public const string Unwritable = "fwmd.record.unwritable";
        /// <summary>Error: a <c>root:</c> that is not a project path, or whose segments after the record's do not walk the schema.</summary>
        public const string BadRoot = "fwmd.record.bad-root";
        /// <summary>Error: a key in <c>record-data:</c> outside the field a partial file's <c>root:</c> names.</summary>
        public const string OutsideRoot = "fwmd.record.outside-root";
        /// <summary>Error: text below the front matter of a file whose record has no body field.</summary>
        public const string UnexpectedBody = "fwmd.record.unexpected-body";
        /// <summary>Error: the body field written in <c>record-data:</c>; its value is the body (owner decision R-6).</summary>
        public const string BodyInFrame = "fwmd.record.body-in-frame";
        /// <summary>Error: a table, list or heading in a rich field whose block features (<see cref="RecordField.BlockFeatures"/>) do not allow it (ADR 0033, owner
        /// decision Q79). One finding for each block, on its lines. Fix: the block as plain paragraphs, exactly as a paste into a field that does not allow it
        /// (<see cref="BlockFlattener"/>); none where the value is not a literal block.</summary>
        public const string BlockNotAllowed = "fwmd.record.block-not-allowed";
        /// <summary>The default id of a host notation checker's finding that gives no code of its own.</summary>
        public const string Notation = "fwmd.record.notation";

        /// <summary>Every id above, for documentation and the conformance check.</summary>
        public static IReadOnlyList<string> All { get; } = new[]
        {
            YamlSyntax, Tab, LineSeparator, DuplicateKey, Anchor, Alias, Tag, MergeKey, Folded, SecondDocument, TooDeep, CommentDropped, Unquoted, ScalarStyle,
            Flow, Empty, KeyOrder, NotARecord, UnknownKey, UnknownKind, SchemaMismatch, UnknownField, WrongShape, NotAnInteger, NotABoolean, BadGuid,
            DuplicateGuid, NotAReference, PlainFieldMarkup, Unwritable, BadRoot, OutsideRoot, UnexpectedBody, BodyInFrame, BlockNotAllowed, Notation,
        };
    }

    /// <summary>The host's checker for text in a field notation (an environment, a feature structure). The reader keeps a notation value as text and asks
    /// this; the notations themselves live in <c>SIL.TextEditing.Project</c>. Its diagnostics are the language service's (ADR 0030), with ranges in the value.</summary>
    public interface INotationChecker
    {
        /// <summary>The problems in <paramref name="text"/>, a value of <paramref name="notation"/> at <paramref name="path"/>; empty when it is fine.
        /// A diagnostic's <see cref="LanguageDiagnostic.Code"/> is its finding id (default <see cref="RecordDiagnosticIds.Notation"/>), and a fix with one
        /// edit is its replacement.</summary>
        IReadOnlyList<LanguageDiagnostic> Check(string notation, string text, string path);
    }

    /// <summary>What the record-file reader needs from its host. Everything is optional.</summary>
    public sealed class RecordReadOptions
    {
        MarkdownOptions _markdown = new MarkdownOptions();

        /// <summary>The options rich values are read with: which writing systems, styles and objects the project has. The file's <c>analysis</c> and
        /// <c>vernacular</c> win over the host's defaults, and in an alternative of a per-writing-system field unmarked text means that writing system.
        /// Never null.</summary>
        public MarkdownOptions Markdown
        {
            get => _markdown;
            set => _markdown = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>The checker for notation values, or null to keep them unchecked.</summary>
        public INotationChecker? Notations { get; set; }
    }

    /// <summary>What the reader made of a record file: the document and every finding, in source order.</summary>
    public sealed class RecordReadResult
    {
        internal RecordReadResult(string source, RecordDocument? document, IReadOnlyList<MarkdownDiagnostic> diagnostics)
        {
            Source = source;
            Document = document;
            Diagnostics = diagnostics;
        }

        /// <summary>The text the findings' offsets refer to.</summary>
        public string Source { get; }

        /// <summary>The document, or null when the text is not a record file. When there are errors it is a best effort.</summary>
        public RecordDocument? Document { get; }

        public IReadOnlyList<MarkdownDiagnostic> Diagnostics { get; }

        public bool HasErrors => Diagnostics.Any(d => d.Severity == MarkdownSeverity.Error);
    }

    /// <summary>Raised by the writer when a document does not fit its schema (a field the type does not have, a value of the wrong shape) or holds a value the
    /// profile cannot spell (an object in a list with nothing in it).</summary>
    public sealed class RecordWriteException : InvalidOperationException
    {
        public RecordWriteException(string message) : base(message) { }
    }
}
