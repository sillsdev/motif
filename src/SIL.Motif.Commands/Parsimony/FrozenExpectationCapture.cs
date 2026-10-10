using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Host.Analysis;
using SIL.Motif.LiveHost.Baselines;
using SIL.Motif.Projection;
using SIL.Motif.Projection.HumanJudgments;

namespace SIL.Motif.Commands.Parsimony;

/// <summary>Freezes parser expectations from the loaded source before any candidate mutation.</summary>
public static class FrozenExpectationCapture
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Builds a closed, independent expectation set for an explicit affected-wordform manifest.</summary>
    public static FrozenExpectationSet Capture(LcmCache cache, BaselineToken baseline,
        IReadOnlyCollection<Guid> affectedWordforms, IReadOnlySet<string> selectionCaseKeys,
        IReadOnlySet<string> heldOutCaseKeys)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(affectedWordforms);
        ArgumentNullException.ThrowIfNull(selectionCaseKeys);
        ArgumentNullException.ThrowIfNull(heldOutCaseKeys);

        var unavailable = new List<string>();
        if (!string.Equals(baseline.ProjectIdentity, cache.LangProject.Guid.ToString("D"), StringComparison.Ordinal))
            unavailable.Add("baseline-project-mismatch: the Baseline belongs to another loaded project.");
        if (!string.Equals(baseline.ProjectionVersion, BaselineSemanticDigest.ProjectionVersion, StringComparison.Ordinal))
            unavailable.Add("baseline-projection-mismatch: the Baseline uses another semantic projection.");
        try
        {
            if (!string.Equals(BaselineSemanticDigest.Compute(cache), baseline.SemanticSnapshotDigest,
                    StringComparison.Ordinal))
                unavailable.Add("baseline-semantic-mismatch: the loaded source differs from the Baseline snapshot.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            unavailable.Add("baseline-semantic-unavailable: " + exception.Message);
        }
        if (affectedWordforms.Count == 0) unavailable.Add("The affected-wordform manifest is empty.");
        if (affectedWordforms.Distinct().Count() != affectedWordforms.Count)
            unavailable.Add("The affected-wordform manifest repeats an identity.");

        var allWordforms = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances()
            .ToDictionary(item => item.Guid);
        var vernacularHandles = cache.ServiceLocator.WritingSystems.VernacularWritingSystems
            .Select(item => item.Handle).ToHashSet();
        var consumedSelection = new HashSet<string>(StringComparer.Ordinal);
        var consumedHeldOut = new HashSet<string>(StringComparer.Ordinal);
        var cases = new List<FrozenExpectationCase>();

        foreach (var id in affectedWordforms.Distinct().Order())
        {
            if (!allWordforms.TryGetValue(id, out var wordform))
            {
                unavailable.Add($"Affected wordform {GuidText(id)} is absent from the loaded source.");
                continue;
            }

            var caseCount = 0;
            foreach (var writingSystemId in wordform.Form.AvailableWritingSystemIds
                         .Where(vernacularHandles.Contains).Order())
            {
                var writingSystem = cache.WritingSystemFactory.GetStrFromWs(writingSystemId);
                var surface = wordform.Form.get_String(writingSystemId)?.Text?.Normalize(NormalizationForm.FormD);
                if (string.IsNullOrEmpty(surface)) continue;

                var membershipKey = CaseKey(CanonicalId.FromGuid(wordform.Guid).Value, writingSystem, surface);
                var inSelection = selectionCaseKeys.Contains(membershipKey);
                var heldOut = heldOutCaseKeys.Contains(membershipKey);
                if (inSelection) consumedSelection.Add(membershipKey);
                if (heldOut) consumedHeldOut.Add(membershipKey);

                var readings = new List<FrozenExpectedReading>();
                foreach (var analysis in wordform.AnalysesOC.OrderBy(item => item.Guid.ToString("D"), StringComparer.Ordinal))
                {
                    var reading = ReadReading(cache, baseline, wordform, analysis, unavailable);
                    if (reading is not null) readings.Add(reading);
                }

                cases.Add(new FrozenExpectationCase(CaseId(wordform.Guid, writingSystem, surface),
                    CanonicalId.FromGuid(wordform.Guid).Value, writingSystem, surface, inSelection, heldOut,
                    Array.AsReadOnly(readings.ToArray())));
                caseCount++;
            }
            if (caseCount == 0)
                unavailable.Add($"Affected wordform {GuidText(id)} has no populated vernacular surface case.");
        }

        foreach (var extra in selectionCaseKeys.Except(consumedSelection, StringComparer.Ordinal))
            unavailable.Add($"Selection membership names uncaptured case '{extra}'.");
        foreach (var extra in heldOutCaseKeys.Except(consumedHeldOut, StringComparer.Ordinal))
            unavailable.Add($"Held-out membership names uncaptured case '{extra}'.");

        var expectationProjection = ParsimonyExpectationProjectionBuilder.Build(cache);
        unavailable.AddRange(expectationProjection.Issues.Select(item =>
            $"{item.Code}: {item.Detail}"));
        var negatives = ReadNegatives(expectationProjection.ReviewedNegatives, unavailable);
        var orderedCases = cases.OrderBy(item => item.CaseId, StringComparer.Ordinal).ToArray();
        var orderedNegatives = negatives.OrderBy(item => item.CaseId, StringComparer.Ordinal).ToArray();
        var manifestJson = JsonSerializer.Serialize(new { cases = orderedCases, reviewedNegatives = orderedNegatives }, JsonOptions);
        var manifestDigest = Digest(CanonicalJson.Canonicalize(manifestJson));
        var humanJson = JsonSerializer.Serialize(new
        {
            projectionDigest = expectationProjection.Digest,
            evaluations = orderedCases.SelectMany(item => item.Readings.Select(reading => new
            {
                item.CaseId,
                reading.ReadingId,
                reading.Opinion,
                reading.Evaluations,
            })),
        }, JsonOptions);
        var humanDigest = Digest(CanonicalJson.Canonicalize(humanJson));
        var revision = "frozen-expectations/v1/" + manifestDigest;
        var value = new FrozenExpectationSet(baseline, humanDigest, manifestDigest, revision,
            Array.AsReadOnly(orderedCases), Array.AsReadOnly(orderedNegatives),
            Array.AsReadOnly(unavailable.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()));
        return FrozenExpectationCodec.Parse(FrozenExpectationCodec.ToJson(value));
    }

    /// <summary>Builds an exact writing-system and surface key for Selection and held-out membership.</summary>
    public static string CaseKey(string wordformId, string writingSystem, string surface) =>
        string.Join('\0', wordformId, writingSystem, surface.Normalize(NormalizationForm.FormD));

    private static FrozenExpectedReading? ReadReading(LcmCache cache, BaselineToken baseline,
        IWfiWordform wordform, IWfiAnalysis analysis, ICollection<string> unavailable)
    {
        var approved = wordform.HumanApprovedAnalyses.Contains(analysis);
        var disapproved = wordform.HumanDisapprovedParses.Contains(analysis);
        var opinion = approved && disapproved ? "mixed" : approved ? "approved" : disapproved ? "disapproved" : "unknown";
        var evaluations = new List<FrozenHumanEvaluation>();
        foreach (var evaluation in analysis.EvaluationsRC.OrderBy(item => item.Guid.ToString("D"), StringComparer.Ordinal))
        {
            if (evaluation.Owner is not ICmAgent agent)
            {
                unavailable.Add($"Evaluation {GuidText(evaluation.Guid)} on reading {GuidText(analysis.Guid)} has no agent.");
                continue;
            }
            var evaluationOpinion = agent.ApprovesOA?.Guid == evaluation.Guid ? "approved"
                : agent.DisapprovesOA?.Guid == evaluation.Guid ? "disapproved" : null;
            if (evaluationOpinion is null)
            {
                unavailable.Add($"Evaluation {GuidText(evaluation.Guid)} on reading {GuidText(analysis.Guid)} has no recorded Opinion.");
                continue;
            }
            evaluations.Add(new FrozenHumanEvaluation(CanonicalId.FromGuid(evaluation.Guid).Value,
                CanonicalId.FromGuid(agent.Guid).Value, evaluationOpinion));
        }

        var morphs = new List<FrozenExpectationMorph>();
        var provenanceMorphs = new List<object>();
        foreach (var bundle in analysis.MorphBundlesOS)
        {
            var bundleTexts = ReadBundleTexts(cache, bundle.Form);
            var form = bundle.MorphRA is null ? null : CanonicalId.FromGuid(bundle.MorphRA.Guid).Value;
            var msa = bundle.MsaRA is null ? null : CanonicalId.FromGuid(bundle.MsaRA.Guid).Value;
            if (msa is null)
            {
                unavailable.Add($"Reading {GuidText(analysis.Guid)} has a bundle without MSA identity.");
                return null;
            }

            var guessedString = form is null
                ? bundle.Form.VernacularDefaultWritingSystem?.Text?.Normalize(NormalizationForm.FormD)
                : null;
            var guessedWritingSystem = form is null
                ? WritingSystemTextReader.SingleId(cache, bundle.Form.VernacularDefaultWritingSystem)
                : null;
            if (form is null && (string.IsNullOrEmpty(guessedString) || guessedWritingSystem is null))
            {
                unavailable.Add($"Reading {GuidText(analysis.Guid)} has unresolved guessed morphology text.");
                return null;
            }

            var bundleValue = new FrozenExpectationMorph(CanonicalId.FromGuid(bundle.Guid).Value, form, msa,
                bundle.InflTypeRA is null ? null : CanonicalId.FromGuid(bundle.InflTypeRA.Guid).Value,
                "whole", guessedString, guessedWritingSystem, bundleTexts);
            morphs.Add(bundleValue);
            provenanceMorphs.Add(new
            {
                bundle = bundleValue,
                glosses = bundle.SenseRA is null ? [] : ReadAlternatives(cache, bundle.SenseRA.Gloss),
                category = analysis.CategoryRA?.Guid is { } categoryId
                    ? CanonicalId.FromGuid(categoryId).Value : null,
                categoryForms = analysis.CategoryRA is null ? [] : ReadAlternatives(cache,
                    analysis.CategoryRA.Abbreviation.AvailableWritingSystemIds.Any()
                        ? analysis.CategoryRA.Abbreviation : analysis.CategoryRA.Name),
            });
        }

        if (morphs.Count == 0)
        {
            unavailable.Add($"Reading {GuidText(analysis.Guid)} has no ordered morphology.");
            return null;
        }

        var textUses = analysis.OccurrencesInTexts.SelectMany(segment =>
                segment.GetOccurrencesOfAnalysis(analysis, int.MaxValue, includeChildren: true)
                    .Select(occurrence => new { segment = GuidText(segment.Guid), occurrence.Index }))
            .OrderBy(item => item.segment, StringComparer.Ordinal).ThenBy(item => item.Index).ToArray();
        var provenanceJson = JsonSerializer.Serialize(new
        {
            sourceModel = baseline.SemanticSnapshotDigest,
            analysis = CanonicalId.FromGuid(analysis.Guid).Value,
            opinion,
            evaluations,
            category = analysis.CategoryRA is null ? null : CanonicalId.FromGuid(analysis.CategoryRA.Guid).Value,
            morphs = provenanceMorphs,
            textUses,
        }, JsonOptions);
        return new FrozenExpectedReading(CanonicalId.FromGuid(analysis.Guid).Value,
            CanonicalId.FromGuid(analysis.Guid).Value, opinion, Digest(CanonicalJson.Canonicalize(provenanceJson)),
            Array.AsReadOnly(evaluations.ToArray()), Array.AsReadOnly(morphs.ToArray()));
    }

    private static IReadOnlyList<FrozenReviewedNegative> ReadNegatives(
        IReadOnlyList<ReviewedNegativeExpectation> expectations, ICollection<string> unavailable)
    {
        var result = new List<FrozenReviewedNegative>();
        foreach (var item in expectations)
        {
            if (item.Status != "eligible")
            {
                unavailable.Add($"Reviewed negative {item.CaseId} is {item.Status}: {item.Issue ?? "unresolved"}.");
                continue;
            }

            var target = item.Target switch
            {
                SurfaceNegativeTarget => "surface",
                ReadingNegativeTarget => "reading",
                _ => "unknown",
            };
            if (target == "unknown")
            {
                unavailable.Add($"Reviewed negative {item.CaseId} has an unsupported target.");
                continue;
            }

            var morphs = new List<FrozenExpectationMorph>();
            if (item.Target is ReadingNegativeTarget reading)
            {
                foreach (var morph in reading.Morphs)
                {
                    if (morph.Identity.Form is null || morph.Identity.Msa is null)
                    {
                        unavailable.Add($"Reviewed negative {item.CaseId} has incomplete ordered reading identity.");
                        morphs.Clear();
                        break;
                    }
                    morphs.Add(new FrozenExpectationMorph(null, morph.Identity.Form, morph.Identity.Msa,
                        morph.Identity.InflType, "whole", morph.Identity.GuessedString?.Normalize(NormalizationForm.FormD),
                        morph.GuessedWritingSystem, []));
                }
                if (morphs.Count == 0) continue;
            }

            result.Add(new FrozenReviewedNegative(item.CaseId, item.RevisionId, item.ContentDigest,
                item.WritingSystem, item.Form.Normalize(NormalizationForm.FormD), item.Context, target,
                Array.AsReadOnly(morphs.ToArray())));
        }
        return result;
    }

    private static IReadOnlyList<FrozenBundleText> ReadBundleTexts(LcmCache cache, IMultiAccessorBase value)
    {
        var result = new List<FrozenBundleText>();
        foreach (var writingSystemId in value.AvailableWritingSystemIds.Order())
        {
            var writingSystem = cache.WritingSystemFactory.GetStrFromWs(writingSystemId);
            var richText = value.get_String(writingSystemId);
            if (richText is null) continue;
            var text = richText.get_NormalizedForm(FwNormalizationMode.knmNFSC)?.Text ?? richText.Text ?? string.Empty;
            result.Add(new FrozenBundleText(writingSystem, text, RichDigest(richText)));
        }
        return Array.AsReadOnly(result.ToArray());
    }

    private static IReadOnlyList<object> ReadAlternatives(LcmCache cache, IMultiAccessorBase value)
    {
        var result = new List<object>();
        if (value is not ITsMultiString strings) return result;
        foreach (var writingSystemId in value.AvailableWritingSystemIds.Order())
        {
            var richText = strings.get_String(writingSystemId);
            if (richText is null) continue;
            var text = value is IMultiUnicode
                ? richText.Text?.Normalize(NormalizationForm.FormD) ?? string.Empty
                : richText.get_NormalizedForm(FwNormalizationMode.knmNFSC)?.Text ?? richText.Text ?? string.Empty;
            result.Add(new { writingSystem = cache.WritingSystemFactory.GetStrFromWs(writingSystemId), text });
        }
        return result;
    }

    private static string RichDigest(ITsString value)
    {
        var runs = Enumerable.Range(0, value.RunCount).Select(index =>
        {
            var properties = value.get_Properties(index);
            var integerProperties = Enumerable.Range(0, properties.IntPropCount).Select(propertyIndex =>
            {
                properties.GetIntProp(propertyIndex, out var type, out var number);
                return new { type, number };
            }).OrderBy(item => item.type).ToArray();
            var stringProperties = Enumerable.Range(0, properties.StrPropCount).Select(propertyIndex =>
            {
                var text = properties.GetStrProp(propertyIndex, out var type);
                return new { type, text };
            }).OrderBy(item => item.type).ToArray();
            return new { text = value.get_RunText(index), integerProperties, stringProperties };
        }).ToArray();
        var json = JsonSerializer.Serialize(new
        {
            normalized = value.get_NormalizedForm(FwNormalizationMode.knmNFSC)?.Text ?? value.Text,
            runs,
        }, JsonOptions);
        return Digest(CanonicalJson.Canonicalize(json));
    }

    private static string CaseId(Guid wordform, string writingSystem, string surface) =>
        CanonicalId.FromGuid(StableGuid(wordform.ToString("D") + "\0" + writingSystem + "\0" + surface)).Value;

    private static Guid StableGuid(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        hash[6] = (byte)((hash[6] & 0x0f) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        var hex = Convert.ToHexStringLower(hash.AsSpan(0, 16));
        return Guid.ParseExact($"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}", "D");
    }

    private static string GuidText(Guid value) => value.ToString("D").ToLowerInvariant();

    private static string Digest(string value) => "sha256:" + Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
