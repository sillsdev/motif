using System.Security.Cryptography;
using System.Text;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Runtime.Review;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Worker.Jobs;

/// <summary>Freezes the whole Trial scope and the observations made before any detail retention.</summary>
public sealed class TrialExportCapture
{
    private readonly ReviewInputArtifact _input;
    private readonly string _revision;
    private readonly string _intentDigest;
    private readonly SideEvidence _before = new();
    private readonly SideEvidence _after = new();
    private string? _candidatePath;
    private bool _published;

    internal TrialExportCapture(MotifDatabase database, ReviewInputArtifact input, TrialJobInput job)
    {
        _input = input;
        _intentDigest = IntentDigest.Compute(ProposalJsonParser.Parse(job.ProposalJson));
        _revision = job.ExportRevision ?? job.ReviewRevision ?? _intentDigest;
        foreach (var batch in job.ReviewBeforeDispatches ?? [])
            _before.Dispatched(batch.WritingSystemId, JobWordLists.Read(database, batch.WordList!), batch.ExecutableSha256);
    }

    internal static TrialExportCapture Unpartitioned(MotifDatabase database, TrialJobInput job, string workspace, string proposalId,
        BaselineToken baseline, string baselinePath, IReadOnlyList<string> words)
    {
        var cases = words.Select(word =>
        {
            var key = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(word)));
            return new ReviewPlannedCase(key, key, word, null, null, [], []);
        }).ToArray();
        var input = new FrozenReviewInputs(workspace, proposalId, "", "", null, [], [], cases);
        return new TrialExportCapture(database, new(input, new Dictionary<string, byte[]>())
            { Baseline = baseline, BaselineFile = baselinePath }, job);
    }

    internal void BindCandidate(string path) => _candidatePath = path;

    internal void Before(string? writingSystem, IEnumerable<AssessedWord> words, IEnumerable<BatchInvocationEvidence?> invocations)
    {
        var evidence = invocations.ToArray();
        if (!_before.HasExactDispatch(writingSystem))
            foreach (var invocation in evidence.OfType<BatchInvocationEvidence>().DistinctBy(invocation => invocation.InvocationId))
                if (BatchInvocationEvidence.ReadVerifiedWords(invocation) is { } batchWords)
                    _before.Dispatched(writingSystem, batchWords, invocation.ExecutableBytesSha256);
        _before.Add(writingSystem, words, evidence);
    }

    internal void After(string? writingSystem, IEnumerable<AssessedWord> words, IEnumerable<BatchInvocationEvidence?> invocations) =>
        _after.Add(writingSystem, words, invocations);

    internal void PlanAfter(string? writingSystem, IReadOnlyList<string> words, LowercasePass? casing)
    {
        if (casing is null) return;
        foreach (var word in words)
            if (casing.LowercaseOf(word) is { } form) _after.AddExpansion(writingSystem, word, form);
    }

    internal void AfterDispatched(string? writingSystem, IEnumerable<string> forms, string? executableSha256) =>
        _after.Dispatched(writingSystem, forms, executableSha256);

    internal void BeforeInterrupted(string code, string? detail = null) => _before.Interrupt(code, detail);
    internal void AfterInterrupted(string code, string? detail = null) => _after.Interrupt(code, detail);

    internal void BeforePartial(IEnumerable<TrialReviewObservation> observations)
    {
        var planned = _input.Inputs.Cases.ToDictionary(item => item.CaseKey, StringComparer.Ordinal);
        foreach (var row in observations)
        {
            if (!planned.TryGetValue(row.CaseKey, out var item)) continue;
            Before(item.WritingSystemId, row.Morphology is null ? [] : [PartialWord(row.Morphology) with { Outcome = row.Outcome }], []);
            Before(item.WritingSystemId, row.ExpandedForms?.Select(form => PartialWord(form) with { LowercaseOf = item.Word }) ?? [], []);
        }
    }

    /// <summary>Publishes observations for a Trial interrupted before the worker could measure the candidate.</summary>
    public static void PublishInterrupted(MotifDatabase database, JobRecord job, Side side, string code, string message)
    {
        var input = TrialJobInput.Parse(job.InputJson);
        if (input.ReviewInputs is not { } reference) return;
        var capture = new TrialExportCapture(database, ReviewInputArtifacts.Read(database, reference), input);
        if (input.ReviewBeforePartial is { } partial)
            capture.BeforePartial(TrialReviewEvidence.ReadBefore(database, partial));
        var repository = new AssessmentRepository(database);
        foreach (var partition in input.ReviewBaselines ?? [])
        {
            var records = partition.AssessmentIds.Select(id => repository.Get(id)).ToArray();
            capture.Before(partition.WritingSystemId, records.SelectMany(item => item.Words ?? []),
                records.Select(item => item.Invocation));
        }
        if (side == Side.Before) capture.BeforeInterrupted(code, message);
        else capture.AfterInterrupted(code, message);
        var ledger = TrialReviewEvidence.ReadJob(database, job.JobId)!;
        TrialReviewEvidence.Save(database, capture.FreezeLedgerReadings(ledger, null));
        capture.Publish(database, job.JobId);
    }

    internal TrialReviewLedger FreezeLedgerReadings(TrialReviewLedger ledger, string? baselinePath)
    {
        FreezeReadings(baselinePath);
        var writingSystems = _input.Inputs.Cases.ToDictionary(item => item.CaseKey, item => item.WritingSystemId, StringComparer.Ordinal);
        TrialReviewObservation Enrich(TrialReviewObservation row, bool before)
        {
            var writingSystem = writingSystems[row.CaseKey];
            return row with
            {
                Morphology = Morphology(before, writingSystem, row.Word) ?? row.Morphology,
                ExpandedForms = row.ExpandedForms?.Select(form => Morphology(before, writingSystem, form.Word) ?? form).ToArray(),
            };
        }
        return ledger with
        {
            Before = ledger.Before.Select(row => Enrich(row, true)).ToArray(),
            After = ledger.After.Select(row => Enrich(row, false)).ToArray(),
        };
    }

    private void FreezeReadings(string? baselinePath)
    {
        _before.FreezeReadings(_input.BaselineFile ?? baselinePath);
        _after.FreezeReadings(_candidatePath);
    }

    internal ParseWordEvidence? Morphology(bool before, string? writingSystem, string word) =>
        (before ? _before : _after).Read(writingSystem, word)?.Morphology;

    internal void Publish(MotifDatabase database, string jobId, string? token = null)
    {
        if (_published || _input.Baseline is not { } baseline) return;
        FreezeReadings(null);
        var cases = _input.Inputs.Cases;
        var expanded = 0;
        var beforeDispatched = 0;
        var beforeFinished = 0;
        var afterDispatched = 0;
        var afterFinished = 0;
        foreach (var item in cases)
        {
            foreach (var form in Forms(item))
            {
                expanded++;
                if (_before.WasDispatched(item.WritingSystemId, form)) beforeDispatched++;
                if (_after.WasDispatched(item.WritingSystemId, form)) afterDispatched++;
                if (Complete(_before.Read(item.WritingSystemId, form))) beforeFinished++;
                if (Complete(_after.Read(item.WritingSystemId, form))) afterFinished++;
            }
        }
        var provenance = new TrialExportProvenance(MotifProductVersion.BuildSourceSha, _input.Inputs.ProposalId,
            _revision, new(IntentDigest.Algorithm, _intentDigest), baseline, _input.BaselineFileDigest,
            CanonicalSidecarDigest(_input.BaselineSidecarDigest), new(_before.Version, _after.Version),
            new(_before.ExecutableSha256, _after.ExecutableSha256),
            _input.Inputs.DeclaredGate);
        var interruption = _before.Causes.Select(cause => new TrialExportInterruption("before", cause))
            .Concat(_after.Causes.Select(cause => new TrialExportInterruption("after", cause))).ToArray();
        TrialExportArtifact.Publish(database, jobId,
            new(cases.Count, expanded, afterFinished, new(beforeDispatched, beforeFinished), new(afterDispatched, afterFinished)),
            provenance, interruption, cases.Select(Project), token);
        _published = true;
    }

    private static string? CanonicalSidecarDigest(string? digest)
    {
        if (digest is null || Sha256Value.IsCanonical(digest)) return digest;
        var prefixed = "sha256:" + digest.ToLowerInvariant();
        return Sha256Value.IsCanonical(prefixed) ? prefixed : null;
    }

    private IEnumerable<string> Forms(ReviewPlannedCase item)
    {
        var planned = _input.BeforeExpansionForms?.GetValueOrDefault(item.CaseKey);
        var matchingBefore = planned is not null && _before.WasDispatched(item.WritingSystemId, planned)
            ? [planned] : Array.Empty<string>();
        var observedBefore = _before.Expansions(item).Where(form => _before.WasDispatched(item.WritingSystemId, form));
        return new[] { item.Word }.Concat(matchingBefore).Concat(observedBefore).Concat(_after.Expansions(item))
            .Distinct(StringComparer.Ordinal);
    }

    private TrialExportWord Project(ReviewPlannedCase item)
    {
        var forms = Forms(item).Select(form => Compare(item, form)).ToArray();
        var summary = forms.MaxBy(form => form.Status)!;
        return new(item.CaseKey, item.WritingSystemId,
            item.ExplicitInput?.SubmittedForm ?? _input.SubmittedForms?.GetValueOrDefault(item.CaseKey) ?? item.Word,
            item.Memberships.Select(member => new TrialExportMembership(member.SetId, member.CaseId, member.Role)).ToArray(),
            forms, summary.Status, summary.ReasonCode, summary.ParserCauses) { ExplicitInput = item.ExplicitInput };
    }

    private TrialExportForm Compare(ReviewPlannedCase item, string form)
    {
        var before = ProjectSide(_before, item.WritingSystemId, form);
        var after = ProjectSide(_after, item.WritingSystemId, form);
        var lost = before.Complete && after.Complete ? Lost(before.Readings, after.Readings) : [];
        var (status, reason, causes) = !after.Complete ? (after.Status, after.ReasonCode, after.ParserCauses) :
            !before.Complete ? (TrialExportStatus.Partial, "incomplete-comparison", before.ParserCauses) :
            lost.Length > 0 ? (TrialExportStatus.Lost, "reading-lost", after.ParserCauses) :
            (after.Status, after.ReasonCode, after.ParserCauses);
        var formKey = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(item.WritingSystemId + "\0" + form)));
        return new(form, formKey, form == item.Word ? "submitted" : "lowercase", status, reason, causes, before, after, lost);
    }

    private static string[] Lost(IReadOnlyList<TrialExportReading> before, IReadOnlyList<TrialExportReading> after)
    {
        var counts = after.CountBy(item => item.Id, StringComparer.Ordinal).ToDictionary(StringComparer.Ordinal);
        var lost = new List<string>();
        foreach (var item in before)
        {
            var count = counts.GetValueOrDefault(item.Id);
            if (count == 0) lost.Add(item.Id);
            else counts[item.Id] = count - 1;
        }
        return lost.ToArray();
    }

    private static bool Complete(AssessedWord? row)
    {
        if (row?.Morphology is not { } morphology) return false;
        if (ReviewCaseClassifier.InventoryRejected(morphology, row.Word)) return true;
        return ReviewCaseClassifier.Observe(morphology, row.Word).Complete &&
            row.Outcome is not ("timed-out" or "memory-capped" or "capped" or "killed" or "skipped" or "stopped");
    }

    private static TrialExportSide ProjectSide(SideEvidence side, string? writingSystem, string form)
    {
        var row = side.Read(writingSystem, form);
        if (row is null)
            return new(null, side.WasDispatched(writingSystem, form), false, TrialExportStatus.Skipped, "not-measured", [], []);

        var morphology = row.Morphology;
        var readings = morphology?.Analyses?.Where(HasMorphemes).Select(ProjectReading).ToArray() ?? [];
        if (ReviewCaseClassifier.InventoryRejected(morphology, row.Word))
            return new(row.Outcome, true, true, TrialExportStatus.Skipped, "inventory-rejected",
                [new("inventory-rejected", ReviewCaseClassifier.InvalidDetails(morphology))], []);
        if (Complete(row))
            return readings.Length > 0
                ? new(row.Outcome, true, true, TrialExportStatus.Accepted, "parsed", [], readings)
                : new(row.Outcome, true, true, TrialExportStatus.Skipped, "no-parse", [], []);

        var (status, reason) = row.Outcome == "killed" ? (TrialExportStatus.Killed, "parser-killed") :
            row.Outcome == "timed-out" || morphology?.TimedOut == true ? (TrialExportStatus.Timeout, "time-limit") :
            row.Outcome == "memory-capped" ? (TrialExportStatus.Capped, "memory-limit") :
            row.Outcome == "capped" || morphology?.Capped == true ? (TrialExportStatus.Capped, "work-limit") :
            morphology is { InvalidShape: true } || morphology?.Unavailable?.Count > 0 ?
                (TrialExportStatus.InvalidEvidence, "invalid-evidence") :
            morphology?.Refusal is not null || row.Outcome == "stopped" ?
                (TrialExportStatus.Stopped, "parser-stopped") :
            row.Outcome == "skipped" ? (TrialExportStatus.Skipped, "not-measured") :
            (TrialExportStatus.InvalidEvidence, "invalid-evidence");
        var causes = new List<TrialExportCause> { new(reason, ReviewCaseClassifier.InvalidDetails(morphology)) };
        if (morphology?.Refusal is { } refusal && refusal.Code != reason)
            causes.Add(new(refusal.Code, [refusal.Reason]));
        return new(row.Outcome, true, false, status, reason, causes, readings);
    }

    private static TrialExportReading ProjectReading(ParseAnalysis reading)
    {
        var signature = reading.Signature ?? new ReadingSignatureReader(null).Read(reading);
        var morphs = MorphemeAcceptance(reading);
        return new(signature.Id, signature, Acceptance(morphs), morphs);
    }

    internal static IReadOnlyList<ReadingAcceptance> MorphemeAcceptance(ParseAnalysis reading) =>
        reading.MorphemeAcceptance ?? reading.Morphs.Select(morph => morph.GuessedString is not null
            ? ReadingAcceptance.Guessed : ReadingAcceptance.Unresolved).ToArray();

    /// <summary>The Reading-level verdict: guessed if any morpheme was guessed, lexicon-backed only when all resolve.</summary>
    internal static ReadingAcceptance Acceptance(IReadOnlyList<ReadingAcceptance> morphs) =>
        morphs.Contains(ReadingAcceptance.Guessed) ? ReadingAcceptance.Guessed :
        morphs.Count == 0 || morphs.Contains(ReadingAcceptance.Unresolved) ? ReadingAcceptance.Unresolved :
        ReadingAcceptance.LexiconBacked;

    private static bool HasMorphemes(ParseAnalysis? reading) =>
        reading?.Morphs is { Count: > 0 } && reading.Morphs.All(morph => morph is not null);

    internal static AssessedWord PartialWord(ParseWordEvidence row) =>
        new(row.Word, row.InvalidShape ? "skipped" : row.TimedOut ? "timed-out" : row.Capped ? "stopped" :
            row.Analyses?.Count > 0 || row.Unavailable?.Count > 0 ? "analysed" : "no-analysis", []) { Morphology = row };

    private sealed class SideEvidence
    {
        private readonly Dictionary<(string? WritingSystem, string Word), AssessedWord> _observations = new();
        private readonly Dictionary<(string? WritingSystem, string Word), HashSet<string>> _expansions = new();
        private readonly HashSet<(string? WritingSystem, string Form)> _dispatched = new();
        private readonly HashSet<string?> _partitionsWithDispatch = new(StringComparer.Ordinal);
        private readonly HashSet<string> _versions = new(StringComparer.Ordinal);
        private readonly HashSet<string> _executableDigests = new(StringComparer.Ordinal);
        private bool _unidentifiedInvocation;
        private readonly List<TrialExportCause> _causes = [];

        internal string? Version => _versions.Count == 1 ? _versions.Single() : null;
        internal string? ExecutableSha256 => !_unidentifiedInvocation && _executableDigests.Count == 1 ? _executableDigests.Single() : null;
        internal IReadOnlyList<TrialExportCause> Causes => _causes;

        internal void Interrupt(string code, string? detail)
        {
            var details = detail is null ? Array.Empty<string>() : [detail];
            if (!_causes.Any(cause => cause.Code == code && cause.Details.SequenceEqual(details)))
                _causes.Add(new(code, details));
        }

        internal void Dispatched(string? writingSystem, IEnumerable<string> forms, string? executableSha256)
        {
            _partitionsWithDispatch.Add(writingSystem);
            if (executableSha256 is not null && Sha256Value.IsCanonical(executableSha256))
                _executableDigests.Add(executableSha256);
            else _unidentifiedInvocation = true;
            foreach (var form in forms) _dispatched.Add((writingSystem, form));
        }

        internal bool HasExactDispatch(string? writingSystem) => _partitionsWithDispatch.Contains(writingSystem);

        internal bool WasDispatched(string? writingSystem, string form) => HasExactDispatch(writingSystem)
            ? _dispatched.Contains((writingSystem, form)) : _observations.ContainsKey((writingSystem, form));

        internal AssessedWord? Read(string? writingSystem, string form) =>
            WasDispatched(writingSystem, form) ? _observations.GetValueOrDefault((writingSystem, form)) : null;

        internal IEnumerable<string> Expansions(ReviewPlannedCase item) =>
            _expansions.TryGetValue((item.WritingSystemId, item.Word), out var forms) ? forms.Order(StringComparer.Ordinal) : [];

        internal void Add(string? writingSystem, IEnumerable<AssessedWord> words, IEnumerable<BatchInvocationEvidence?> invocations)
        {
            foreach (var invocation in invocations)
            {
                if (!string.IsNullOrWhiteSpace(invocation?.ExecutableVersion)) _versions.Add(invocation.ExecutableVersion);
                if (Sha256Value.IsCanonical(invocation?.ExecutableBytesSha256)) _executableDigests.Add(invocation!.ExecutableBytesSha256);
                else _unidentifiedInvocation = true;
            }
            foreach (var word in words)
            {
                var key = (writingSystem, word.Word);
                if (word.Morphology is not null || !_observations.ContainsKey(key)) _observations[key] = word;
                if (word.LowercaseOf is { } original) AddExpansion(writingSystem, original, word.Word);
            }
        }

        internal void AddExpansion(string? writingSystem, string word, string form)
        {
            var key = (writingSystem, word);
            if (!_expansions.TryGetValue(key, out var forms)) _expansions[key] = forms = new(StringComparer.Ordinal);
            forms.Add(form);
        }

        internal void FreezeReadings(string? path)
        {
            var pending = _observations.Where(pair => pair.Value.Morphology?.Analyses?.Any(NeedsCapture) == true).ToArray();
            if (pending.Length == 0) return;
            using var project = path is not null && File.Exists(path) ? BaselineReadCache.Open(path) : null;
            var reader = new ReadingSignatureReader(project?.Cache);
            foreach (var (key, word) in pending)
            {
                var morphology = word.Morphology!;
                _observations[key] = word with
                {
                    Morphology = morphology with
                    {
                        Analyses = morphology.Analyses.Select(item => NeedsCapture(item) ? reader.CaptureVerified(item) : item).ToArray(),
                    },
                };
            }
        }

        private static bool NeedsCapture(ParseAnalysis analysis) => HasMorphemes(analysis) &&
            analysis.MorphemeAcceptance is null;
    }
}
