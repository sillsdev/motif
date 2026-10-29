using System.Text.Json;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Store;
using SIL.Motif.Projection;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Reads what Motif knows about one inspector subject, each section from its own source and each match by exact
/// identity: FieldWorks' facts from the current Baseline, which need no Parse all words; the words that use it and
/// the words it ran in from the stored Parse all words that matches the Baseline and default Selection; and the
/// stored grammar check's findings that name it or reach it.
/// </summary>
public static class InspectQuery
{
    private const string NoParse =
        "No Parse all words matches the current Baseline and Selection. Parse all words to see your words.";

    /// <summary>Answers <paramref name="request"/>, every section saying whether it could be read and why not.</summary>
    public static CommandOutcome<InspectResponse> Query(InspectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Subject is null)
            return CommandOutcome<InspectResponse>.Refused(new Refusal(
                "inspect.invalid-request", FailureReason.InvalidArgument, "Name a subject to inspect."));
        var subject = request.Subject;

        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            var current = CurrentEvidenceQuery.ReadCurrentEvidence(database, project, includeResolvedReadings: true);
            if (!current.Succeeded) return CommandOutcome<InspectResponse>.Refused(current.Refusal!);
            var snapshot = current.Value!;
            if (request.ProposalId is { } proposalId)
            {
                var trial = Trials.LatestFor(database, project, proposalId);
                if (trial is null)
                    return CommandOutcome<InspectResponse>.Refused(new Refusal("trial.none", FailureReason.NotFound,
                        "No Trial is recorded for this Proposal."));
                var selected = EvidenceSets.ForTrial(database, project, trial);
                if (!selected.Succeeded) return CommandOutcome<InspectResponse>.Refused(selected.Refusal!);
                var scratchRoot = TrialScratchPath.ForJob(trial.JobId);
                var scratchProject = TrialScratchPath.ProjectFile(scratchRoot);
                if (scratchProject is null)
                    return CommandOutcome<InspectResponse>.Refused(new Refusal("trial.scratch-gone",
                        FailureReason.NotFound, "The Trial's details were cleared. Run the Trial again to bring them back."));
                snapshot = ForTrial(database, project, snapshot, selected.Value!, scratchRoot, scratchProject);
            }

            var (resolution, facts) = snapshot.Baseline is { } baseline
                ? FactsOf(subject, baseline, snapshot.Navigation ??
                    SavedProjectNavigation.Read(baseline.FwDataPath, baseline.Token.ProjectIdentity))
                : (InspectorResolution.NoBaseline, null);
            var timingKey = subject.Kind switch
            {
                InspectorSubjectKind.Rule => subject.TimingKey,
                InspectorSubjectKind.Morpheme => facts?.TimingKey,
                _ => null,
            };
            var response = new InspectResponse(subject, resolution)
            {
                ObjectTimingDetail = snapshot.TimingDetail,
                IsStale = snapshot.Freshness == EvidenceFreshness.Stale,
                BaselineDigest = snapshot.Baseline?.Token.BundleDigest,
                AssessmentId = snapshot.MatchingAssessment?.AssessmentId,
                TimingKey = timingKey,
                Facts = facts is not null ? InspectorSection<ObjectFacts>.Of(facts)
                    : InspectorSection<ObjectFacts>.Not(
                        resolution == InspectorResolution.Unsupported ? InspectorSectionStatus.Unsupported : InspectorSectionStatus.Absent,
                        FactsReason(resolution)),
                Warnings = WarningsSection(database, snapshot, subject, timingKey),
            };
            if (snapshot.Assessment is not { } assessment || snapshot.MatchingAssessment is null)
                return CommandOutcome<InspectResponse>.Success(response with
                {
                    Uses = UsesSection(subject, null),
                    RanIn = RanInSection(subject, timingKey, null, null),
                });
            var timings = snapshot.EffectiveObjectTimings;
            return CommandOutcome<InspectResponse>.Success(response with
            {
                Uses = UsesSection(subject, assessment.Words),
                RanIn = RanInSection(subject, timingKey, assessment.Words, timings),
            });
        });
    }

    private static CurrentEvidenceSnapshot ForTrial(MotifDatabase database, ProjectLocator project,
        CurrentEvidenceSnapshot current, EvidenceSet set, string scratchRoot, string scratchProject)
    {
        var assessments = new AssessmentRepository(database);
        AssessmentRecord? Read(string id)
        {
            try { return assessments.Get(id); }
            catch (KeyNotFoundException) { return null; }
        }
        var root = set.RootAssessmentId is null ? null : Read(set.RootAssessmentId);
        if (root is null) return current with
        {
            Baseline = current.Baseline is null ? null : current.Baseline with
            {
                Token = set.Baseline,
                RootDirectory = scratchRoot,
                FwDataPath = scratchProject,
            },
            Freshness = EvidenceFreshness.Stale,
            Set = set,
            Navigation = SavedProjectNavigation.Read(scratchProject, set.Baseline.ProjectIdentity),
        };
        var replacements = set.ReplacementAssessmentIds.Select(Read).Where(record => record is not null)
            .Cast<AssessmentRecord>().ToArray();
        var selected = AssessmentEvidenceSet.Create(root, replacements);
        var source = current.Baseline;
        var baseline = source is null
            ? new BaselineRecord(ProjectWorkspaceKey.Compute(project), set.Baseline, scratchRoot, scratchProject,
                DateTimeOffset.MinValue, DateTimeOffset.MinValue)
            : source with { Token = set.Baseline, RootDirectory = scratchRoot, FwDataPath = scratchProject };
        return current with
        {
            Baseline = baseline,
            Freshness = current.Baseline?.Token == set.Baseline && current.Freshness == EvidenceFreshness.Current
                ? EvidenceFreshness.Current : EvidenceFreshness.Stale,
            Selection = new ResolvedSelectionSnapshot(root.Selection,
                new Dictionary<string, int>(StringComparer.Ordinal), 0, 0),
            MatchingAssessment = root,
            RerunAssessments = replacements,
            MatchingCorrectnessAssessmentId = set.MeasurementIds.GetValueOrDefault(AssessmentKinds.Correctness),
            MatchingObjectTimingAssessmentId = set.MeasurementIds.GetValueOrDefault(AssessmentKinds.ObjectTiming),
            EvidenceSet = selected,
            Set = set,
            Navigation = SavedProjectNavigation.Read(scratchProject, set.Baseline.ProjectIdentity),
        };
    }

    /// <summary>
    /// The findings among <paramref name="findings"/> that name <paramref name="subject"/> or reach it: a subject part
    /// that is the object, or whose stored reach holds its allomorph, its grammatical info or
    /// <paramref name="timingKey"/>. Matched by identity alone, GUIDs as GUIDs; a reach by spelling names no object.
    /// Exact and membership references both identify related findings; membership remains candidate evidence,
    /// never confirmed word use. A non-route outcome matches only a subject it explicitly names.
    /// A warning subject matches the findings with its code that name its object.
    /// </summary>
    public static IReadOnlyList<GrammarWarning> WarningsNaming(IReadOnlyList<GrammarWarning> findings,
        InspectorSubject subject, TraceTimingKey? timingKey)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(subject);
        if (subject.Kind == InspectorSubjectKind.Warning)
            return findings.Where(finding => finding.Code == subject.WarningCode &&
                (subject.ObjectId is null || finding.Subject.Any(part => Same(part.SubjectGuid, subject.ObjectId) ||
                    Same(part.FieldWorksGuid, subject.ObjectId)))).ToArray();
        var ids = new[] { subject.AllomorphId, subject.GrammaticalInfoId, subject.ObjectId }
            .Where(id => id is { Length: > 0 }).Select(id => IdKey(id!)).ToHashSet(StringComparer.Ordinal);
        if (timingKey?.IdentityQuality == "authored" && Guid.TryParse(timingKey.Key, out var timedGuid)) ids.Add(timedGuid.ToString("D"));
        bool Names(GrammarWarningPart part) =>
            new[] { part.SubjectGuid, part.FieldWorksGuid }.Any(id => id is { Length: > 0 } && ids.Contains(IdKey(id))) ||
            part.Reach is { IsRoute: true } reach && (
                subject.AllomorphId is { } allomorph && reach.AllomorphIds.Concat(reach.MembershipAllomorphIds)
                    .Any(id => Same(id, allomorph)) ||
                subject.GrammaticalInfoId is { } info && reach.GrammaticalInfoIds.Concat(reach.MembershipGrammaticalInfoIds)
                    .Any(id => Same(id, info)) ||
                timingKey is { } key && reach.TimingKeys.Concat(reach.MembershipTimingKeys)
                    .Any(timed => ObjectIdentity.Same(timed.Identity, key.Identity)));
        return findings.Where(finding => finding.Subject.Any(Names)).ToArray();
    }

    // The Baseline's own copy, opened as a scratch: reading it can never change the project the linguist edits.
    private static (InspectorResolution, ObjectFacts?) FactsOf(InspectorSubject subject, BaselineRecord baseline,
        SavedProjectNavigation navigation)
    {
        if (subject.Kind is not (InspectorSubjectKind.Morpheme or InspectorSubjectKind.Rule))
            return (InspectorResolution.Unsupported, null);
        if (subject.IdentityQuality != "authored" ||
            subject.Kind == InspectorSubjectKind.Rule && !Guid.TryParse(subject.TimingKey?.Key, out _))
            return (InspectorResolution.NotAuthored, null);
        using var reader = BaselineReadCache.Open(baseline.FwDataPath);
        var cache = reader.Cache;
        var (resolution, reference) = InspectorSubjectResolver.Resolve(cache, subject);
        if (reference is null) return (resolution, null);
        var facts = ObjectFactsReader.Read(cache, reference, found => navigation.TargetFor(cache, found));
        return facts is null ? (InspectorResolution.NotInBaseline, null) : (resolution, facts);
    }

    private static string FactsReason(InspectorResolution resolution) => resolution switch
    {
        InspectorResolution.NoBaseline => "No Baseline yet. Refresh to read the FieldWorks project.",
        InspectorResolution.NotInBaseline => "The Baseline's FieldWorks project doesn't hold this object.",
        InspectorResolution.Contradictory => "The names given belong to different FieldWorks objects, so Motif shows neither.",
        InspectorResolution.NotAuthored => "PanGloss named this without a FieldWorks identity, so FieldWorks can't be asked about it.",
        _ => "Motif doesn't read FieldWorks' facts for this kind of object yet.",
    };

    private static InspectorSection<ObjectUseWords> UsesSection(InspectorSubject subject,
        IReadOnlyList<AssessmentWordResult>? words) =>
        subject.Kind != InspectorSubjectKind.Morpheme
            ? InspectorSection<ObjectUseWords>.Not(InspectorSectionStatus.Unsupported, "Only a morpheme is used by words.")
            : subject.IdentityQuality != "authored" ||
                subject.AllomorphId is not null && !Guid.TryParse(subject.AllomorphId, out _) ||
                subject.GrammaticalInfoId is not null && !Guid.TryParse(subject.GrammaticalInfoId, out _)
                ? InspectorSection<ObjectUseWords>.Not(
                InspectorSectionStatus.Absent, FactsReason(InspectorResolution.NotAuthored))
            : words is null ? InspectorSection<ObjectUseWords>.Not(InspectorSectionStatus.Absent, NoParse)
            : InspectorSection<ObjectUseWords>.Of(ObjectUsesQuery.UsesOf(words,
                new ObjectUseRef { AllomorphId = subject.AllomorphId, GrammaticalInfoId = subject.GrammaticalInfoId }));

    // Only a key PanGloss records is looked up: a slot, environment or feature gets no made-up timing identity.
    private static InspectorSection<ObjectUseWords> RanInSection(InspectorSubject subject, TraceTimingKey? key,
        IReadOnlyList<AssessmentWordResult>? words, IReadOnlyList<AssessmentObjectTiming>? timings) =>
        subject.Kind is not (InspectorSubjectKind.Morpheme or InspectorSubjectKind.Rule)
            ? InspectorSection<ObjectUseWords>.Not(InspectorSectionStatus.Unsupported, "PanGloss doesn't time this kind of object.")
            : key is null ? InspectorSection<ObjectUseWords>.Not(InspectorSectionStatus.Absent,
                "Motif can't tell which object PanGloss times this under.")
            : words is null || timings is null ? InspectorSection<ObjectUseWords>.Not(InspectorSectionStatus.Absent, NoParse)
            : InspectorSection<ObjectUseWords>.Of(ObjectUsesQuery.RanIn(words, timings, ObjectUseRef.ForTimingKey(key)));

    private static InspectorSection<IReadOnlyList<GrammarWarning>> WarningsSection(MotifDatabase database,
        CurrentEvidenceSnapshot snapshot, InspectorSubject subject, TraceTimingKey? timingKey)
    {
        if (subject.Kind == InspectorSubjectKind.Morpheme && subject.IdentityQuality != "authored")
            return InspectorSection<IReadOnlyList<GrammarWarning>>.Not(InspectorSectionStatus.Absent,
                FactsReason(InspectorResolution.NotAuthored));
        if (snapshot.Baseline is not { } baseline)
            return InspectorSection<IReadOnlyList<GrammarWarning>>.Not(InspectorSectionStatus.Absent,
                "No Baseline yet, so no grammar check.");
        var token = JsonSerializer.Serialize(baseline.Token, MotifJson.CreateOptions());
        var check = snapshot.Set is
            { Origin: { Kind: EvidenceOriginKind.Trial, ProposalId: { } proposalId, IntentDigest: { } intentDigest } }
            ? new GrammarCheckRepository(database).GetLatest(token, proposalId, intentDigest)
            : snapshot.Set?.Origin.Kind == EvidenceOriginKind.Trial
                ? null
                : new GrammarCheckRepository(database).GetLatest(token);
        if (check is not null)
            check = snapshot.Navigation!.Verify(StoredParseWarnings.Merge(check, snapshot));
        return check is null
            ? InspectorSection<IReadOnlyList<GrammarWarning>>.Not(InspectorSectionStatus.Absent,
                "The grammar hasn't been checked since the last Refresh.")
            : InspectorSection<IReadOnlyList<GrammarWarning>>.Of(
                WarningsNaming((snapshot.Assessment is { } assessment
                    ? WarningWordsQuery.WithYourWords(check, assessment.Words, snapshot.EffectiveObjectTimings, snapshot.TimingDetail)
                    : check).Findings, subject, timingKey));
    }

    private static bool Same(string? left, string? right) =>
        ObjectIdentity.Same(ObjectIdentity.Create("model", left), ObjectIdentity.Create("model", right));

    // A GUID compares as a GUID, whatever its case or braces; any other key compares exactly.
    private static string IdKey(string id) => ObjectIdentity.CanonicalKey(id)!;
}
