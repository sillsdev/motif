using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SIL.TextEditing.Markdown;
using SIL.TextEditing.Markdown.Records;

namespace SIL.TextEditing.CommandLine;

internal sealed class ToolConfig
{
    public string Analysis { get; private set; } = "en";
    public string Vernacular { get; private set; } = "en";
    readonly Dictionary<string, WritingSystemInfo> _systems = new(StringComparer.Ordinal);
    Catalog? _styles;
    public bool ChecksStyles => _styles != null;

    public static ToolConfig Load(string path)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var root = json.RootElement;
        Require(root, JsonValueKind.Object, "config");
        var config = new ToolConfig();
        foreach (var property in root.EnumerateObject())
        {
            switch (property.Name)
            {
                case "analysis": config.Analysis = Text(property.Value, property.Name); break;
                case "vernacular": config.Vernacular = Text(property.Value, property.Name); break;
                case "writingSystems":
                    Require(property.Value, JsonValueKind.Object, property.Name);
                    foreach (var ws in property.Value.EnumerateObject())
                    {
                        Require(ws.Value, JsonValueKind.Object, ws.Name);
                        var fallback = WritingSystemInfo.Default(ws.Name);
                        string language = fallback.Language, direction = fallback.Direction, font = fallback.Font;
                        foreach (var detail in ws.Value.EnumerateObject())
                        {
                            string value = Text(detail.Value, detail.Name);
                            switch (detail.Name)
                            {
                                case "language": language = value; break;
                                case "direction":
                                    if (value != "ltr" && value != "rtl") throw new UsageException("direction must be ltr or rtl.");
                                    direction = value; break;
                                case "font": font = value; break;
                                default: throw new UsageException("Unknown writing-system property: " + detail.Name);
                            }
                        }
                        config._systems.Add(ws.Name, new WritingSystemInfo(language, direction, font));
                    }
                    break;
                case "styles":
                    Require(property.Value, JsonValueKind.Object, property.Name);
                    var paragraphs = new List<string>();
                    var characters = new List<string>();
                    foreach (var styles in property.Value.EnumerateObject())
                    {
                        var list = styles.Name switch
                        {
                            "paragraph" => paragraphs,
                            "character" => characters,
                            _ => throw new UsageException("Unknown styles property: " + styles.Name)
                        };
                        Require(styles.Value, JsonValueKind.Array, styles.Name);
                        foreach (var style in styles.Value.EnumerateArray()) list.Add(Text(style, styles.Name));
                    }
                    config._styles = new Catalog(paragraphs, characters);
                    break;
                default: throw new UsageException("Unknown config property: " + property.Name);
            }
        }
        return config;
    }

    static void Require(JsonElement value, JsonValueKind kind, string name)
    {
        if (value.ValueKind != kind) throw new UsageException(name + " must be a JSON " + kind + ".");
    }

    static string Text(JsonElement value, string name)
    {
        Require(value, JsonValueKind.String, name);
        var text = value.GetString()!;
        if (string.IsNullOrWhiteSpace(text)) throw new UsageException(name + " must not be empty.");
        return text;
    }

    public MarkdownOptions Options(MarkdownFrontMatter? front, RecordDocument? record)
    {
        var systems = new Dictionary<string, WritingSystemInfo>(_systems, StringComparer.Ordinal);
        if (front != null) foreach (var ws in front.WritingSystems) systems[ws.Key] = ws.Value;
        if (record != null) foreach (var ws in record.WritingSystems) systems[ws.Key] = ws.Value;
        string analysis = record?.Analysis ?? front?.Analysis ?? Analysis;
        string vernacular = record?.Vernacular ?? front?.Vernacular ?? Vernacular;
        systems.TryAdd(analysis, WritingSystemInfo.Default(analysis));
        systems.TryAdd(vernacular, WritingSystemInfo.Default(vernacular));
        return new MarkdownOptions
        {
            WritingSystems = new Systems(analysis, vernacular, systems),
            Styles = _styles,
            Objects = new PictureMapper(),
            WriteFrontMatter = front != null,
        };
    }

    sealed class Systems(string analysis, string vernacular, Dictionary<string, WritingSystemInfo> systems)
        : IWritingSystemMapper, IKnownWritingSystems, IWritingSystemDetails
    {
        public string DefaultAnalysis => analysis;
        public string DefaultVernacular => vernacular;
        public bool IsKnown(string tag) => systems.ContainsKey(tag);
        public IReadOnlyList<string> KnownTags { get; } = systems.Keys.ToArray();
        public WritingSystemInfo? DescribeWritingSystem(string id) => systems.GetValueOrDefault(id);
    }

    sealed class Catalog(List<string> paragraphs, List<string> characters) : IStyleCatalog, IKnownStyles
    {
        readonly HashSet<string> _paragraphs = new(paragraphs, StringComparer.Ordinal);
        readonly HashSet<string> _characters = new(characters, StringComparer.Ordinal);
        public bool IsParagraphStyle(string name) => _paragraphs.Contains(name);
        public bool IsCharacterStyle(string name) => _characters.Contains(name);
        public IReadOnlyList<string> ParagraphStyleNames => paragraphs;
        public IReadOnlyList<string> CharacterStyleNames => characters;
    }
}
