namespace SIL.Motif.Tests.PanGloss;

/// <summary>
/// Shapes recorded from the why-not format document, with invented ids and guids of a made-up language, so no grammar
/// data is committed. Each is what the parser writes, trimmed to the fields Motif reads.
/// </summary>
internal static class WhyNotFixtures
{
    internal static string Describe(string? whyNot, bool command = true, bool hidden = false)
    {
        var interfaces = "\"serveProtocol\": 2, \"batchTsv\": 5" + (whyNot is null ? "" : ", \"whyNot\": " + whyNot);
        var rows = new List<string>
        {
            "{ \"name\": \"parse\", \"positionals\": [\"grammar\", \"word\"], \"flags\": [ { \"name\": \"--trace\", \"takes_value\": true } ] }",
        };
        if (command)
            rows.Add("{ \"name\": \"why-not\", \"hidden\": " + (hidden ? "true" : "false") + ", " + WhyNotRow);
        return "{ \"schema_version\": 1, \"binary\": \"pangloss\", \"interfaces\": { " + interfaces + " }, \"commands\": [" +
            string.Join(',', rows) + "] }";
    }

    private const string WhyNotRow = """
        "positionals": ["grammar", "word|queries.jsonl", "output.jsonl?"],
        "flags": [ { "name": "--expect", "takes_value": true }, { "name": "--expect-guid", "takes_value": true },
          { "name": "--expect-json", "takes_value": true }, { "name": "--json", "takes_value": false },
          { "name": "--work-cap", "takes_value": true }, { "name": "--word-timeout-ms", "takes_value": true } ] }
        """;

    internal const string DeclaredWhyNot = """
        { "schema": "pangloss.whynot.v1", "ids": "grammar-xml", "suiteRows": true,
          "kinds": ["no_morph_match", "environment_unmet", "adhoc_prohibition"] }
        """;

    private const string Expect = """
        "expect": [[{"morpheme":"kib"}],[{"morpheme":"ak"}],[{"morpheme":"ek"}]]
        """;

    internal static string Report(params string[] results) =>
        "{\"schema\":\"pangloss.whynot.v1\",\"word\":\"kibakek\",\"results\":[" + string.Join(',', results) + "],\"best\":0}";

    internal const string Accepted = $$"""
        { {{Expect}}, "result": "accepted",
          "analysis": { "analysis": { "morphs": [ {"morpheme":"kib","allomorph":"kib-1"}, {"morpheme":"ak","allomorph":"ak-1"},
            {"morpheme":"ek","allomorph":"ek-1"} ], "root_index": 0, "category": "posN", "guessed": false }, "used": [] },
          "failure": null, "stop": null }
        """;

    internal const string NoMorphMatch = $$"""
        { {{Expect}}, "result": "rejected", "analysis": null,
          "failure": { "kind": "no_morph_match",
            "plain": "No morph matches at position 1: nothing in the word can be the expected suffix `ak`.",
            "position": 1, "hc_reason": null, "provenance": "derived",
            "object": { "type": "affix", "id": "ak", "guid": "00000000-0000-0000-0000-000000000015" },
            "morphs": [ {"morpheme":"ak"} ], "context": null, "environment": null, "stem_name": null,
            "features": [], "alternatives": null },
          "stop": null }
        """;

    internal const string EnvironmentUnmet = $$"""
        { {{Expect}}, "result": "rejected", "analysis": null,
          "failure": { "kind": "environment_unmet",
            "plain": "The allomorph `ek-a` of suffix `ek` needs a left context of a back vowel.",
            "position": 2, "hc_reason": "Environments", "provenance": "hc_reported",
            "object": { "type": "allomorph", "id": "ek-a", "guid": null },
            "morphs": [ {"morpheme":"ek","allomorph":"ek-a"} ],
            "context": { "left": "kib", "right": "k", "provenance": "derived" },
            "environment": { "allomorph_id": "ek-a", "index": 0 },
            "stem_name": null, "features": [], "alternatives": 1 },
          "stop": null }
        """;

    /// <summary>A snapshot-route environment failure: one environment failed, so it is named with its guid and text.</summary>
    internal const string EnvironmentUnmetOnSnapshot = $$$"""
        { {{{Expect}}}, "result": "rejected", "analysis": null,
          "failure": { "kind": "environment_unmet",
            "plain": "The allomorph `aSal` of suffix `sal` is not allowed here (environment /[V]_).",
            "position": 1, "hc_reason": "Environments", "provenance": "hc_reported",
            "object": { "type": "allomorph", "id": "aSal", "guid": "00000000-0000-0000-0000-000000000111" },
            "morphs": [ {"morpheme":"sal","allomorph":"aSal","form_guid":"00000000-0000-0000-0000-000000000111",
              "guid":{"entry_guid":"00000000-0000-0000-0000-000000000100","msa_guid":"00000000-0000-0000-0000-000000000102"}} ],
            "context": { "left": "da", "right": "", "provenance": "derived" },
            "environment": { "allomorph_id": "aSal", "guid": "00000000-0000-0000-0000-000000000017", "text": "/[V]_", "index": 0 },
            "stem_name": null, "features": [], "alternatives": null },
          "stop": null }
        """;

    /// <summary>An any-of failure: every environment of the allomorph failed, so the allomorph is named and each one listed.</summary>
    internal const string EnvironmentAnyOfUnmet = $$$"""
        { {{{Expect}}}, "result": "rejected", "analysis": null,
          "failure": { "kind": "environment_unmet",
            "plain": "The allomorph `aSal` of suffix `sal` is not allowed here: none of the environments /[V]_, /t_ is met.",
            "position": 1, "hc_reason": "Environments", "provenance": "hc_reported",
            "object": { "type": "allomorph", "id": "aSal", "guid": "00000000-0000-0000-0000-000000000101" },
            "morphs": [ {"morpheme":"sal","allomorph":"aSal","form_guid":"00000000-0000-0000-0000-000000000101"} ],
            "context": null,
            "environment": { "allomorph_id": "aSal" },
            "environments": [
              { "allomorph_id": "aSal", "guid": "00000000-0000-0000-0000-000000000017", "text": "/[V]_", "index": 0 },
              { "allomorph_id": "aSal", "guid": "00000000-0000-0000-0000-000000000030", "text": "/t_", "index": 1 } ],
            "stem_name": null, "features": [], "alternatives": null },
          "stop": null }
        """;

    /// <summary>A co-occurrence prohibition found on the snapshot route, with the rule's guid and every member.</summary>
    internal const string AdhocProhibition = $$$"""
        { {{{Expect}}}, "result": "rejected", "analysis": null,
          "failure": { "kind": "adhoc_prohibition",
            "plain": "The rule forbids `tal` with `sal` in one word.",
            "position": null, "hc_reason": "AdhocProhibition", "provenance": "hc_reported",
            "object": { "type": "rule", "id": null, "guid": "00000000-0000-0000-0000-0000000009a1" },
            "morphs": [ {"morpheme":"tal","allomorph":"aTal"}, {"morpheme":"sal","allomorph":"aSal"} ],
            "members": [
              {"morpheme":"tal","allomorph":"aTal","form_guid":"00000000-0000-0000-0000-000000000201",
                "guid":{"entry_guid":"00000000-0000-0000-0000-000000000200","msa_guid":"00000000-0000-0000-0000-000000000202"}},
              {"morpheme":"sal","allomorph":"aSal","form_guid":"00000000-0000-0000-0000-000000000101",
                "guid":{"entry_guid":"00000000-0000-0000-0000-000000000100","msa_guid":"00000000-0000-0000-0000-000000000102"}} ],
            "context": null, "environment": null, "stem_name": null, "features": [], "alternatives": null },
          "stop": null }
        """;

    /// <summary>Two equal prohibitions: the parser cannot say which fired, so the rule's guid is null though the members are known.</summary>
    internal const string AdhocProhibitionAmbiguous = $$$"""
        { {{{Expect}}}, "result": "rejected", "analysis": null,
          "failure": { "kind": "adhoc_prohibition",
            "plain": "A rule forbids `tal` with `sal` in one word.",
            "position": null, "hc_reason": "AdhocProhibition", "provenance": "hc_reported",
            "object": { "type": "rule", "id": null, "guid": null },
            "morphs": [ {"morpheme":"tal","allomorph":"aTal"}, {"morpheme":"sal","allomorph":"aSal"} ],
            "members": [ {"morpheme":"tal","allomorph":"aTal","form_guid":"00000000-0000-0000-0000-000000000201"},
              {"morpheme":"sal","allomorph":"aSal","form_guid":"00000000-0000-0000-0000-000000000101"} ],
            "context": null, "environment": null, "stem_name": null, "features": [], "alternatives": null },
          "stop": null }
        """;

    internal const string Unknown = $$"""
        { {{Expect}}, "result": "unknown", "analysis": null, "failure": null,
          "stop": "unclassified_failure", "stop_note": "HC stopped the derivation for a reason this release does not name." }
        """;
}
