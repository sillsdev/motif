using System.Collections.Generic;
using System.Linq;
using SIL.TextEditing.Avalonia;
using SIL.TextEditing.Markdown;

namespace SIL.TextEditing.Playground
{
    /// <summary>What the playground tells the FieldWorks Markdown converter: unmarked text is the default analysis (en), <c>.v</c> is the default
    /// vernacular (swh), and the known writing systems are the registry's. The Markdown pane writes the body only: the front matter would list
    /// the same writing systems, and a host supplies them. Sample data only.</summary>
    public sealed class SampleMarkdown : IWritingSystemMapper, IKnownWritingSystems, IObjectMapper
    {
        readonly WritingSystemRegistry _registry;

        public SampleMarkdown(WritingSystemRegistry registry)
        {
            _registry = registry;
            KnownTags = SampleWritingSystems.All.Select(w => w.Tag).ToList();
        }

        public string DefaultAnalysis => SampleWritingSystems.DefaultAnalysis;
        public string DefaultVernacular => SampleWritingSystems.DefaultVernacular;
        public bool IsKnown(string tag) => _registry.Contains(tag);
        public IReadOnlyList<string> KnownTags { get; }

        /// <summary>A picture of the sample project is written as an image with its path and GUID; any other object as the default mapper says.</summary>
        public MarkdownObject? Describe(string objData)
        {
            var picture = SamplePictures.Find(objData);
            if (picture != null) return new MarkdownObject { Kind = MarkdownObjectKind.Picture, Guid = picture.Guid, Path = picture.Path, Caption = picture.Caption };
            return DefaultObjectMapper.Instance.Describe(objData);
        }

        /// <summary>A picture is found by its GUID, or by its path when the Markdown has no GUID; an unknown one is <c>fwmd.picture.unknown</c>.</summary>
        public bool TryCreateObjData(MarkdownObject target, out string objData, out string? problem)
        {
            if (target.Kind == MarkdownObjectKind.Picture)
            {
                var picture = SamplePictures.All.FirstOrDefault(p => target.Guid != null ? p.Guid == target.Guid : p.Path == target.Path);
                objData = picture?.ObjData ?? string.Empty;
                problem = picture == null ? "no picture " + (target.Guid?.ToString() ?? target.Path) + " in the sample project" : null;
                return picture != null;
            }
            return DefaultObjectMapper.Instance.TryCreateObjData(target, out objData, out problem);
        }

        /// <summary>The options the Markdown pane writes and reads with. The style names are the playground's stylesheet's (SampleStyles): its plain
        /// paragraphs have no style name, where a FieldWorks project's have "Normal".</summary>
        public MarkdownOptions Options() => new MarkdownOptions
        {
            WritingSystems = this,
            Objects = this,
            WriteFrontMatter = false,
            StyleNames = new MarkdownStyleNames
            {
                Normal = string.Empty,
                Heading1 = "Heading 1",
                BulletedList = "List Bullet",
                NumberedList = "List Number",
                BlockQuote = "Block Quote",
                TableHeader = "Table Header",
                TableRow = "Table Row",
            },
        };
    }
}
