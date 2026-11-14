using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;
using ReviewWordRow = SIL.Motif.Contract.Review.WordRow;

namespace SIL.Motif.Worker.Runtime.Review;

/// <summary>Identifies the retained Trial evidence from which to publish a report.</summary>
public sealed record ReviewPublishRequest(string JobId);

/// <summary>Produces a frozen report from the exact input and evidence retained by one Trial.</summary>
public sealed class TrialReviewPublisher(MotifDatabase database)
{
    private readonly MotifDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    /// <summary>Classifies the retained observations and atomically publishes their summary and detail.</summary>
    public async Task<ReviewReportId> PublishAsync(ReviewPublishRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var job = new JobRepository(_database).Get(request.JobId) ?? throw new KeyNotFoundException("The Trial was not found.");
        if (!JobStateMachine.IsTerminal(job.Status)) throw new InvalidOperationException("The Trial has not finished.");
        var input = TrialJobInput.Parse(job.InputJson);
        var reference = input.ReviewInputs ?? throw new InvalidOperationException("The Trial did not capture review inputs.");
        var artifact = ReviewInputArtifacts.Read(_database, reference);
        var inputs = artifact.Inputs;
        var baseline = artifact.Baseline ?? throw new InvalidDataException("The Trial did not retain its Baseline identity.");
        var proposal = ProposalJsonParser.Parse(input.ProposalJson);
        if (inputs.ProjectKey != job.ProjectKey || inputs.ProposalId != proposal.ProposalId.Value)
            throw new InvalidDataException("The captured review scope does not match its Trial.");
        var ledger = TrialReviewEvidence.ReadJob(_database, job.JobId) ?? throw new InvalidDataException("The Trial evidence is missing.");
        var before = Index(ledger.Before);
        var after = Index(ledger.After);
        var accumulator = new ReviewSummaryAccumulator(inputs.Sets);
        var parsimony = TrialReviewParsimony.Read(_database, job, input, baseline, IntentDigest.Compute(proposal));
        var hasWarnings = ledger.BeforeWarnings is not null && ledger.AfterWarnings is not null;
        var warningDelta = hasWarnings ? ReviewFindingComparison.Compare(new ReviewFindingInputs(
            ledger.BeforeWarnings!.Select(ReviewDiagnosticIdentity.FromGrammarWarning).ToArray(),
            ledger.AfterWarnings!.Select(ReviewDiagnosticIdentity.FromGrammarWarning).ToArray(), null, null)) : null;
        if (warningDelta is not null || parsimony is not null)
            accumulator.SetFindings(new ReviewFindingDelta(warningDelta?.AddedWarnings ?? [], warningDelta?.RemovedWarnings ?? [],
                parsimony?.Findings.Measures ?? [],
                (warningDelta?.Limitations.Where(code => code != "parsimony-unavailable") ?? ["warnings-unavailable"])
                    .Concat(parsimony?.Findings.Limitations.Where(code => code != "warnings-unavailable") ?? ["parsimony-unavailable"])
                    .Distinct(StringComparer.Ordinal).ToArray()));
        var work = TrialReviewWork.Read(_database, input, job.ResultJson, inputs, baseline, ledger);
        if (work is not null) accumulator.SetWork(work);
        var ruleWorkRows = TrialReviewRuleWork.Read(_database, input, job.ResultJson, inputs, work);
        var wordWorkRows = work is null ? null : WordWorkRows(inputs, before, after, work);
        var overlappingLosses = new HashSet<(string Word, string Reading)>();
        var negativeSets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in Classify(inputs, before, after))
        {
            ct.ThrowIfCancellationRequested();
            accumulator.Add(item);
            if (item.NewNoParse || item.NewApprovedLoss)
                foreach (var loss in item.ReadingLosses) overlappingLosses.Add((item.WordKey, loss.ReadingIdentity));
            if (item.NewlyAcceptedNegative)
            {
                var confirmed = item.Assertions.Where(assertion => assertion.Eligible && assertion.WholeWordNegative)
                    .Select(assertion => assertion.Id).ToHashSet(StringComparer.Ordinal);
                foreach (var membership in item.Memberships.Where(membership => membership.Role == "negative" &&
                    membership.ExpectationIds.Any(confirmed.Contains))) negativeSets.Add(membership.SetId);
            }
        }
        var facts = accumulator.Snapshot();
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(new { input = reference.Sha256, job.JobId,
            job.InputJson, job.ResultJson, ledger, parsimony = parsimony?.ResultDigest, algorithm = "review-v2" }, MotifJson.CreateOptions());
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(manifestBytes));
        var beforeAssessmentIds = (input.BaselineAssessmentIds ?? []).Concat(input.ReviewBaselines?.SelectMany(part => part.AssessmentIds) ?? [])
            .Distinct(StringComparer.Ordinal).ToArray();
        var afterAssessmentIds = AfterAssessments(job.ResultJson).Distinct(StringComparer.Ordinal).ToArray();
        var checkpoints = new[] { Checkpoint(Side.Before, beforeAssessmentIds, input, inputs),
            Checkpoint(Side.After, afterAssessmentIds, input, inputs) };
        var dryRun = ReadDryRun(job.JobId, job.DryRunJson, IntentDigest.Compute(proposal));
        var pins = new ReportPins(inputs.ProjectKey, inputs.ProposalId, null, IntentDigest.Compute(proposal), input.ReviewRevision,
            (input.Prerequisites ?? []).Select(item => new PrerequisitePin(item.ProposalId, item.IntentDigest)).ToArray(),
            job.JobId, baseline, new CandidatePin(job.JobId, ledger.CandidateFileDigest, null), dryRun?.Pin, checkpoints, inputs.Sets.Count,
            Digest(inputs.Sets), Digest(inputs.Cases.Select(item => item.Assertions)), new WarningCheckPins(null, null),
            parsimony?.Pins ?? [], inputs.EvidenceManifestDigest, "review-v2");
        var summary = BuildSummary(facts, inputs, ledger, overlappingLosses.Count, negativeSets.Order(StringComparer.Ordinal).ToArray(), hasWarnings)
            with { Gains = BuildGains(facts, inputs, proposal.Operations) };
        summary = summary with { SectionCounts = summary.SectionCounts.Select(section => section.Section == "parser-work"
            ? new SectionCount("parser-work", wordWorkRows is null ? null : work!.RequiredCases.Count + (ruleWorkRows?.Count ?? 0), "rows",
                wordWorkRows is null ? null : work!.RequiredCases.Count + (ruleWorkRows?.Count ?? 0)) : section).ToArray() };
        if (dryRun is not null) summary = summary with { Effects = new EffectsSummary(dryRun.Value.Count, true) };
        var limitations = facts.Limitations.Select(code => new Limitation(code,
            code == "negative-context-unavailable" ? ["negative-controls"] :
            code == "expectation-identity-unavailable" ? ["wordforms", "controls"] : [], code, [])).ToList();
        foreach (var code in facts.Findings?.Limitations.Where(code => code is not ("warnings-unavailable" or "parsimony-unavailable")) ?? [])
            limitations.Add(new Limitation(code, ["warnings", "parsimony"], code, []));
        if (inputs.OmittedSetIds.Count > 0)
            limitations.Add(new Limitation("sets-omitted", ["evidence"], "Some configured review sets were omitted.", inputs.OmittedSetIds));
        if (facts.ParserWork is not { Comparison: WorkComparison.Comparable })
            limitations.Add(new Limitation("work-unavailable", ["parser-work"], "Comparable parser work is unavailable.", []));
        if (ruleWorkRows is null)
            limitations.Add(new Limitation("rule-work-unavailable", ["parser-work"], "Paired rule detail is unavailable.", []));
        if (!hasWarnings)
            limitations.Add(new Limitation("warnings-unavailable", ["warnings"], "Paired warning evidence is unavailable.", []));
        if (parsimony is null)
            limitations.Add(new Limitation("parsimony-unavailable", ["parsimony"], "Paired Parsimony evidence is unavailable.", []));
        if (dryRun is not null)
            limitations.Add(new Limitation("effects-detail-unavailable", ["effects"], "The Dry Run effect rows are not retained in this report.", []));
        summary = summary with { Incomplete = summary.Incomplete with { EvidenceGapCount = limitations.Count } };
        var evidence = Enum.GetValues<ReviewFlagId>().Select(id => new FlagEvidence(id,
            id == ReviewFlagId.NewWarningsOrParsimony && parsimony is null && !hasWarnings || id == ReviewFlagId.ParserWorkIncrease &&
                facts.ParserWork is not { Comparison: WorkComparison.Comparable } ? EvidenceState.Unavailable :
            id == ReviewFlagId.KnownNonwordsNowAccepted && !inputs.Sets.Any(set => set.Role == "negative") ? EvidenceState.NotConfigured :
            id == ReviewFlagId.NewWarningsOrParsimony && (parsimony is null || !hasWarnings ||
                (facts.Findings?.Limitations.Count ?? 0) > 0) ? EvidenceState.Partial :
            id == ReviewFlagId.KnownNonwordsNowAccepted && facts.Limitations.Contains("negative-context-unavailable") ||
            id is ReviewFlagId.NewWordFailures or ReviewFlagId.ConfirmedReadingsLost &&
                facts.Limitations.Contains("expectation-identity-unavailable") ? EvidenceState.Partial :
            facts.IncompleteCases > 0 && id != ReviewFlagId.ParserWorkIncrease ? EvidenceState.Partial : EvidenceState.Complete,
            id == ReviewFlagId.KnownNonwordsNowAccepted && facts.Limitations.Contains("negative-context-unavailable")
                ? ["negative-context-unavailable"] :
            id is ReviewFlagId.NewWordFailures or ReviewFlagId.ConfirmedReadingsLost &&
                facts.Limitations.Contains("expectation-identity-unavailable") ? ["expectation-identity-unavailable"] : [],
            id switch { ReviewFlagId.NewWordFailures => facts.NewWordFailureWords,
                ReviewFlagId.ConfirmedReadingsLost => facts.UniqueReadingLosses,
                ReviewFlagId.KnownNonwordsNowAccepted => facts.NewlyAcceptedNegativeWords,
                ReviewFlagId.ParserWorkIncrease => facts.Flags.Contains(ReviewFlagId.ParserWorkIncrease) ? 1 : 0,
                ReviewFlagId.NewWarningsOrParsimony => (facts.Findings?.AddedWarnings.Count ?? 0) +
                    (facts.Findings?.Measures.Sum(measure => measure.Limitation is null ? measure.Added.Count : 0) ?? 0), _ => 0 })).ToArray();
        var report = new ReportView("motif.proposal-review-report", 1, "pending", digest, DetailLevel.Summary,
            pins, new Freshness(FreshnessRelation.Unknown, ["freshness-not-checked"]), summary, evidence,
            limitations, DetailAvailability.Available, [], null, null, null, []);
        var estimate = Math.Min(100_000_001L, checked((long)manifestBytes.Length * 12 + (long)inputs.Cases.Count * 20_000 + 100_000));
        var references = (input.BaselineAssessmentIds ?? []).Concat(input.ReviewBaselines?.SelectMany(part => part.AssessmentIds) ?? [])
            .Concat(AfterAssessments(job.ResultJson)).Concat(["sha256:" + reference.Sha256])
            .Concat(input.ReviewBeforePartial is { } partial ? ["review-before:" + partial] : [])
            .Concat(parsimony?.Pins.SelectMany(pair => new[] { pair.BeforeReportId, pair.AfterReportId }).OfType<string>() ?? [])
            .Distinct(StringComparer.Ordinal).ToArray();
        var warningRows = hasWarnings ? WarningRows(ledger.BeforeWarnings!, ledger.AfterWarnings!) : null;
        var sections = new List<string> { "evidence" };
        if (wordWorkRows is not null || ruleWorkRows is not null) sections.Add("parser-work");
        if (warningRows is not null) sections.Add("warnings");
        if (parsimony is not null) sections.Add("parsimony");
        var publication = new ReviewReportPublication(inputs.ProjectKey, inputs.ProposalId, job.JobId, digest, report, references, estimate)
        { AvailableSections = sections,
            SectionItems = Sections(SetRows(inputs).Concat(warningRows ?? []).Concat(parsimony?.Rows ?? [])
                .Concat(wordWorkRows ?? []).Concat(ruleWorkRows ?? []), ct) };
        var id = await new ReviewReportRepository(_database).PublishAsync(publication,
            Cases(inputs, before, after, ct), ct).ConfigureAwait(false);
        TrialScratchRetention.TrySweepReviewInputs(_database);
        return id;
    }

    internal static IEnumerable<ReviewSectionItem> WordWorkRows(FrozenReviewInputs inputs,
        IReadOnlyDictionary<string, TrialReviewObservation> before, IReadOnlyDictionary<string, TrialReviewObservation> after,
        ReviewWorkInputs work)
    {
        var cases = inputs.Cases.ToDictionary(item => item.CaseKey, StringComparer.Ordinal);
        var old = work.Before.ToDictionary(item => item.CaseKey, StringComparer.Ordinal);
        var current = work.After.ToDictionary(item => item.CaseKey, StringComparer.Ordinal);
        WordSide Side(TrialReviewObservation? observation) => new(observation?.Completion ?? Completion.NotMeasured,
            observation?.StopReasons ?? [], observation?.Morphology?.Analyses.Count, null, null, null, null, null, null, null, null);
        var requests = inputs.Cases.ToLookup(item => (item.WritingSystemId, item.Word));
        foreach (var key in work.RequiredCases)
        {
            var item = cases[key];
            var matchingCases = requests[(item.WritingSystemId, item.Word)];
            var memberships = matchingCases.SelectMany(candidate => candidate.Memberships).ToArray();
            before.TryGetValue(key, out var b);
            after.TryGetValue(key, out var a);
            var comparison = ReviewWorkComparison.Compare(work with { RequiredCases = [key],
                Before = old.TryGetValue(key, out var one) ? [one] : [],
                After = current.TryGetValue(key, out var two) ? [two] : [] });
            var row = new ReviewWordRow(key, item.WordKey, item.Word, item.WritingSystemId, Side(b), Side(a), [], [],
                memberships.Select(member => member.SetId).Distinct(StringComparer.Ordinal).ToArray(),
                memberships.Length, matchingCases.Sum(candidate => candidate.Assertions.Count), comparison, Attribution.NotAttributed,
                [new ReadTarget(TargetKind.Tool, "Word detail", null, "motif_difference",
                    new ReadArguments(Proposal: inputs.ProposalId, Detail: DetailLevel.Full, Case: key))]);
            yield return new ReviewSectionItem("parser-work", row,
                Outcome: comparison.PercentChange switch { > 0 => "work-increase", < 0 => "work-decrease",
                    0 => "work-unchanged", _ => "work-unavailable" },
                Changed: comparison.PercentChange is not null and not 0);
        }
    }

    private static (DryRunPin Pin, int Count)? ReadDryRun(string job, string? json, string intent)
    {
        if (json is null) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var anchor = root.GetProperty("anchor");
            var effect = root.GetProperty("effectDigest").GetString();
            var footprint = anchor.GetProperty("footprintDigest").GetString();
            if (root.GetProperty("intentDigest").GetString() != intent || anchor.GetProperty("intentDigest").GetString() != intent ||
                effect is null || effect != anchor.GetProperty("effectDigest").GetString() || footprint is null) return null;
            return (new DryRunPin(job, effect, footprint), root.GetProperty("expectedEffects").GetArrayLength());
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException) { return null; }
    }

    private CheckpointPin Checkpoint(Side side, IReadOnlyList<string> ids, TrialJobInput input, FrozenReviewInputs inputs)
    {
        var records = new List<AssessmentRecord>();
        var repository = new AssessmentRepository(_database);
        foreach (var id in ids)
        {
            try { records.Add(repository.Get(id)); }
            catch (KeyNotFoundException) { }
        }
        string? Common(IEnumerable<string?> values)
        {
            var distinct = values.Distinct(StringComparer.Ordinal).ToArray();
            return records.Count == ids.Count && distinct.Length == 1 ? distinct[0] : null;
        }
        var assessor = Common(records.Select(item => item.Assessor));
        var version = Common(records.Select(item => item.Invocation?.ExecutableVersion));
        var parser = Common(records.Select(item => item.Invocation?.ExecutableBytesSha256));
        var tokeniser = Common(records.Select(item => item.TokeniserName));
        var tokeniserVersion = Common(records.Select(item => item.TokeniserVersion));
        return new CheckpointPin(side, ids, assessor, version, parser, input.ReviewCounterSemantics?.ToString(),
            inputs.ScopeDigest, tokeniser is null || tokeniserVersion is null ? null : new TokeniserPin(tokeniser, tokeniserVersion));
    }

    private static string Digest<T>(T value) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(value, MotifJson.CreateOptions())));

    private static Dictionary<string, TrialReviewObservation> Index(IReadOnlyList<TrialReviewObservation> observations)
    {
        var result = new Dictionary<string, TrialReviewObservation>(StringComparer.Ordinal);
        foreach (var observation in observations)
            if (!result.TryAdd(observation.CaseKey, observation)) throw new InvalidDataException("Duplicate Trial observation.");
        return result;
    }

    private static IEnumerable<ReviewCaseDelta> Classify(FrozenReviewInputs inputs,
        IReadOnlyDictionary<string, TrialReviewObservation> before, IReadOnlyDictionary<string, TrialReviewObservation> after)
    {
        foreach (var item in inputs.Cases)
        {
            before.TryGetValue(item.CaseKey, out var b);
            after.TryGetValue(item.CaseKey, out var a);
            var facts = new ReviewCaseFacts(item.CaseKey, item.WordKey, item.Word, item.WritingSystemId,
                Evidence(b), Evidence(a), item.Assertions, item.Memberships)
            { IdentityAvailable = !string.IsNullOrWhiteSpace(item.CaseKey) && !string.IsNullOrWhiteSpace(item.WordKey) &&
                !string.IsNullOrWhiteSpace(item.WritingSystemId) };
            var delta = ReviewCaseClassifier.Compare(facts);
            var readings = Readings(item.Assertions, b, Side.Before).Concat(Readings(item.Assertions, a, Side.After)).ToArray();
            yield return delta with { Before = SideState(delta.Before, b), After = SideState(delta.After, a),
                Readings = ReadingsCaptured(b) && ReadingsCaptured(a) && (b?.Morphology is not null || a?.Morphology is not null)
                    ? readings : null };
        }
    }

    private static bool ReadingsCaptured(TrialReviewObservation? observation) => observation?.Morphology is null ||
        observation.Morphology is { InvalidShape: false, Refusal: null, Analyses: { } analyses } &&
        analyses.All(ReviewCaseClassifier.Exact);

    private static ReviewCaseSideFacts SideState(ReviewCaseSideFacts side, TrialReviewObservation? observation) =>
        observation?.Completion == Completion.Stopped
            ? side with { Completion = Completion.Stopped, StopReasons = observation.StopReasons }
            : side;

    private static IEnumerable<ReadingRow> Readings(IReadOnlyList<ReviewAssertion> assertions,
        TrialReviewObservation? observation, Side side)
    {
        if (observation?.Morphology is not { InvalidShape: false, Refusal: null, Analyses: { } analyses }) yield break;
        foreach (var analysis in analyses.Where(ReviewCaseClassifier.Exact))
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(analysis.Morphs, MotifJson.CreateOptions());
            var identity = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
            var matches = assertions.Where(assertion => assertion.Eligible && assertion.Morphology is not null &&
                ReviewCaseClassifier.Exact(assertion.Morphology) && AnalysisMorphologyMatcher.Matches(analysis, assertion.Morphology)).ToArray();
            yield return new ReadingRow(identity, side, IdentityState.Exact,
                analysis.Morphs.Select(morph => new Morph(morph.Form, morph.Msa,
                    morph.InflType is null ? [] : [morph.InflType], morph.GuessedString ?? "", "")).ToArray(),
                true, matches.Select(assertion => assertion.Id).ToArray(),
                matches.SelectMany(assertion => assertion.SourceRefs).Distinct(StringComparer.Ordinal).ToArray());
        }
    }

    private static IReadOnlyList<ReviewSectionItem>? WarningRows(IReadOnlyList<GrammarWarning> before,
        IReadOnlyList<GrammarWarning> after)
    {
        Dictionary<string, (GrammarWarning Warning, ReviewDiagnostic Diagnostic)>? Map(IReadOnlyList<GrammarWarning> warnings)
        {
            var rows = new Dictionary<string, (GrammarWarning, ReviewDiagnostic)>(StringComparer.Ordinal);
            foreach (var warning in warnings)
            {
                var diagnostic = ReviewDiagnosticIdentity.FromGrammarWarning(warning);
                if (!ReviewDiagnosticIdentity.TryCreate(diagnostic, out var id)) return null;
                rows.TryAdd(id, (warning, diagnostic));
            }
            return rows;
        }
        var old = Map(before);
        var current = Map(after);
        if (old is null || current is null) return null;
        return old.Keys.Union(current.Keys).Order(StringComparer.Ordinal).Select(id =>
        {
            var hasBefore = old.TryGetValue(id, out var earlier);
            var hasAfter = current.TryGetValue(id, out var later);
            var chosen = hasAfter ? later : earlier;
            var change = !hasBefore ? DiagnosticChange.Added : !hasAfter ? DiagnosticChange.Removed :
                earlier.Diagnostic.Severity == later.Diagnostic.Severity ? DiagnosticChange.Unchanged : DiagnosticChange.Changed;
            var row = new DiagnosticRow(id, id, chosen.Diagnostic.Code, change, chosen.Diagnostic.Severity,
                chosen.Diagnostic.SubjectIds ?? [], chosen.Warning.Description, "not-attributed", [])
            { BeforeSeverity = hasBefore ? earlier.Diagnostic.Severity : null };
            return new ReviewSectionItem("warnings", row, Group: chosen.Diagnostic.Code, Outcome: change.ToString().ToLowerInvariant(),
                Flags: hasAfter && (later.Diagnostic.Severity is "warning" or "error") &&
                    (!hasBefore || earlier.Diagnostic.Severity == "info")
                    ? [ReviewFlagId.NewWarningsOrParsimony] : [], Changed: change != DiagnosticChange.Unchanged);
        }).ToArray();
    }

    private static IEnumerable<ReviewSectionItem> SetRows(FrozenReviewInputs inputs)
    {
        var rows = inputs.Sets.ToDictionary(set => set.Id,
            set => (Memberships: 0L, Words: new HashSet<string>(StringComparer.Ordinal),
                Expectations: new HashSet<string>(StringComparer.Ordinal)), StringComparer.Ordinal);
        foreach (var item in inputs.Cases)
        foreach (var member in item.Memberships)
        {
            if (!rows.TryGetValue(member.SetId, out var counts)) continue;
            counts.Memberships++;
            counts.Words.Add(item.WordKey);
            counts.Expectations.UnionWith(member.ExpectationIds);
            rows[member.SetId] = counts;
        }
        foreach (var set in inputs.Sets.OrderBy(set => set.Id, StringComparer.Ordinal))
        {
            var counts = rows[set.Id];
            yield return new ReviewSectionItem("evidence", new SetRow(set.Id, set.Name, set.Role,
                set.MembershipDigest, set.ExpectationDigest, counts.Memberships, counts.Words.Count,
                counts.Expectations.Count, []), Set: set.Id);
        }
    }

    private static async IAsyncEnumerable<ReviewSectionItem> Sections(IEnumerable<ReviewSectionItem> rows,
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            yield return row;
            await Task.Yield();
        }
    }

    private static IEnumerable<string> AfterAssessments(string? json)
    {
        if (json is null) return [];
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("assessmentIds", out var ids) || ids.ValueKind != JsonValueKind.Array) return [];
        return ids.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString()!).ToArray();
    }

    private static ParseWordEvidence? Evidence(TrialReviewObservation? observation)
    {
        if (observation?.Morphology is not { } evidence) return null;
        if (observation.Completion == Completion.Complete) return evidence;
        if (observation.Completion == Completion.Stopped && !evidence.Capped && !evidence.TimedOut)
            return evidence with { Unavailable = [.. evidence.Unavailable, "stopped"] };
        return evidence;
    }

    private static async IAsyncEnumerable<ReviewCaseDelta> Cases(FrozenReviewInputs inputs,
        IReadOnlyDictionary<string, TrialReviewObservation> before, IReadOnlyDictionary<string, TrialReviewObservation> after,
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var item in Classify(inputs, before, after))
        {
            ct.ThrowIfCancellationRequested();
            yield return item;
            await Task.Yield();
        }
    }

    private static GainSummary BuildGains(ReviewAccumulatedFacts facts, FrozenReviewInputs inputs,
        IEnumerable<OperationEnvelope> operations) =>
        GainSummary.Create(facts.AttestedGained, facts.GeneratedGained, GrammarSizeDeltaCounter.Count(operations),
            inputs.GainGate ?? GainGatePolicy.Default, facts.IncompleteCases > 0);

    private static ReviewSummary BuildSummary(ReviewAccumulatedFacts facts, FrozenReviewInputs inputs, TrialReviewLedger ledger,
        long overlappingLosses, IReadOnlyList<string> negativeSets, bool hasWarnings)
    {
        var work = facts.ParserWork ?? new WorkChange(null, inputs.Cases.Count, inputs.ScopeDigest, WorkComparison.NotComparable, ["work-unavailable"]);
        var flags = new List<FlagFact>();
        var state = facts.IncompleteCases > 0 ? EvidenceState.Partial : EvidenceState.Complete;
        if (facts.NewWordFailureWords > 0)
            flags.Add(new(ReviewFlagId.NewWordFailures, "New word failures", facts.NewWordFailureWords, null, state,
                ["wordforms"], [], new NewWordFacts(facts.NewNoParseWords, facts.NewApprovedLossWords, facts.OverlapWords)));
        if (facts.UniqueReadingLosses > 0)
            flags.Add(new(ReviewFlagId.ConfirmedReadingsLost, "Confirmed correct readings lost", null, facts.UniqueReadingLosses, state,
                ["controls"], [], new ReadingLossFacts(facts.UniqueReadingLosses, facts.ApprovedReadingLosses,
                    facts.ReferenceReadingLosses, facts.OverlapReadingLosses, overlappingLosses)));
        if (facts.NewlyAcceptedNegativeWords > 0)
            flags.Add(new(ReviewFlagId.KnownNonwordsNowAccepted, "Known non-words now accepted", facts.NewlyAcceptedNegativeWords,
                null, state, ["negative-controls"], [], new NegativeFacts(facts.NewlyAcceptedNegativeWords,
                    facts.NewlyAcceptedNegativeMemberships, negativeSets)));
        if (facts.Flags.Contains(ReviewFlagId.ParserWorkIncrease))
            flags.Add(new(ReviewFlagId.ParserWorkIncrease, "Parser work increased", null, null, EvidenceState.Complete,
                ["parser-work"], [], work));
        if (facts.Findings is { } findingDelta && (findingDelta.AddedWarnings.Count > 0 ||
            findingDelta.Measures.Any(measure => measure.Limitation is null && measure.Added.Count > 0)))
            flags.Add(new(ReviewFlagId.NewWarningsOrParsimony, "New warnings or Parsimony findings", null, null,
                findingDelta.Limitations.Count == 0 ? EvidenceState.Complete : EvidenceState.Partial,
                ["warnings", "parsimony"], [], new NewFindingFacts(hasWarnings ? findingDelta.AddedWarnings.Count : null, findingDelta.Measures.Where(measure => measure.Limitation is null && measure.Added.Count > 0)
                    .Select(measure => new FindingCount(measure.MeasureId, measure.Added.Count, measure.Unit)).ToArray())));
        var interruptions = ledger.Interruptions.Select(item => new RunInterruption(ledger.JobId, item.Code,
            item.Side == "before" ? Side.Before : Side.After, inputs.ScopeDigest, item.StoppedCaseKeys.Count, item.PendingCaseKeys.Count)).ToArray();
        return new ReviewSummary("No red flags means only 'not bad', never 'good'.", flags,
            new IncompleteSummary("Incomplete", inputs.Cases.Count, facts.IncompleteWords,
                facts.BeforeIncomplete, facts.AfterIncomplete, interruptions, 2),
            new WordformSummary(inputs.Cases.Count, facts.WordBefore, facts.WordAfter, facts.NewNoParseWords, facts.NewApprovedLossWords),
            new ApprovedSummary(facts.ApprovedExpected, facts.ApprovedBeforeMatched, facts.ApprovedAfterMatched,
                facts.ApprovedNewlyLost, facts.ApprovedAfterUnknown),
            new CoverageSummary(facts.SetCountsByRole.GetValueOrDefault("sampled-tokens"), facts.TokensBefore, facts.TokensAfter,
                ReviewCoverage.DeltaPoints(facts.TokensBefore, facts.TokensAfter, true)),
            new CoverageSummary(facts.SetCountsByRole.GetValueOrDefault("paradigm-cells"), facts.CellsBefore, facts.CellsAfter,
                ReviewCoverage.DeltaPoints(facts.CellsBefore, facts.CellsAfter, true)),
            facts.Controls, facts.Negatives, work, new WarningSummary(hasWarnings ? facts.Findings?.AddedWarnings.Count : null,
                hasWarnings ? facts.Findings?.RemovedWarnings.Count : null, null),
            facts.Findings?.Measures.Select(measure => new ParsimonySummary(measure.Group, measure.MeasureId,
                measure.Limitation is null ? measure.Added.Count : null, measure.Limitation is null ? measure.Removed.Count : null,
                measure.Limitation is null ? measure.EvidenceChanged.Count : null, measure.Unit,
                measure.Limitation is null ? EvidenceState.Complete : EvidenceState.Unavailable)).ToArray() ?? [],
            new EffectsSummary(null, false), facts.Sections);
    }
}
