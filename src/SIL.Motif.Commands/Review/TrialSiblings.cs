using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SIL.LCModel;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Projection;

namespace SIL.Motif.Commands.Review;

/// <summary>Asks a Trial to measure corpus words that share a form the Proposal touches.</summary>
/// <param name="CorpusTypesPath">The corpus-types file the candidates come from; required.</param>
/// <param name="Max">The most siblings to add, apart from any touched words.</param>
/// <param name="Pattern">A regular expression replacing the default match; <c>{form}</c> stands for the touched forms joined by <c>|</c>.</param>
public sealed record TrialSiblingsRequest(string? CorpusTypesPath, int Max = TrialSiblingSampler.DefaultMax, string? Pattern = null);

/// <summary>The types of a corpus-types file and the digest of its bytes.</summary>
/// <param name="Types">The types, one per row.</param>
/// <param name="Sha256">The file's digest, recorded with the sample.</param>
internal sealed record CorpusTypes(IReadOnlyList<CorpusType> Types, string Sha256);

/// <summary>A corpus type and whether the corpus-types file marks it parsed.</summary>
/// <param name="Word">The type in NFC and lower case.</param>
/// <param name="Parsed">True when the file says it parsed.</param>
internal sealed record CorpusType(string Word, bool Parsed);

/// <summary>The sibling words one Trial adds to its list, with how they were chosen.</summary>
internal sealed record TrialSiblingSample(IReadOnlyList<string> Words, string Pattern, long Seed, int Max,
    IReadOnlyList<string> Forms, string CorpusTypesSha256, int Candidates, int ParsedCandidates)
{
    /// <summary>What the sample could not use; null when nothing was dropped.</summary>
    public string? Warning { get; init; }

    /// <summary>The line the CLI prints on standard error.</summary>
    public string Describe() => $"siblings: added {Words.Count} word(s) of {Candidates} corpus candidate(s) " +
        $"({ParsedCandidates} parsed) sharing {Forms.Count} touched form(s); seed {Seed}, cap {Max}" +
        (Warning is null ? "" : "; warning: " + Warning);
}

/// <summary>
/// Chooses the sibling words: corpus types that share a form the Proposal touches. The gate that lives in the grammar lanes does
/// the same, so a small gold list cannot pass a Proposal that breaks other forms of the same root.
/// </summary>
internal static class TrialSiblingSampler
{
    /// <summary>The default cap on the sample.</summary>
    public const int DefaultMax = 1000;

    /// <summary>The pattern label when each touched form drew its own group.</summary>
    public const string PerForm = "per-form";

    /// <summary>The largest corpus-types file read, 256 MiB.</summary>
    public const long MaxCorpusBytes = 256L << 20;

    private static readonly string[] UnparsedStates = ["open", "disposition", ""];

    /// <summary>Reads an Assess JSON report (<c>words[]</c> with <c>outcome</c>), a ledger TSV with a header, or a plain word list.</summary>
    public static CommandOutcome<CorpusTypes> ReadCorpus(string path)
    {
        byte[] bytes;
        try
        {
            if (new FileInfo(path).Length > MaxCorpusBytes)
                return Invalid<CorpusTypes>($"The corpus-types file '{path}' is larger than {MaxCorpusBytes >> 20} MiB.");
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Invalid<CorpusTypes>($"The corpus-types file '{path}' could not be read: {exception.Message}");
        }
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('﻿'); }
        catch (DecoderFallbackException)
        {
            return Invalid<CorpusTypes>($"The corpus-types file '{path}' is not valid UTF-8.");
        }
        var rows = new List<CorpusType>();
        if (text.TrimStart().StartsWith('['))
            return Invalid<CorpusTypes>($"'{path}' is a JSON array; give an Assess report (an object with words[]), a ledger or a word list.");
        if (text.TrimStart().StartsWith('{'))
        {
            try
            {
                using var document = JsonDocument.Parse(text);
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty("words", out var words) || words.ValueKind != JsonValueKind.Array)
                    return Invalid<CorpusTypes>($"'{path}' is JSON without a words array.");
                foreach (var item in words.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("word", out var word) &&
                        word.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(word.GetString()))
                        rows.Add(new CorpusType(Key(word.GetString()!),
                            item.TryGetProperty("outcome", out var outcome) && outcome.ValueKind == JsonValueKind.String &&
                            outcome.GetString() == "analysed"));
            }
            catch (JsonException exception)
            {
                return Invalid<CorpusTypes>($"'{path}' is not valid JSON: {exception.Message}");
            }
        }
        else
        {
            var lines = text.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0 && !line.StartsWith('#')).ToArray();
            var header = lines.Length == 0 ? [] : lines[0].Split('\t').Select(name => name.Trim().ToLowerInvariant()).ToArray();
            var stateColumn = Array.IndexOf(header, "state");
            var wordColumn = Math.Max(0, Array.IndexOf(header, "word"));
            foreach (var line in lines.Skip(stateColumn >= 0 || header.Contains("word") ? 1 : 0))
            {
                var cells = line.Split('\t');
                if (wordColumn >= cells.Length || string.IsNullOrWhiteSpace(cells[wordColumn])) continue;
                rows.Add(new CorpusType(Key(cells[wordColumn]),
                    stateColumn >= 0 && stateColumn < cells.Length &&
                    !UnparsedStates.Contains(cells[stateColumn].Trim().ToLowerInvariant())));
            }
        }
        return rows.Count == 0
            ? Invalid<CorpusTypes>($"The corpus-types file '{path}' holds no words.")
            : CommandOutcome<CorpusTypes>.Success(new CorpusTypes(rows, sha256));
    }

    /// <summary>
    /// Takes at most <paramref name="max"/> corpus types that share a touched form and are not in <paramref name="already"/>:
    /// parsed types first, shuffled by a seed from <paramref name="revision"/> and the forms, one group per form taken in turn so a
    /// rare form is not starved. A <paramref name="pattern"/> replaces the per-form groups with one.
    /// </summary>
    public static CommandOutcome<TrialSiblingSample> Sample(IReadOnlyList<CorpusType> corpus, string corpusSha256,
        IReadOnlyList<string> forms, string? pattern, int max, string revision, IReadOnlySet<string> already)
    {
        var keys = forms.Select(Key).Where(form => form.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var seed = Seed(revision, keys);
        var types = corpus.GroupBy(type => Key(type.Word), StringComparer.Ordinal)
            .Select(group => new CorpusType(group.Key, group.Any(type => type.Parsed)))
            .Where(type => !already.Contains(type.Word)).ToArray();
        List<CorpusType>[] groups;
        string label;
        if (pattern is not null)
        {
            Regex regex;
            try
            {
                var joined = "(?:" + string.Join("|", keys.Select(Regex.Escape)) + ")";
                regex = new Regex("^(?:" + pattern.Replace("{form}", joined, StringComparison.Ordinal) + ")$",
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            }
            catch (ArgumentException exception)
            {
                return Invalid<TrialSiblingSample>("--siblings-pattern is not a regular expression: " + exception.Message);
            }
            try { groups = [[.. types.Where(type => regex.IsMatch(type.Word))]]; }
            catch (RegexMatchTimeoutException)
            {
                return Invalid<TrialSiblingSample>("--siblings-pattern took too long to match; use a simpler pattern.");
            }
            label = pattern;
        }
        else
        {
            groups = [.. keys.Select(form => types.Where(type => type.Word.Contains(form, StringComparison.OrdinalIgnoreCase)).ToList())];
            label = PerForm;
        }
        var candidates = groups.SelectMany(group => group).Select(type => type.Word).Distinct(StringComparer.Ordinal).Count();
        var parsedCandidates = groups.SelectMany(group => group).Where(type => type.Parsed).Select(type => type.Word)
            .Distinct(StringComparer.Ordinal).Count();
        var ordered = groups.Select((group, index) => Shuffle(group.Where(type => type.Parsed), seed, index * 2L)
            .Concat(Shuffle(group.Where(type => !type.Parsed), seed, index * 2L + 1)).ToArray()).ToArray();
        var picked = new List<string>();
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var cursors = new int[ordered.Length];
        while (picked.Count < max)
        {
            var progressed = false;
            for (var group = 0; group < ordered.Length && picked.Count < max; group++)
            {
                while (cursors[group] < ordered[group].Length)
                {
                    var word = ordered[group][cursors[group]++].Word;
                    if (!taken.Add(word)) continue;
                    picked.Add(word);
                    progressed = true;
                    break;
                }
            }
            if (!progressed) break;
        }
        return picked.Count == 0
            ? CommandOutcome<TrialSiblingSample>.Refused(new Refusal(RefusalCodes.TrialSiblingsNone, FailureReason.NotFound,
                (candidates == 0 ? "No corpus type shares " + (keys.Length == 0 ? "a touched form; the Proposal names none" : "a touched form (" + string.Join(", ", keys.Take(5)) + ")")
                    : "Every corpus type that shares a touched form is already in the word list") +
                ", so there is no sibling to measure. Next: pass --siblings-pattern <regex> to name the forms, or leave --siblings out."))
            : CommandOutcome<TrialSiblingSample>.Success(new TrialSiblingSample(picked, label, seed, max, keys, corpusSha256,
                candidates, parsedCandidates));
    }

    /// <summary>The form as the corpus keys it: NFC, lower case, with the affix and clitic marks a lexicon writes removed from its ends.</summary>
    internal static string Key(string form) =>
        form.Normalize(NormalizationForm.FormC).Trim().Trim('-', '=', '~', '*', '<', '>').ToLowerInvariant();

    private static long Seed(string revision, IEnumerable<string> forms)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(revision + "\n" + string.Join("\n", forms)));
        return BitConverter.ToInt64(hash, 0) & long.MaxValue;
    }

    // Fisher-Yates over a seeded splitmix64 stream, so the order is the same on every machine and runtime.
    private static CorpusType[] Shuffle(IEnumerable<CorpusType> items, long seed, long stream)
    {
        var array = items.OrderBy(item => item.Word, StringComparer.Ordinal).ToArray();
        var state = unchecked((ulong)seed + 0x9E3779B97F4A7C15UL * (ulong)(stream + 1));
        ulong Next()
        {
            state = unchecked(state + 0x9E3779B97F4A7C15UL);
            var z = state;
            z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
            z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
            return z ^ (z >> 31);
        }
        for (var index = array.Length - 1; index > 0; index--)
        {
            var swap = (int)(Next() % (ulong)(index + 1));
            (array[index], array[swap]) = (array[swap], array[index]);
        }
        return array;
    }

    private static CommandOutcome<T> Invalid<T>(string message) where T : class => CommandOutcome<T>.Refused(
        new Refusal(RefusalCodes.TrialSiblingsInvalid, FailureReason.InvalidArgument, message));
}

/// <summary>Finds the forms a Proposal touches and chooses a Trial's siblings from them.</summary>
internal static class TrialSiblingResolver
{
    /// <summary>
    /// Resolves the touched forms of the Proposal and samples siblings from the corpus-types file. A form is touched when an
    /// operation targets an entry, or anything an entry owns, that the Baseline holds (all the entry's forms count), or writes
    /// a new form with a <c>create…Form(s)</c> or <c>set…Form</c> operation.
    /// </summary>
    public static CommandOutcome<TrialSiblingSample> Resolve(EnqueueTrialRequest request, IReadOnlySet<string> already)
    {
        if (Check(request) is { } invalid) return CommandOutcome<TrialSiblingSample>.Refused(invalid);
        var options = request.Siblings!;
        var corpus = TrialSiblingSampler.ReadCorpus(options.CorpusTypesPath!);
        if (!corpus.Succeeded) return CommandOutcome<TrialSiblingSample>.Refused(corpus.Refusal!);
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            Proposal proposal;
            try { proposal = TrialTouchedWords.LoadProposal(database, request.ProposalId); }
            catch (Exception exception) when (exception is not ProjectWritingSystemsUnavailableException)
            {
                return CommandOutcome<TrialSiblingSample>.Refused(ProposalCommands.ProposalLoadRefusal(exception));
            }
            var forms = new List<string>(NewForms(proposal));
            var current = CurrentEvidenceQuery.ReadCurrentEvidence(database, project, includeDefaultSelection: false,
                includeResolvedReadings: false, includeWordContext: false);
            if (!current.Succeeded) return CommandOutcome<TrialSiblingSample>.Refused(current.Refusal!);
            string? warning = null;
            if (current.Value!.Baseline is { } baseline)
            {
                using var reader = BaselineReadCache.Open(baseline.FwDataPath);
                foreach (var id in TrialTouchedWords.TouchedIds(proposal)) forms.AddRange(ExistingForms(reader.Cache, id));
            }
            else if (TrialTouchedWords.TouchedIds(proposal).Count > 0)
                warning = "no Baseline, so the forms of the existing entries the Proposal edits were not used";
            var sampled = TrialSiblingSampler.Sample(corpus.Value!.Types, corpus.Value.Sha256, forms, options.Pattern, options.Max,
                IntentDigest.Compute(proposal), already);
            return sampled.Succeeded ? CommandOutcome<TrialSiblingSample>.Success(sampled.Value! with { Warning = warning }) : sampled;
        });
    }

    /// <summary>Why the request cannot ask for siblings, from its shape alone; null when it can or does not.</summary>
    public static Refusal? Check(EnqueueTrialRequest request)
    {
        if (request.Siblings is not { } options) return null;
        if (string.IsNullOrWhiteSpace(options.CorpusTypesPath))
            return InvalidRefusal("Siblings are sampled from a corpus-types file, and none was given. Next: pass --corpus-types <file>, or leave --siblings out.");
        if (options.Max < 1) return InvalidRefusal("--siblings-max must be one or more.");
        if (request.Words is not { Count: > 0 } || request.AllWords)
            return InvalidRefusal("Siblings add to a word list, and none was given. Next: pass --words <file>, or leave --siblings out.");
        return null;
    }

    private static Refusal InvalidRefusal(string message) => new(RefusalCodes.TrialSiblingsInvalid, FailureReason.InvalidArgument, message);

    /// <summary>Every form of the entry that owns the object, or none when the Baseline has no such object or it belongs to no entry.</summary>
    internal static IEnumerable<string> ExistingForms(LcmCache cache, Guid id)
    {
        if (!cache.ServiceLocator.ObjectRepository.TryGetObject(id, out var found)) yield break;
        var owner = found;
        while (owner is not null and not SIL.LCModel.ILexEntry) owner = owner.Owner;
        if (owner is not SIL.LCModel.ILexEntry entry) yield break;
        var forms = new List<SIL.LCModel.IMoForm>();
        if (entry.LexemeFormOA is { } lexeme) forms.Add(lexeme);
        forms.AddRange(entry.AlternateFormsOS);
        foreach (var form in forms)
            for (var slot = 0; slot < form.Form.StringCount; slot++)
                if (form.Form.GetStringFromIndex(slot, out _)?.Text is { Length: > 0 } text) yield return text;
    }

    /// <summary>The text a lexical entry's <c>create…Form(s)</c> or <c>set…Form</c> operation writes; a wordform is not a form.</summary>
    internal static IEnumerable<string> NewForms(Proposal proposal)
    {
        foreach (var operation in proposal.Operations)
        {
            var name = operation.Kind[(operation.Kind.LastIndexOf('/') + 1)..];
            if (!operation.Kind.StartsWith("lexical/lexEntry/", StringComparison.Ordinal) ||
                !Regex.IsMatch(name, "^(create|set)[A-Za-z]*Forms?$") || operation.After is not { } after)
                continue;
            foreach (var text in FormStrings(after)) yield return text;
        }
    }

    private static IEnumerable<string> FormStrings(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name is "text" or "form" && property.Value.ValueKind == JsonValueKind.String &&
                    property.Value.GetString() is { Length: > 0 } text)
                    yield return text;
                else
                    foreach (var nested in FormStrings(property.Value)) yield return nested;
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray())
                foreach (var nested in FormStrings(item)) yield return nested;
    }

}
