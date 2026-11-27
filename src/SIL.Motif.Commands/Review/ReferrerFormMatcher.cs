using System.Globalization;
using System.Text;
using SIL.Motif.Contract.Review;

namespace SIL.Motif.Commands.Review;

/// <summary>
/// Chooses which forms (of the referring entries, or the touched ones) may select corpus types, and matches them. A form keeps its affix mark: a suffix
/// (<c>-x</c>, <c>=x</c>) may sit anywhere but at the very start of the type, a prefix (<c>x-</c>, <c>x=</c>) anywhere but at the
/// very end, since suffixes and prefixes stack; anything else may sit anywhere. A form
/// shorter than <see cref="MinGraphemes"/> or found in more than <see cref="MaxShare"/> of the corpus types would select the
/// corpus at large, so it is skipped and reported, unless it is the only form of an edited object: that one is kept, on a quarter of
/// the cap when it is still common. The share limit needs <see cref="MinTypesForShare"/> types to mean anything.
/// </summary>
internal sealed class ReferrerFormMatcher
{
    /// <summary>The shortest form that may select types, in text elements.</summary>
    public const int MinGraphemes = 2;

    /// <summary>The most corpus types a form may match, as a share of all of them.</summary>
    public const double MaxShare = 0.05;

    /// <summary>The fewest corpus types for which the share limit applies.</summary>
    public const int MinTypesForShare = 100;

    /// <summary>The reason recorded for a sole form that was kept although short or common.</summary>
    public const string KeptOnlyForm = "kept: only form";

    private const int Anywhere = 0, Suffix = 1, Prefix = 2;

    private readonly string[] _forms;
    private readonly int[] _kinds;

    private ReferrerFormMatcher(string[] forms, int[] kinds, bool[] capped, IReadOnlyList<TrialSkippedForm> skipped)
    {
        (_forms, _kinds, Skipped) = (forms, kinds, skipped);
        Capped = capped;
    }

    /// <summary>Per kept form, true when it is common and kept only as an object's sole form, so its group is capped.</summary>
    public bool[] Capped { get; }

    /// <summary>The kept form, without its mark.</summary>
    public string Core(int index) => _forms[index];

    /// <summary>True when the word matches the kept form at <paramref name="index"/>.</summary>
    public bool Fits(string word, int index) => _kinds[index] switch
    {
        Suffix => word.AsSpan(Math.Min(1, word.Length)).Contains(_forms[index].AsSpan(), StringComparison.Ordinal),
        Prefix => word.AsSpan(0, Math.Max(0, word.Length - 1)).Contains(_forms[index].AsSpan(), StringComparison.Ordinal),
        _ => word.Contains(_forms[index], StringComparison.Ordinal),
    };

    /// <summary>How many forms may select types.</summary>
    public int Count => _forms.Length;

    /// <summary>The forms left out, in order, with why.</summary>
    public IReadOnlyList<TrialSkippedForm> Skipped { get; }

    /// <summary>Reads the forms, drops the short ones, then counts how many of <paramref name="words"/> each remaining form matches.</summary>
    public static ReferrerFormMatcher Create(IEnumerable<string> rawForms, IReadOnlyList<string> words, string role = "referrer",
        IReadOnlySet<string>? onlyForms = null)
    {
        var skipped = new List<TrialSkippedForm>();
        var wanted = new Dictionary<(string Core, int Kind), string>();
        foreach (var raw in rawForms.Select(form => form.Normalize(NormalizationForm.FormC).Trim()).Where(form => form.Length > 0).Distinct())
        {
            var core = TrialSiblingSampler.Key(raw);
            if (core.Length == 0) continue;
            var lead = "-=".Contains(raw[0]);
            var trail = "-=".Contains(raw[^1]);
            var kind = lead && !trail ? Suffix : trail && !lead ? Prefix : Anywhere;
            wanted.TryAdd((core, kind), raw);
        }
        var forms = wanted.Keys.OrderBy(key => key.Kind).ThenBy(key => key.Core, StringComparer.Ordinal).ToArray();
        var probe = new ReferrerFormMatcher([.. forms.Select(key => key.Core)], [.. forms.Select(key => key.Kind)], [], []);
        var counts = new int[forms.Length];
        for (var index = 0; index < words.Count; index++)
            probe.Match(words[index], hit => counts[hit]++);
        var limit = words.Count >= MinTypesForShare ? words.Count * MaxShare : double.MaxValue;
        var keep = new List<int>();
        var capped = new List<bool>();
        for (var index = 0; index < forms.Length; index++)
        {
            var raw = wanted[forms[index]];
            var only = onlyForms?.Contains(forms[index].Core) == true;
            var tooShort = new StringInfo(forms[index].Core).LengthInTextElements < MinGraphemes;
            var common = counts[index] > limit;
            if ((tooShort || common) && !only)
            {
                skipped.Add(new TrialSkippedForm(raw, tooShort ? $"shorter than {MinGraphemes} characters"
                    : $"matches {counts[index]} of {words.Count} corpus types (over {(int)(MaxShare * 100)}%)", role));
                continue;
            }
            if (tooShort || common) skipped.Add(new TrialSkippedForm(raw, KeptOnlyForm, role));
            keep.Add(index);
            capped.Add(common);
        }
        return new ReferrerFormMatcher([.. keep.Select(index => forms[index].Core)], [.. keep.Select(index => forms[index].Kind)],
            [.. capped], [.. skipped.OrderBy(form => form.Form, StringComparer.Ordinal)]);
    }

    /// <summary>True when the word matches any kept form.</summary>
    public bool Matches(string word)
    {
        var any = false;
        Match(word, _ => any = true);
        return any;
    }

    // Calls back once per matching form, scanning by form length so the cost follows the word.
    private Dictionary<int, Dictionary<string, List<int>>>? _byLength;

    private void Match(string word, Action<int> hit)
    {
        _byLength ??= Index();
        var seen = new HashSet<int>();
        foreach (var (length, forms) in _byLength)
        {
            if (length > word.Length) continue;
            var lookup = forms.GetAlternateLookup<ReadOnlySpan<char>>();
            for (var start = 0; start + length <= word.Length; start++)
                if (lookup.TryGetValue(word.AsSpan(start, length), out var indexes))
                    foreach (var index in indexes)
                        if (Fits(_kinds[index], start, length, word.Length) && seen.Add(index)) hit(index);
        }
    }

    private static bool Fits(int kind, int start, int length, int wordLength) =>
        kind == Anywhere || (kind == Suffix ? start > 0 : start + length < wordLength);

    private Dictionary<int, Dictionary<string, List<int>>> Index()
    {
        var byLength = new Dictionary<int, Dictionary<string, List<int>>>();
        for (var index = 0; index < _forms.Length; index++)
        {
            if (!byLength.TryGetValue(_forms[index].Length, out var forms))
                byLength[_forms[index].Length] = forms = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            if (!forms.TryGetValue(_forms[index], out var list)) forms[_forms[index]] = list = [];
            list.Add(index);
        }
        return byLength;
    }
}
