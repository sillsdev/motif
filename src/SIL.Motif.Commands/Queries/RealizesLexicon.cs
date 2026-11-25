using System.Text;
using System.Xml;
using SIL.LCModel;
using SIL.Motif.Contract.Query;

namespace SIL.Motif.Commands.Queries;

/// <summary>A real morpheme the planner may generate with: its identity, its written form and how it attaches.</summary>
/// <param name="EntryGuid">The lexical entry's GUID, lowercase.</param>
/// <param name="MsaGuid">The analysis's GUID, lowercase.</param>
/// <param name="MsaClass">The LibLCM class of the analysis, such as <c>MoStemMsa</c>.</param>
/// <param name="Form">The lexeme form without its hyphen or equals sign.</param>
/// <param name="Kind"><c>stem</c>, <c>prefix</c>, <c>suffix</c> or <c>other</c>.</param>
/// <param name="Tokens">How often the corpus has the form as a word, so a frequent morpheme is preferred.</param>
internal sealed record PlannerCandidate(string EntryGuid, string MsaGuid, string MsaClass, string Form, string Kind, long Tokens = 0)
{
    internal string Id => EntryGuid + ":" + MsaGuid;
}

/// <summary>
/// The lexicon of a FieldWorks project file read in one streaming pass, without opening the project, so a planner can choose test
/// morphemes from a large project in seconds. It reads only the lexeme form, the morph type and the analyses of each entry.
/// </summary>
internal static class RealizesLexicon
{
    /// <summary>The morphemes of the project file; an empty list when the file is not FieldWorks XML.</summary>
    public static IReadOnlyList<PlannerCandidate> Read(string fwdataPath)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        var forms = new Dictionary<string, (string Owner, string Form, Guid? Type)>(StringComparer.Ordinal);
        var msas = new List<(string Guid, string Class, string Owner)>();
        try
        {
            using var reader = XmlReader.Create(fwdataPath, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, IgnoreWhitespace = true });
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.Name != "rt") continue;
                var cls = reader.GetAttribute("class") ?? "";
                var guid = reader.GetAttribute("guid") ?? "";
                var owner = reader.GetAttribute("ownerguid") ?? "";
                var isAllomorph = cls is "MoStemAllomorph" or "MoAffixAllomorph";
                var isMsa = cls.StartsWith("Mo", StringComparison.Ordinal) && cls.EndsWith("Msa", StringComparison.Ordinal);
                if (isMsa) msas.Add((guid.ToLowerInvariant(), cls, owner.ToLowerInvariant()));
                if (cls is not ("LexEntry" or "MoStemAllomorph" or "MoAffixAllomorph")) continue;
                using var element = reader.ReadSubtree();
                string? form = null, lexeme = null;
                Guid? type = null;
                var path = new Stack<string>();
                while (element.Read())
                {
                    switch (element.NodeType)
                    {
                        case XmlNodeType.Element:
                            if (element.Name == "objsur" && path.Count > 0 && Guid.TryParse(element.GetAttribute("guid"), out var target))
                            {
                                if (path.Peek() == "LexemeForm") lexeme ??= target.ToString();
                                else if (path.Peek() == "MorphType") type ??= target;
                            }
                            if (!element.IsEmptyElement) path.Push(element.Name);
                            break;
                        case XmlNodeType.Text when isAllomorph && form is null && path.Count > 1 && path.Peek() == "AUni" && path.Contains("Form"):
                            form = element.Value.Normalize(NormalizationForm.FormC);
                            break;
                        case XmlNodeType.EndElement:
                            if (path.Count > 0) path.Pop();
                            break;
                    }
                }
                if (cls == "LexEntry" && lexeme is not null) entries[guid.ToLowerInvariant()] = lexeme;
                else if (isAllomorph && form is not null) forms[guid.ToLowerInvariant()] = (owner.ToLowerInvariant(), form, type);
            }
        }
        catch (XmlException) { return []; }
        var found = new List<PlannerCandidate>();
        foreach (var (guid, cls, owner) in msas)
        {
            if (!entries.TryGetValue(owner, out var lexemeGuid) || !forms.TryGetValue(lexemeGuid, out var lexeme)) continue;
            var bare = lexeme.Form.Trim().Trim('-', '=').Trim();
            if (bare.Length == 0 || bare.Any(char.IsWhiteSpace)) continue;
            found.Add(new PlannerCandidate(owner, guid, cls, bare, KindOf(cls, lexeme.Type)));
        }
        return found;
    }

    /// <summary>
    /// Whether the grammar file has an affix that is not a plain prefix or suffix: an affix process (infix, reduplication, stem
    /// change), or an affix of another morph type such as an infix or circumfix. Null when the file cannot be read as FieldWorks XML.
    /// </summary>
    public static string? Processes(string fwdataPath)
    {
        try
        {
            using var reader = XmlReader.Create(fwdataPath, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, IgnoreWhitespace = true });
            var sawRoot = false;
            var found = new SortedSet<string>(StringComparer.Ordinal);
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element) continue;
                if (reader.Name == "languageproject") sawRoot = true;
                if (reader.Name != "rt") continue;
                var cls = reader.GetAttribute("class") ?? "";
                if (cls == "MoAffixProcess") found.Add("affix process");
                if (cls is "MoEndoCompound" or "MoExoCompound") found.Add("compounding rule");
                var isAffix = cls == "MoAffixAllomorph";
                if (!isAffix && cls != "MoStemAllomorph") continue;
                using var element = reader.ReadSubtree();
                var inType = false;
                var typed = false;
                while (element.Read())
                {
                    if (element.NodeType == XmlNodeType.Element && element.Name == "MorphType") inType = true;
                    else if (element.NodeType == XmlNodeType.EndElement && element.Name == "MorphType") inType = false;
                    else if (inType && element.NodeType == XmlNodeType.Element && element.Name == "objsur" && Guid.TryParse(element.GetAttribute("guid"), out var kind))
                    {
                        typed = true;
                        if (isAffix ? !IsPlainAffix(kind) : !IsPlainStem(kind))
                            found.Add(isAffix ? "affix of another morph type" : "stem of another morph type");
                    }
                }
                if (!typed) found.Add(isAffix ? "affix with no morph type" : "stem with no morph type");
            }
            return sawRoot ? string.Join(", ", found) : null;
        }
        catch (Exception exception) when (exception is XmlException or IOException) { return null; }
    }

    private static bool IsPlainAffix(Guid kind) =>
        kind == MoMorphTypeTags.kguidMorphPrefix || kind == MoMorphTypeTags.kguidMorphPrefixingInterfix || kind == MoMorphTypeTags.kguidMorphProclitic ||
        kind == MoMorphTypeTags.kguidMorphSuffix || kind == MoMorphTypeTags.kguidMorphSuffixingInterfix || kind == MoMorphTypeTags.kguidMorphEnclitic;

    private static bool IsPlainStem(Guid kind) =>
        kind == MoMorphTypeTags.kguidMorphStem || kind == MoMorphTypeTags.kguidMorphRoot ||
        kind == MoMorphTypeTags.kguidMorphBoundStem || kind == MoMorphTypeTags.kguidMorphBoundRoot;

    /// <summary>Counts, for each candidate, how often the corpus has its form as a word.</summary>
    public static IReadOnlyList<PlannerCandidate> Weighted(IReadOnlyList<PlannerCandidate> candidates, IEnumerable<(string Form, long Tokens)> corpus)
    {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (form, tokens) in corpus) counts[form] = counts.GetValueOrDefault(form) + tokens;
        return [.. candidates.Select(candidate => candidate with { Tokens = counts.GetValueOrDefault(candidate.Form) })];
    }

    private static string KindOf(string msaClass, Guid? type)
    {
        if (msaClass == ResolvedMorpheme.StemClass) return "stem";
        if (type is not { } kind) return "other";
        if (kind == MoMorphTypeTags.kguidMorphPrefix || kind == MoMorphTypeTags.kguidMorphPrefixingInterfix ||
            kind == MoMorphTypeTags.kguidMorphProclitic) return "prefix";
        if (kind == MoMorphTypeTags.kguidMorphSuffix || kind == MoMorphTypeTags.kguidMorphSuffixingInterfix ||
            kind == MoMorphTypeTags.kguidMorphEnclitic) return "suffix";
        return "other";
    }
}
