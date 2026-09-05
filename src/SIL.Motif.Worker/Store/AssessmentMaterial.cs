using SIL.Motif.Contract.Ids;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;

namespace SIL.Motif.Worker.Store;

/// <summary>
/// Turns one produced Assessment's raw material into a recordable row: the one interpretation of
/// <see cref="AssessmentRaw"/> and <see cref="ProducedAssessment"/> every recorder shares, so a Trial's
/// job handler and a Baseline's <c>assess</c> command can never read <c>ProducedAssessment</c> two
/// different ways.
/// </summary>
public static class AssessmentMaterial
{
    /// <summary>Reduces one kind's raw shape to word rows, or a cache path and digest.</summary>
    public static (IReadOnlyList<AssessedWord> Words, string? CachePath, string? CacheDigest) From(AssessmentRaw raw) => raw switch
    {
        AssessmentRaw.WordMeasurements measurements => (measurements.Words, null, null),
        AssessmentRaw.Batch batch => (batch.Analysis.Words
            .Select(word => new AssessedWord(
                word.Word, word.Outcome.ToStoredOutcome(), Array.Empty<ParsedAnalysis>(), word.ElapsedMs))
            .ToArray(), null, null),
        AssessmentRaw.FileCache fileCache => (Array.Empty<AssessedWord>(), fileCache.Path, fileCache.Digest),
        _ => throw new ArgumentOutOfRangeException(nameof(raw)),
    };

    /// <summary>Builds one recordable Assessment from what an Assessor produced, sharing every recorder's fields.</summary>
    public static NewAssessmentRecord ToRecord(
        ProducedAssessment produced, string assessmentId, CanonicalId? proposalId, string? proposalIntentDigest,
        string assessor, string scopeJson, string scopeDigest, string tokeniserName, string tokeniserVersion,
        string baselineToken, Selection selection)
    {
        var (words, cachePath, cacheDigest) = From(produced.Raw);
        return new NewAssessmentRecord(
            AssessmentId: assessmentId,
            ProposalId: proposalId,
            ProposalIntentDigest: proposalIntentDigest,
            Assessor: assessor,
            Kind: produced.Kind.ToStoredKind(),
            ScopeJson: scopeJson,
            ScopeDigest: scopeDigest,
            TokeniserName: tokeniserName,
            TokeniserVersion: tokeniserVersion,
            BaselineToken: baselineToken,
            Selection: selection,
            OutcomeDigest: produced.OutcomeDigest,
            SemanticDigest: produced.SemanticDigest,
            GrammarSourceSha256: produced.GrammarSourceSha256,
            ModelFingerprint: produced.ModelFingerprint,
            Pipeline: produced.Pipeline,
            DiagnosticCount: produced.DiagnosticCount,
            Words: words,
            CachePath: cachePath,
            CacheDigest: cacheDigest);
    }

    /// <summary>Hashes a recorded scope's wire JSON the same way for every recorder.</summary>
    public static string Digest(string json)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json));
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
