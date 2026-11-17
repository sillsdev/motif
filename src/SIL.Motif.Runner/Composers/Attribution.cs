using System.Text;
using System.Text.RegularExpressions;
using SIL.LCModel;
using SIL.Motif.Contract.Parsing;

namespace SIL.Motif.Runner.Composers;

/// <summary>
/// The Attribution tag <c>&lt;source&gt;@&lt;revision&gt; &lt;licence&gt;</c>, such as <c>lang-sme@9ba7be7 GPL-3.0</c>, and the
/// text rules the two Attribution composers share: where the tag is written, what counts as already written, and how it
/// is joined to text the field already holds.
/// </summary>
/// <param name="Source">The name of the data's source; no spaces or <c>@</c>.</param>
/// <param name="Revision">The revision of the source the data came from; no spaces or <c>@</c>.</param>
/// <param name="Licence">The licence, as the source states it; may hold spaces, never a line break or <c>;</c>.</param>
public sealed record AttributionTag(string Source, string Revision, string Licence)
{
    private static readonly Regex Shape = new(@"^(?<source>[^\s@;]+)@(?<revision>[^\s@;]+) (?<licence>[^\r\n;\u2028\u2029]*\S)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>The prefix the project notice carries before the tag, so a reader of the Description knows what it is.</summary>
    public const string NoticePrefix = "Attribution: ";

    private const char LineSeparator = '\u2028';

    /// <summary>The identity of the source and revision, which a licence never changes: <c>lang-sme@9ba7be7</c>.</summary>
    public string Key => Source + "@" + Revision;

    /// <summary>The tag as it is written into an entry's Bibliography.</summary>
    public string Text => Key + " " + Licence;

    /// <summary>The notice as it is written into the project's Description.</summary>
    public string Notice => NoticePrefix + Text;

    /// <summary>Reads a tag, refusing anything that is not <c>source@revision licence</c> in NFD without edge spaces.</summary>
    public static AttributionTag Parse(string? text, string construct)
    {
        if (!TryParse(text, out var tag))
            throw new ContractParseException(
                $"'{construct}': the tag must be '<source>@<revision> <licence>' in NFD, such as 'lang-sme@9ba7be7 GPL-3.0', " +
                "with no spaces in the source or the revision and no semicolon or line break anywhere.");
        return tag;
    }

    /// <summary>Whether <paramref name="text"/> reads as a tag.</summary>
    public static bool TryParse(string? text, out AttributionTag tag)
    {
        tag = null!;
        if (text is null || text != text.Trim() || !text.IsNormalized(NormalizationForm.FormD)) return false;
        var match = Shape.Match(text);
        if (!match.Success) return false;
        tag = new AttributionTag(match.Groups["source"].Value, match.Groups["revision"].Value, match.Groups["licence"].Value);
        return true;
    }

    /// <summary>The text with <paramref name="item"/> added, or null when it is already there.</summary>
    /// <param name="current">What the field holds now, in the writing system the tag is written in.</param>
    /// <param name="item">The tag or notice to add.</param>
    /// <param name="separator">What joins it to the text already there.</param>
    /// <param name="prefix">The text before the tag in each item of this field, empty for an entry.</param>
    /// <remarks>
    /// Other text is kept untouched. An item for the same source and revision under another licence is refused rather
    /// than added beside it, because the field would then state two licences for one revision.
    /// </remarks>
    public string? AddTo(string current, string separator, string prefix)
    {
        foreach (var part in current.Split([';', '\n', LineSeparator]))
        {
            var piece = part.Trim();
            if (!piece.StartsWith(prefix, StringComparison.Ordinal) || !TryParse(piece[prefix.Length..], out var found) ||
                found.Key != Key)
                continue;
            if (found.Licence == Licence) return null;
            throw new InvalidOperationException(
                $"The field already says '{found.Text}' for {Key}; this tag says '{Text}'. " +
                "Which licence applies is for a person to decide, so Motif will not state both.");
        }
        var item = prefix + Text;
        return current.Length == 0 ? item : current.TrimEnd() + separator + item;
    }

    /// <summary>The writing system tag the Attribution is written in: the one asked for, else the default analysis one.</summary>
    public static string AnalysisWritingSystem(LcmCache cache, string? requested, string construct)
    {
        var tag = requested ?? cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        if (cache.WritingSystemFactory.GetWsFromStr(tag) == 0 ||
            !cache.ServiceLocator.WritingSystems.AnalysisWritingSystems.Any(system => system.Id == tag))
            throw new ContractParseException(
                $"'{construct}': '{tag}' is not an analysis writing system of this project; its analysis writing systems are " +
                string.Join(", ", cache.ServiceLocator.WritingSystems.AnalysisWritingSystems.Select(system => system.Id)) + ".");
        return tag;
    }
}
