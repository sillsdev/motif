using SIL.LCModel.Core.Text;
using SIL.LCModel.Core.WritingSystems;
using SIL.Motif.Host.LcmUtils;

namespace SIL.Motif.Host.Parser;

/// <summary>
/// FieldWorks' lowercase second pass, which lives here in the host layer and never in the parser (ADR 0078).
/// </summary>
/// <remarks>
/// <para>
/// Every word is parsed as written. When the whole word's lowercase form differs, that form is parsed too, even
/// when the original parsed. Its readings belong to the lowercase wordform as a row of its own, never merged into
/// the original's. The lowercase form comes from the default vernacular writing system's ICU casing
/// (<see cref="CaseFunctions"/>, the case alias before the ICU locale), never from <c>ToLower</c> or Unicode case
/// folding, so Turkish and Azeri dotted and dotless I lowercase as ICU says.
/// </para>
/// <para>
/// Both parse costs are real work, so a caller that sums parse steps over its rows already counts the pass.
/// </para>
/// </remarks>
public sealed class LowercasePass
{
    private static readonly object CacheGate = new();
    private static (string Key, LowercasePass Pass)? _lastProject;

    private readonly CaseFunctions? _casing;

    private LowercasePass(CaseFunctions? casing) => _casing = casing;

    /// <summary>A pass that never adds a word.</summary>
    public static LowercasePass None { get; } = new(null);

    /// <summary>The pass for one writing system's casing.</summary>
    public static LowercasePass For(CoreWritingSystemDefinition writingSystem)
    {
        ArgumentNullException.ThrowIfNull(writingSystem);
        FwDataProjectLoader.Init();
        return new LowercasePass(new CaseFunctions(writingSystem));
    }

    /// <summary>The pass for an open project's default vernacular writing system, so no second load is needed.</summary>
    public static LowercasePass ForCache(SIL.LCModel.LcmCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        return For(cache.ServiceLocator.WritingSystems.DefaultVernacularWritingSystem);
    }

    /// <summary>
    /// The pass for the project's default vernacular writing system, which is the one the parser's words are in.
    /// A project that cannot be opened gets <see cref="None"/>: the parser reads the same file and reports the
    /// real failure in its own words, which a casing error here would only hide. The last project read is kept,
    /// keyed by path, size and write time, because Try a Word asks for every word it traces.
    /// </summary>
    public static LowercasePass ForProject(string fwDataPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(fwDataPath);
        var info = new FileInfo(fwDataPath);
        if (!info.Exists) return None;
        var key = $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        lock (CacheGate)
            if (_lastProject is { } last && last.Key == key) return last.Pass;
        LowercasePass pass;
        try
        {
            using var cache = new FwDataProjectLoader().LoadScratchCache(fwDataPath);
            pass = new LowercasePass(new CaseFunctions(cache.ServiceLocator.WritingSystems.DefaultVernacularWritingSystem));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return None;
        }
        lock (CacheGate) _lastProject = (key, pass);
        return pass;
    }

    /// <summary>
    /// The pass for <paramref name="fwDataPath"/>, opened only when some word could change. ASCII lowercase,
    /// digits and punctuation lowercase to themselves in every ICU locale, so a list of those never opens the project.
    /// </summary>
    public static LowercasePass ForProjectIfNeeded(string fwDataPath, IEnumerable<string> words) =>
        words.Any(MayChange) ? ForProject(fwDataPath) : None;

    private static bool MayChange(string word) => word.Any(ch => ch >= 0x80 || char.IsAsciiLetterUpper(ch));

    /// <summary>The whole word in lowercase, or <c>null</c> when that is the word itself and no second parse is due.</summary>
    public string? LowercaseOf(string word)
    {
        if (_casing is null || word.Length == 0) return null;
        var lower = _casing.ToLower(word);
        return string.Equals(lower, word, StringComparison.Ordinal) ? null : lower;
    }

    /// <summary>
    /// <paramref name="words"/> in their order, then each lowercase form that is not already in the list. A lowercase
    /// form shared by several words, or already asked for, is parsed once. The returned map names, for each appended
    /// form, the first word it came from.
    /// </summary>
    public (IReadOnlyList<string> Words, IReadOnlyDictionary<string, string> LowercaseOf) Expand(
        IReadOnlyList<string> words)
    {
        var asked = new HashSet<string>(words, StringComparer.Ordinal);
        var all = new List<string>(words);
        var origin = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var word in words)
        {
            if (LowercaseOf(word) is not { } lower || !asked.Add(lower)) continue;
            all.Add(lower);
            origin[lower] = word;
        }
        return (all, origin);
    }
}
