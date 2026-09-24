using System.Text.Json;
using System.Security.Cryptography;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.LCModel;

namespace SIL.Motif.Commands;

/// <summary>The live fit of one collected change against its authored wordform and analysis.</summary>
public sealed record ChangeFitResult(string OperationId, bool StillFits, string Reason, string BaselineToken);

/// <summary>The identities and observed form against which a collected change was composed.</summary>
public sealed record ChangeFitFingerprint(
    string WordformId, string? AnalysisId, string WordformForm, string BaselineToken,
    string? AnalysisContentDigest = null, string? ReadingContentDigest = null, ParseAnalysis? Reading = null);

/// <summary>Checks collected changes against the live project immediately before Apply.</summary>
public static class ChangeFitPreflight
{
    public static IReadOnlyList<ChangeFitResult> Check(LcmCache cache, Proposal proposal)
    {
        var result = new List<ChangeFitResult>();
        var objects = cache.ServiceLocator.ObjectRepository;
        foreach (var operation in proposal.Operations)
        {
            if (operation.Extensions is not { } extensions ||
                extensions.ValueKind != JsonValueKind.Object ||
                !extensions.TryGetProperty("changeFit", out var fit))
                continue;
            var fingerprint = JsonSerializer.Deserialize<ChangeFitFingerprint>(fit.GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("A change fit fingerprint is missing.");
            var wordId = CanonicalId.Parse(fingerprint.WordformId);
            if (!objects.TryGetObject(wordId.ToGuid(), out var wordObject) || wordObject is not IWfiWordform wordform)
            {
                result.Add(new ChangeFitResult(operation.OperationId.Value, false,
                    $"Wordform {wordId.Value} was deleted.", fingerprint.BaselineToken));
                continue;
            }
            var currentForm = wordform.Form.VernacularDefaultWritingSystem?.Text ?? "";
            if (!string.Equals(currentForm, fingerprint.WordformForm, StringComparison.Ordinal))
            {
                result.Add(new ChangeFitResult(operation.OperationId.Value, false,
                    $"Wordform {wordId.Value} changed form.", fingerprint.BaselineToken));
                continue;
            }
            if (fingerprint.AnalysisId is { } analysisId)
            {
                var parsed = CanonicalId.Parse(analysisId);
                if (!wordform.AnalysesOC.Any(analysis => analysis.Guid == parsed.ToGuid()))
                {
                    result.Add(new ChangeFitResult(operation.OperationId.Value, false,
                        $"Analysis {parsed.Value} was deleted or moved from wordform {wordId.Value}.",
                        fingerprint.BaselineToken));
                    continue;
                }
                var analysis = wordform.AnalysesOC.Single(item => item.Guid == parsed.ToGuid());
                if (fingerprint.AnalysisContentDigest is { } expected &&
                    !string.Equals(ContentDigest(analysis), expected, StringComparison.Ordinal))
                {
                    result.Add(new ChangeFitResult(operation.OperationId.Value, false,
                        $"Analysis {parsed.Value} changed its reading.", fingerprint.BaselineToken));
                    continue;
                }
            }
            if (operation.Kind == WfiAnalysisOperationKinds.CreateAnalysis &&
                fingerprint.Reading is { } reading &&
                wordform.AnalysesOC.Any(analysis => AnalysisChangeComposer.Matches(analysis, reading)))
            {
                result.Add(new ChangeFitResult(operation.OperationId.Value, false,
                    $"The parser reading already exists under wordform {wordId.Value}.", fingerprint.BaselineToken));
                continue;
            }
            result.Add(new ChangeFitResult(operation.OperationId.Value, true,
                "Still fits the live project.", fingerprint.BaselineToken));
        }
        return result;
    }

    public static string ContentDigest(IWfiAnalysis analysis) => Digest(analysis.MorphBundlesOS.Select(bundle => new
    {
        form = bundle.MorphRA?.Guid.ToString("D"),
        msa = bundle.MsaRA?.Guid.ToString("D"),
        inflType = bundle.InflTypeRA?.Guid.ToString("D"),
        guessedString = bundle.MorphRA is null ? bundle.Form.VernacularDefaultWritingSystem?.Text : null,
    }).ToArray());

    public static string ReadingDigest(ParseAnalysis reading) => Digest(reading.Morphs.Select(morph => new
    {
        form = morph.Form,
        msa = morph.Msa,
        inflType = morph.InflType,
        guessedString = morph.GuessedString,
    }).ToArray());

    private static string Digest<T>(T value) => "sha256:" +
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value))).ToLowerInvariant();
}
