using System.Globalization;
using System.Text;

namespace SIL.Motif.Worker.Corpus.Ledger;

internal sealed record FrequencyEntry(string Key, long Weight);

/// <summary>The authoritative pinned frequency list: weights, and the input policy that produced its keys.</summary>
internal sealed record PinnedFrequencyList(
    string Digest, string TokenizerId, string TokenizerSha256, string Fold, string Normalization,
    IReadOnlyList<FrequencyEntry> Entries);

/// <summary>Why the whole join cannot be trusted; every affected type stays unmeasured.</summary>
internal enum JoinBlock { InputPolicyMismatch, FrequencyDigestMismatch, DuplicateFrequencyKey, EmptyFrequencyList }

internal sealed record JoinedType(
    string Key, long Weight, LedgerRow? Row, IReadOnlyList<LedgerRow> DuplicateRows, bool Missing, bool RevisionIncompatible,
    long? ConflictingTokens)
{
    public bool Resolved => Row is not null && !RevisionIncompatible && ConflictingTokens is null;
}

internal sealed record WeightConflict(string Key, long FrequencyWeight, long LedgerTokens)
{
    public long SignedDifference => LedgerTokens - FrequencyWeight;
}

internal sealed record ExtraLedgerRow(string Key, long DeclaredTokens, string? Provenance, int RowNumber);

internal sealed record LedgerJoin(
    IReadOnlyList<JoinedType> Types, IReadOnlyList<JoinBlock> Blocks, IReadOnlyList<ExtraLedgerRow> Extra,
    IReadOnlyList<WeightConflict> Conflicts, long EligibleTypes, long EligibleTokens,
    long MissingTypes, long MissingWeight, long DuplicateTypes, long DuplicateWeight,
    long RevisionTypes, long RevisionWeight, long ConflictTypes, long ConflictWeight,
    long DuplicateFrequencyKeys, long ResidualTypes, long ResidualWeight, LedgerSidecar Sidecar, string LedgerDigest)
{
    public bool Blocked => Blocks.Count > 0;
    public long ExtraDeclaredTokens => Extra.Sum(row => row.DeclaredTokens);
}

internal static class LedgerJoiner
{
    public static string Key(string word) => SIL.Motif.Contract.Dispositions.WordTypeKey.Of(word);

    public static LedgerJoin Join(LedgerFile ledger, LedgerSidecar sidecar, PinnedFrequencyList frequency,
        string? declaredFrequencySha256 = null)
    {
        var blocks = new List<JoinBlock>();
        if (frequency.Entries.Count == 0) blocks.Add(JoinBlock.EmptyFrequencyList);
        if (declaredFrequencySha256 is not null &&
            !string.Equals(declaredFrequencySha256, frequency.Digest, StringComparison.OrdinalIgnoreCase))
            blocks.Add(JoinBlock.FrequencyDigestMismatch);
        if (!string.Equals(frequency.TokenizerSha256, sidecar.TokenizerSha256, StringComparison.OrdinalIgnoreCase) ||
            frequency.Fold != sidecar.Fold || frequency.Normalization != sidecar.Normalization)
            blocks.Add(JoinBlock.InputPolicyMismatch);

        var weights = new Dictionary<string, long>(StringComparer.Ordinal);
        var order = new List<string>();
        long duplicateFrequency = 0;
        foreach (var entry in frequency.Entries)
        {
            var key = Key(entry.Key);
            if (weights.TryAdd(key, entry.Weight)) order.Add(key);
            else duplicateFrequency++;
        }
        if (duplicateFrequency > 0) blocks.Add(JoinBlock.DuplicateFrequencyKey);

        var byKey = new Dictionary<string, List<LedgerRow>>(StringComparer.Ordinal);
        foreach (var row in ledger.Rows)
        {
            var key = Key(row.Word);
            if (!byKey.TryGetValue(key, out var list)) byKey[key] = list = [];
            list.Add(row);
        }

        var types = new List<JoinedType>(order.Count);
        long missing = 0, missingWeight = 0, dup = 0, dupWeight = 0, rev = 0, revWeight = 0, conflict = 0, conflictWeight = 0;
        long residualTypes = 0, residualWeight = 0, tokens = 0;
        var conflicts = new List<WeightConflict>();
        foreach (var key in order)
        {
            var weight = weights[key];
            tokens = checked(tokens + weight);
            byKey.TryGetValue(key, out var rows);
            JoinedType joined;
            if (rows is null) joined = new(key, weight, null, [], true, false, null);
            else if (rows.Count > 1) joined = new(key, weight, null, rows, false, false, null);
            else
            {
                var row = rows[0];
                joined = new(key, weight, row, [], false, row.GrammarRevision != sidecar.GrammarRevision,
                    row.Tokens != weight ? row.Tokens : null);
                if (joined.ConflictingTokens is not null) conflicts.Add(new(key, weight, row.Tokens));
            }
            types.Add(joined);
            if (joined.Missing) { missing++; missingWeight += weight; }
            if (joined.DuplicateRows.Count > 0) { dup++; dupWeight += weight; }
            if (joined.RevisionIncompatible) { rev++; revWeight += weight; }
            if (joined.ConflictingTokens is not null) { conflict++; conflictWeight += weight; }
            if (!joined.Resolved) { residualTypes++; residualWeight += weight; }
        }

        var extra = ledger.Rows.Where(row => !weights.ContainsKey(Key(row.Word)))
            .Select(row => new ExtraLedgerRow(Key(row.Word), row.Tokens, row.Provenance, row.RowNumber)).ToArray();
        return new(types, blocks, extra, conflicts, types.Count, tokens, missing, missingWeight, dup, dupWeight, rev, revWeight,
            conflict, conflictWeight, duplicateFrequency, residualTypes, residualWeight, sidecar, ledger.Digest);
    }
}

/// <summary>Reads a frequency list from the two-column tab-separated or word-to-count JSON shapes lanes keep.</summary>
internal static class FrequencyListReader
{
    public static PinnedFrequencyList ReadTsv(byte[] bytes, LedgerSidecar pins, bool hasHeader)
    {
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { throw new LedgerRefusalException(LedgerRefusalCode.NotUtf8, "The frequency list is not UTF-8."); }
        var entries = new List<FrequencyEntry>();
        var number = 0;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            number++;
            if (line.Length == 0 && number > 1) continue;
            if (hasHeader && number == 1) continue;
            var fields = line.Split('\t');
            if (fields.Length < 2 || fields[0].Length == 0 || !fields[1].All(char.IsAsciiDigit) || fields[1].Length == 0 ||
                !long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var weight) || weight <= 0)
                throw new LedgerRefusalException(LedgerRefusalCode.FrequencyMalformed, "A frequency row is malformed.", number);
            entries.Add(new(fields[0], weight));
        }
        return new(LedgerTsvReader.Sha256(bytes), pins.TokenizerId, pins.TokenizerSha256, pins.Fold, pins.Normalization, entries);
    }

    public static PinnedFrequencyList ReadWordCountJson(byte[] bytes, LedgerSidecar pins)
    {
        var entries = new List<FrequencyEntry>();
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(bytes);
            foreach (var property in document.RootElement.EnumerateObject())
                entries.Add(new(property.Name, property.Value.GetInt64()));
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            throw new LedgerRefusalException(LedgerRefusalCode.FrequencyMalformed, "The frequency list is not a word-to-count object.");
        }
        return new(LedgerTsvReader.Sha256(bytes), pins.TokenizerId, pins.TokenizerSha256, pins.Fold, pins.Normalization, entries);
    }
}
