using SIL.Motif.Contract.Parsimony;

namespace SIL.Motif.Host.Parsimony;

/// <summary>Reads Assessment-bound parser cases and exact Disapproved-reading matches.</summary>
public sealed partial class ParsimonyQuerySession
{
    /// <summary>Whether this Report bound at least one parser Assessment.</summary>
    public bool HasParserOverlay => _inputs.AssessmentIds.Count > 0;

    /// <summary>Reads parser case completion and the counters captured by Motif.</summary>
    public IReadOnlyList<ParsimonyParserCaseViewRow> ReadParserCases(string? caseKey = null)
    {
        ThrowIfLeaseLost();
        var cases = new Dictionary<(string Assessment, string Case), ParserCaseViewBuilder>();
        using (var command = NewCommand())
        {
            command.CommandText = "SELECT c.assessment_id, r.invocation_id, r.source_sha256, r.parser_sha256, " +
                "c.case_key, c.surface, c.surface_nfd, c.writing_system, c.wordform_guid, c.completion, " +
                "c.identity_status, c.reason, c.capped, c.timed_out, c.invalid_shape, c.analysis_count, " +
                "c.attempts, c.passes, r.status FROM evidence.parser_cases AS c " +
                "JOIN evidence.parser_runs AS r USING (assessment_id) " +
                "WHERE ($case IS NULL OR c.case_key=$case) ORDER BY c.assessment_id, c.case_key;";
            command.Parameters.AddWithValue("$case", (object?)caseKey ?? DBNull.Value);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var key = (reader.GetString(0), reader.GetString(4));
                var completion = reader.GetString(9);
                var identity = reader.GetString(10);
                var runStatus = reader.GetString(18);
                var status = runStatus == "unavailable" ? "unavailable"
                    : completion != "complete" ? "incomplete"
                    : identity != "complete" ? "identity-unavailable" : "complete";
                var counters = new Dictionary<string, long?>(StringComparer.Ordinal)
                {
                    ["analyses"] = reader.GetInt64(15),
                    ["capped"] = reader.GetInt32(12),
                    ["timedOut"] = reader.GetInt32(13),
                    ["invalidShape"] = reader.GetInt32(14),
                    ["attempts"] = reader.IsDBNull(16) ? null : reader.GetInt64(16),
                    ["passes"] = reader.IsDBNull(17) ? null : reader.GetInt64(17),
                };
                cases.Add(key, new ParserCaseViewBuilder(reader.GetString(0), reader.GetString(1),
                    reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
                    reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8), status, completion, identity,
                    reader.IsDBNull(11) ? null : reader.GetString(11), counters));
            }
        }

        using (var command = NewCommand())
        {
            command.CommandText = "SELECT a.assessment_id, a.case_key, a.ordinal, a.signature, a.identity_status, " +
                "m.morph_ordinal, m.form_guid, m.msa_guid, m.infl_type_guid, m.guessed_string_nfd, " +
                "d.disapproved_analysis_guid FROM evidence.parser_analyses AS a " +
                "LEFT JOIN evidence.parser_analysis_morphs AS m ON m.assessment_id=a.assessment_id " +
                "AND m.case_key=a.case_key AND m.analysis_ordinal=a.ordinal " +
                "LEFT JOIN evidence.parser_disapproved_matches AS d ON d.assessment_id=a.assessment_id " +
                "AND d.case_key=a.case_key AND d.parser_analysis_ordinal=a.ordinal " +
                "WHERE ($case IS NULL OR a.case_key=$case) " +
                "ORDER BY a.assessment_id, a.case_key, a.ordinal, m.morph_ordinal, d.disapproved_analysis_guid;";
            command.Parameters.AddWithValue("$case", (object?)caseKey ?? DBNull.Value);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var caseBuilder = cases[(reader.GetString(0), reader.GetString(1))];
                var analysisOrdinal = reader.GetInt32(2);
                if (!caseBuilder.Analyses.TryGetValue(analysisOrdinal, out var analysis))
                    caseBuilder.Analyses.Add(analysisOrdinal, analysis = new ParserAnalysisViewBuilder(
                        analysisOrdinal, reader.GetString(3), reader.GetString(4)));
                if (!reader.IsDBNull(5))
                    analysis.Morphs.TryAdd(reader.GetInt32(5), new ParsimonyParserMorphViewRow(reader.GetInt32(5),
                        reader.IsDBNull(6) ? null : reader.GetString(6),
                        reader.IsDBNull(7) ? null : reader.GetString(7),
                        reader.IsDBNull(8) ? null : reader.GetString(8),
                        reader.IsDBNull(9) ? null : reader.GetString(9)));
                if (!reader.IsDBNull(10)) analysis.DisapprovedAnalysisGuids.Add(reader.GetString(10));
            }
        }

        using (var command = NewCommand())
        {
            command.CommandText = "SELECT assessment_id, case_key, case_id, revision_id, content_digest, " +
                "expectation_status, accepted, accepted_analysis_ordinals_json, reason " +
                "FROM evidence.parser_reviewed_negative_cases WHERE case_key IS NOT NULL " +
                "AND ($case IS NULL OR case_key=$case) " +
                "ORDER BY assessment_id, case_key, case_id, revision_id;";
            command.Parameters.AddWithValue("$case", (object?)caseKey ?? DBNull.Value);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var caseBuilder = cases[(reader.GetString(0), reader.GetString(1))];
                caseBuilder.ReviewedNegatives.Add(new ParsimonyReviewedNegativeParserCase(
                    reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
                    reader.GetInt32(6) != 0,
                    Array.AsReadOnly(System.Text.Json.JsonSerializer.Deserialize<int[]>(reader.GetString(7)) ?? []),
                    reader.IsDBNull(8) ? null : reader.GetString(8)));
            }
        }

        return Array.AsReadOnly(cases.Values.Select(item => item.Build()).ToArray());
    }

    /// <summary>Reads exact parser matches and eligibility for the Disapproved-reading measure.</summary>
    public ParserOverlayMeasureFacts ReadDisapprovedProducedFacts()
    {
        ThrowIfLeaseLost();
        if (!HasParserOverlay)
            return new ParserOverlayMeasureFacts(false, false, 0, 0, 0, 0, ParsimonyNotes.NoAssessmentReason, []);

        var completeRuns = ScalarCount("SELECT COUNT(*) FROM evidence.parser_runs WHERE status='complete';");
        if (completeRuns == 0)
            return new ParserOverlayMeasureFacts(true, false, 0, 0, 0, 0,
                "no parser cases finished, so there is nothing to compare.", []);

        var eligible = ScalarCount("""
            SELECT COUNT(*) FROM (
                SELECT DISTINCT c.wordform_guid, d.signature
                FROM evidence.parser_cases AS c
                JOIN evidence.parser_disapproved_morphologies AS d
                  ON d.assessment_id=c.assessment_id AND d.case_key=c.case_key
                WHERE c.completion='complete' AND c.identity_status='complete'
                  AND d.identity_status='complete' AND c.wordform_guid IS NOT NULL
            );
            """);
        var matches = new List<ParserDisapprovedMatchFact>();
        using (var command = NewCommand())
        {
            command.CommandText = "SELECT c.wordform_guid, d.signature, m.assessment_id, m.case_key, " +
                "m.parser_analysis_ordinal, d.analysis_guids_json FROM evidence.parser_disapproved_matches AS m " +
                "JOIN evidence.parser_cases AS c ON c.assessment_id=m.assessment_id AND c.case_key=m.case_key " +
                "JOIN evidence.parser_disapproved_morphologies AS d ON d.assessment_id=m.assessment_id " +
                "AND d.case_key=m.case_key AND d.signature=m.disapproved_signature " +
                "WHERE c.completion='complete' AND c.identity_status='complete' " +
                "AND d.identity_status='complete' AND c.wordform_guid IS NOT NULL " +
                "ORDER BY c.wordform_guid, d.signature, m.assessment_id, m.case_key, m.parser_analysis_ordinal;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                matches.Add(new ParserDisapprovedMatchFact(reader.GetString(0), reader.GetString(1),
                    reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetString(5)));
        }

        var distinctMatches = matches.GroupBy(item => (item.WordformGuid, item.Signature))
            .Select(group => group.First()).ToArray();
        var matched = distinctMatches.LongLength;
        var affectedWords = distinctMatches.Select(item => item.WordformGuid)
            .Distinct(StringComparer.Ordinal).LongCount();
        var excluded = ScalarCount("""
            SELECT COUNT(*) FROM evidence.parser_cases AS c
            WHERE c.completion<>'complete' OR c.identity_status<>'complete'
            """) + ScalarCount("""
            SELECT COUNT(*) FROM evidence.parser_disapproved_morphologies AS d
            WHERE d.identity_status<>'complete'
            """) + ScalarCount("SELECT COUNT(*) FROM evidence.parser_runs WHERE status='unavailable';");
        var detail = eligible == 0
            ? "no finished parser case is a word with a Disapproved analysis, so there is nothing to compare."
            : excluded == 0 ? null :
            $"parser evidence for {excluded} item(s) is incomplete or cannot be attributed, so those items do not count as passes.";
        return new ParserOverlayMeasureFacts(true, eligible > 0, eligible, matched, affectedWords, excluded,
            detail, Array.AsReadOnly(distinctMatches));
    }

    /// <summary>Reads completed exact parser outcomes for Notebook-reviewed negative cases.</summary>
    public ParserReviewedNegativeMeasureFacts ReadReviewedNegativeAcceptedFacts()
    {
        ThrowIfLeaseLost();
        if (!HasParserOverlay)
            return new ParserReviewedNegativeMeasureFacts(false, false, 0, 0, 0,
                ParsimonyNotes.NoAssessmentReason, []);

        var rows = new List<ParserReviewedNegativeCaseProjection>();
        using (var command = NewCommand())
        {
            command.CommandText = "SELECT assessment_id, case_id, revision_id, content_digest, writing_system, " +
                "form_nfd, expectation_status, case_key, completion, identity_status, accepted, " +
                "accepted_analysis_ordinals_json, reason FROM evidence.parser_reviewed_negative_cases " +
                "ORDER BY case_id, revision_id, assessment_id, row_ordinal;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                rows.Add(new ParserReviewedNegativeCaseProjection(reader.GetString(0), reader.GetString(1),
                    reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
                    reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetString(8),
                    reader.GetString(9), reader.GetInt32(10) != 0, reader.GetString(11),
                    reader.IsDBNull(12) ? null : reader.GetString(12)));
        }

        var effective = rows.Where(item => item.ExpectationStatus == "eligible")
            .GroupBy(item => (item.CaseId, item.RevisionId)).ToArray();
        if (effective.Length == 0)
            return new ParserReviewedNegativeMeasureFacts(true, false, 0, 0, rows.Count,
                "the project has no conflict-free reviewed negative expectations, so there is nothing to compare.", []);

        var completed = effective.Select(group => group.Where(IsCompletedReviewedNegativeCase)
                .OrderBy(item => item.AssessmentId, StringComparer.Ordinal).FirstOrDefault())
            .Where(item => item is not null).Select(item => item!).ToArray();
        if (completed.Length == 0)
            return new ParserReviewedNegativeMeasureFacts(true, false, 0, 0, effective.Length,
                "no reviewed negative has a completed, exact parser case, so there is nothing to compare.", []);

        var accepted = effective.Select(group => group.Where(IsCompletedReviewedNegativeCase)
                .Where(item => item.Accepted)
                .OrderBy(item => item.AssessmentId, StringComparer.Ordinal).FirstOrDefault())
            .Where(item => item is not null).Select(item => item!).ToArray();
        var excluded = effective.LongCount(group => !group.Any(IsCompletedReviewedNegativeCase));
        var detail = excluded == 0 ? null :
            $"the parser case for {excluded} reviewed negative(s) is not completed and exact, so they were not counted.";
        return new ParserReviewedNegativeMeasureFacts(true, true, completed.LongLength,
            accepted.LongLength, excluded, detail, Array.AsReadOnly(accepted));
    }

    private static bool IsCompletedReviewedNegativeCase(ParserReviewedNegativeCaseProjection item) =>
        item.Completion == "complete" && item.IdentityStatus == "complete" && item.CaseKey is not null;

    private long ScalarCount(string sql)
    {
        using var command = NewCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed class ParserCaseViewBuilder(string assessmentId, string invocationId, string sourceSha256,
        string parserSha256, string caseKey, string surface, string surfaceNfd, string? writingSystem,
        string? wordformGuid, string status, string completion, string identityStatus, string? reason,
        IReadOnlyDictionary<string, long?> counters)
    {
        public SortedDictionary<int, ParserAnalysisViewBuilder> Analyses { get; } = [];
        public List<ParsimonyReviewedNegativeParserCase> ReviewedNegatives { get; } = [];

        public ParsimonyParserCaseViewRow Build() => new(assessmentId, invocationId, sourceSha256,
            parserSha256, caseKey, surface, surfaceNfd, writingSystem, wordformGuid, status, completion,
            identityStatus, reason, Array.AsReadOnly(Analyses.Values.Select(item => item.Build()).ToArray()),
            Array.AsReadOnly(ReviewedNegatives.OrderBy(item => item.CaseId, StringComparer.Ordinal)
                .ThenBy(item => item.RevisionId, StringComparer.Ordinal).ToArray()),
            counters);
    }

    private sealed class ParserAnalysisViewBuilder(int ordinal, string signature, string identityStatus)
    {
        public SortedDictionary<int, ParsimonyParserMorphViewRow> Morphs { get; } = [];
        public SortedSet<string> DisapprovedAnalysisGuids { get; } = new(StringComparer.Ordinal);

        public ParsimonyParserAnalysisViewRow Build() => new(ordinal, signature, identityStatus,
            Array.AsReadOnly(DisapprovedAnalysisGuids.ToArray()),
            Array.AsReadOnly(Morphs.Values.ToArray()));
    }
}

/// <summary>Exact complete-case counts and matches for the Disapproved-reading detector.</summary>
public sealed record ParserOverlayMeasureFacts(bool Requested, bool Available, long EligibleMorphologies,
    long MatchedMorphologies, long AffectedWords, long ExcludedEvidence, string? Detail,
    IReadOnlyList<ParserDisapprovedMatchFact> Matches);

/// <summary>One parser-produced morphology matched to one Disapproved source morphology.</summary>
public sealed record ParserDisapprovedMatchFact(string WordformGuid, string Signature,
    string AssessmentId, string CaseKey, int ParserAnalysisOrdinal, string AnalysisGuidsJson);

/// <summary>Completed-case denominator and exact acceptances for reviewed negative inputs.</summary>
public sealed record ParserReviewedNegativeMeasureFacts(bool Requested, bool Available,
    long EligibleNegatives, long AcceptedNegatives, long ExcludedNegatives, string? Detail,
    IReadOnlyList<ParserReviewedNegativeCaseProjection> AcceptedCases);
