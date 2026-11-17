using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SIL.Motif.Runner.Operations;

/// <summary>One approved Occurrence of a Source as stored: the analysis it has and what it was approved against.</summary>
/// <param name="Analysis">The WfiAnalysis the Occurrence has.</param>
/// <param name="Verse">The fingerprint of the verse text when the approval was made.</param>
/// <param name="Word">The surface of the word at that word number when the approval was made.</param>
/// <param name="Signature">The fingerprint of the analysis's morphology when the approval was made.</param>
public sealed record ScriptureApproval(Guid Analysis, string Verse, string Word, string Signature);

/// <summary>
/// The approved Occurrences of one Document of a Source, which for a Paratext Source is one chapter, gathered from
/// every installation's approvals and leaving out those in conflict. An Occurrence has at most one analysis here; see
/// <see cref="ScriptureApprovalBlob"/> for how approvals are stored.
/// </summary>
public sealed class ScriptureApprovalDocument
{
    private static readonly Regex AnchorShape = new(@"^([A-Z0-9]{3}) (\d+):(\d+)#(\d+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly SortedDictionary<string, ScriptureApproval> _byAnchor = new(AnchorOrder.Instance);
    private readonly SortedDictionary<string, IReadOnlyList<ScriptureApproval>> _evidence = new(AnchorOrder.Instance);

    /// <summary>
    /// The approvals by Occurrence location, such as <c>MAT 5:3#2</c>, in location order: the first of the
    /// Occurrence's <see cref="Evidence"/>.
    /// </summary>
    public IReadOnlyDictionary<string, ScriptureApproval> Approvals => _byAnchor;

    /// <summary>
    /// Every approval of the Occurrence's one analysis that stands, as several installations may each have approved it
    /// against different text; in order of what each was approved against, never of who approved it.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ScriptureApproval>> Evidence => _evidence;

    private static readonly Regex PassageShape = new(@"^[A-Z0-9]{3} \d+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Whether the text is a Passage such as <c>MAT 5</c>.</summary>
    public static bool IsPassage(string? value) => value is not null && PassageShape.IsMatch(value);

    /// <summary>Whether the text is a location such as <c>MAT 5:3#2</c>.</summary>
    public static bool IsAnchor(string? value) => value is not null && AnchorShape.IsMatch(value);

    /// <summary>The Passage a location is in, such as <c>MAT 5</c>; throws for a text that is not a location.</summary>
    public static string PassageOf(string anchor)
    {
        var match = AnchorShape.Match(anchor);
        if (!match.Success) throw new FormatException($"'{anchor}' is not an Occurrence location such as MAT 5:3#2.");
        return match.Groups[1].Value + " " + match.Groups[2].Value;
    }

    /// <summary>The verse a location is in, such as <c>MAT 5:3</c>; throws for a text that is not a location.</summary>
    public static string VerseOf(string anchor)
    {
        var match = AnchorShape.Match(anchor);
        if (!match.Success) throw new FormatException($"'{anchor}' is not an Occurrence location such as MAT 5:3#2.");
        return match.Groups[1].Value + " " + match.Groups[2].Value + ":" + match.Groups[3].Value;
    }

    /// <summary>A short stable fingerprint of text, for a verse or a morphology; empty text gives an empty fingerprint.</summary>
    public static string Fingerprint(string text) => text.Length == 0
        ? ""
        : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32];

    /// <summary>Sets the Occurrence's analysis, replacing any analysis it had.</summary>
    public void Set(string anchor, ScriptureApproval approval) => SetEvidence(anchor, [approval]);

    /// <summary>
    /// Sets every approval that stands at the Occurrence; they all have one analysis. Throws for none, or for approvals
    /// of different analyses, which are a conflict and not an approval.
    /// </summary>
    public void SetEvidence(string anchor, IEnumerable<ScriptureApproval> approvals)
    {
        if (!IsAnchor(anchor)) throw new FormatException($"'{anchor}' is not an Occurrence location such as MAT 5:3#2.");
        var evidence = approvals.Distinct().OrderBy(approval => approval.Verse, StringComparer.Ordinal)
            .ThenBy(approval => approval.Word, StringComparer.Ordinal).ThenBy(approval => approval.Signature, StringComparer.Ordinal).ToArray();
        if (evidence.Length == 0) throw new ArgumentException("An approval needs at least one analysis.", nameof(approvals));
        if (evidence.Any(approval => approval.Analysis != evidence[0].Analysis))
            throw new ArgumentException("Approvals of different analyses at one Occurrence are a conflict.", nameof(approvals));
        _byAnchor[anchor] = evidence[0];
        _evidence[anchor] = evidence;
    }

    /// <summary>Removes the Occurrence's approval; false when it had none.</summary>
    public bool Remove(string anchor) => _evidence.Remove(anchor) & _byAnchor.Remove(anchor);

    /// <summary>Orders locations by book, chapter, verse and word number.</summary>
    public static IComparer<string> LocationOrder => AnchorOrder.Instance;

    private sealed class AnchorOrder : IComparer<string>
    {
        public static readonly AnchorOrder Instance = new();

        public int Compare(string? left, string? right)
        {
            var a = left is null ? null : AnchorShape.Match(left);
            var b = right is null ? null : AnchorShape.Match(right);
            if (a is not { Success: true } || b is not { Success: true }) return string.CompareOrdinal(left, right);
            var book = string.CompareOrdinal(a.Groups[1].Value, b.Groups[1].Value);
            if (book != 0) return book;
            for (var group = 2; group <= 4; group++)
            {
                var order = int.Parse(a.Groups[group].Value, CultureInfo.InvariantCulture)
                    .CompareTo(int.Parse(b.Groups[group].Value, CultureInfo.InvariantCulture));
                if (order != 0) return order;
            }
            return 0;
        }
    }
}


/// <summary>Names one entry of one installation at one Occurrence, as a settle record names the entries it overrules.</summary>
/// <param name="Installation">The installation whose item holds the entry.</param>
/// <param name="Stamp">The entry's stamp, new each time its installation writes it.</param>
public readonly record struct ScriptureApprovalMark(Guid Installation, string Stamp);

/// <summary>
/// What one installation says about one Occurrence: an approval, or a clear when <see cref="Approval"/> is null. The
/// marks in <see cref="Overrules"/> are its settle record: the other installations' entries it set aside.
/// </summary>
/// <param name="Anchor">The Occurrence location, such as <c>MAT 5:3#2</c>.</param>
/// <param name="Approval">The analysis and what it was approved against; null for a clear.</param>
/// <param name="Stamp">New each time the installation writes the entry, so a mark names one version of it.</param>
/// <param name="Overrules">The entries this one overrules, in a fixed order.</param>
public sealed record ScriptureApprovalEntry(string Anchor, ScriptureApproval? Approval, string Stamp,
    IReadOnlyList<ScriptureApprovalMark> Overrules)
{
    /// <summary>Whether two entries say the same thing; the stamp is not compared.</summary>
    public bool SaysTheSame(ScriptureApprovalEntry other) =>
        Anchor == other.Anchor && Approval == other.Approval && Overrules.SequenceEqual(other.Overrules);
}

/// <summary>One entry at an Occurrence together with the installation that wrote it.</summary>
/// <param name="Installation">The installation whose item holds the entry.</param>
/// <param name="Entry">The entry.</param>
public sealed record ScriptureApprovalHolding(Guid Installation, ScriptureApprovalEntry Entry)
{
    /// <summary>The mark that names this entry.</summary>
    public ScriptureApprovalMark Mark => new(Installation, Entry.Stamp);
}

/// <summary>
/// The approvals one installation of Motif made in one Passage of one Source: the text of the approvals field on that
/// installation's item for the Passage. Only that installation writes it, so Send/Receive never sees two people edit
/// one item. The text is four header lines and then one tab-separated line per Occurrence in location order:
/// <code>
/// motif-approvals	1
/// source	&lt;Source item guid&gt;
/// passage	MAT 5
/// installation	&lt;installation guid&gt;
/// MAT 5:3#2	&lt;analysis guid&gt;	&lt;verse fingerprint&gt;	&lt;word&gt;	&lt;signature&gt;	&lt;stamp&gt;	&lt;installation:stamp,...&gt;
/// </code>
/// A clear has empty analysis, verse, word and signature columns.
/// </summary>
public sealed class ScriptureApprovalBlob
{
    /// <summary>The only version written and read.</summary>
    public const int Version = 1;

    private const string Magic = "motif-approvals";
    private readonly SortedDictionary<string, ScriptureApprovalEntry> _entries = new(ScriptureApprovalDocument.LocationOrder);

    /// <summary>An empty blob for one installation's approvals in one Passage of one Source.</summary>
    public ScriptureApprovalBlob(Guid source, string passage, Guid installation)
    {
        if (!ScriptureApprovalDocument.IsPassage(passage)) throw new FormatException($"'{passage}' is not a Passage such as MAT 5.");
        (Source, Passage, Installation) = (source, passage, installation);
    }

    /// <summary>The Guid of the Source's item in the Motif Sources list.</summary>
    public Guid Source { get; }

    /// <summary>The Passage, such as <c>MAT 5</c>.</summary>
    public string Passage { get; }

    /// <summary>The installation that writes this blob.</summary>
    public Guid Installation { get; }

    /// <summary>The entries by Occurrence location, in location order.</summary>
    public IReadOnlyDictionary<string, ScriptureApprovalEntry> Entries => _entries;

    /// <summary>Puts the entry at its location, replacing the one there.</summary>
    public void Set(ScriptureApprovalEntry entry)
    {
        if (!ScriptureApprovalDocument.IsAnchor(entry.Anchor) || ScriptureApprovalDocument.PassageOf(entry.Anchor) != Passage)
            throw new FormatException($"'{entry.Anchor}' is not an Occurrence location in {Passage}.");
        _entries[entry.Anchor] = entry;
    }

    /// <summary>Removes the entry at a location; false when there was none.</summary>
    public bool Remove(string anchor) => _entries.Remove(anchor);

    /// <summary>The field text.</summary>
    public string Serialize()
    {
        var text = new StringBuilder();
        text.Append(Magic).Append('\t').Append(Version.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append("source\t").Append(Source.ToString("D")).Append('\n')
            .Append("passage\t").Append(Passage).Append('\n')
            .Append("installation\t").Append(Installation.ToString("D"));
        foreach (var entry in _entries.Values)
        {
            var approval = entry.Approval;
            string[] columns =
            [
                entry.Anchor, approval?.Analysis.ToString("D") ?? "", approval?.Verse ?? "", approval?.Word ?? "",
                approval?.Signature ?? "", entry.Stamp,
                string.Join(",", entry.Overrules.Select(mark => mark.Installation.ToString("N") + ":" + mark.Stamp)),
            ];
            if (columns.Any(column => column.IndexOfAny(['\t', '\n', '\r']) >= 0))
                throw new FormatException($"The approval at {entry.Anchor} holds a tab or a line break.");
            text.Append('\n').AppendJoin('\t', columns);
        }
        return text.ToString();
    }

    /// <summary>
    /// Reads the field text. Throws <see cref="FormatException"/> for text that is not this shape, so a stored value is
    /// never silently read as nothing.
    /// </summary>
    public static ScriptureApprovalBlob Parse(string? text)
    {
        ScriptureApprovalCounts.Parsed();
        if (string.IsNullOrWhiteSpace(text)) throw new FormatException("The approvals are empty.");
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length < 4 || lines[0] != Magic + "\t" + Version.ToString(CultureInfo.InvariantCulture))
            throw new FormatException("The approvals are not in a version this program reads.");
        if (!Guid.TryParse(Header(lines[1], "source"), out var source)) throw new FormatException("The approvals name no Source.");
        if (!Guid.TryParse(Header(lines[3], "installation"), out var installation))
            throw new FormatException("The approvals name no installation.");
        var blob = new ScriptureApprovalBlob(source, Header(lines[2], "passage"), installation);
        foreach (var line in lines.Skip(4))
        {
            if (line.Length == 0) continue;
            var columns = line.Split('\t');
            if (columns.Length != 7) throw new FormatException($"The approval line '{line}' does not have 7 columns.");
            ScriptureApproval? approval = null;
            if (columns[1].Length > 0)
            {
                if (!Guid.TryParse(columns[1], out var analysis)) throw new FormatException($"'{columns[1]}' is not an analysis.");
                approval = new(analysis, columns[2], columns[3], columns[4]);
            }
            if (columns[5].Length == 0) throw new FormatException($"The approval at {columns[0]} has no stamp.");
            var marks = new List<ScriptureApprovalMark>();
            foreach (var mark in columns[6].Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var colon = mark.IndexOf(':');
                if (colon <= 0 || !Guid.TryParse(mark[..colon], out var other) || colon == mark.Length - 1)
                    throw new FormatException($"'{mark}' does not name an installation's approval.");
                marks.Add(new(other, mark[(colon + 1)..]));
            }
            blob.Set(new(columns[0], approval, columns[5], marks));
        }
        return blob;
    }

    private static string Header(string line, string name) => line.StartsWith(name + "\t", StringComparison.Ordinal)
        ? line[(name.Length + 1)..]
        : throw new FormatException($"The approvals have no {name} line.");

    /// <summary>
    /// The entries at an Occurrence that stand, over every installation's blob for its Passage: every entry that no
    /// entry at that Occurrence names in its settle record, in installation order. A mark counts even when the entry
    /// that holds it is itself overruled, since a stamp is never written twice.
    /// </summary>
    public static IReadOnlyList<ScriptureApprovalHolding> Standing(IEnumerable<ScriptureApprovalBlob> blobs, string anchor)
    {
        var held = blobs.Select(blob => blob.Entries.TryGetValue(anchor, out var entry) ? new ScriptureApprovalHolding(blob.Installation, entry) : null)
            .OfType<ScriptureApprovalHolding>().ToArray();
        if (held.Length == 0) return held;
        var overruled = held.SelectMany(holding => holding.Entry.Overrules).ToHashSet();
        return held.Where(holding => !overruled.Contains(holding.Mark))
            .OrderBy(holding => holding.Installation.ToString("D"), StringComparer.Ordinal).ToArray();
    }

    /// <summary>A new stamp: eight random hexadecimal digits.</summary>
    public static string NewStamp() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
}
