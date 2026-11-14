using System.Text;
using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Host.Config;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Store;
using SIL.Motif.Projection.Review;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Review;

internal static class ReviewTrialCapture
{
    internal static CommandOutcome<EnqueueTrialRequest> Capture(EnqueueTrialRequest request,
        IReadOnlyList<string>? ordinaryWords, string? parserPath)
    {
        if (request.ReviewInputs is not null) return CommandOutcome<EnqueueTrialRequest>.Success(request);
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            try
            {
                var configuration = new ProjectConfigurationReader().Read(project);
                var selected = request.ReviewSetIds?.ToHashSet(StringComparer.Ordinal);
                if (request.ReviewSetIds is { } ids && ids.Count != selected!.Count)
                    throw new InvalidOperationException("Review set IDs cannot be repeated.");
                if (selected is not null && selected.Except(configuration.ReviewWordSets.Select(set => set.Id),
                        StringComparer.Ordinal).Any())
                    throw new InvalidOperationException("A selected review set is not declared.");
                var current = CurrentEvidenceQuery.ReadCurrentEvidence(database, project,
                    includeResolvedReadings: false, includeWordContext: false);
                if (!current.Succeeded) return CommandOutcome<EnqueueTrialRequest>.Refused(current.Refusal!);
                var evidence = current.Value!;
                if ((evidence.Baseline is null || evidence.ProjectSummary is null || !File.Exists(evidence.Baseline.FwDataPath)) &&
                    configuration.ReviewWordSets.Count == 0)
                    return CommandOutcome<EnqueueTrialRequest>.Success(request);
                if (evidence.Baseline is null || evidence.ProjectSummary is null)
                    throw new InvalidOperationException("A published Baseline is required to capture review inputs.");
                using var reader = BaselineReadCache.Open(evidence.Baseline.FwDataPath);
                var cache = reader.Cache;
                var identities = ReviewWordIdentityProjection.Read(cache);
                var approved = ReviewExpectationProjection.ReadApproved(cache).Values.SelectMany(value => value).ToArray();
                var applied = SourceValidatedDecisions.Read(database);
                var decisions = ExcludingCandidate(applied.ReviewDecisions, request.ProposalId);
                var source = ReviewExpectationProjection.ReadSourceValidated(cache, decisions)
                    .Values.SelectMany(value => value).ToArray();
                var assertions = approved.Concat(source).ToArray();
                var negatives = ReviewExpectationProjection.ReadReviewedNegatives(ParsimonyExpectationProjectionBuilder.Build(cache));
                var sets = new List<ReviewCaptureSet>();
                var sources = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                var manifestChecks = new List<(ReviewWordSetManifest Manifest, string Path)>();
                var selectionChecks = new List<(string Name, long Revision)>();
                var configurationPath = ProjectConfigurationReader.PathFor(project);
                foreach (var set in configuration.ReviewWordSets)
                {
                    if (selected is not null && !selected.Contains(set.Id))
                    {
                        sets.Add(new(set.Id, set.Name, set.Role, string.Empty, []));
                        continue;
                    }
                    if (set.Manifest is not null)
                    {
                        var path = set.ResolveManifestPath(configurationPath)!;
                        var manifest = ReviewWordSetManifestReader.Read(path, set.Role);
                        sources.Add(set.Id, manifest.SourceBytes);
                        manifestChecks.Add((manifest, path));
                        sets.Add(new(set.Id, set.Name, set.Role, manifest.SourceDigest, manifest.Members.ToArray()));
                        continue;
                    }
                    var selection = new NamedSelectionRepository(database).Get(set.Selection!)
                        ?? throw new InvalidOperationException($"Review set '{set.Id}' names a missing Selection.");
                    selectionChecks.Add((selection.Name, selection.Revision));
                    var resolved = CurrentEvidenceQuery.ResolveSelection(evidence.ProjectSummary, selection);
                    if (!resolved.Succeeded) throw new InvalidOperationException(resolved.Refusal!.Message);
                    var members = resolved.Value!.Selection.Words.SelectMany(word =>
                    {
                        var matches = identities.Where(item => item.Word == word).ToArray();
                        IEnumerable<ReviewProjectWordIdentity?> candidates = matches.Length == 0
                            ? new ReviewProjectWordIdentity?[] { null } : matches.Cast<ReviewProjectWordIdentity?>();
                        return candidates.Select(identity =>
                        {
                            var expectationIds = identity is null ? [] : assertions.Where(item =>
                                Guid.TryParse(item.Morphology?.SourceWordformGuid, out var guid) &&
                                SIL.Motif.Contract.Ids.CanonicalId.FromGuid(guid).Value == identity.WordformId)
                                .Select(item => item.Id).Distinct(StringComparer.Ordinal).ToArray();
                            var caseId = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                                Encoding.UTF8.GetBytes((identity?.WritingSystemId ?? string.Empty) + "\0" + word +
                                    "\0" + (identity?.WordformId ?? string.Empty))));
                            return new ReviewWordSetMember(caseId, word, identity?.WritingSystemId ?? string.Empty,
                                identity?.WordformId, new ReviewWordSetSource("selection:" + selection.Name, null),
                                expectationIds, null, null);
                        });
                    }).ToArray();
                    var selectionBytes = JsonSerializer.SerializeToUtf8Bytes(selection);
                    sources.Add(set.Id, selectionBytes);
                    var digest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(selectionBytes));
                    sets.Add(new(set.Id, set.Name, set.Role, digest, members));
                }
                var limits = request.Limits ?? JobCommands.ResolveTrialLimits(database, project, request.Scope);
                var scope = configuration.Scopes.FirstOrDefault(item => item.Name ==
                    (request.Scope ?? AssessmentScopeConfiguration.DefaultName)) ?? configuration.Scopes[0];
                var ordinary = ordinaryWords ?? (request.AllWords
                    ? WordQueryResolver.Resolve(WordQueryResolver.AllWordformsQueryText, cache).ToArray()
                    : evidence.Selection?.Selection.Words ?? WordQueryResolver.Resolve(scope.Query, cache).ToArray());
                var policyBytes = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    request.Scope,
                    limits,
                    scope,
                });
                var policyDigest = "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(policyBytes));
                IReadOnlyList<string>? selectedIds = request.ReviewSetIds;
                if (request.NegativeWords is { Count: > 0 } probes)
                {
                    const string probeSetId = "$inline-negative-probes";
                    var members = probes.Select(word => word.Normalize(NormalizationForm.FormD)).Distinct(StringComparer.Ordinal)
                        .Select(word =>
                        {
                            var matches = identities.Where(identity => identity.Word == word).ToArray();
                            var identity = matches.Length == 1 ? matches[0] : null;
                            var id = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(word)));
                            return new ReviewWordSetMember("$probe:" + id, word, identity?.WritingSystemId ?? string.Empty,
                                identity?.WordformId, new ReviewWordSetSource("Trial inline negative probe", null), [], null, null);
                        }).ToArray();
                    var probeBytes = JsonSerializer.SerializeToUtf8Bytes(members);
                    var digest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(probeBytes));
                    sets.Add(new(probeSetId, "Inline negative probes", "negative", digest, members));
                    sources.Add(probeSetId, probeBytes);
                    if (selectedIds is not null) selectedIds = selectedIds.Append(probeSetId).ToArray();
                }
                var inputs = ReviewInputCapture.CaptureAsync(ProjectWorkspaceKey.Compute(project), request.ProposalId,
                    ParserIdentity.Read(parserPath)?.Sha256, sets, selectedIds, ordinary, identities,
                    assertions, negatives, policyDigest: policyDigest).GetAwaiter().GetResult() with
                    {
                        GainGate = configuration.GainGate,
                        ParsimonyBlockOn = configuration.ParsimonyBlockOn.Count == 0 ? null : configuration.ParsimonyBlockOn,
                        VariantForms = VariantFormCapture.Read(cache, request.ReviewProposal),
                    };
                IReadOnlyList<ReviewBaselinePartition>? suppliedBaselines = null;
                if (request.BaselineAssessmentIds is { } assessmentIds)
                {
                    var partitions = ReviewInputPartitions.Create(inputs);
                    if (partitions.Count > 1)
                        throw new InvalidOperationException("Supplied Baseline Assessments do not identify writing-system partitions.");
                    var expectedWords = inputs.Cases.Select(item => item.Word).ToHashSet(StringComparer.Ordinal);
                    foreach (var id in assessmentIds)
                    {
                        var assessment = new AssessmentRepository(database).Get(id);
                        var baseline = JsonSerializer.Deserialize<BaselineToken>(assessment.BaselineToken, MotifJson.CreateOptions());
                        if (baseline != evidence.Baseline.Token || !expectedWords.SetEquals(assessment.Selection.Words))
                            throw new InvalidOperationException("The supplied Baseline Assessment does not match the captured review scope.");
                    }
                    if (partitions.Count == 1)
                        suppliedBaselines = [new(partitions[0].WritingSystemId, partitions[0].CaseKeys, assessmentIds.ToArray())];
                }
                foreach (var check in manifestChecks) check.Manifest.EnsureSourceUnchanged(check.Path);
                foreach (var check in selectionChecks)
                    if (new NamedSelectionRepository(database).Get(check.Name)?.Revision != check.Revision)
                        throw new InvalidOperationException($"Selection '{check.Name}' changed during review capture.");
                var appliedAfter = SourceValidatedDecisions.Read(database);
                if (!applied.ReviewDecisions.SequenceEqual(appliedAfter.ReviewDecisions) ||
                    !applied.Unreadable.SequenceEqual(appliedAfter.Unreadable))
                    throw new InvalidOperationException("Applied source decisions changed during review capture.");
                using var baselineBytes = File.OpenRead(evidence.Baseline.FwDataPath);
                var baselineFileDigest = "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(baselineBytes));
                var reference = ReviewInputArtifacts.Store(database, new ReviewInputArtifact(inputs, sources)
                    { Baseline = evidence.Baseline.Token, Scope = scope, Limits = limits, BaselineFile = evidence.Baseline.FwDataPath,
                        BaselineFileDigest = baselineFileDigest });
                return CommandOutcome<EnqueueTrialRequest>.Success(request with
                {
                    Words = inputs.Cases.Select(item => item.Word).Distinct(StringComparer.Ordinal).ToArray(),
                    AllWords = false,
                    Limits = limits,
                    ReviewInputs = reference,
                    ReviewSnapshot = inputs,
                    ReviewBaselines = suppliedBaselines,
                });
            }
            catch (Exception exception) when (exception is not (OperationCanceledException or
                SIL.Motif.Projection.ProjectWritingSystemsUnavailableException))
            {
                return CommandOutcome<EnqueueTrialRequest>.Refused(new Refusal(
                    RefusalCodes.TrialReviewInputUnavailable, FailureReason.Refused, exception.Message));
            }
        });
    }

    internal static IReadOnlyList<ReviewSourceDecision> ExcludingCandidate(
        IReadOnlyList<ReviewSourceDecision> decisions, string proposalId)
    {
        if (!SIL.Motif.Contract.Ids.CanonicalId.TryParse(proposalId, out var candidate)) return decisions;
        return decisions.Where(decision => !SIL.Motif.Contract.Ids.CanonicalId.TryParse(decision.ProposalId, out var source) ||
            source != candidate).ToArray();
    }
}
