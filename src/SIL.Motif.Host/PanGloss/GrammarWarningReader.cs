using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SIL.LCModel;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// Turns the grammar findings the parser writes to its error stream into <see cref="GrammarWarning"/>s a
/// person can read: the context split from the problem, and every identifier replaced by the name of the
/// project object it denotes, with a link that opens FieldWorks on that object.
/// </summary>
/// <remarks>
/// The parser prints only prose — its stable finding codes stay inside it — so this reads the prose's one
/// regular shape: an optional <c>warning:</c> or <c>capability:</c> prefix, a context of
/// <c>kind "identifier"</c> pairs, a colon, then the problem, which quotes the values it names the same
/// way. Nothing here depends on the wording around a quoted value, so a reworded finding still reads.
/// </remarks>
public static class GrammarWarningReader
{
    // A Rust `{:?}`-formatted string: double quotes, with an embedded quote or backslash escaped.
    private static readonly Regex Quoted = new(@"""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);

    /// <summary>
    /// Reads each line into a <see cref="GrammarWarning"/>, resolving identifiers against
    /// <paramref name="cache"/> and linking them into the FieldWorks project named <paramref name="projectName"/>.
    /// </summary>
    /// <param name="cache">The project the parser read, or a copy of it with the same identifiers.</param>
    /// <param name="projectName">The FieldWorks project name a link opens, as FieldWorks lists it.</param>
    /// <param name="lines">The finding lines, one per element.</param>
    public static IReadOnlyList<GrammarWarning> Read(LcmCache cache, string projectName, IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(projectName);
        ArgumentNullException.ThrowIfNull(lines);
        var objects = cache.ServiceLocator.ObjectRepository;
        return lines.Select(line => Read(line, id =>
            objects.TryGetObject(id, out var found) ? Describe(cache, projectName, found) : null)).ToArray();
    }

    /// <summary>
    /// Reads one line, asking <paramref name="resolve"/> for each identifier it quotes; a <see langword="null"/>
    /// answer marks that identifier <c>missing</c>.
    /// </summary>
    internal static GrammarWarning Read(string line, Func<Guid, ResolvedObject?> resolve)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(resolve);

        var body = line.Trim();
        var severity = string.Empty;
        foreach (var prefix in new[] { "warning", "capability" })
        {
            if (body.StartsWith(prefix + ":", StringComparison.OrdinalIgnoreCase))
            {
                severity = prefix;
                body = body[(prefix.Length + 1)..].TrimStart();
                break;
            }
        }

        var split = FirstColonOutsideQuotes(body);
        var subject = split < 0 ? [] : Parts(body[..split], resolve);
        var problem = Parts(split < 0 ? body : body[(split + 1)..].TrimStart(), resolve);
        var kind = subject.FirstOrDefault(part => part.Role == "object")?.Kind ?? string.Empty;
        return new GrammarWarning(severity, kind, subject, problem, line.Trim());
    }

    /// <summary>What an identifier resolved to: its name, what sort of object it is, and a link if one exists.</summary>
    internal sealed record ResolvedObject(string Name, string Kind, string? FieldWorksLink);

    private static int FirstColonOutsideQuotes(string text)
    {
        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (inQuotes && text[i] == '\\') { i++; continue; }
            if (text[i] == '"') inQuotes = !inQuotes;
            else if (!inQuotes && text[i] == ':' && (i + 1 == text.Length || text[i + 1] == ' ')) return i;
        }
        return -1;
    }

    private static IReadOnlyList<GrammarWarningPart> Parts(string text, Func<Guid, ResolvedObject?> resolve)
    {
        var parts = new List<GrammarWarningPart>();
        var at = 0;
        foreach (Match match in Quoted.Matches(text))
        {
            AddText(parts, text[at..match.Index]);
            var value = Unescape(match.Groups[1].Value);
            if (Guid.TryParseExact(value, "D", out var id))
            {
                var resolved = resolve(id);
                parts.Add(resolved is null
                    ? new GrammarWarningPart(value, "missing", value)
                    : new GrammarWarningPart(resolved.Name, "object", value, resolved.Kind, resolved.FieldWorksLink));
            }
            else
            {
                parts.Add(new GrammarWarningPart(value, "value"));
            }
            at = match.Index + match.Length;
        }
        AddText(parts, text[at..]);
        return parts;
    }

    // Only an escaped quote or backslash can occur in an identifier or a name the parser echoes.
    private static string Unescape(string quoted)
    {
        if (!quoted.Contains('\\', StringComparison.Ordinal)) return quoted;
        var text = new StringBuilder(quoted.Length);
        for (var i = 0; i < quoted.Length; i++)
        {
            if (quoted[i] == '\\' && i + 1 < quoted.Length) i++;
            text.Append(quoted[i]);
        }
        return text.ToString();
    }

    private static void AddText(List<GrammarWarningPart> parts, string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length > 0) parts.Add(new GrammarWarningPart(trimmed, "text"));
    }

    private static ResolvedObject Describe(LcmCache cache, string projectName, ICmObject found)
    {
        var name = found is ILexEntry entry ? entry.HeadWord?.Text : found.ShortName;
        if (string.IsNullOrWhiteSpace(name) || name == "***") name = "(unnamed)";
        return new ResolvedObject(name, KindOf(cache, found), FieldWorksLinks.For(cache, projectName, found));
    }

    private static string KindOf(LcmCache cache, ICmObject found) => found switch
    {
        ILexEntry => "Entry",
        ILexSense => "Sense",
        IMoMorphSynAnalysis => "Grammatical info",
        IMoForm => "Allomorph",
        IPhPhoneme => "Phoneme",
        IPhNaturalClass => "Natural class",
        IPhEnvironment => "Environment",
        IPhSegmentRule => "Phonological rule",
        IMoCompoundRule => "Compound rule",
        IMoAdhocProhib => "Ad hoc rule",
        IMoInflAffixTemplate => "Affix template",
        IMoInflAffixSlot => "Slot",
        IMoInflClass => "Inflection class",
        IMoStemName => "Stem name",
        IPartOfSpeech => "Category",
        ILexEntryInflType => "Inflection type",
        ILexEntryType => "Variant type",
        IFsFeatDefn => "Feature",
        IFsSymFeatVal => "Feature value",
        ICmPossibility when found.Owner == cache.LangProject.MorphologicalDataOA?.ProdRestrictOA =>
            "Exception feature",
        _ => cache.MetaDataCacheAccessor.GetClassName(found.ClassID),
    };
}
