using System.Text.Json;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Worker.Corpus;

internal sealed class GrammarCoverageAssessment
{
    private readonly Dictionary<string, AssessedWord> _checks = new(StringComparer.Ordinal);
    private readonly bool _unsupportedGrammar;
    private readonly CheckLimitation _retryLimitation;
    private readonly HashSet<string> _retriedWords = new(StringComparer.Ordinal);

    public AssessmentInputBinding InputBinding { get; }

    public GrammarCoverageAssessment(AssessmentRecord assessment, GrammarCoverageBasis basis,
        GrammarCoverageInventory inventory, BaselineRecord registeredBaseline,
        Func<BaselineRecord, string, LowercasePass> readCasing, CancellationToken cancellationToken)
    {
        if (assessment.Subject is not AssessmentSubject.Words { TokeniserName: "none", TokeniserVersion: "1" } subject ||
            assessment.Assessor != PanGlossAssessor.AssessorName || assessment.Kind is not ("ParseTime" or "Correctness") ||
            assessment.ProposalId is not null || assessment.Invocation is not { } invocation || assessment.Words is not { } words ||
            basis.Baseline.ProjectIdentity != basis.ProjectId || ReadBaseline(assessment.BaselineToken) != basis.Baseline ||
            assessment.GrammarSourceSha256 != basis.GrammarId || invocation.SourceBytesSha256 != basis.GrammarId ||
            ParserId(invocation) != basis.ParserId || LimitsId(invocation) != basis.LimitsId ||
            invocation.ImportedSnapshotSha256 != basis.ImportedSnapshotDigest)
            throw new InvalidDataException("The saved Assessment does not match the requested grammar coverage basis.");
        var scope = ScopeCodec.ReadTrial(assessment.ScopeJson, "grammar coverage");
        if (assessment.ScopeDigest != AssessmentMaterial.Digest(assessment.ScopeJson) ||
            !subject.Selection.HashMatchesStoredWords() || !scope.Words.SequenceEqual(subject.Selection.Words) ||
            scope.PerWordLimit?.TotalMilliseconds != invocation.PerWordTimeoutMs ||
            scope.PerWordParserWorkLimit != invocation.PerWordParserWorkLimit)
            throw new InvalidDataException("The saved Assessment scope and invocation disagree.");

        var profile = invocation.InputProfile ?? throw new GrammarCoverageReadException(GrammarCoverageReadFailure.Unavailable,
            "The saved Assessment has no producer-recorded writing system, LDML digest and casing policy.");
        var baselinePath = registeredBaseline.FwDataPath;
        var grammarHash = BatchInvocationEvidence.DigestFile(baselinePath);
        var sidecars = ProgressSourceIdentity.CaptureSidecars(baselinePath);
        if (grammarHash != basis.GrammarId || sidecars?.Digest != profile.LdmlDigest ||
            profile.WritingSystemId != inventory.Provenance.WritingSystemId || profile.LdmlDigest != inventory.Provenance.ProfileHash)
            throw new InvalidDataException("The Assessment grammar or writing-system profile disagrees with the Wordform mapping.");

        var casing = readCasing(registeredBaseline, profile.WritingSystemId);
        if (!sidecars.Current(baselinePath))
            throw new InvalidDataException("The registered writing-system profile changed during verification.");
        var (parsedWords, origins) = GrammarCoverageParserInputs.Expand(subject.Selection.Words, casing, profile.CasePolicy);
        if (invocation.WordsSha256 != GrammarCoverageParserInputs.WordsDigest(parsedWords, "\n") &&
            invocation.WordsSha256 != GrammarCoverageParserInputs.WordsDigest(parsedWords, "\r\n"))
            throw new InvalidDataException("The saved parser input digest does not match the Selection and its casing policy.");
        ValidateMapping(inventory, casing, profile.CasePolicy, cancellationToken);

        foreach (var word in words)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = GrammarCoverageInventory.Normalize(word.Word);
            var origin = word.LowercaseOf is { } source ? GrammarCoverageInventory.Normalize(source) : null;
            if (!origins.TryGetValue(key, out var expectedOrigin) || expectedOrigin != origin || !_checks.TryAdd(key, word))
                throw new InvalidDataException("The saved Assessment has an incompatible or duplicate parser key.");
        }
        _unsupportedGrammar = invocation.FirstPassGrammarWarningLines.Any(BatchInvocationEvidence.IsUnsupportedGrammarWarning);
        if (invocation.StoppedWordRetry is { Failure: null } retry)
        {
            _retryLimitation = retry.RetryStandardErrorSha256 is null ? CheckLimitation.RetryDiagnostics :
                retry.GrammarWarningLines.Any(BatchInvocationEvidence.IsUnsupportedGrammarWarning)
                    ? CheckLimitation.UnsupportedGrammar : 0;
            if (_retryLimitation != 0)
                _retriedWords.UnionWith(retry.ReplacedWords().Select(GrammarCoverageInventory.Normalize));
        }
        InputBinding = new(subject.Selection.Sha256, invocation.WordsSha256, profile);
    }

    private static void ValidateMapping(GrammarCoverageInventory inventory, LowercasePass casing, string producingPolicy,
        CancellationToken cancellationToken)
    {
        var policy = inventory.Provenance.CaseFallback switch
        {
            "exact" => GrammarCoverageParserInputs.Exact,
            "fieldworks-lowercase" => BatchInputProfile.WholeWordLowercase,
            "first-letter-lowercase" => GrammarCoverageParserInputs.FirstLetterLowercase,
            "explicit" => producingPolicy,
            _ => throw new InvalidDataException("The Wordform fallback policy is unavailable."),
        };
        foreach (var row in inventory.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (inventory.Provenance.Fold && row.WritingSystemId == inventory.Provenance.WritingSystemId &&
                (row.CasedForm is not { } cased ||
                    GrammarCoverageInventory.Normalize(casing.LowercaseOf(cased) ?? cased) != row.ParserKey))
                throw new InvalidDataException("A folded parser key must be the pinned lowercase form of its own Wordform.");
            var expected = row.WritingSystemId == inventory.Provenance.WritingSystemId
                ? GrammarCoverageParserInputs.LowercaseOf(row.ParserKey, casing, policy) : null;
            if (expected is not null) expected = GrammarCoverageInventory.Normalize(expected);
            if (expected == row.ParserKey) expected = null;
            if ((inventory.Provenance.CaseFallback != "explicit" || row.FallbackParserKey is not null) &&
                expected != row.FallbackParserKey)
                throw new InvalidDataException("The Wordform mapping does not match its pinned fallback policy.");
        }
    }

    public static string ParserId(BatchInvocationEvidence invocation) =>
        !string.IsNullOrWhiteSpace(invocation.ExecutableVersion) && !string.IsNullOrWhiteSpace(invocation.ExecutableBytesSha256)
            ? invocation.ExecutableVersion + "/" + invocation.ExecutableBytesSha256
            : throw new InvalidDataException("The saved Assessment has no pinned parser identity.");

    public static string LimitsId(BatchInvocationEvidence invocation) =>
        LimitsId(invocation.PerWordTimeoutMs, invocation.PerWordParserWorkLimit, invocation.StoppedWordRetry?.LimitMs);

    public static string LimitsId(int? timeoutMs, ParserWorkLimit workLimit, int? retryLimitMs) =>
        GrammarCoverageInventory.Digest(new { PerWordTimeoutMs = timeoutMs, PerWordParserWorkLimit = workLimit, RetryLimitMs = retryLimitMs });

    internal static BaselineToken ReadBaseline(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<BaselineToken>(json, MotifJson.CreateOptions())
                ?? throw new InvalidDataException("The saved Assessment has no Baseline.");
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            throw new InvalidDataException("The grammar coverage basis has an invalid Baseline.", exception);
        }
    }

    public WordEvidence Measure(FrequencyRow row, string writingSystem)
    {
        if (row.WritingSystemId != writingSystem) return new(null, null, null, CheckLimitation.OtherWritingSystem);

        var exact = Resolve(row.ParserKey);
        if (exact.Analysed == true || row.FallbackParserKey is null) return exact;
        var fallback = Resolve(row.FallbackParserKey);
        var limitations = exact.Limitations | fallback.Limitations;
        if (fallback.Analysed == true)
            return new(true, fallback.Ambiguous == true || exact.Analysed is not null ? fallback.Ambiguous : null,
                exact.InvalidShape, limitations);

        var miss = exact.Analysed == false && fallback.Analysed == false ? (bool?)false : null;
        return new(miss, miss, exact.InvalidShape, limitations);
    }

    private WordEvidence Resolve(string key)
    {
        if (!_checks.TryGetValue(key, out var word))
            return new(null, null, null, CheckLimitation.Missing);

        if (!word.Outcome.TryParseStoredOutcome(out var outcome))
            return new(null, null, null, CheckLimitation.Incomplete);
        var morphology = word.Morphology;
        var stopped = outcome == WordOutcome.TimedOut || morphology?.TimedOut == true;
        var capped = outcome is WordOutcome.Capped or WordOutcome.MemoryCapped || morphology?.Capped == true;
        var nativePaths = morphology?.Analyses.Where(IsNative).ToArray();
        var foundPath = nativePaths is null ? word.Analyses.Count > 0 : nativePaths.Length > 0;
        var success = outcome != WordOutcome.Skipped && morphology?.InvalidShape != true && foundPath;
        bool? invalid = morphology?.InvalidShape ?? (success ? false : null);
        var unavailable = morphology is null || morphology.Unavailable.Count > 0 || morphology.Refusal is not null;
        var grammarLimitation = (_unsupportedGrammar ? CheckLimitation.UnsupportedGrammar : 0) |
            (_retriedWords.Contains(key) ? _retryLimitation : 0);
        var finished = !word.IsIncomplete && !stopped && !capped && invalid == false && !unavailable && grammarLimitation == 0;
        var miss = finished && outcome == WordOutcome.NoAnalysis && !success && morphology!.Analyses.Count == 0;
        var complete = miss || finished && success && outcome == WordOutcome.Analysed;
        var limitations = (stopped ? CheckLimitation.Stopped : 0) | (capped ? CheckLimitation.Capped : 0) |
            (invalid == true ? CheckLimitation.InvalidShape : 0) |
            (unavailable ? CheckLimitation.Unavailable : 0) |
            grammarLimitation;
        if (!complete && limitations == 0) limitations = CheckLimitation.Incomplete;
        if (!success) return new(miss ? false : null, miss ? false : null, invalid, limitations);

        var ambiguous = Ambiguity(nativePaths, complete);
        if (ambiguous is null) limitations |= CheckLimitation.ReadingIdentity;
        return new(true, ambiguous, invalid, limitations);
    }

    private static bool? Ambiguity(ParseAnalysis[]? nativePaths, bool complete)
    {
        if (nativePaths is null) return null;

        var paths = nativePaths.Where(HasMappedMorphs).Select(analysis => GrammarCoverageInventory.Digest(
            analysis.Morphs.Select(morph => new
            {
                Form = Guid.Parse(morph.Form!), Msa = Guid.Parse(morph.Msa!),
                InflType = morph.InflType is { } type ? (Guid?)Guid.Parse(type) : null,
            }))).Distinct(StringComparer.Ordinal).Take(2).Count();
        if (paths >= 2) return true;
        // Equal morph-bundle paths can still carry distinct features outside this evidence shape.
        return complete && paths == 1 && nativePaths.Length == 1 ? false : null;
    }

    private static bool IsNative(ParseAnalysis analysis) =>
        analysis.Morphs.Count != 0 && analysis.Morphs.All(morph => morph.GuessedString is null);

    private static bool HasMappedMorphs(ParseAnalysis analysis) => analysis.Morphs.All(morph =>
        Guid.TryParse(morph.Form, out _) && Guid.TryParse(morph.Msa, out _) &&
        (morph.InflType is null || Guid.TryParse(morph.InflType, out _)));
}

[Flags]
internal enum CheckLimitation
{
    Missing = 1, Stopped = 2, Capped = 4, Incomplete = 8, ReadingIdentity = 16, OtherWritingSystem = 32,
    InvalidShape = 64, Unavailable = 128, UnsupportedGrammar = 256, RetryDiagnostics = 512,
}

internal readonly record struct WordEvidence(bool? Analysed, bool? Ambiguous, bool? InvalidShape, CheckLimitation Limitations);
