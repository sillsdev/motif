using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using SIL.Motif.RecordStore.HandAnalysis;
using SIL.WritingSystems;

namespace SIL.Motif.RecordStore.Lcm;

/// <summary>
/// Collation equivalence from each writing system's stored LDML collation, through the ICU library the host loaded.
/// The version names ICU, the sort-key algorithm and every definition, so any change moves the Stamp.
/// </summary>
internal sealed class IcuFormCollation : IFormCollation
{
    private const string Algorithm = "ldml-sortkey-1";
    private readonly Dictionary<string, WsDefinition> systems;
    private readonly Dictionary<string, ICollator?> collators = new(StringComparer.Ordinal);

    private IcuFormCollation(string version, ImmutableArray<WsDefinition> systems)
    {
        Version = version;
        this.systems = systems.ToDictionary(s => s.Tag, StringComparer.Ordinal);
    }

    public string Version { get; }

    /// <summary>The factory the host uses when it can load ICU; initialise ICU (see FwDataProjectLoader) first.</summary>
    public static FormCollationFactory Factory => systems =>
    {
        var icu = Icu.Wrapper.IcuVersion + "+u" + Icu.Wrapper.UnicodeVersion + "+" + DataProbe();
        var bindings = string.Join(";", systems.OrderBy(s => s.Tag, StringComparer.Ordinal).Select(s => s.Tag + "=" + s.CollationBinding));
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(bindings)))[..16];
        return new IcuFormCollation($"icu-{icu}/{Algorithm}/{digest}", systems);
    };

    // ICU's version names only major.minor; a fixed probe's sort keys stand in for the collation data.
    private static string DataProbe()
    {
        using var root = Icu.Collation.Collator.Create("");
        var probe = string.Concat("aAbB\u00e4\u00df\u00ad\u0301\u4e2d\u0627\u1e9e-_ 09zZ".Select(c => Convert.ToHexString(root.GetSortKey(c.ToString()).KeyData)));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(probe)))[..8];
    }

    private static ICollator? Read(WsDefinition system)
    {
        var definition = new WritingSystemDefinition(system.Tag);
        var path = Path.Combine(Path.GetTempPath(), "motif-ws-" + Guid.NewGuid().ToString("N") + ".ldml");
        try
        {
            File.WriteAllBytes(path, system.LdmlBytes);
            new LdmlDataMapper(new WritingSystemFactory()).Read(path, definition);
            return definition.DefaultCollation?.Collator;
        }
        finally { File.Delete(path); }
    }

    public string Key(string ws, string nfdText)
    {
        ICollator? collator;
        lock (collators)
        {
            if (!collators.TryGetValue(ws, out collator))
                collators[ws] = collator = systems.TryGetValue(ws, out var system) ? Read(system) : null;
        }
        if (collator is null) return nfdText;
        lock (collator) return Convert.ToHexStringLower(collator.GetSortKey(nfdText).KeyData);
    }
}
