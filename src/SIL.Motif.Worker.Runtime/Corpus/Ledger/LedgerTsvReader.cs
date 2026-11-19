using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SIL.Motif.Contract.Ids;

namespace SIL.Motif.Worker.Corpus.Ledger;

/// <summary>Reads the shared eight-column lane ledger exactly as written, refusing anything malformed.</summary>
internal static class LedgerTsvReader
{
    public const string Header = "word\ttokens\tstate\texpected_readings\tclass\tevidence\tprovenance\tgrammar_revision";

    public static string Sha256(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static LedgerFile Read(byte[] bytes)
    {
        var digest = Sha256(bytes);
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes.AsSpan(bytes.AsSpan().StartsWith("﻿"u8.ToArray()) ? 3 : 0)); }
        catch (DecoderFallbackException) { throw new LedgerRefusalException(LedgerRefusalCode.NotUtf8, "The ledger is not valid UTF-8."); }
        if (text.Length == 0) throw new LedgerRefusalException(LedgerRefusalCode.EmptyFile, "The ledger is empty.");

        var rows = new List<LedgerRow>();
        long tokenSum = 0;
        var number = 0;
        var start = 0;
        while (start < text.Length)
        {
            var end = text.IndexOf('\n', start);
            var lineEnd = end < 0 ? text.Length : end;
            var line = text.AsSpan(start, lineEnd - start);
            if (line.EndsWith("\r")) line = line[..^1];
            start = end < 0 ? text.Length : end + 1;
            number++;
            if (number == 1)
            {
                if (!line.SequenceEqual(Header))
                    throw new LedgerRefusalException(LedgerRefusalCode.BadHeader, "The ledger header is not the eight literal columns.", 1);
                continue;
            }
            var row = ParseRow(number, line.ToString());
            tokenSum = checked(tokenSum + row.Tokens);
            rows.Add(row);
        }
        return new(digest, rows, tokenSum);
    }

    private static LedgerRow ParseRow(int number, string line)
    {
        var fields = line.Split('\t');
        if (fields.Length != 8)
            throw new LedgerRefusalException(LedgerRefusalCode.WrongFieldCount, "A ledger row must have eight fields.", number);
        if (fields[0].Length == 0)
            throw new LedgerRefusalException(LedgerRefusalCode.EmptyWord, "A ledger row has an empty word.", number);
        if (fields[7].Length == 0)
            throw new LedgerRefusalException(LedgerRefusalCode.EmptyGrammarRevision, "A ledger row has no grammar revision.", number);
        if (fields[1].Length == 0 || !fields[1].All(char.IsAsciiDigit) ||
            !long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var tokens) || tokens <= 0)
            throw new LedgerRefusalException(LedgerRefusalCode.BadTokens, "A ledger row needs a positive base-10 token count.", number);
        LedgerState state = fields[2] switch
        {
            "Expected" => LedgerState.Expected,
            "Unverified" => LedgerState.Unverified,
            "Disposition" => LedgerState.Disposition,
            "Open" => LedgerState.Open,
            "Lost" => LedgerState.Lost,
            _ => throw new LedgerRefusalException(LedgerRefusalCode.BadState, "A ledger row has an unknown state.", number),
        };
        return new(number, fields[0], tokens, state, fields[2], fields[3], ExpectedReadingsParser.Parse(fields[3], number),
            Absent(fields[4]), Absent(fields[5]), Absent(fields[6]), fields[7]);
    }

    private static string? Absent(string field) => field.Length == 0 ? null : field;
}

/// <summary>Parses the semicolon-separated <c>entry/sense/msa|morph,...|feat=val,...</c> projection.</summary>
internal static class ExpectedReadingsParser
{
    public static IReadOnlyList<ExpectedReadingItem> Parse(string cell, int row)
    {
        if (cell.Length == 0) return [];
        var items = new List<ExpectedReadingItem>();
        foreach (var item in cell.Split(';'))
        {
            var groups = item.Split('|');
            if (groups.Length != 3) throw Bad(row);
            var head = groups[0].Split('/');
            if (head.Length != 3) throw Bad(row);
            var morphs = groups[1].Split(',').Select(id => Canonical(id, row, optional: false)!).ToArray();
            var features = new SortedSet<string>(StringComparer.Ordinal);
            if (groups[2].Length != 0)
                foreach (var pair in groups[2].Split(','))
                {
                    var parts = pair.Split('=');
                    if (parts.Length != 2) throw Bad(row);
                    features.Add(Canonical(parts[0], row, optional: false) + "=" + Canonical(parts[1], row, optional: false));
                }
            var entry = Canonical(head[0], row, optional: true);
            var sense = Canonical(head[1], row, optional: true);
            var msa = Canonical(head[2], row, optional: true);
            items.Add(new(entry, sense, msa, morphs, features.ToArray(),
                $"{entry}/{sense}/{msa}|{string.Join(',', morphs)}|{string.Join(',', features)}"));
        }
        return items;
    }

    private static string? Canonical(string text, int row, bool optional)
    {
        if (text.Length == 0) return optional ? null : throw Bad(row);
        Guid guid;
        if (text.Length == CanonicalId.SuffixLength)
        {
            if (!CanonicalId.TryParse(text, out var id)) throw Bad(row);
            guid = id.ToGuid();
        }
        else if (!Guid.TryParseExact(text, "D", out guid)) throw Bad(row);
        return guid == Guid.Empty ? throw Bad(row) : CanonicalId.FromGuid(guid).Value;
    }

    private static LedgerRefusalException Bad(int row) =>
        new(LedgerRefusalCode.BadExpectedReadings, "An expected_readings cell is malformed.", row);
}
