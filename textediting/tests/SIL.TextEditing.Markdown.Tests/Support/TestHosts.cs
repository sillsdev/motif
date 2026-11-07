using System;
using System.Collections.Generic;

namespace SIL.TextEditing.Markdown.Tests
{
    /// <summary>A small project for the tests that are not conformance fixtures: links are <c>silfw://</c> URLs (the default mapper's), names by GUID, pictures by GUID.</summary>
    internal sealed class TestObjects : IObjectMapper
    {
        public const char ExternalPath = (char)4;
        public const char PictureType = (char)8;
        public const char OwnName = (char)5;

        /// <summary>Target names by GUID: the host's current names.</summary>
        public Dictionary<Guid, string> Names = new Dictionary<Guid, string>();
        public HashSet<Guid> Pictures = new HashSet<Guid>();

        public static string LinkData(string tool, Guid guid) => ExternalPath + SilfwUrl.Build(guid, tool);

        public static string PictureData(Guid guid) => Conformance.FixtureHost.PictureData(guid);

        public MarkdownObject? Describe(string objData)
        {
            if (objData.Length == 9 && objData[0] == PictureType)
            {
                var chars = objData.Substring(1);
                var bytes = new byte[16];
                for (int i = 0; i < 8; i++) { bytes[2 * i] = (byte)chars[i]; bytes[2 * i + 1] = (byte)(chars[i] >> 8); }
                var guid = new Guid(bytes);
                if (Pictures.Contains(guid)) return new MarkdownObject { Kind = MarkdownObjectKind.Picture, Guid = guid, Caption = "a picture" };
                return null;
            }
            var described = DefaultObjectMapper.Instance.Describe(objData);
            if (described != null && described.Guid is Guid g && Names.TryGetValue(g, out var name)) described.Name = name;
            return described;
        }

        public bool TryCreateObjData(MarkdownObject target, out string objData, out string? problem)
        {
            if (target.Kind == MarkdownObjectKind.Picture)
            {
                problem = null;
                if (target.Guid is Guid pg && Pictures.Contains(pg)) { objData = PictureData(pg); return true; }
                objData = string.Empty;
                problem = "no picture " + target.Guid;
                return false;
            }
            return DefaultObjectMapper.Instance.TryCreateObjData(target, out objData, out problem);
        }
    }

    internal sealed class TestWritingSystems : IWritingSystemMapper
    {
        public string DefaultAnalysis => "en";
        public string DefaultVernacular => "ceb";
        public bool IsKnown(string tag) => tag != "zzz-unknown";
    }

    internal static class Hosts
    {
        /// <summary>The table styles the tests supply; the library names none.</summary>
        public static MarkdownStyleNames Tables() => new MarkdownStyleNames { TableHeader = "GDR Table Header", TableRow = "GDR Table Row" };

        /// <summary>Options for a test of the body of a text: no front matter is written.</summary>
        public static MarkdownOptions Project(IObjectMapper? objects = null) => new MarkdownOptions
        {
            WritingSystems = new TestWritingSystems(),
            Objects = objects ?? new TestObjects(),
            WriteFrontMatter = false,
            StyleNames = Tables(),
        };
    }
}
