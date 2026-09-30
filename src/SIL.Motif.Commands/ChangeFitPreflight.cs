using System.Text.Json;
using System.Security.Cryptography;
using System.Threading;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Texts;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Worker.Baselines;
using SIL.LCModel;

namespace SIL.Motif.Commands;

/// <summary>The identities and observed form against which a collected change was composed.</summary>
public sealed record ChangeFitFingerprint(
    string WordformId, string? AnalysisId, string WordformForm, string BaselineToken,
    string? AnalysisContentDigest = null, string? ReadingContentDigest = null, ParseAnalysis? Reading = null,
    string? HumanOpinion = null, int? SpellingStatus = null, OccurrenceFitEvidence? Occurrence = null);

/// <summary>Checks collected changes against the saved project used by Review and Apply.</summary>
public static class ChangeFitPreflight
{
    public static IReadOnlyList<ChangeFitResult> Check(LcmCache cache, Proposal proposal,
        BaselineToken? currentBaseline = null, bool requireSameBaseline = true)
    {
        var result = new List<ChangeFitResult>();
        var objects = cache.ServiceLocator.ObjectRepository;
        var authoredChanges = AuthoredChangeOperationIds(proposal);
        var occurrenceTextIds = proposal.Operations.Select(OccurrenceTextId)
            .Where(id => id is not null).Select(id => id!.Value).ToHashSet();
        TextWordsProjection? occurrenceProjection = null;
        foreach (var operation in proposal.Operations)
        {
            string? changeId = null;
            ChangeFitResult NoFit(string reason, string baselineToken,
                string status = ChangeFitStatus.NoLongerFits, ChangeUncertainty? uncertainty = null) =>
                new(operation.OperationId.Value, false, reason, baselineToken)
                {
                    ChangeId = changeId,
                    Status = status,
                    Uncertainty = uncertainty,
                };

            if (operation.Extensions is not { } extensions)
            {
                if (authoredChanges.Contains(operation.OperationId.Value))
                    result.Add(NoFit(ChangeFitReasons.FingerprintMissing, ""));
                continue;
            }
            if (extensions.ValueKind != JsonValueKind.Object)
            {
                result.Add(NoFit(ChangeFitReasons.FingerprintMissing, ""));
                continue;
            }
            changeId = PendingChanges.Property(extensions, "changeId");
            var hasFit = extensions.TryGetProperty("changeFit", out var fit);
            if (!hasFit && !extensions.TryGetProperty("changeId", out _) &&
                !authoredChanges.Contains(operation.OperationId.Value)) continue;
            if (!hasFit || fit.ValueKind != JsonValueKind.Object)
            {
                result.Add(NoFit(ChangeFitReasons.FingerprintMissing, ""));
                continue;
            }
            ChangeFitFingerprint? fingerprint;
            try
            {
                fingerprint = JsonSerializer.Deserialize<ChangeFitFingerprint>(fit.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException)
            {
                result.Add(NoFit(ChangeFitReasons.FingerprintMalformed, ""));
                continue;
            }
            if (fingerprint is null || !CanonicalId.TryParse(fingerprint.WordformId, out var wordId) ||
                string.IsNullOrWhiteSpace(fingerprint.WordformForm) ||
                string.IsNullOrWhiteSpace(fingerprint.BaselineToken))
            {
                result.Add(NoFit(ChangeFitReasons.FingerprintIncomplete,
                    fingerprint?.BaselineToken ?? ""));
                continue;
            }
            if (!objects.TryGetObject(wordId.ToGuid(), out var wordObject) || wordObject is not IWfiWordform wordform)
            {
                result.Add(NoFit(ChangeFitReasons.WordformDeleted(wordId.Value), fingerprint.BaselineToken));
                continue;
            }
            var currentForm = wordform.Form.VernacularDefaultWritingSystem?.Text ?? "";
            if (!string.Equals(currentForm.Normalize(System.Text.NormalizationForm.FormD),
                fingerprint.WordformForm.Normalize(System.Text.NormalizationForm.FormD), StringComparison.Ordinal))
            {
                result.Add(NoFit(ChangeFitReasons.WordformChangedForm(wordId.Value), fingerprint.BaselineToken));
                continue;
            }
            if (operation.Kind == WfiWordformSpellingStatusOperationKinds.SetSpellingStatus &&
                fingerprint.SpellingStatus is null)
            {
                result.Add(NoFit(ChangeFitReasons.SpellingEvidenceMissing,
                    fingerprint.BaselineToken));
                continue;
            }
            if (operation.Kind == WfiWordformSpellingStatusOperationKinds.SetSpellingStatus &&
                wordform.SpellingStatus != fingerprint.SpellingStatus)
            {
                result.Add(NoFit(ChangeFitReasons.WordformSpellingChanged(wordId.Value), fingerprint.BaselineToken));
                continue;
            }
            if (fingerprint.AnalysisId is { } analysisId)
            {
                if (!CanonicalId.TryParse(analysisId, out var parsed))
                {
                    result.Add(NoFit(ChangeFitReasons.AnalysisIdentityInvalid, fingerprint.BaselineToken));
                    continue;
                }
                if (!wordform.AnalysesOC.Any(analysis => analysis.Guid == parsed.ToGuid()))
                {
                    result.Add(NoFit(ChangeFitReasons.AnalysisMissing(parsed.Value, wordId.Value),
                        fingerprint.BaselineToken));
                    continue;
                }
                var analysis = wordform.AnalysesOC.Single(item => item.Guid == parsed.ToGuid());
                if (fingerprint.AnalysisContentDigest is { } expected &&
                    !string.Equals(ContentDigest(analysis), expected, StringComparison.Ordinal))
                {
                    result.Add(NoFit(ChangeFitReasons.AnalysisReadingChanged(parsed.Value), fingerprint.BaselineToken));
                    continue;
                }
                if (operation.Kind is WfiAnalysisOperationKinds.AddRefEvaluations or
                    WfiAnalysisOperationKinds.RemoveRefEvaluations or
                    WfiAnalysisOperationKinds.DeleteAnalysis &&
                    !string.Equals(analysis.GetAgentOpinion(cache.LangProject.DefaultUserAgent).ToString(),
                        fingerprint.HumanOpinion, StringComparison.Ordinal))
                {
                    result.Add(NoFit(ChangeFitReasons.AnalysisOpinionChanged(parsed.Value), fingerprint.BaselineToken));
                    continue;
                }
            }
            if (operation.Kind == WfiAnalysisOperationKinds.CreateAnalysis)
            {
                var morphs = CreateAnalysisPayload.Parse(operation.After ?? throw new InvalidDataException(
                    "A candidate operation has no morphs."));
                var missing = morphs.SelectMany(morph => new[]
                    {
                        (Id: morph.Form, Type: typeof(IMoForm)),
                        (Id: morph.Msa, Type: typeof(IMoMorphSynAnalysis)),
                        (Id: morph.InflType, Type: typeof(ILexEntryInflType)),
                    })
                    .FirstOrDefault(reference => reference.Id is { } id &&
                        (!objects.TryGetObject(id.ToGuid(), out var value) ||
                         !reference.Type.IsInstanceOfType(value)));
                if (missing.Id is { } missingId)
                {
                    result.Add(NoFit(ChangeFitReasons.MorphReferenceMissing(missingId.Value),
                        fingerprint.BaselineToken));
                    continue;
                }
            }
            if (operation.Kind == WfiAnalysisOperationKinds.CreateAnalysis &&
                fingerprint.Reading is { } reading &&
                wordform.AnalysesOC.Any(analysis => AnalysisChangeComposer.Matches(analysis, reading)))
            {
                result.Add(NoFit(ChangeFitReasons.ReadingAlreadyExists(wordId.Value),
                    fingerprint.BaselineToken));
                continue;
            }
            BaselineToken? captured;
            try
            {
                captured = JsonSerializer.Deserialize<BaselineToken>(fingerprint.BaselineToken,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException) { captured = null; }
            if (captured is null || currentBaseline is null ||
                requireSameBaseline && !captured.HasSameSemanticIdentity(currentBaseline))
            {
                result.Add(NoFit(ChangeFitReasons.BaselineNotCurrent, fingerprint.BaselineToken));
                continue;
            }
            if (fingerprint.Occurrence is { } occurrence)
            {
                occurrenceProjection ??= TextWordsProjectionBuilder.Build(
                    cache, CancellationToken.None, occurrenceTextIds);
                if (OccurrenceFitEvidenceResolver.Compare(occurrence, occurrenceProjection) is { } uncertainty)
                {
                    result.Add(NoFit(uncertainty.Reason, fingerprint.BaselineToken,
                        ChangeFitStatus.Uncertain, uncertainty));
                    continue;
                }
            }
            result.Add(new ChangeFitResult(operation.OperationId.Value, true,
                "Still fits the live project.", fingerprint.BaselineToken)
            {
                ChangeId = changeId,
            });
        }
        return result;
    }

    private static HashSet<string> AuthoredChangeOperationIds(Proposal proposal)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (proposal.Extensions is not { ValueKind: JsonValueKind.Object } extensions ||
            !extensions.TryGetProperty("composers", out var composers) ||
            composers.ValueKind != JsonValueKind.Array) return ids;
        foreach (var composer in composers.EnumerateArray())
        {
            if (composer.ValueKind != JsonValueKind.Object ||
                !composer.TryGetProperty("changeId", out _) ||
                !composer.TryGetProperty("operationIds", out var operations) ||
                operations.ValueKind != JsonValueKind.Array) continue;
            foreach (var id in operations.EnumerateArray())
                if (id.ValueKind == JsonValueKind.String && id.GetString() is { } value) ids.Add(value);
        }
        return ids;
    }

    private static Guid? OccurrenceTextId(OperationEnvelope operation)
    {
        if (operation.Extensions is not { ValueKind: JsonValueKind.Object } extensions ||
            !extensions.TryGetProperty("changeFit", out var fit) || fit.ValueKind != JsonValueKind.Object)
            return null;
        try
        {
            return JsonSerializer.Deserialize<ChangeFitFingerprint>(fit.GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?.Occurrence?.Anchor.TextId;
        }
        catch (JsonException) { return null; }
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
