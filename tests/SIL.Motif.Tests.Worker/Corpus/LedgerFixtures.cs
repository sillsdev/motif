using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Worker.Corpus.Ledger;

namespace SIL.Motif.Tests.Worker.Corpus;

internal static class LedgerFixtures
{
    public const string Revision = "rev-a";
    public const string Pin = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    public static string Id(int n) => CanonicalId.FromGuid(new Guid(n, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1)).Value;

    public static string Reading(int entry, int morph, string features = "") =>
        $"{Id(entry)}/{Id(entry + 100)}/{Id(entry + 200)}|{Id(morph)}|{features}";

    public static string Row(string word, long tokens, string state, string expected = "", string cls = "",
        string evidence = "", string provenance = "p", string revision = Revision) =>
        string.Join('\t', word, tokens.ToString(), state, expected, cls, evidence, provenance, revision);

    public static byte[] Tsv(params string[] rows) =>
        Encoding.UTF8.GetBytes(LedgerTsvReader.Header + "\n" + string.Concat(rows.Select(row => row + "\n")));

    public static string SidecarJson(long rowCount, long tokenSum, string revision = Revision, string fold = "none",
        string normalization = "NFC", string? extra = null) => $$"""
        {"tokenizerId":"t@1","tokenizerSha256":"{{Pin}}","fold":"{{fold}}","normalization":"{{normalization}}",
         "sourceIds":["s1","s2"],"scopeFilter":"all","frequencySource":"freq.tsv","grammarRevision":"{{revision}}",
         "rowCount":{{rowCount}},"tokenSum":{{tokenSum}}{{(extra is null ? "" : "," + extra)}}}
        """;

    public static LedgerSidecar Sidecar(long rowCount = 0, long tokenSum = 0, string revision = Revision) =>
        LedgerSidecarReader.Read(Encoding.UTF8.GetBytes(SidecarJson(rowCount, tokenSum, revision)));

    public static PinnedFrequencyList Frequency(params (string Key, long Weight)[] entries) =>
        new("f-digest", "t@1", Pin, "none", "NFC", entries.Select(entry => new FrequencyEntry(entry.Key, entry.Weight)).ToArray());

    public static LedgerCoverageReport Report(string[] rows, (string, long)[] frequency,
        IReadOnlyDictionary<string, TypeCheck>? checks = null, string? audit = null, string? protectedSet = null)
    {
        var ledger = LedgerTsvReader.Read(Tsv(rows));
        var sidecar = Sidecar(ledger.Rows.Count, ledger.TokenSum);
        return LedgerCoverage.Build(new(ledger, sidecar, Frequency(frequency), checks, null, audit, protectedSet));
    }

    public static TypeCheck Complete(bool present, params string[] produced) =>
        new(CheckCompleteness.Complete, true, present, false, produced, Revision);

    public static string WriteJson(string path, object value)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(value));
        return path;
    }
}
