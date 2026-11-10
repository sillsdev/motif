using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SIL.Motif.Contract.Responses;

/// <summary>
/// One fingerprint of a paragraph's runs: each run's text (NFD), writing-system tag, object data and every other
/// integer and string property. A paragraph edit records it for the text it began from, and Apply compares it with the
/// project, so a change of formatting alone is caught as well as a change of text.
/// </summary>
public static class TextRunsDigest
{
    /// <summary>One run as the fingerprint reads it; the property bags exclude the writing system and object data.</summary>
    public sealed record RunFacts(
        string Text, string Ws, IEnumerable<KeyValuePair<int, (int Variation, int Value)>> Ints,
        IEnumerable<KeyValuePair<int, string>> Strings, string? ObjData);

    /// <summary>The fingerprint of <paramref name="runs"/>, in order.</summary>
    public static string Of(IEnumerable<RunFacts> runs)
    {
        var canonical = runs.Select(run => new object?[]
        {
            run.Text.Normalize(NormalizationForm.FormD),
            run.Ws,
            run.Ints.OrderBy(pair => pair.Key).Select(pair => new[] { pair.Key, pair.Value.Variation, pair.Value.Value }).ToArray(),
            run.Strings.OrderBy(pair => pair.Key).Select(pair => new object[] { pair.Key, pair.Value }).ToArray(),
            run.ObjData,
        }).ToArray();
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(canonical))).ToLowerInvariant();
    }

    /// <summary>The fingerprint of a paragraph as <c>gdr show</c> returns it.</summary>
    public static string Of(GdrParagraph paragraph) => OfRuns(paragraph.Runs);

    /// <summary>The fingerprint of runs as the project's readers return them.</summary>
    public static string OfRuns(IEnumerable<GdrRun> runs) => Of(runs.Select(run => new RunFacts(run.Text, run.WritingSystem,
        run.Properties.Ints.Select(pair => KeyValuePair.Create(pair.Key, (pair.Value.Variation, pair.Value.Value))),
        run.Properties.Strings, run.ObjData)));
}
