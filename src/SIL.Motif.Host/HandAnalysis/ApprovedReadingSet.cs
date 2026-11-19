using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using SIL.LCModel;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Host.HandAnalysis;

/// <summary>One human-approved analysis: its native ids, bundle facts and Reading identity when the bundles verify.</summary>
/// <param name="Signature">Null when the analysis has no morph bundles or a bundle does not verify; never guessed.</param>
public sealed record ApprovedAnalysisFact(Guid AnalysisId, Guid WordformId, Guid EvaluationId, Guid AgentId,
    ImmutableArray<BundleFact> Bundles, ReadingSignature? Signature, string? UnsignedCause);

/// <summary>Why an approved analysis carries no Reading identity.</summary>
public static class ApprovedUnsignedCause
{
    public const string NoMorphBundles = "no-morph-bundles";
    public const string UnverifiedBundle = "unverified-bundle";
}

/// <summary>
/// The human-approved analyses of one project state, matched on the sense-free Reading identity.
/// </summary>
/// <remarks>
/// Approval is FieldWorks' own: <c>IWfiWordform.HumanApprovedAnalyses</c>, the analyses whose evaluation by the
/// default user agent approves (liblcm <c>WfiWordform.AnalysesWithHumanEvaluation</c> and
/// <c>WfiAnalysis.GetAgentOpinion</c>). <c>AnalysisGuessServices.IsHumanApproved</c> accepts any agent flagged human;
/// an analysis approved only that way is not counted as approved and is reported in
/// <see cref="SkippedOtherHumanAgentApprovals"/>. A parser reading cannot choose a sense, so approvals are found by
/// <see cref="ReadingSignature.SenseFree"/> and kept by <see cref="ReadingSignature.ApprovalKept"/>.
/// </remarks>
public sealed class ApprovedReadingSet
{
    private const int SampleIds = 20;
    private readonly ILookup<string, ApprovedAnalysisFact> _bySenseFree;
    private readonly ILookup<Guid, ApprovedAnalysisFact> _byWordform;
    private readonly ILookup<Guid, ApprovedAnalysisFact> _byAnalysis;

    private ApprovedReadingSet(ImmutableArray<ApprovedAnalysisFact> analyses, int wordforms, int skipped,
        ImmutableArray<Guid> skippedSample)
    {
        Analyses = analyses;
        WordformCount = wordforms;
        SkippedOtherHumanAgentApprovals = skipped;
        SkippedSample = skippedSample;
        _byWordform = analyses.ToLookup(fact => fact.WordformId);
        _byAnalysis = analyses.ToLookup(fact => fact.AnalysisId);
        _bySenseFree = analyses.Where(fact => fact.Signature is not null)
            .ToLookup(fact => fact.Signature!.SenseFree().Id, StringComparer.Ordinal);
    }

    /// <summary>Every approved analysis, in analysis-GUID order.</summary>
    public ImmutableArray<ApprovedAnalysisFact> Analyses { get; }

    public int WordformCount { get; }

    /// <summary>Analyses approved by another human agent but not by the default user agent; not counted as approved.</summary>
    public int SkippedOtherHumanAgentApprovals { get; }

    /// <summary>A bounded sample of the skipped analyses' ids.</summary>
    public ImmutableArray<Guid> SkippedSample { get; }

    public int SignedCount => Analyses.Count(fact => fact.Signature is not null);

    /// <summary>The approved analyses of one wordform, in analysis-GUID order.</summary>
    public ImmutableArray<ApprovedAnalysisFact> ForWordform(Guid wordformId) => [.. _byWordform[wordformId]];

    /// <summary>The approved analysis with this id, if it is approved.</summary>
    public ImmutableArray<ApprovedAnalysisFact> ForAnalysis(Guid analysisId) => [.. _byAnalysis[analysisId]];

    /// <summary>The approved analyses a parser reading still yields.</summary>
    public IReadOnlyList<ApprovedAnalysisFact> KeptBy(ReadingSignature parserReading) =>
        [.. _bySenseFree[parserReading.SenseFree().Id].Where(fact => ReadingSignature.ApprovalKept(fact.Signature!, parserReading))];

    /// <summary>Digest of the approved facts, identifying this state's approvals.</summary>
    public string ContentDigest()
    {
        var text = new StringBuilder();
        foreach (var fact in Analyses)
            text.Append(fact.AnalysisId).Append('|').Append(fact.WordformId).Append('|')
                .Append(fact.Signature?.Id ?? "-").Append('\n');
        text.Append("skipped:").Append(SkippedOtherHumanAgentApprovals);
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    /// <summary>Reads the approvals from an open project without changing it.</summary>
    public static ApprovedReadingSet Read(LcmCache cache, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cache);
        var userAgent = cache.LangProject.DefaultUserAgent;
        var facts = ImmutableArray.CreateBuilder<ApprovedAnalysisFact>();
        var skipped = ImmutableArray.CreateBuilder<Guid>();
        var skippedCount = 0;
        var wordforms = 0;
        foreach (var wordform in cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances())
        {
            ct.ThrowIfCancellationRequested();
            wordforms++;
            var approved = wordform.HumanApprovedAnalyses.ToHashSet();
            foreach (var analysis in approved)
            {
                var evaluation = analysis.EvaluationsRC.First(item => item.Owner == userAgent);
                var signature = ReadingSignatureReader.Read(analysis);
                var cause = signature is not null ? null
                    : analysis.MorphBundlesOS.Count == 0 ? ApprovedUnsignedCause.NoMorphBundles
                    : ApprovedUnsignedCause.UnverifiedBundle;
                facts.Add(new(analysis.Guid, wordform.Guid, evaluation.Guid, userAgent.Guid, Bundles(analysis), signature, cause));
            }

            foreach (var analysis in wordform.AnalysesOC.Where(item => !approved.Contains(item) &&
                         item.EvaluationsRC.Any(evaluation => evaluation.Approves && evaluation.Owner is ICmAgent { Human: true } agent &&
                                                              agent != userAgent)))
            {
                skippedCount++;
                if (skipped.Count < SampleIds) skipped.Add(analysis.Guid);
            }
        }

        return new([.. facts.OrderBy(fact => fact.AnalysisId)], wordforms, skippedCount, skipped.ToImmutable());
    }

    private static ImmutableArray<BundleFact> Bundles(IWfiAnalysis analysis) =>
        [.. analysis.MorphBundlesOS.Select(bundle => new BundleFact(bundle.Guid, null, bundle.MorphRA?.Guid,
            bundle.SenseRA?.Guid, bundle.MsaRA?.Guid, bundle.InflTypeRA?.Guid, ImmutableDictionary<string, string>.Empty,
            ImmutableDictionary<string, string>.Empty))];
}
