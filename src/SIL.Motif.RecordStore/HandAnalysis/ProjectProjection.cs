using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Xml.Linq;
using SIL.Motif.Contract.HandAnalyses;
using SIL.Motif.Contract.HandAnalysis;

namespace SIL.Motif.RecordStore.HandAnalysis;

/// <summary>The project-wide facts of a pinned point: morph types, writing systems and the category tree.</summary>
internal sealed class ProjectProjection
{
    private static readonly Dictionary<Guid, (string Role, bool Affix)> Roles = new()
    {
        [Guid.Parse("d7f713e4-e8cf-11d3-9764-00c04f186933")] = ("bound-root", false),
        [Guid.Parse("d7f713e7-e8cf-11d3-9764-00c04f186933")] = ("bound-stem", false),
        [Guid.Parse("d7f713df-e8cf-11d3-9764-00c04f186933")] = ("circumfix", true),
        [Guid.Parse("c2d140e5-7ca9-41f4-a69a-22fc7049dd2c")] = ("clitic", false),
        [Guid.Parse("0cc8c35a-cee9-434d-be58-5d29130fba5b")] = ("discontiguous-phrase", false),
        [Guid.Parse("d7f713e1-e8cf-11d3-9764-00c04f186933")] = ("enclitic", false),
        [Guid.Parse("d7f713da-e8cf-11d3-9764-00c04f186933")] = ("infix", true),
        [Guid.Parse("56db04bf-3d58-44cc-b292-4c8aa68538f4")] = ("particle", false),
        [Guid.Parse("a23b6faa-1052-4f4d-984b-4b338bdaf95f")] = ("phrase", false),
        [Guid.Parse("d7f713db-e8cf-11d3-9764-00c04f186933")] = ("prefix", true),
        [Guid.Parse("d7f713e2-e8cf-11d3-9764-00c04f186933")] = ("proclitic", false),
        [Guid.Parse("d7f713e5-e8cf-11d3-9764-00c04f186933")] = ("root", false),
        [Guid.Parse("d7f713dc-e8cf-11d3-9764-00c04f186933")] = ("simulfix", true),
        [Guid.Parse("d7f713e8-e8cf-11d3-9764-00c04f186933")] = ("stem", false),
        [Guid.Parse("d7f713dd-e8cf-11d3-9764-00c04f186933")] = ("suffix", true),
        [Guid.Parse("d7f713de-e8cf-11d3-9764-00c04f186933")] = ("suprafix", true),
        [Guid.Parse("af6537b0-7175-4387-ba6a-36547d37fb13")] = ("prefixing-interfix", true),
        [Guid.Parse("18d9b1c3-b5b6-4c07-b92c-2fe1d2281bd4")] = ("infixing-interfix", true),
        [Guid.Parse("3433683d-08a9-4bae-ae53-2a7798f64068")] = ("suffixing-interfix", true)
    };

    public ImmutableArray<MorphTypeFact> MorphTypes { get; private init; }
    public ImmutableDictionary<Guid, MorphTypeFact> MorphTypeById { get; private init; } = ImmutableDictionary<Guid, MorphTypeFact>.Empty;
    public ImmutableArray<WritingSystemFact> WritingSystems { get; private init; }
    public ImmutableArray<CategoryFact> Categories { get; private init; }

    /// <summary>The marker pairs a form of this type is found under; a clitic is also found unmarked.</summary>
    public IEnumerable<HandAnalysisMarkers> PairsOf(MorphTypeFact type)
    {
        yield return type.Markers;
        if (type.SemanticRole is "clitic" or "proclitic" or "enclitic" && type.Markers != new HandAnalysisMarkers("", ""))
            yield return new("", "");
    }

    public static ProjectProjection Build(IReadOnlyDictionary<Guid, GenericRecord> native, NativeUsageSchema usage,
        ImmutableArray<WsDefinition> systems, string comparisonVersion, CancellationToken ct)
    {
        var schema = new LexSchema(usage);
        var types = ImmutableArray.CreateBuilder<MorphTypeFact>();
        foreach (var list in native.Values.Where(r => usage.Is(r.ClassId, "CmPossibilityList")))
        {
            ct.ThrowIfCancellationRequested();
            var position = 0;
            foreach (var id in LexiconProjection.Seq(list, schema.Possibilities))
            {
                var record = WfiProjection.Get(native, id);
                if (!usage.Is(record.ClassId, "MoMorphType")) break;
                var (role, affix) = Roles.GetValueOrDefault(id, ("custom", false));
                types.Add(new(id, role, affix ? MorphFamily.Affix : MorphFamily.Stem,
                    new(Plain(record, schema.Prefix), Plain(record, schema.Postfix)), position++));
            }
        }

        var categories = ImmutableArray.CreateBuilder<CategoryFact>();
        void Walk(IEnumerable<Guid> ids, Guid? parent)
        {
            var order = 0;
            foreach (var id in ids)
            {
                ct.ThrowIfCancellationRequested();
                var record = WfiProjection.Get(native, id);
                categories.Add(new(id, parent, order++, LexiconProjection.Multi(record, schema.Name),
                    LexiconProjection.Multi(record, schema.Abbreviation)));
                Walk(LexiconProjection.Seq(record, schema.SubPossibilities), id);
            }
        }
        foreach (var list in native.Values.Where(r => usage.Is(r.ClassId, "CmPossibilityList")))
        {
            var members = LexiconProjection.Seq(list, schema.Possibilities).ToArray();
            if (members.Length > 0 && usage.Is(WfiProjection.Get(native, members[0]).ClassId, "PartOfSpeech")) Walk(members, null);
        }

        var project = native.Values.FirstOrDefault(r => usage.Is(r.ClassId, "LangProject"));
        var vernacular = Tags(project, usage, "CurVernWss");
        var analysis = Tags(project, usage, "CurAnalysisWss");
        var facts = systems.Select(s => Facts(s, vernacular, analysis, comparisonVersion)).ToImmutableArray();
        return new()
        {
            MorphTypes = types.ToImmutable(), MorphTypeById = types.ToImmutableDictionary(t => t.Id),
            WritingSystems = facts, Categories = categories.ToImmutable()
        };
    }

    private static string Plain(GenericRecord record, int field) =>
        record.Values.TryGetValue(WfiProjection.Key(field), out var v)
            ? v.ValueKind == System.Text.Json.JsonValueKind.Null ? ""
                : NativeTextReferences.TryRaw(v, out var raw) ? raw ?? "" : throw new InvalidDataException("Malformed native UTF-16 text.") : "";

    private static string[] Tags(GenericRecord? project, NativeUsageSchema usage, string name) =>
        project is not null && usage.FieldId("LangProject", name, 15) is { } field ? Plain(project, field)
            .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries) : [];

    private static WritingSystemFact Facts(WsDefinition system, string[] vernacular, string[] analysis, string comparisonVersion)
    {
        var text = new UTF8Encoding(false).GetString(system.LdmlBytes).TrimStart('﻿');
        var xml = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
        string? Attribute(string element, string attribute) => xml.Descendants().FirstOrDefault(e => e.Name.LocalName == element)
            ?.Attribute(attribute)?.Value;
        var rules = string.Concat(xml.Descendants().Where(e => e.Name.LocalName == "cr").Select(e => e.Value));
        var rtl = xml.Descendants().FirstOrDefault(e => e.Name.LocalName == "characterOrder")?.Value == "right-to-left";
        int? Order(string[] tags) => Array.IndexOf(tags, system.Tag) is var i and >= 0 ? i : null;
        return new(system.Tag, system.Id, Order(vernacular), Order(analysis), rtl, Attribute("font", "name"),
            Attribute("kbd", "id"), text, rules, comparisonVersion, [.. system.LdmlBytes]);
    }
}
