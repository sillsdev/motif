using System;
using System.Collections.Generic;

namespace SIL.TextEditing.Markdown
{
    /// <summary>What a link is written as (spec 4.4). The caller chooses; the writer never guesses. Forms B to D need a path, and a link with no
    /// known path is written as <see cref="ObjectAlias"/> whatever was asked.</summary>
    public enum MarkdownLinkForm
    {
        /// <summary>A: a path from the project root with <c>guid=</c>: <c>[palit](/lexicon/palit(2)){.name guid=...}</c>. The default (agents, MCP, source mode, the clipboard).</summary>
        RootPath,
        /// <summary>B: a path relative to the file (<see cref="MarkdownOptions.LinkBase"/>), for a full export to files.</summary>
        FileRelative,
        /// <summary>C: a <c>silfw://</c> back-link that opens FieldWorks.</summary>
        SilfwUrl,
        /// <summary>D: a <c>motif://projects/&lt;project&gt;/&lt;path&gt;?guid=</c> back-link that opens Motif.</summary>
        MotifUrl,
        /// <summary>E: <c>/objects/&lt;guid&gt;</c>.</summary>
        ObjectAlias,
    }

    /// <summary>What the reader does with a link whose <c>guid=</c> is unknown in the project (spec 4.8).</summary>
    public enum UnknownGuidPolicy
    {
        /// <summary>The checker for agent submissions: never rebind; mark the link broken and warn (<c>fwmd.link.broken</c>).</summary>
        MarkBroken,
        /// <summary>Interactive paste: bind the link by its path when exactly one object matches, and say so (<c>fwmd.link.rebound</c>).</summary>
        RebindByPath,
    }

    /// <summary>What the converter needs from its host: the writing systems that unmarked text and <c>.v</c> stand for, the style names the block
    /// notation uses, which styles exist, how paths and object data relate, and how links are written. Everything is optional; the defaults
    /// need no project (ADR 0011, ADR 0028).</summary>
    public sealed class MarkdownOptions
    {
        IWritingSystemMapper _writingSystems = new FixedWritingSystems("en", "en");
        MarkdownStyleNames _styleNames = new MarkdownStyleNames();
        IObjectMapper _objects = DefaultObjectMapper.Instance;

        /// <summary>The writing system of unmarked text and of <c>.v</c> when the text has no front matter (the front matter's own defaults win when
        /// it has). Default: <see cref="FixedWritingSystems"/> with "en" for both. Never null.</summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public IWritingSystemMapper WritingSystems
        {
            get => _writingSystems;
            set => _writingSystems = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>The paragraph and hyperlink style names the notation uses. Default: the spec's names. Never null.</summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public MarkdownStyleNames StyleNames
        {
            get => _styleNames;
            set => _styleNames = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>Whether a style name exists in the project; null accepts every name.</summary>
        public IStyleCatalog? Styles { get; set; }

        /// <summary>True (the default) when <see cref="MarkdownConverter.Parse"/> and <see cref="MarkdownConverter.ParseInline"/> return the text of
        /// every run as NFD, the form the editor stores (ADR 0018, issue 75). Only run text is normalised: attribute values (<c>s6</c> object data,
        /// <c>url</c>, <c>guid</c>, <c>sN</c> or <c>pN</c> strings) are opaque and come back byte for byte. Finding and object
        /// offsets refer to the source as written either way. False leaves the text as written.</summary>
        public bool NormalizeText { get; set; } = true;

        /// <summary>What an object run's data means. Default: <see cref="DefaultObjectMapper"/>. Never null.</summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public IObjectMapper Objects
        {
            get => _objects;
            set => _objects = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>Turns a path into a GUID and back (spec 4.1). Null: the core reads and writes paths verbatim and keeps <c>guid=</c>.</summary>
        public IProjectPathResolver? Paths { get; set; }

        /// <summary>True (the default) when <see cref="MarkdownConverter.ToMarkdown(Document, MarkdownOptions)"/> starts the text with the front matter of
        /// spec 2.1 (<c>fwmd: 1</c>, the two default writing systems and the table of the writing systems the text uses). False writes the body only.</summary>
        public bool WriteFrontMatter { get; set; } = true;

        /// <summary>The form links are written in. Default: <see cref="MarkdownLinkForm.RootPath"/> (A).</summary>
        public MarkdownLinkForm LinkForm { get; set; } = MarkdownLinkForm.RootPath;

        /// <summary>For <see cref="MarkdownLinkForm.FileRelative"/>: the path of the file being written, such as <c>/lists/GDR/GDR-47.fw.md</c>. Also the
        /// base against which the reader resolves a relative link; without one a relative link is <c>fwmd.link.relative-without-base</c>.</summary>
        public string? LinkBase { get; set; }

        /// <summary>For <see cref="MarkdownLinkForm.MotifUrl"/>: the Known project's name in <c>motif://projects/&lt;project&gt;/...</c>. The reader takes a Motif
        /// link that names another project to be a cross-project link; with no name set every named project is another, so the link keeps its project in the stored URL.</summary>
        public string? ProjectName { get; set; }

        /// <summary>The <c>database=</c> a <c>silfw://</c> URL names for this project. Default: <c>this$</c>, which FieldWorks writes for the current project.</summary>
        public string SilfwDatabase { get; set; } = SilfwUrl.CurrentProject;

        /// <summary>What the reader does with a <c>guid=</c> the project does not know. Default: <see cref="UnknownGuidPolicy.MarkBroken"/>.</summary>
        public UnknownGuidPolicy UnknownGuids { get; set; } = UnknownGuidPolicy.MarkBroken;
    }

    /// <summary>The language tag, direction and default font of one writing system, as the front matter lists them (spec 2.1).</summary>
    public sealed class WritingSystemInfo
    {
        public WritingSystemInfo(string language, string direction, string font)
        {
            Language = language ?? throw new ArgumentNullException(nameof(language));
            Direction = direction ?? throw new ArgumentNullException(nameof(direction));
            Font = font ?? throw new ArgumentNullException(nameof(font));
        }

        public string Language { get; }
        /// <summary><c>ltr</c> or <c>rtl</c>.</summary>
        public string Direction { get; }
        public string Font { get; }

        /// <summary>The fallback for a host that does not describe its writing systems: the id as the language tag, left to right unless the language is
        /// written right to left, and FieldWorks' default font.</summary>
        public static WritingSystemInfo Default(string id)
        {
            string primary = id;
            int dash = id.IndexOf('-');
            if (dash > 0) primary = id.Substring(0, dash);
            bool rtl = Array.IndexOf(RtlLanguages, primary.ToLowerInvariant()) >= 0
                || id.IndexOf("-Arab", StringComparison.OrdinalIgnoreCase) >= 0 || id.IndexOf("-Hebr", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("-Thaa", StringComparison.OrdinalIgnoreCase) >= 0 || id.IndexOf("-Syrc", StringComparison.OrdinalIgnoreCase) >= 0;
            return new WritingSystemInfo(id, rtl ? "rtl" : "ltr", "Charis SIL");
        }

        static readonly string[] RtlLanguages = { "ar", "he", "fa", "ur", "yi", "ps", "sd", "ug", "dv", "ckb", "syr" };
    }

    /// <summary>Optional: a writing-system mapper that also says what the front matter lists for each writing system. When
    /// <see cref="MarkdownOptions.WritingSystems"/> implements it the writer uses it; otherwise <see cref="WritingSystemInfo.Default"/>.</summary>
    public interface IWritingSystemDetails
    {
        /// <summary>The language tag, direction and font of the writing system, or null when the project does not know it.</summary>
        WritingSystemInfo? DescribeWritingSystem(string id);
    }

    /// <summary>The writing systems the notation leaves implicit (spec 3.2). A run's <see cref="Run.Ws"/> is a writing-system id, so
    /// the host supplies the two defaults and says which ids exist; it needs no libpalaso type here.</summary>
    public interface IWritingSystemMapper
    {
        /// <summary>The writing system of unmarked text: the project's default analysis (or the base's, at import).</summary>
        string DefaultAnalysis { get; }

        /// <summary>The writing system <c>.v</c> stands for: the project's default vernacular.</summary>
        string DefaultVernacular { get; }

        /// <summary>True when the id is a writing system of the project; false makes the parser report <c>fwmd.ws.unknown</c>.</summary>
        bool IsKnown(string tag);
    }

    /// <summary>Optional: a writing-system mapper that also lists the tags of the project. When <see cref="MarkdownOptions.WritingSystems"/>
    /// implements it, <c>fwmd.ws.unknown</c> carries them as its choices and the closest ones as its nearest.</summary>
    public interface IKnownWritingSystems
    {
        /// <summary>The ids of the project's writing systems (the spec's choices for <c>fwmd.ws.unknown</c>: its analysis and vernacular systems).</summary>
        IReadOnlyList<string> KnownTags { get; }
    }

    /// <summary>Optional: a writing-system mapper that can say more than yes or no about a tag. When <see cref="MarkdownOptions.WritingSystems"/>
    /// implements it, the parser calls <see cref="CheckWritingSystem"/> instead of <see cref="IWritingSystemMapper.IsKnown"/>.</summary>
    public interface IWritingSystemChecker
    {
        /// <summary>Null when the tag is fine. Otherwise the problem to report on the tag: an error with the default id <c>fwmd.ws.unknown</c>, or a
        /// warning or suggestion for a tag that is accepted but worth a word (a legacy system); the text keeps the tag either way. The host's
        /// <see cref="MarkdownProblem.Choices"/> and <see cref="MarkdownProblem.Nearest"/> are reported; when it leaves them empty and also
        /// implements <see cref="IKnownWritingSystems"/>, its list and the closest tags fill them.</summary>
        MarkdownProblem? CheckWritingSystem(string tag);
    }

    /// <summary>A mapper with two fixed defaults that knows every tag.</summary>
    public sealed class FixedWritingSystems : IWritingSystemMapper
    {
        public FixedWritingSystems(string defaultAnalysis, string defaultVernacular)
        {
            DefaultAnalysis = defaultAnalysis ?? throw new ArgumentNullException(nameof(defaultAnalysis));
            DefaultVernacular = defaultVernacular ?? throw new ArgumentNullException(nameof(defaultVernacular));
        }

        public string DefaultAnalysis { get; }
        public string DefaultVernacular { get; }
        public bool IsKnown(string tag) => true;
    }

    /// <summary>The paragraph styles the block notation maps to (spec 3.1) and the character style a link implies (spec 3.2).
    /// The defaults are the names a new FieldWorks project already has. The table styles have no default: the host names them (spec 3.1), or has none.</summary>
    public sealed class MarkdownStyleNames
    {
        string _normal = "Normal", _heading1 = "Heading 1", _heading2 = "Heading 2", _heading3 = "Heading 3", _bulletedList = "Bulleted List",
            _numberedList = "Numbered List", _blockQuote = "Block Quote", _hyperlink = "Hyperlink";
        string? _tableHeader, _tableRow;

        // No name is null: a null would only show as a NullReferenceException deep in the reader.
        static string Name(string? value) => value ?? throw new ArgumentNullException(nameof(value));

        /// <exception cref="ArgumentNullException">A style name is set to null.</exception>
        public string Normal { get => _normal; set => _normal = Name(value); }
        public string Heading1 { get => _heading1; set => _heading1 = Name(value); }
        public string Heading2 { get => _heading2; set => _heading2 = Name(value); }
        public string Heading3 { get => _heading3; set => _heading3 = Name(value); }
        public string BulletedList { get => _bulletedList; set => _bulletedList = Name(value); }
        public string NumberedList { get => _numberedList; set => _numberedList = Name(value); }
        public string BlockQuote { get => _blockQuote; set => _blockQuote = Name(value); }
        /// <summary>The style of a pipe table's header row, named by the host (Motif's are its GDR template's). Null (the default): the host has no table styles, a pipe table is
        /// <c>fwmd.syntax.unsupported-markup</c> and its rows are read as <see cref="Normal"/>, and the writer never writes a pipe table.</summary>
        public string? TableHeader { get => _tableHeader; set => _tableHeader = value; }
        /// <summary>The style of a pipe table's body rows; see <see cref="TableHeader"/>.</summary>
        public string? TableRow { get => _tableRow; set => _tableRow = value; }
        /// <summary>The character style a link run has unless the notation says otherwise (<c>.nostyle</c>, or <c>custom-style=</c>).</summary>
        public string Hyperlink { get => _hyperlink; set => _hyperlink = Name(value); }

        /// <summary>The recogniser (ADR 0033) for these names: which paragraphs are headings, list items and table rows (the table styles when the host names them), and
        /// <see cref="Normal"/> as the style a flattened block gets. A host gives it to <c>RichTextField.BlockRecognizer</c> so that pasted Markdown is recognised by the
        /// names it was read with, and the record reader uses it to find the blocks a field does not allow.</summary>
        public IBlockRecognizer ToBlockRecognizer() => new StyleBlockRecognizer(
            new[] { Heading1, Heading2, Heading3 }, new[] { BulletedList, NumberedList },
            TableHeader != null && TableRow != null ? new[] { TableHeader, TableRow } : null, Normal);

        internal string? Heading(int level) => level == 1 ? Heading1 : level == 2 ? Heading2 : level == 3 ? Heading3 : null;
    }

    /// <summary>Which style names exist in the project, so the parser can report <c>fwmd.style.unknown</c>.</summary>
    public interface IStyleCatalog
    {
        bool IsParagraphStyle(string name);
        bool IsCharacterStyle(string name);
    }

    /// <summary>Optional: a style catalog that also lists the style names of the project. When <see cref="MarkdownOptions.Styles"/> implements
    /// it, <c>fwmd.style.unknown</c> carries the closest names as its nearest (the spec: <c>Nearest: Example</c>).</summary>
    public interface IKnownStyles
    {
        IReadOnlyList<string> ParagraphStyleNames { get; }
        IReadOnlyList<string> CharacterStyleNames { get; }
    }

    /// <summary>Optional: a style catalog that can say more than yes or no about a style name. When <see cref="MarkdownOptions.Styles"/> implements it,
    /// the parser calls <see cref="CheckStyle"/> instead of <see cref="IStyleCatalog.IsParagraphStyle"/> and <see cref="IStyleCatalog.IsCharacterStyle"/>.</summary>
    public interface IStyleChecker
    {
        /// <summary>Null when the style is fine. Otherwise the problem to report on the name: an error with the default id <c>fwmd.style.unknown</c>, or a
        /// warning or suggestion for a style that is accepted but worth a word. The host's <see cref="MarkdownProblem.Nearest"/> is reported; when it
        /// leaves it empty and also implements <see cref="IKnownStyles"/>, the closest names fill it.</summary>
        MarkdownProblem? CheckStyle(string name, bool paragraph);
    }
}
