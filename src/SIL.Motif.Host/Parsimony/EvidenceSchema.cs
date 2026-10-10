namespace SIL.Motif.Host.Parsimony;

/// <summary>The current disposable Motif evidence-file schema.</summary>
public static class EvidenceSchema
{
    /// <summary>The file identity written into <c>PRAGMA application_id</c>.</summary>
    public const int ApplicationId = 0x4D4F5445;

    /// <summary>The exact schema generation accepted by this build.</summary>
    public const int Version = 3;

    internal const string Ddl = """
        CREATE TABLE artifact_metadata (
            singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
            application_id INTEGER NOT NULL,
            schema_version INTEGER NOT NULL,
            projection_version INTEGER NOT NULL,
            baseline_token_json TEXT NOT NULL,
            baseline_key TEXT NOT NULL,
            input_kind TEXT NOT NULL CHECK (input_kind IN ('baseline', 'candidate')),
            candidate_identity TEXT COLLATE BINARY,
            source_sha256 TEXT NOT NULL,
            model_fingerprint TEXT NOT NULL,
            complete INTEGER NOT NULL CHECK (complete IN (0, 1)),
            text_count INTEGER NOT NULL CHECK (text_count >= 0),
            occurrence_count INTEGER NOT NULL CHECK (occurrence_count >= 0),
            wordform_count INTEGER NOT NULL CHECK (wordform_count >= 0),
            analysis_count INTEGER NOT NULL CHECK (analysis_count >= 0),
            scope_count INTEGER NOT NULL CHECK (scope_count >= 0),
            scope_word_count INTEGER NOT NULL CHECK (scope_word_count >= 0),
            capability_count INTEGER NOT NULL CHECK (capability_count >= 0),
            parser_run_count INTEGER NOT NULL CHECK (parser_run_count >= 0),
            parser_case_count INTEGER NOT NULL CHECK (parser_case_count >= 0),
            parser_analysis_count INTEGER NOT NULL CHECK (parser_analysis_count >= 0),
            parser_morph_count INTEGER NOT NULL CHECK (parser_morph_count >= 0),
            parser_disapproved_count INTEGER NOT NULL CHECK (parser_disapproved_count >= 0),
            parser_match_count INTEGER NOT NULL CHECK (parser_match_count >= 0),
            parser_reviewed_negative_count INTEGER NOT NULL CHECK (parser_reviewed_negative_count >= 0),
            CHECK ((input_kind = 'candidate') = (candidate_identity IS NOT NULL))
        );
        CREATE TABLE texts (
            text_guid TEXT PRIMARY KEY COLLATE BINARY,
            title TEXT NOT NULL,
            title_ws TEXT COLLATE BINARY
        );
        CREATE TABLE segments (
            text_guid TEXT NOT NULL REFERENCES texts(text_guid),
            paragraph_guid TEXT NOT NULL COLLATE BINARY,
            segment_guid TEXT NOT NULL COLLATE BINARY,
            line_number INTEGER NOT NULL CHECK (line_number > 0),
            sentence TEXT NOT NULL,
            sentence_ws TEXT COLLATE BINARY,
            parse_current INTEGER NOT NULL CHECK (parse_current IN (0, 1)),
            PRIMARY KEY (text_guid, paragraph_guid, segment_guid)
        );
        CREATE TABLE wordforms (
            wordform_guid TEXT PRIMARY KEY COLLATE BINARY,
            spelling_status INTEGER NOT NULL
        );
        CREATE TABLE wordform_forms (
            wordform_guid TEXT NOT NULL REFERENCES wordforms(wordform_guid),
            writing_system TEXT NOT NULL COLLATE BINARY,
            form TEXT NOT NULL,
            form_nfd TEXT NOT NULL,
            PRIMARY KEY (wordform_guid, writing_system)
        );
        CREATE TABLE occurrences (
            text_guid TEXT NOT NULL,
            paragraph_guid TEXT NOT NULL COLLATE BINARY,
            segment_guid TEXT NOT NULL COLLATE BINARY,
            token_ordinal INTEGER NOT NULL CHECK (token_ordinal >= 0),
            wordform_guid TEXT COLLATE BINARY REFERENCES wordforms(wordform_guid),
            text TEXT NOT NULL,
            status TEXT COLLATE BINARY,
            PRIMARY KEY (text_guid, segment_guid, token_ordinal),
            FOREIGN KEY (text_guid, paragraph_guid, segment_guid)
                REFERENCES segments(text_guid, paragraph_guid, segment_guid)
        );
        CREATE TABLE occurrence_forms (
            text_guid TEXT NOT NULL,
            segment_guid TEXT NOT NULL COLLATE BINARY,
            token_ordinal INTEGER NOT NULL,
            ordinal INTEGER NOT NULL CHECK (ordinal >= 0),
            writing_system TEXT COLLATE BINARY,
            form TEXT NOT NULL,
            form_nfd TEXT NOT NULL,
            PRIMARY KEY (text_guid, segment_guid, token_ordinal, ordinal),
            FOREIGN KEY (text_guid, segment_guid, token_ordinal)
                REFERENCES occurrences(text_guid, segment_guid, token_ordinal)
        );
        CREATE TABLE analyses (
            analysis_guid TEXT PRIMARY KEY COLLATE BINARY,
            wordform_guid TEXT NOT NULL REFERENCES wordforms(wordform_guid),
            opinion TEXT NOT NULL CHECK (opinion IN ('approved', 'disapproved', 'unknown')),
            source_kind TEXT NOT NULL COLLATE BINARY,
            content_sha256 TEXT NOT NULL COLLATE BINARY
        );
        CREATE TABLE analysis_morphs (
            analysis_guid TEXT NOT NULL REFERENCES analyses(analysis_guid),
            ordinal INTEGER NOT NULL CHECK (ordinal >= 0),
            morph_guid TEXT COLLATE BINARY,
            msa_guid TEXT COLLATE BINARY,
            infl_type_guid TEXT COLLATE BINARY,
            entry_guid TEXT COLLATE BINARY,
            sense_guid TEXT COLLATE BINARY,
            PRIMARY KEY (analysis_guid, ordinal)
        );
        CREATE TABLE analysis_morph_forms (
            analysis_guid TEXT NOT NULL,
            morph_ordinal INTEGER NOT NULL,
            writing_system TEXT NOT NULL COLLATE BINARY,
            form TEXT NOT NULL,
            form_nfd TEXT NOT NULL,
            PRIMARY KEY (analysis_guid, morph_ordinal, writing_system),
            FOREIGN KEY (analysis_guid, morph_ordinal)
                REFERENCES analysis_morphs(analysis_guid, ordinal)
        );
        CREATE TABLE analysis_morph_texts (
            analysis_guid TEXT NOT NULL,
            morph_ordinal INTEGER NOT NULL,
            kind TEXT NOT NULL COLLATE BINARY CHECK (kind IN ('gloss', 'category')),
            writing_system TEXT NOT NULL COLLATE BINARY,
            text TEXT NOT NULL,
            text_nfd TEXT NOT NULL,
            PRIMARY KEY (analysis_guid, morph_ordinal, kind, writing_system),
            FOREIGN KEY (analysis_guid, morph_ordinal)
                REFERENCES analysis_morphs(analysis_guid, ordinal)
        );
        CREATE TABLE occurrence_analyses (
            text_guid TEXT NOT NULL COLLATE BINARY,
            segment_guid TEXT NOT NULL COLLATE BINARY,
            token_ordinal INTEGER NOT NULL,
            analysis_guid TEXT NOT NULL COLLATE BINARY REFERENCES analyses(analysis_guid),
            role TEXT NOT NULL COLLATE BINARY CHECK (role = 'chosen'),
            PRIMARY KEY (text_guid, segment_guid, token_ordinal, analysis_guid, role),
            FOREIGN KEY (text_guid, segment_guid, token_ordinal)
                REFERENCES occurrences(text_guid, segment_guid, token_ordinal)
        );
        CREATE TABLE scope_descriptor (
            scope_id TEXT PRIMARY KEY COLLATE BINARY CHECK (scope_id IN ('project-approved', 'default-selection')),
            status TEXT NOT NULL CHECK (status IN ('complete', 'unavailable')),
            name TEXT COLLATE BINARY,
            selection_sha256 TEXT COLLATE BINARY,
            word_count INTEGER NOT NULL CHECK (word_count >= 0),
            text_count INTEGER CHECK (text_count >= 0),
            provenance_json TEXT NOT NULL,
            reason_code TEXT COLLATE BINARY
        );
        CREATE TABLE scope_texts (
            scope_id TEXT NOT NULL COLLATE BINARY REFERENCES scope_descriptor(scope_id),
            text_guid TEXT NOT NULL COLLATE BINARY REFERENCES texts(text_guid),
            PRIMARY KEY (scope_id, text_guid)
        );
        CREATE TABLE scope_words (
            scope_id TEXT NOT NULL COLLATE BINARY REFERENCES scope_descriptor(scope_id),
            ordinal INTEGER NOT NULL CHECK (ordinal >= 0),
            form TEXT,
            writing_system TEXT COLLATE BINARY,
            wordform_guid TEXT COLLATE BINARY REFERENCES wordforms(wordform_guid),
            source_kind TEXT NOT NULL COLLATE BINARY CHECK (source_kind IN ('judged-wordform', 'text', 'typed')),
            PRIMARY KEY (scope_id, ordinal)
        );
        CREATE TABLE evidence_capability (
            capability TEXT PRIMARY KEY COLLATE BINARY,
            status TEXT NOT NULL CHECK (status IN ('complete', 'unavailable', 'not_requested')),
            reason TEXT COLLATE BINARY
        );
        CREATE TABLE parser_runs (
            assessment_id TEXT PRIMARY KEY COLLATE BINARY,
            invocation_id TEXT NOT NULL COLLATE BINARY,
            source_sha256 TEXT NOT NULL COLLATE BINARY,
            parser_sha256 TEXT NOT NULL COLLATE BINARY,
            model_fingerprint TEXT COLLATE BINARY,
            status TEXT NOT NULL CHECK (status IN ('complete', 'unavailable')),
            timeout_ms INTEGER CHECK (timeout_ms > 0),
            step_limit TEXT NOT NULL COLLATE BINARY,
            threads INTEGER NOT NULL CHECK (threads > 0),
            requested_case_count INTEGER NOT NULL CHECK (requested_case_count >= 0),
            completed_case_count INTEGER NOT NULL CHECK (completed_case_count >= 0),
            reason TEXT COLLATE BINARY,
            CHECK (completed_case_count <= requested_case_count)
        );
        CREATE TABLE parser_cases (
            assessment_id TEXT NOT NULL COLLATE BINARY REFERENCES parser_runs(assessment_id),
            case_key TEXT NOT NULL COLLATE BINARY,
            surface TEXT NOT NULL,
            surface_nfd TEXT NOT NULL,
            writing_system TEXT COLLATE BINARY,
            wordform_guid TEXT COLLATE BINARY REFERENCES wordforms(wordform_guid),
            completion TEXT NOT NULL CHECK (completion IN ('complete', 'incomplete')),
            identity_status TEXT NOT NULL CHECK (identity_status IN ('complete', 'unavailable')),
            reason TEXT COLLATE BINARY,
            capped INTEGER NOT NULL CHECK (capped IN (0, 1)),
            timed_out INTEGER NOT NULL CHECK (timed_out IN (0, 1)),
            invalid_shape INTEGER NOT NULL CHECK (invalid_shape IN (0, 1)),
            attempts INTEGER CHECK (attempts >= 0),
            passes INTEGER CHECK (passes >= 0),
            analysis_count INTEGER NOT NULL CHECK (analysis_count >= 0),
            PRIMARY KEY (assessment_id, case_key)
        );
        CREATE TABLE parser_analyses (
            assessment_id TEXT NOT NULL COLLATE BINARY,
            case_key TEXT NOT NULL COLLATE BINARY,
            ordinal INTEGER NOT NULL CHECK (ordinal >= 0),
            signature TEXT NOT NULL COLLATE BINARY,
            identity_status TEXT NOT NULL CHECK (identity_status IN ('complete', 'unavailable')),
            PRIMARY KEY (assessment_id, case_key, ordinal),
            FOREIGN KEY (assessment_id, case_key) REFERENCES parser_cases(assessment_id, case_key)
        );
        CREATE TABLE parser_analysis_morphs (
            assessment_id TEXT NOT NULL COLLATE BINARY,
            case_key TEXT NOT NULL COLLATE BINARY,
            analysis_ordinal INTEGER NOT NULL,
            morph_ordinal INTEGER NOT NULL CHECK (morph_ordinal >= 0),
            form_guid TEXT COLLATE BINARY,
            msa_guid TEXT COLLATE BINARY,
            infl_type_guid TEXT COLLATE BINARY,
            guessed_string_nfd TEXT,
            PRIMARY KEY (assessment_id, case_key, analysis_ordinal, morph_ordinal),
            FOREIGN KEY (assessment_id, case_key, analysis_ordinal)
                REFERENCES parser_analyses(assessment_id, case_key, ordinal)
        );
        CREATE TABLE parser_disapproved_morphologies (
            assessment_id TEXT NOT NULL COLLATE BINARY,
            case_key TEXT NOT NULL COLLATE BINARY,
            signature TEXT NOT NULL COLLATE BINARY,
            analysis_guids_json TEXT NOT NULL,
            identity_status TEXT NOT NULL CHECK (identity_status IN ('complete', 'unavailable')),
            reason TEXT COLLATE BINARY,
            PRIMARY KEY (assessment_id, case_key, signature),
            FOREIGN KEY (assessment_id, case_key) REFERENCES parser_cases(assessment_id, case_key)
        );
        CREATE TABLE parser_disapproved_matches (
            assessment_id TEXT NOT NULL COLLATE BINARY,
            case_key TEXT NOT NULL COLLATE BINARY,
            parser_analysis_ordinal INTEGER NOT NULL,
            disapproved_signature TEXT NOT NULL COLLATE BINARY,
            disapproved_analysis_guid TEXT NOT NULL COLLATE BINARY REFERENCES analyses(analysis_guid),
            PRIMARY KEY (assessment_id, case_key, parser_analysis_ordinal, disapproved_signature,
                disapproved_analysis_guid),
            FOREIGN KEY (assessment_id, case_key, parser_analysis_ordinal)
                REFERENCES parser_analyses(assessment_id, case_key, ordinal),
            FOREIGN KEY (assessment_id, case_key, disapproved_signature)
                REFERENCES parser_disapproved_morphologies(assessment_id, case_key, signature)
        );
        CREATE TABLE parser_reviewed_negative_cases (
            assessment_id TEXT NOT NULL COLLATE BINARY REFERENCES parser_runs(assessment_id),
            row_ordinal INTEGER NOT NULL CHECK (row_ordinal >= 0),
            case_id TEXT NOT NULL COLLATE BINARY,
            revision_id TEXT NOT NULL COLLATE BINARY,
            content_digest TEXT NOT NULL COLLATE BINARY,
            writing_system TEXT NOT NULL COLLATE BINARY,
            form_nfd TEXT NOT NULL,
            expectation_status TEXT NOT NULL COLLATE BINARY CHECK (expectation_status IN ('eligible', 'conflict')),
            case_key TEXT COLLATE BINARY,
            completion TEXT NOT NULL COLLATE BINARY CHECK (completion IN ('complete', 'incomplete', 'unavailable')),
            identity_status TEXT NOT NULL COLLATE BINARY CHECK (identity_status IN ('complete', 'unavailable')),
            accepted INTEGER NOT NULL CHECK (accepted IN (0, 1)),
            accepted_analysis_ordinals_json TEXT NOT NULL,
            reason TEXT COLLATE BINARY,
            PRIMARY KEY (assessment_id, row_ordinal),
            FOREIGN KEY (assessment_id, case_key) REFERENCES parser_cases(assessment_id, case_key)
        );
        CREATE TABLE human_judgment_metadata (
            singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
            projection_version INTEGER NOT NULL,
            source_project_id TEXT NOT NULL COLLATE BINARY,
            source_capture_digest TEXT NOT NULL COLLATE BINARY,
            capability TEXT NOT NULL COLLATE BINARY,
            capability_message TEXT NOT NULL,
            snapshot_digest TEXT NOT NULL COLLATE BINARY,
            projection_digest TEXT NOT NULL COLLATE BINARY,
            revision_count INTEGER NOT NULL CHECK (revision_count >= 0),
            head_count INTEGER NOT NULL CHECK (head_count >= 0),
            unavailable_count INTEGER NOT NULL CHECK (unavailable_count >= 0)
        );
        CREATE TABLE human_judgment_revisions (
            record_guid TEXT PRIMARY KEY COLLATE BINARY,
            judgment_id TEXT NOT NULL COLLATE BINARY,
            revision_id TEXT NOT NULL UNIQUE COLLATE BINARY,
            content_digest TEXT NOT NULL COLLATE BINARY,
            format TEXT NOT NULL COLLATE BINARY,
            version INTEGER NOT NULL,
            body_kind TEXT NOT NULL COLLATE BINARY,
            physical_value TEXT NOT NULL,
            judgment_json TEXT NOT NULL,
            state TEXT NOT NULL COLLATE BINARY CHECK (state IN ('head', 'superseded', 'unavailable'))
        );
        CREATE TABLE human_judgment_subjects (
            revision_id TEXT PRIMARY KEY COLLATE BINARY REFERENCES human_judgment_revisions(revision_id),
            measure_id TEXT NOT NULL COLLATE BINARY,
            subject_kind TEXT NOT NULL COLLATE BINARY,
            subject_key TEXT NOT NULL COLLATE BINARY,
            subject_json TEXT NOT NULL,
            evidence_digest TEXT NOT NULL COLLATE BINARY,
            evidence_contract TEXT NOT NULL COLLATE BINARY,
            disposition TEXT NOT NULL COLLATE BINARY,
            reason TEXT,
            question TEXT,
            subject_caption TEXT NOT NULL,
            measure_caption TEXT NOT NULL,
            source_report_id TEXT COLLATE BINARY,
            proposal_id TEXT COLLATE BINARY
        );
        CREATE TABLE human_judgment_lineage (
            revision_id TEXT NOT NULL COLLATE BINARY,
            predecessor_ordinal INTEGER NOT NULL CHECK (predecessor_ordinal >= 0),
            predecessor_revision_id TEXT NOT NULL COLLATE BINARY,
            predecessor_content_digest TEXT NOT NULL COLLATE BINARY,
            PRIMARY KEY (revision_id, predecessor_ordinal)
        );
        CREATE TABLE human_judgment_heads (
            judgment_id TEXT NOT NULL COLLATE BINARY,
            revision_id TEXT NOT NULL COLLATE BINARY,
            record_guid TEXT NOT NULL COLLATE BINARY,
            state TEXT NOT NULL COLLATE BINARY CHECK (state IN ('effective', 'conflict', 'unavailable')),
            issue TEXT,
            PRIMARY KEY (judgment_id, revision_id)
        );
        CREATE TABLE human_judgment_unavailable (
            record_guid TEXT NOT NULL COLLATE BINARY,
            judgment_id TEXT COLLATE BINARY,
            reason TEXT NOT NULL,
            physical_digest TEXT NOT NULL COLLATE BINARY,
            PRIMARY KEY (record_guid, reason)
        );
        CREATE INDEX ix_analyses_wordform_opinion ON analyses(wordform_guid, opinion);
        CREATE INDEX ix_analysis_morphs_morph_guid ON analysis_morphs(morph_guid);
        CREATE INDEX ix_analysis_morphs_msa_guid ON analysis_morphs(msa_guid);
        CREATE INDEX ix_analysis_morphs_infl_type_guid ON analysis_morphs(infl_type_guid);
        CREATE INDEX ix_analysis_morphs_entry_guid ON analysis_morphs(entry_guid);
        CREATE INDEX ix_analysis_morphs_sense_guid ON analysis_morphs(sense_guid);
        CREATE INDEX ix_scope_words_wordform_guid ON scope_words(scope_id, wordform_guid);
        CREATE INDEX ix_scope_words_form ON scope_words(scope_id, form);
        CREATE INDEX ix_parser_cases_wordform ON parser_cases(wordform_guid, completion);
        CREATE INDEX ix_parser_matches_signature ON parser_disapproved_matches
            (assessment_id, case_key, disapproved_signature);
        CREATE INDEX ix_occurrences_wordform_guid ON occurrences(wordform_guid);
        CREATE INDEX ix_human_judgment_subject_identity ON human_judgment_subjects
            (measure_id, subject_key, evidence_digest);
        CREATE INDEX ix_human_judgment_heads_id ON human_judgment_heads(judgment_id, state);
        """;
}
