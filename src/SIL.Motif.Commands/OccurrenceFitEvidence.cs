using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Texts;

namespace SIL.Motif.Commands;

/// <summary>Baseline-relative evidence for the word occurrence that framed a pending decision.</summary>
public sealed record OccurrenceFitEvidence(
    OccurrenceAnchor Anchor,
    string WordformId,
    string? AnalysisId,
    string Token,
    bool ParseIsCurrent,
    string WordDigest,
    IReadOnlyList<OccurrenceWordToken> Tokens,
    int? WordPosition = null);

internal static class OccurrenceFitEvidenceResolver
{
    public static bool TryCapture(TextWordsProjection projection, OccurrenceAnchor anchor,
        out OccurrenceFitEvidence? evidence, out string reason)
    {
        evidence = null;
        if (anchor.TextId == Guid.Empty || anchor.ParagraphId == Guid.Empty || anchor.SegmentId == Guid.Empty ||
            anchor.Index < 0)
        {
            reason = "The occurrence anchor has an invalid identity or index.";
            return false;
        }

        var texts = projection.Texts.Where(text => text.TextId == anchor.TextId).Take(2).ToArray();
        var lines = texts.Length == 1
            ? texts[0].Lines.Where(line => line.ParagraphId == anchor.ParagraphId &&
                line.SegmentId == anchor.SegmentId).Take(2).ToArray()
            : [];
        if (texts.Length != 1 || lines.Length != 1)
        {
            reason = "The occurrence anchor does not resolve to one Segment in the current Baseline.";
            return false;
        }

        var tokens = lines[0].Tokens.Where(token => token.OccurrenceIndex == anchor.Index).Take(2).ToArray();
        if (tokens.Length != 1 || tokens[0].WordformId is not { } wordformId)
        {
            reason = "The occurrence anchor does not resolve to one word.";
            return false;
        }

        var wordTokens = WordTokens(lines[0]);
        var wordPosition = Array.FindIndex(wordTokens, token => token.Index == anchor.Index);
        evidence = new OccurrenceFitEvidence(anchor, CanonicalId.FromGuid(wordformId).Value,
            tokens[0].AnalysisId is { } analysisId ? CanonicalId.FromGuid(analysisId).Value : null,
            tokens[0].Text, lines[0].ParseIsCurrent, WordDigest(wordTokens), wordTokens, wordPosition);
        reason = string.Empty;
        return true;
    }

    public static ChangeUncertainty? Compare(OccurrenceFitEvidence expected, TextWordsProjection projection)
    {
        var texts = projection.Texts.Where(text => text.TextId == expected.Anchor.TextId).Take(2).ToArray();
        var lines = texts.Length == 1
            ? texts[0].Lines.Where(line => line.ParagraphId == expected.Anchor.ParagraphId &&
                line.SegmentId == expected.Anchor.SegmentId).Take(2).ToArray()
            : [];
        if (lines.Length != 1)
            return Uncertain("The source Segment is gone or no longer resolves uniquely.", expected.Tokens, []);

        var line = lines[0];
        var currentTokens = WordTokens(line);
        if (expected.WordPosition is not { } wordPosition || wordPosition < 0 || wordPosition >= currentTokens.Length)
            return Uncertain("The source occurrence no longer resolves uniquely.", expected.Tokens, currentTokens);
        var selectedWord = currentTokens[wordPosition];
        var anchorTokens = line.Tokens.Where(token => token.OccurrenceIndex == selectedWord.Index).Take(2).ToArray();
        if (anchorTokens.Length != 1 || anchorTokens[0].WordformId is not { } currentWordformId)
            return Uncertain("The source occurrence no longer resolves uniquely.", expected.Tokens, currentTokens);
        if (!line.ParseIsCurrent)
            return Uncertain("The paragraph parse is not current.", expected.Tokens, currentTokens);
        if (!expected.ParseIsCurrent)
            return Uncertain("The paragraph parse was not current when the decision was collected.",
                expected.Tokens, currentTokens);
        if (CanonicalId.FromGuid(currentWordformId).Value != expected.WordformId ||
            (anchorTokens[0].AnalysisId is { } currentAnalysisId
                ? CanonicalId.FromGuid(currentAnalysisId).Value : null) != expected.AnalysisId ||
            WordDigest(currentTokens) != expected.WordDigest)
            return Uncertain("The words in the source sentence have changed.", expected.Tokens, currentTokens);

        return null;
    }

    private static OccurrenceWordToken[] WordTokens(TextWordsProjectedLine line) => line.Tokens
        .Where(token => token.WordformId is not null)
        .OrderBy(token => token.OccurrenceIndex)
        .Select(token => new OccurrenceWordToken(token.OccurrenceIndex,
            CanonicalId.FromGuid(token.WordformId!.Value).Value, token.Text))
        .ToArray();

    private static string WordDigest(IReadOnlyList<OccurrenceWordToken> tokens)
    {
        var words = tokens.Select(token => new
        {
            wordformId = token.WordformId,
            form = token.Form.Normalize(NormalizationForm.FormD),
        }).ToArray();
        var json = JsonSerializer.Serialize(words);
        var canonical = CanonicalJson.CanonicalizeToUtf8(json);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant();
    }

    private static ChangeUncertainty Uncertain(string reason, IReadOnlyList<OccurrenceWordToken> before,
        IReadOnlyList<OccurrenceWordToken> after) => new(reason, before, after);
}
