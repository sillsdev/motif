using System.Collections.Generic;
using SIL.TextEditing.Avalonia;
using SIL.WritingSystems;

namespace SIL.TextEditing.Playground
{
    /// <summary>One sample writing system as a project would list it: a tag, a name, and whether it is an analysis or
    /// a vernacular language. Sample data only.</summary>
    public sealed class SampleWritingSystem
    {
        public SampleWritingSystem(string tag, string name, bool vernacular, string font, double size, bool rtl = false)
        {
            Tag = tag;
            Name = name;
            Vernacular = vernacular;
            Definition = WritingSystemRegistry.Define(tag, font, (float)size, rtl);
        }

        public string Tag { get; }
        public string Name { get; }
        public bool Vernacular { get; }
        public WritingSystemDefinition Definition { get; }
        public string Kind => Vernacular ? "vernacular" : "analysis";
    }

    /// <summary>The writing systems the playground offers: English and French for analysis; Arabic, Swahili, Hindi,
    /// Tamil and Thai for vernacular.</summary>
    public static class SampleWritingSystems
    {
        public const string DefaultAnalysis = "en";
        public const string DefaultVernacular = "swh";

        public static IReadOnlyList<SampleWritingSystem> All { get; } = new List<SampleWritingSystem>
        {
            new SampleWritingSystem("en", "English", false, "Segoe UI", 16),
            new SampleWritingSystem("fr", "French", false, "Segoe UI", 16),
            new SampleWritingSystem("ar", "Arabic", true, "Segoe UI", 18, rtl: true),
            new SampleWritingSystem("swh", "Swahili", true, "Segoe UI", 16),
            new SampleWritingSystem("hi", "Hindi", true, "Nirmala UI", 18),
            new SampleWritingSystem("ta", "Tamil", true, "Nirmala UI", 18),
            new SampleWritingSystem("th", "Thai", true, "Leelawadee UI", 18),
        };

        public static SampleWritingSystem? Find(string tag)
        {
            foreach (var ws in All)
                if (ws.Tag == tag) return ws;
            return null;
        }

        /// <summary>The library's default registry (which also knows he and x-pua) plus the sample writing systems.</summary>
        public static WritingSystemRegistry CreateRegistry()
        {
            var registry = WritingSystemRegistry.CreateDefault();
            foreach (var ws in All) registry.Add(ws.Definition);
            return registry;
        }
    }
}
