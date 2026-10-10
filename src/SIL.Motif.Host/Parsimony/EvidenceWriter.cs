using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Host.Parsimony;

/// <summary>Writes and validates one closed, versioned evidence database.</summary>
public static class EvidenceWriter
{
    /// <summary>Writes a new evidence artifact and returns its final byte digest.</summary>
    public static ParsimonyArtifactDigest Write(string path, ParsimonyEvidenceProjection projection,
        string baselineTokenJson, string sourceSha256, string modelFingerprint,
        string inputKind = "baseline", string? candidateIdentity = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentException.ThrowIfNullOrWhiteSpace(baselineTokenJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelFingerprint);
        ValidateInputBinding(inputKind, candidateIdentity);
        if (File.Exists(path)) throw new IOException("The evidence output already exists.");

        try
        {
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = ParsimonySqlitePath.DataSource(path),
                ForeignKeys = true,
                Pooling = false,
            }.ToString()))
            {
                connection.Open();
                Execute(connection, "PRAGMA page_size=4096; PRAGMA encoding='UTF-8'; PRAGMA journal_mode=DELETE; " +
                    $"PRAGMA synchronous=FULL; PRAGMA application_id={EvidenceSchema.ApplicationId}; " +
                    $"PRAGMA user_version={EvidenceSchema.Version};");
                Execute(connection, EvidenceSchema.Ddl);
                using var transaction = connection.BeginTransaction();
                InsertMetadata(connection, transaction, projection, baselineTokenJson, sourceSha256,
                    modelFingerprint, inputKind, candidateIdentity, complete: false);
                InsertWordforms(connection, transaction, projection.Wordforms);
                InsertAnalyses(connection, transaction, projection.Wordforms);
                InsertTexts(connection, transaction, projection.Texts);
                InsertSegments(connection, transaction, projection.Segments);
                InsertOccurrences(connection, transaction, projection.Occurrences);
                InsertScopes(connection, transaction, projection.Scopes, projection.ScopeTexts, projection.ScopeWords);
                InsertCapabilities(connection, transaction, projection.Capabilities);
                InsertHumanJudgments(connection, transaction, projection.HumanJudgments, sourceSha256);
                InsertParserOverlay(connection, transaction, projection.ParserOverlay);
                using (var complete = connection.CreateCommand())
                {
                    complete.Transaction = transaction;
                    complete.CommandText = "UPDATE artifact_metadata SET complete=1 WHERE singleton=1;";
                    complete.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            Validate(path, baselineTokenJson, sourceSha256, modelFingerprint, inputKind, candidateIdentity);
            using (var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                file.Flush(flushToDisk: true);
            return new ParsimonyArtifactDigest(EvidenceSchema.Version, Digest(File.ReadAllBytes(path)));
        }
        catch
        {
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>
    /// Checks the file identity and schema generation only. Runs before any table is read, so an older
    /// layout is refused with the rebuild guidance rather than a missing-table error.
    /// </summary>
    public static void ValidateIdentity(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var connection = OpenReadOnly(path);
        RequireIdentity(connection);
    }

    /// <summary>Checks the artifact schema, Baseline binding, row counts, and SQLite integrity.</summary>
    public static void Validate(string path, string baselineTokenJson, string sourceSha256,
        string modelFingerprint, string inputKind = "baseline", string? candidateIdentity = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ValidateInputBinding(inputKind, candidateIdentity);
        using var connection = OpenReadOnly(path);
        RequireIdentity(connection);
        using (var metadata = connection.CreateCommand())
        {
            metadata.CommandText = "SELECT baseline_token_json, baseline_key, input_kind, candidate_identity, " +
                "source_sha256, model_fingerprint, " +
                "complete, text_count, occurrence_count, wordform_count, analysis_count, scope_count, " +
                "scope_word_count, capability_count, parser_run_count, parser_case_count, parser_analysis_count, " +
                "parser_morph_count, parser_disapproved_count, parser_match_count, " +
                "parser_reviewed_negative_count FROM artifact_metadata " +
                "WHERE singleton=1;";
            using var reader = metadata.ExecuteReader();
            if (!reader.Read()) throw new InvalidDataException("The evidence artifact has no metadata row.");
            var token = reader.GetString(0);
            var expectedKey = Digest(Encoding.UTF8.GetBytes(token));
            if (token != baselineTokenJson || reader.GetString(1) != expectedKey ||
                reader.GetString(2) != inputKind ||
                (reader.IsDBNull(3) ? null : reader.GetString(3)) != candidateIdentity ||
                reader.GetString(4) != sourceSha256 || reader.GetString(5) != modelFingerprint ||
                reader.GetInt32(6) != 1 || reader.GetInt64(7) != Count(connection, "texts") ||
                reader.GetInt64(8) != Count(connection, "occurrences") ||
                reader.GetInt64(9) != Count(connection, "wordforms") ||
                reader.GetInt64(10) != Count(connection, "analyses") ||
                reader.GetInt64(11) != Count(connection, "scope_descriptor") ||
                reader.GetInt64(12) != Count(connection, "scope_words") ||
                reader.GetInt64(13) != Count(connection, "evidence_capability") ||
                reader.GetInt64(14) != Count(connection, "parser_runs") ||
                reader.GetInt64(15) != Count(connection, "parser_cases") ||
                reader.GetInt64(16) != Count(connection, "parser_analyses") ||
                reader.GetInt64(17) != Count(connection, "parser_analysis_morphs") ||
                reader.GetInt64(18) != Count(connection, "parser_disapproved_morphologies") ||
                reader.GetInt64(19) != Count(connection, "parser_disapproved_matches") ||
                reader.GetInt64(20) != Count(connection, "parser_reviewed_negative_cases") || reader.Read())
                throw new InvalidDataException("The evidence metadata does not match the published rows or input.");
        }
        using (var judgments = connection.CreateCommand())
        {
            judgments.CommandText = "SELECT projection_version, source_capture_digest, projection_digest, " +
                "revision_count, head_count, unavailable_count FROM human_judgment_metadata WHERE singleton=1;";
            using var reader = judgments.ExecuteReader();
            if (!reader.Read())
                throw new InvalidDataException("The human-judgment metadata does not match its captured rows.");
            var version = reader.GetInt32(0);
            var captureDigest = reader.GetString(1);
            var projectionDigest = reader.GetString(2);
            var revisionCount = reader.GetInt64(3);
            var headCount = reader.GetInt64(4);
            var unavailableCount = reader.GetInt64(5);
            var duplicate = reader.Read();
            reader.Close();
            if (version != 1 || captureDigest != sourceSha256 || !IsLowerSha256(projectionDigest) || duplicate ||
                revisionCount != Count(connection, "human_judgment_revisions") ||
                headCount != Count(connection, "human_judgment_heads") ||
                unavailableCount != Count(connection, "human_judgment_unavailable"))
                throw new InvalidDataException("The human-judgment metadata does not match its captured rows.");
        }
        ValidateParserOverlayCapability(connection);
        if (!string.Equals(Scalar(connection, "PRAGMA integrity_check;") as string, "ok", StringComparison.Ordinal))
            throw new InvalidDataException("The evidence artifact failed SQLite integrity validation.");
        using var foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = "PRAGMA foreign_key_check;";
        using var violations = foreignKeys.ExecuteReader();
        if (violations.Read()) throw new InvalidDataException("The evidence artifact has broken local references.");
    }

    private static void InsertMetadata(SqliteConnection connection, SqliteTransaction transaction,
        ParsimonyEvidenceProjection projection, string baselineTokenJson, string sourceSha256,
        string modelFingerprint, string inputKind, string? candidateIdentity, bool complete)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO artifact_metadata " +
            "(singleton, application_id, schema_version, projection_version, baseline_token_json, baseline_key, " +
            "input_kind, candidate_identity, source_sha256, model_fingerprint, complete, text_count, occurrence_count, wordform_count, analysis_count, " +
            "scope_count, scope_word_count, capability_count, parser_run_count, parser_case_count, parser_analysis_count, " +
            "parser_morph_count, parser_disapproved_count, parser_match_count, parser_reviewed_negative_count) " +
            "VALUES (1, $app, $schema, 1, $token, $key, $inputKind, $candidate, $source, $fingerprint, $complete, $texts, $occurrences, " +
            "$wordforms, $analyses, $scopes, $scopeWords, $capabilities, $parserRuns, $parserCases, $parserAnalyses, " +
            "$parserMorphs, $parserDisapproved, $parserMatches, $parserReviewedNegatives);";
        command.Parameters.AddWithValue("$app", EvidenceSchema.ApplicationId);
        command.Parameters.AddWithValue("$schema", EvidenceSchema.Version);
        command.Parameters.AddWithValue("$token", baselineTokenJson);
        command.Parameters.AddWithValue("$key", Digest(Encoding.UTF8.GetBytes(baselineTokenJson)));
        command.Parameters.AddWithValue("$inputKind", inputKind);
        command.Parameters.AddWithValue("$candidate", (object?)candidateIdentity ?? DBNull.Value);
        command.Parameters.AddWithValue("$source", sourceSha256);
        command.Parameters.AddWithValue("$fingerprint", modelFingerprint);
        command.Parameters.AddWithValue("$complete", complete ? 1 : 0);
        command.Parameters.AddWithValue("$texts", projection.Texts.Count);
        command.Parameters.AddWithValue("$occurrences", projection.Occurrences.Count);
        command.Parameters.AddWithValue("$wordforms", projection.Wordforms.Count);
        command.Parameters.AddWithValue("$analyses", projection.Wordforms.Sum(wordform => wordform.Analyses.Count));
        command.Parameters.AddWithValue("$scopes", projection.Scopes.Count);
        command.Parameters.AddWithValue("$scopeWords", projection.ScopeWords.Count);
        command.Parameters.AddWithValue("$capabilities", projection.Capabilities.Count);
        command.Parameters.AddWithValue("$parserRuns", projection.ParserOverlay.Runs.Count);
        command.Parameters.AddWithValue("$parserCases", projection.ParserOverlay.Cases.Count);
        command.Parameters.AddWithValue("$parserAnalyses", projection.ParserOverlay.Analyses.Count);
        command.Parameters.AddWithValue("$parserMorphs", projection.ParserOverlay.Morphs.Count);
        command.Parameters.AddWithValue("$parserDisapproved", projection.ParserOverlay.DisapprovedMorphologies.Count);
        command.Parameters.AddWithValue("$parserMatches", projection.ParserOverlay.DisapprovedMatches.Count);
        command.Parameters.AddWithValue("$parserReviewedNegatives", projection.ParserOverlay.ReviewedNegativeCases.Count);
        command.ExecuteNonQuery();
    }

    private static void ValidateInputBinding(string inputKind, string? candidateIdentity)
    {
        if (inputKind is not ("baseline" or "candidate") ||
            (inputKind == "candidate") != !string.IsNullOrWhiteSpace(candidateIdentity))
            throw new ArgumentException("Candidate identity must be present exactly for candidate evidence.",
                nameof(candidateIdentity));
    }

    private static void InsertWordforms(SqliteConnection connection, SqliteTransaction transaction,
        IReadOnlyList<EvidenceWordform> wordforms)
    {
        using var wordformCommand = connection.CreateCommand();
        wordformCommand.Transaction = transaction;
        wordformCommand.CommandText = "INSERT INTO wordforms(wordform_guid, spelling_status) VALUES ($guid, $status);";
        var guid = wordformCommand.Parameters.Add("$guid", SqliteType.Text);
        var status = wordformCommand.Parameters.Add("$status", SqliteType.Integer);
        wordformCommand.Prepare();
        using var formCommand = connection.CreateCommand();
        formCommand.Transaction = transaction;
        formCommand.CommandText = "INSERT INTO wordform_forms(wordform_guid, writing_system, form, form_nfd) " +
            "VALUES ($guid, $ws, $form, $nfd);";
        var formGuid = formCommand.Parameters.Add("$guid", SqliteType.Text);
        var writingSystem = formCommand.Parameters.Add("$ws", SqliteType.Text);
        var formText = formCommand.Parameters.Add("$form", SqliteType.Text);
        var formNfd = formCommand.Parameters.Add("$nfd", SqliteType.Text);
        formCommand.Prepare();
        foreach (var wordform in wordforms)
        {
            guid.Value = GuidText(wordform.Guid);
            status.Value = wordform.SpellingStatus;
            wordformCommand.ExecuteNonQuery();
            foreach (var form in wordform.Forms)
            {
                if (string.IsNullOrWhiteSpace(form.WritingSystem)) continue;
                formGuid.Value = GuidText(wordform.Guid);
                writingSystem.Value = form.WritingSystem;
                formText.Value = form.Text;
                formNfd.Value = form.TextNfd;
                formCommand.ExecuteNonQuery();
            }
        }
    }

    private static void InsertAnalyses(SqliteConnection connection, SqliteTransaction transaction,
        IReadOnlyList<EvidenceWordform> wordforms)
    {
        using var analysisCommand = connection.CreateCommand();
        analysisCommand.Transaction = transaction;
        analysisCommand.CommandText = "INSERT INTO analyses " +
            "(analysis_guid, wordform_guid, opinion, source_kind, content_sha256) " +
            "VALUES ($guid, $wordform, $opinion, $source, $digest);";
        var guid = analysisCommand.Parameters.Add("$guid", SqliteType.Text);
        var wordform = analysisCommand.Parameters.Add("$wordform", SqliteType.Text);
        var opinion = analysisCommand.Parameters.Add("$opinion", SqliteType.Text);
        var source = analysisCommand.Parameters.Add("$source", SqliteType.Text);
        var digest = analysisCommand.Parameters.Add("$digest", SqliteType.Text);
        analysisCommand.Prepare();
        using var morphCommand = connection.CreateCommand();
        morphCommand.Transaction = transaction;
        morphCommand.CommandText = "INSERT INTO analysis_morphs " +
            "(analysis_guid, ordinal, morph_guid, msa_guid, infl_type_guid, entry_guid, sense_guid) " +
            "VALUES ($analysis, $ordinal, $morph, $msa, $infl, $entry, $sense);";
        var morphAnalysis = morphCommand.Parameters.Add("$analysis", SqliteType.Text);
        var ordinal = morphCommand.Parameters.Add("$ordinal", SqliteType.Integer);
        var morph = morphCommand.Parameters.Add("$morph", SqliteType.Text);
        var msa = morphCommand.Parameters.Add("$msa", SqliteType.Text);
        var inflType = morphCommand.Parameters.Add("$infl", SqliteType.Text);
        var entry = morphCommand.Parameters.Add("$entry", SqliteType.Text);
        var sense = morphCommand.Parameters.Add("$sense", SqliteType.Text);
        morphCommand.Prepare();
        using var formCommand = connection.CreateCommand();
        formCommand.Transaction = transaction;
        formCommand.CommandText = "INSERT INTO analysis_morph_forms " +
            "(analysis_guid, morph_ordinal, writing_system, form, form_nfd) " +
            "VALUES ($analysis, $ordinal, $ws, $form, $nfd);";
        var formAnalysis = formCommand.Parameters.Add("$analysis", SqliteType.Text);
        var formOrdinal = formCommand.Parameters.Add("$ordinal", SqliteType.Integer);
        var ws = formCommand.Parameters.Add("$ws", SqliteType.Text);
        var formText = formCommand.Parameters.Add("$form", SqliteType.Text);
        var formNfd = formCommand.Parameters.Add("$nfd", SqliteType.Text);
        formCommand.Prepare();
        using var textCommand = connection.CreateCommand();
        textCommand.Transaction = transaction;
        textCommand.CommandText = "INSERT INTO analysis_morph_texts " +
            "(analysis_guid, morph_ordinal, kind, writing_system, text, text_nfd) " +
            "VALUES ($analysis, $ordinal, $kind, $ws, $text, $nfd);";
        var textAnalysis = textCommand.Parameters.Add("$analysis", SqliteType.Text);
        var textOrdinal = textCommand.Parameters.Add("$ordinal", SqliteType.Integer);
        var textKind = textCommand.Parameters.Add("$kind", SqliteType.Text);
        var textWs = textCommand.Parameters.Add("$ws", SqliteType.Text);
        var textValue = textCommand.Parameters.Add("$text", SqliteType.Text);
        var textNfd = textCommand.Parameters.Add("$nfd", SqliteType.Text);
        textCommand.Prepare();
        foreach (var item in wordforms)
        foreach (var analysis in item.Analyses)
        {
            guid.Value = GuidText(analysis.Guid);
            wordform.Value = GuidText(item.Guid);
            opinion.Value = analysis.Opinion;
            source.Value = analysis.SourceKind;
            digest.Value = analysis.ContentSha256;
            analysisCommand.ExecuteNonQuery();
            for (var index = 0; index < analysis.Morphs.Count; index++)
            {
                var morphItem = analysis.Morphs[index];
                morphAnalysis.Value = GuidText(analysis.Guid);
                ordinal.Value = index;
                morph.Value = NullableGuid(morphItem.MorphGuid);
                msa.Value = NullableGuid(morphItem.MsaGuid);
                inflType.Value = NullableGuid(morphItem.InflTypeGuid);
                entry.Value = NullableGuid(morphItem.EntryGuid);
                sense.Value = NullableGuid(morphItem.SenseGuid);
                morphCommand.ExecuteNonQuery();
                foreach (var form in morphItem.Forms)
                {
                    if (string.IsNullOrWhiteSpace(form.WritingSystem)) continue;
                    formAnalysis.Value = GuidText(analysis.Guid);
                    formOrdinal.Value = index;
                    ws.Value = form.WritingSystem;
                    formText.Value = form.Text;
                    formNfd.Value = form.TextNfd;
                    formCommand.ExecuteNonQuery();
                }
                InsertMorphTexts(textCommand, textAnalysis, textOrdinal, textKind, textWs, textValue, textNfd,
                    analysis.Guid, index, "gloss", morphItem.Glosses);
                InsertMorphTexts(textCommand, textAnalysis, textOrdinal, textKind, textWs, textValue, textNfd,
                    analysis.Guid, index, "category", morphItem.Categories);
            }
        }
    }

    private static void InsertMorphTexts(SqliteCommand command, SqliteParameter analysis, SqliteParameter ordinal,
        SqliteParameter kind, SqliteParameter writingSystem, SqliteParameter text, SqliteParameter textNfd,
        Guid analysisGuid, int morphOrdinal, string textKind, IReadOnlyList<EvidenceForm> values)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value.WritingSystem)) continue;
            analysis.Value = GuidText(analysisGuid);
            ordinal.Value = morphOrdinal;
            kind.Value = textKind;
            writingSystem.Value = value.WritingSystem;
            text.Value = value.Text;
            textNfd.Value = value.TextNfd;
            command.ExecuteNonQuery();
        }
    }

    private static void InsertTexts(SqliteConnection connection, SqliteTransaction transaction,
        IReadOnlyList<EvidenceText> texts)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO texts(text_guid, title, title_ws) VALUES ($guid, $title, $ws);";
        var guid = command.Parameters.Add("$guid", SqliteType.Text);
        var title = command.Parameters.Add("$title", SqliteType.Text);
        var ws = command.Parameters.Add("$ws", SqliteType.Text);
        command.Prepare();
        foreach (var text in texts)
        {
            guid.Value = GuidText(text.Guid);
            title.Value = text.Title;
            ws.Value = (object?)text.WritingSystem ?? DBNull.Value;
            command.ExecuteNonQuery();
        }
    }

    private static void InsertSegments(SqliteConnection connection, SqliteTransaction transaction,
        IReadOnlyList<EvidenceSegment> segments)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO segments " +
            "(text_guid, paragraph_guid, segment_guid, line_number, sentence, sentence_ws, parse_current) " +
            "VALUES ($text, $paragraph, $segment, $line, $sentence, $ws, $current);";
        var text = command.Parameters.Add("$text", SqliteType.Text);
        var paragraph = command.Parameters.Add("$paragraph", SqliteType.Text);
        var segment = command.Parameters.Add("$segment", SqliteType.Text);
        var line = command.Parameters.Add("$line", SqliteType.Integer);
        var sentence = command.Parameters.Add("$sentence", SqliteType.Text);
        var ws = command.Parameters.Add("$ws", SqliteType.Text);
        var current = command.Parameters.Add("$current", SqliteType.Integer);
        command.Prepare();
        foreach (var item in segments)
        {
            text.Value = GuidText(item.TextGuid);
            paragraph.Value = GuidText(item.ParagraphGuid);
            segment.Value = GuidText(item.SegmentGuid);
            line.Value = item.LineNumber;
            sentence.Value = item.Sentence;
            ws.Value = (object?)item.WritingSystem ?? DBNull.Value;
            current.Value = item.ParseCurrent ? 1 : 0;
            command.ExecuteNonQuery();
        }
    }

    private static void InsertOccurrences(SqliteConnection connection, SqliteTransaction transaction,
        IReadOnlyList<EvidenceOccurrence> occurrences)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO occurrences " +
            "(text_guid, paragraph_guid, segment_guid, token_ordinal, wordform_guid, text, " +
            "status) VALUES ($text, $paragraph, $segment, $ordinal, $wordform, $token, $status);";
        var text = command.Parameters.Add("$text", SqliteType.Text);
        var paragraph = command.Parameters.Add("$paragraph", SqliteType.Text);
        var segment = command.Parameters.Add("$segment", SqliteType.Text);
        var ordinal = command.Parameters.Add("$ordinal", SqliteType.Integer);
        var wordform = command.Parameters.Add("$wordform", SqliteType.Text);
        var token = command.Parameters.Add("$token", SqliteType.Text);
        var status = command.Parameters.Add("$status", SqliteType.Text);
        command.Prepare();
        using var formCommand = connection.CreateCommand();
        formCommand.Transaction = transaction;
        formCommand.CommandText = "INSERT INTO occurrence_forms " +
            "(text_guid, segment_guid, token_ordinal, ordinal, writing_system, form, form_nfd) " +
            "VALUES ($text, $segment, $token_ordinal, $ordinal, $ws, $form, $nfd);";
        var formText = formCommand.Parameters.Add("$text", SqliteType.Text);
        var formSegment = formCommand.Parameters.Add("$segment", SqliteType.Text);
        var formToken = formCommand.Parameters.Add("$token_ordinal", SqliteType.Integer);
        var formOrdinal = formCommand.Parameters.Add("$ordinal", SqliteType.Integer);
        var formWs = formCommand.Parameters.Add("$ws", SqliteType.Text);
        var formValue = formCommand.Parameters.Add("$form", SqliteType.Text);
        var formNfd = formCommand.Parameters.Add("$nfd", SqliteType.Text);
        formCommand.Prepare();
        foreach (var item in occurrences)
        {
            text.Value = GuidText(item.TextGuid);
            paragraph.Value = GuidText(item.ParagraphGuid);
            segment.Value = GuidText(item.SegmentGuid);
            ordinal.Value = item.Ordinal;
            wordform.Value = NullableGuid(item.WordformGuid);
            token.Value = item.Text;
            status.Value = (object?)item.Status ?? DBNull.Value;
            command.ExecuteNonQuery();
            if (item.SelectedAnalysisGuid is { } selectedAnalysisGuid)
            {
                using var selected = connection.CreateCommand();
                selected.Transaction = transaction;
                selected.CommandText = "INSERT INTO occurrence_analyses " +
                    "(text_guid, segment_guid, token_ordinal, analysis_guid, role) " +
                    "VALUES ($text, $segment, $token, $analysis, 'chosen');";
                selected.Parameters.AddWithValue("$text", GuidText(item.TextGuid));
                selected.Parameters.AddWithValue("$segment", GuidText(item.SegmentGuid));
                selected.Parameters.AddWithValue("$token", item.Ordinal);
                selected.Parameters.AddWithValue("$analysis", GuidText(selectedAnalysisGuid));
                selected.ExecuteNonQuery();
            }
            for (var index = 0; index < item.Forms.Count; index++)
            {
                var form = item.Forms[index];
                formText.Value = GuidText(item.TextGuid);
                formSegment.Value = GuidText(item.SegmentGuid);
                formToken.Value = item.Ordinal;
                formOrdinal.Value = index;
                formWs.Value = (object?)form.WritingSystem ?? DBNull.Value;
                formValue.Value = form.Text;
                formNfd.Value = form.TextNfd;
                formCommand.ExecuteNonQuery();
            }
        }
    }

    private static void InsertScopes(SqliteConnection connection, SqliteTransaction transaction,
        IReadOnlyList<EvidenceScopeDescriptor> scopes, IReadOnlyList<EvidenceScopeText> texts,
        IReadOnlyList<EvidenceScopeWord> words)
    {
        using var scope = connection.CreateCommand();
        scope.Transaction = transaction;
        scope.CommandText = "INSERT INTO scope_descriptor(scope_id, status, name, selection_sha256, word_count, " +
            "text_count, provenance_json, reason_code) VALUES ($id, $status, $name, $digest, $words, $texts, $provenance, $reason);";
        var scopeId = scope.Parameters.Add("$id", SqliteType.Text);
        var status = scope.Parameters.Add("$status", SqliteType.Text);
        var name = scope.Parameters.Add("$name", SqliteType.Text);
        var digest = scope.Parameters.Add("$digest", SqliteType.Text);
        var wordCount = scope.Parameters.Add("$words", SqliteType.Integer);
        var textCount = scope.Parameters.Add("$texts", SqliteType.Integer);
        var provenance = scope.Parameters.Add("$provenance", SqliteType.Text);
        var reason = scope.Parameters.Add("$reason", SqliteType.Text);
        scope.Prepare();
        foreach (var item in scopes)
        {
            scopeId.Value = item.ScopeId;
            status.Value = item.Status;
            name.Value = (object?)item.Name ?? DBNull.Value;
            digest.Value = (object?)item.SelectionSha256 ?? DBNull.Value;
            wordCount.Value = item.WordCount;
            textCount.Value = (object?)item.TextCount ?? DBNull.Value;
            provenance.Value = item.ProvenanceJson;
            reason.Value = (object?)item.ReasonCode ?? DBNull.Value;
            scope.ExecuteNonQuery();
        }

        using var text = connection.CreateCommand();
        text.Transaction = transaction;
        text.CommandText = "INSERT INTO scope_texts(scope_id, text_guid) VALUES ($scope, $text);";
        var textScope = text.Parameters.Add("$scope", SqliteType.Text);
        var textGuid = text.Parameters.Add("$text", SqliteType.Text);
        text.Prepare();
        foreach (var item in texts)
        {
            textScope.Value = item.ScopeId;
            textGuid.Value = GuidText(item.TextGuid);
            text.ExecuteNonQuery();
        }

        using var word = connection.CreateCommand();
        word.Transaction = transaction;
        word.CommandText = "INSERT INTO scope_words(scope_id, ordinal, form, writing_system, wordform_guid, source_kind) " +
            "VALUES ($scope, $ordinal, $form, $ws, $wordform, $source);";
        var wordScope = word.Parameters.Add("$scope", SqliteType.Text);
        var ordinal = word.Parameters.Add("$ordinal", SqliteType.Integer);
        var form = word.Parameters.Add("$form", SqliteType.Text);
        var writingSystem = word.Parameters.Add("$ws", SqliteType.Text);
        var wordform = word.Parameters.Add("$wordform", SqliteType.Text);
        var source = word.Parameters.Add("$source", SqliteType.Text);
        word.Prepare();
        foreach (var item in words)
        {
            wordScope.Value = item.ScopeId;
            ordinal.Value = item.Ordinal;
            form.Value = (object?)item.Form ?? DBNull.Value;
            writingSystem.Value = (object?)item.WritingSystem ?? DBNull.Value;
            wordform.Value = NullableGuid(item.WordformGuid);
            source.Value = item.SourceKind;
            word.ExecuteNonQuery();
        }
    }

    private static void InsertCapabilities(SqliteConnection connection, SqliteTransaction transaction,
        IReadOnlyList<EvidenceCaptureCapability> capabilities)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO evidence_capability(capability, status, reason) VALUES ($name, $status, $reason);";
        var name = command.Parameters.Add("$name", SqliteType.Text);
        var status = command.Parameters.Add("$status", SqliteType.Text);
        var reason = command.Parameters.Add("$reason", SqliteType.Text);
        command.Prepare();
        foreach (var item in capabilities)
        {
            name.Value = item.Capability;
            status.Value = item.Status;
            reason.Value = (object?)item.Reason ?? DBNull.Value;
            command.ExecuteNonQuery();
        }
    }

    private static void InsertParserOverlay(SqliteConnection connection, SqliteTransaction transaction,
        ParserOverlayProjection overlay)
    {
        using var runs = connection.CreateCommand();
        runs.Transaction = transaction;
        runs.CommandText = "INSERT INTO parser_runs (assessment_id, invocation_id, source_sha256, parser_sha256, " +
            "model_fingerprint, status, timeout_ms, step_limit, threads, requested_case_count, completed_case_count, reason) " +
            "VALUES ($assessment, $invocation, $source, $parser, $model, $status, $timeout, $step, $threads, $requested, $completed, $reason);";
        foreach (var item in overlay.Runs)
        {
            runs.Parameters.Clear();
            runs.Parameters.AddWithValue("$assessment", item.AssessmentId);
            runs.Parameters.AddWithValue("$invocation", item.InvocationId);
            runs.Parameters.AddWithValue("$source", item.SourceSha256);
            runs.Parameters.AddWithValue("$parser", item.ParserSha256);
            runs.Parameters.AddWithValue("$model", (object?)item.ModelFingerprint ?? DBNull.Value);
            runs.Parameters.AddWithValue("$status", item.Status);
            runs.Parameters.AddWithValue("$timeout", (object?)item.TimeoutMs ?? DBNull.Value);
            runs.Parameters.AddWithValue("$step", item.StepLimit);
            runs.Parameters.AddWithValue("$threads", item.Threads);
            runs.Parameters.AddWithValue("$requested", item.RequestedCaseCount);
            runs.Parameters.AddWithValue("$completed", item.CompletedCaseCount);
            runs.Parameters.AddWithValue("$reason", (object?)item.Reason ?? DBNull.Value);
            runs.ExecuteNonQuery();
        }

        using var cases = connection.CreateCommand();
        cases.Transaction = transaction;
        cases.CommandText = "INSERT INTO parser_cases (assessment_id, case_key, surface, surface_nfd, writing_system, " +
            "wordform_guid, completion, identity_status, reason, capped, timed_out, invalid_shape, attempts, passes, analysis_count) " +
            "VALUES ($assessment, $key, $surface, $nfd, $ws, $wordform, $completion, $identity, $reason, $capped, $timeout, $invalid, $attempts, $passes, $count);";
        foreach (var item in overlay.Cases)
        {
            cases.Parameters.Clear();
            cases.Parameters.AddWithValue("$assessment", item.AssessmentId);
            cases.Parameters.AddWithValue("$key", item.CaseKey);
            cases.Parameters.AddWithValue("$surface", item.Surface);
            cases.Parameters.AddWithValue("$nfd", item.SurfaceNfd);
            cases.Parameters.AddWithValue("$ws", (object?)item.WritingSystem ?? DBNull.Value);
            cases.Parameters.AddWithValue("$wordform", (object?)item.WordformGuid ?? DBNull.Value);
            cases.Parameters.AddWithValue("$completion", item.Completion);
            cases.Parameters.AddWithValue("$identity", item.IdentityStatus);
            cases.Parameters.AddWithValue("$reason", (object?)item.Reason ?? DBNull.Value);
            cases.Parameters.AddWithValue("$capped", item.Capped ? 1 : 0);
            cases.Parameters.AddWithValue("$timeout", item.TimedOut ? 1 : 0);
            cases.Parameters.AddWithValue("$invalid", item.InvalidShape ? 1 : 0);
            cases.Parameters.AddWithValue("$attempts", (object?)item.Attempts ?? DBNull.Value);
            cases.Parameters.AddWithValue("$passes", (object?)item.Passes ?? DBNull.Value);
            cases.Parameters.AddWithValue("$count", item.AnalysisCount);
            cases.ExecuteNonQuery();
        }

        using var analyses = connection.CreateCommand();
        analyses.Transaction = transaction;
        analyses.CommandText = "INSERT INTO parser_analyses (assessment_id, case_key, ordinal, signature, identity_status) " +
            "VALUES ($assessment, $key, $ordinal, $signature, $identity);";
        foreach (var item in overlay.Analyses)
        {
            analyses.Parameters.Clear();
            analyses.Parameters.AddWithValue("$assessment", item.AssessmentId);
            analyses.Parameters.AddWithValue("$key", item.CaseKey);
            analyses.Parameters.AddWithValue("$ordinal", item.Ordinal);
            analyses.Parameters.AddWithValue("$signature", item.Signature);
            analyses.Parameters.AddWithValue("$identity", item.IdentityStatus);
            analyses.ExecuteNonQuery();
        }

        using var morphs = connection.CreateCommand();
        morphs.Transaction = transaction;
        morphs.CommandText = "INSERT INTO parser_analysis_morphs (assessment_id, case_key, analysis_ordinal, " +
            "morph_ordinal, form_guid, msa_guid, infl_type_guid, guessed_string_nfd) " +
            "VALUES ($assessment, $key, $analysis, $morph, $form, $msa, $infl, $guess);";
        foreach (var item in overlay.Morphs)
        {
            morphs.Parameters.Clear();
            morphs.Parameters.AddWithValue("$assessment", item.AssessmentId);
            morphs.Parameters.AddWithValue("$key", item.CaseKey);
            morphs.Parameters.AddWithValue("$analysis", item.AnalysisOrdinal);
            morphs.Parameters.AddWithValue("$morph", item.MorphOrdinal);
            morphs.Parameters.AddWithValue("$form", (object?)item.FormGuid ?? DBNull.Value);
            morphs.Parameters.AddWithValue("$msa", (object?)item.MsaGuid ?? DBNull.Value);
            morphs.Parameters.AddWithValue("$infl", (object?)item.InflTypeGuid ?? DBNull.Value);
            morphs.Parameters.AddWithValue("$guess", (object?)item.GuessedStringNfd ?? DBNull.Value);
            morphs.ExecuteNonQuery();
        }

        using var disapproved = connection.CreateCommand();
        disapproved.Transaction = transaction;
        disapproved.CommandText = "INSERT INTO parser_disapproved_morphologies (assessment_id, case_key, signature, " +
            "analysis_guids_json, identity_status, reason) VALUES ($assessment, $key, $signature, $guids, $identity, $reason);";
        foreach (var item in overlay.DisapprovedMorphologies)
        {
            disapproved.Parameters.Clear();
            disapproved.Parameters.AddWithValue("$assessment", item.AssessmentId);
            disapproved.Parameters.AddWithValue("$key", item.CaseKey);
            disapproved.Parameters.AddWithValue("$signature", item.Signature);
            disapproved.Parameters.AddWithValue("$guids", item.AnalysisGuidsJson);
            disapproved.Parameters.AddWithValue("$identity", item.IdentityStatus);
            disapproved.Parameters.AddWithValue("$reason", (object?)item.Reason ?? DBNull.Value);
            disapproved.ExecuteNonQuery();
        }

        using var matches = connection.CreateCommand();
        matches.Transaction = transaction;
        matches.CommandText = "INSERT INTO parser_disapproved_matches (assessment_id, case_key, " +
            "parser_analysis_ordinal, disapproved_signature, disapproved_analysis_guid) " +
            "VALUES ($assessment, $key, $analysis, $signature, $guid);";
        foreach (var item in overlay.DisapprovedMatches)
        {
            matches.Parameters.Clear();
            matches.Parameters.AddWithValue("$assessment", item.AssessmentId);
            matches.Parameters.AddWithValue("$key", item.CaseKey);
            matches.Parameters.AddWithValue("$analysis", item.ParserAnalysisOrdinal);
            matches.Parameters.AddWithValue("$signature", item.DisapprovedSignature);
            matches.Parameters.AddWithValue("$guid", item.DisapprovedAnalysisGuid);
            matches.ExecuteNonQuery();
        }

        using var reviewedNegatives = connection.CreateCommand();
        reviewedNegatives.Transaction = transaction;
        reviewedNegatives.CommandText = "INSERT INTO parser_reviewed_negative_cases (assessment_id, row_ordinal, " +
            "case_id, revision_id, content_digest, writing_system, form_nfd, expectation_status, case_key, " +
            "completion, identity_status, accepted, accepted_analysis_ordinals_json, reason) " +
            "VALUES ($assessment, $ordinal, $case, $revision, $digest, $ws, $form, $expectationStatus, " +
            "$caseKey, $completion, $identity, $accepted, $ordinals, $reason);";
        foreach (var (item, ordinal) in overlay.ReviewedNegativeCases.Select((item, index) => (item, index)))
        {
            reviewedNegatives.Parameters.Clear();
            reviewedNegatives.Parameters.AddWithValue("$assessment", item.AssessmentId);
            reviewedNegatives.Parameters.AddWithValue("$ordinal", ordinal);
            reviewedNegatives.Parameters.AddWithValue("$case", item.CaseId);
            reviewedNegatives.Parameters.AddWithValue("$revision", item.RevisionId);
            reviewedNegatives.Parameters.AddWithValue("$digest", item.ContentDigest);
            reviewedNegatives.Parameters.AddWithValue("$ws", item.WritingSystem);
            reviewedNegatives.Parameters.AddWithValue("$form", item.FormNfd);
            reviewedNegatives.Parameters.AddWithValue("$expectationStatus", item.ExpectationStatus);
            reviewedNegatives.Parameters.AddWithValue("$caseKey", (object?)item.CaseKey ?? DBNull.Value);
            reviewedNegatives.Parameters.AddWithValue("$completion", item.Completion);
            reviewedNegatives.Parameters.AddWithValue("$identity", item.IdentityStatus);
            reviewedNegatives.Parameters.AddWithValue("$accepted", item.Accepted ? 1 : 0);
            reviewedNegatives.Parameters.AddWithValue("$ordinals", item.AcceptedAnalysisOrdinalsJson);
            reviewedNegatives.Parameters.AddWithValue("$reason", (object?)item.Reason ?? DBNull.Value);
            reviewedNegatives.ExecuteNonQuery();
        }
    }

    private static void ValidateParserOverlayCapability(SqliteConnection connection)
    {
        var runCount = Count(connection, "parser_runs");
        var completeRunCount = Convert.ToInt64(Scalar(connection,
            "SELECT COUNT(*) FROM parser_runs WHERE status='complete';"),
            System.Globalization.CultureInfo.InvariantCulture);
        var status = Scalar(connection,
            "SELECT status FROM evidence_capability WHERE capability='parser-overlay';") as string;
        var expected = runCount == 0 ? "not_requested" : completeRunCount > 0 ? "complete" : "unavailable";
        if (!StringComparer.Ordinal.Equals(status, expected))
            throw new InvalidDataException("The parser-overlay capability does not match its captured runs.");
    }

    private static void InsertHumanJudgments(SqliteConnection connection, SqliteTransaction transaction,
        SIL.Motif.Projection.HumanJudgments.HumanJudgmentLineageProjection projection, string sourceCaptureDigest)
    {
        using (var metadata = connection.CreateCommand())
        {
            metadata.Transaction = transaction;
            metadata.CommandText = "INSERT INTO human_judgment_metadata " +
                "(singleton, projection_version, source_project_id, source_capture_digest, capability, " +
                "capability_message, snapshot_digest, projection_digest, revision_count, head_count, unavailable_count) " +
                "VALUES (1, 1, $project, $capture, $capability, $message, $snapshot, $projection, $revisions, $heads, $unavailable);";
            metadata.Parameters.AddWithValue("$project", projection.ProjectId);
            metadata.Parameters.AddWithValue("$capture", sourceCaptureDigest);
            metadata.Parameters.AddWithValue("$capability", projection.Capability);
            metadata.Parameters.AddWithValue("$message", projection.CapabilityMessage);
            metadata.Parameters.AddWithValue("$snapshot", projection.SnapshotDigest);
            metadata.Parameters.AddWithValue("$projection", projection.Digest);
            metadata.Parameters.AddWithValue("$revisions", projection.Revisions.Count);
            metadata.Parameters.AddWithValue("$heads", projection.Heads.Count);
            metadata.Parameters.AddWithValue("$unavailable", projection.Unavailable.Count);
            metadata.ExecuteNonQuery();
        }

        using var revision = connection.CreateCommand();
        revision.Transaction = transaction;
        revision.CommandText = "INSERT INTO human_judgment_revisions " +
            "(record_guid, judgment_id, revision_id, content_digest, format, version, body_kind, physical_value, " +
            "judgment_json, state) VALUES ($record, $judgment, $revision, $digest, $format, $version, $kind, " +
            "$physical, $json, $state);";
        var recordId = revision.Parameters.Add("$record", SqliteType.Text);
        var judgmentId = revision.Parameters.Add("$judgment", SqliteType.Text);
        var revisionId = revision.Parameters.Add("$revision", SqliteType.Text);
        var contentDigest = revision.Parameters.Add("$digest", SqliteType.Text);
        var format = revision.Parameters.Add("$format", SqliteType.Text);
        var version = revision.Parameters.Add("$version", SqliteType.Integer);
        var bodyKind = revision.Parameters.Add("$kind", SqliteType.Text);
        var physicalValue = revision.Parameters.Add("$physical", SqliteType.Text);
        var judgmentJson = revision.Parameters.Add("$json", SqliteType.Text);
        var state = revision.Parameters.Add("$state", SqliteType.Text);
        revision.Prepare();

        using var lineage = connection.CreateCommand();
        lineage.Transaction = transaction;
        lineage.CommandText = "INSERT INTO human_judgment_lineage " +
            "(revision_id, predecessor_ordinal, predecessor_revision_id, predecessor_content_digest) " +
            "VALUES ($revision, $ordinal, $predecessor, $digest);";
        var lineageRevision = lineage.Parameters.Add("$revision", SqliteType.Text);
        var ordinal = lineage.Parameters.Add("$ordinal", SqliteType.Integer);
        var predecessorId = lineage.Parameters.Add("$predecessor", SqliteType.Text);
        var predecessorDigest = lineage.Parameters.Add("$digest", SqliteType.Text);
        lineage.Prepare();

        using var subject = connection.CreateCommand();
        subject.Transaction = transaction;
        subject.CommandText = "INSERT INTO human_judgment_subjects " +
            "(revision_id, measure_id, subject_kind, subject_key, subject_json, evidence_digest, evidence_contract, " +
            "disposition, reason, question, subject_caption, measure_caption, source_report_id, proposal_id) " +
            "VALUES ($revision, $measure, $subjectKind, $subjectKey, $subjectJson, $evidence, $contract, $disposition, " +
            "$reason, $question, $subjectCaption, $measureCaption, $report, $proposal);";

        foreach (var item in projection.Revisions)
        {
            recordId.Value = item.RecordId;
            judgmentId.Value = item.JudgmentId;
            revisionId.Value = item.RevisionId;
            contentDigest.Value = item.ContentDigest;
            format.Value = item.Format;
            version.Value = item.Version;
            bodyKind.Value = item.BodyKind;
            physicalValue.Value = item.PhysicalValue;
            judgmentJson.Value = HumanJudgmentCodec.ToJson(item.Judgment);
            state.Value = item.State;
            revision.ExecuteNonQuery();

            foreach (var parent in item.Judgment.Replaces.Select((value, index) => (value, index)))
            {
                lineageRevision.Value = item.RevisionId;
                ordinal.Value = parent.index;
                predecessorId.Value = parent.value.RevisionId;
                predecessorDigest.Value = parent.value.ContentDigest;
                lineage.ExecuteNonQuery();
            }

            if (item.Judgment.Body is not DispositionJudgment disposition) continue;
            var subjectKind = disposition.Subject switch
            {
                ObjectJudgmentSubject => "object",
                ProjectJudgmentSubject => "project",
                EdgeJudgmentSubject => "edge",
                GroupJudgmentSubject => "group",
                _ => "unknown",
            };
            subject.Parameters.Clear();
            subject.Parameters.AddWithValue("$revision", item.RevisionId);
            subject.Parameters.AddWithValue("$measure", disposition.MeasureId);
            subject.Parameters.AddWithValue("$subjectKind", subjectKind);
            subject.Parameters.AddWithValue("$subjectKey", HumanJudgmentCodec.SubjectKey(disposition.Subject));
            subject.Parameters.AddWithValue("$subjectJson", JsonSerializer.Serialize(disposition.Subject,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            subject.Parameters.AddWithValue("$evidence", disposition.EvidenceDigest);
            subject.Parameters.AddWithValue("$contract", disposition.EvidenceContract);
            subject.Parameters.AddWithValue("$disposition", disposition.Disposition.ToString().ToLowerInvariant());
            subject.Parameters.AddWithValue("$reason", (object?)item.Judgment.Reason ?? DBNull.Value);
            subject.Parameters.AddWithValue("$question", (object?)disposition.Question ?? DBNull.Value);
            subject.Parameters.AddWithValue("$subjectCaption", disposition.SubjectCaption);
            subject.Parameters.AddWithValue("$measureCaption", disposition.MeasureCaption);
            subject.Parameters.AddWithValue("$report", (object?)item.Judgment.Source?.ReportId ?? DBNull.Value);
            subject.Parameters.AddWithValue("$proposal", (object?)item.Judgment.Source?.ProposalId ?? DBNull.Value);
            subject.ExecuteNonQuery();
        }

        using var head = connection.CreateCommand();
        head.Transaction = transaction;
        head.CommandText = "INSERT INTO human_judgment_heads " +
            "(judgment_id, revision_id, record_guid, state, issue) VALUES ($judgment, $revision, $record, $state, $issue);";
        foreach (var item in projection.Heads)
        {
            head.Parameters.Clear();
            head.Parameters.AddWithValue("$judgment", item.JudgmentId);
            head.Parameters.AddWithValue("$revision", item.RevisionId);
            head.Parameters.AddWithValue("$record", item.RecordId);
            head.Parameters.AddWithValue("$state", item.State);
            head.Parameters.AddWithValue("$issue", (object?)item.Issue ?? DBNull.Value);
            head.ExecuteNonQuery();
        }

        using var invalid = connection.CreateCommand();
        invalid.Transaction = transaction;
        invalid.CommandText = "INSERT INTO human_judgment_unavailable " +
            "(record_guid, judgment_id, reason, physical_digest) VALUES ($record, $judgment, $reason, $digest);";
        foreach (var item in projection.Unavailable)
        {
            invalid.Parameters.Clear();
            invalid.Parameters.AddWithValue("$record", item.RecordId);
            invalid.Parameters.AddWithValue("$judgment", (object?)item.JudgmentId ?? DBNull.Value);
            invalid.Parameters.AddWithValue("$reason", item.Reason);
            invalid.Parameters.AddWithValue("$digest", item.PhysicalDigest);
            invalid.ExecuteNonQuery();
        }
    }

    private static long Count(SqliteConnection connection, string table) =>
        Convert.ToInt64(Scalar(connection, $"SELECT COUNT(*) FROM {table};"), System.Globalization.CultureInfo.InvariantCulture);

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = ParsimonySqlitePath.DataSource(path),
            Mode = SqliteOpenMode.ReadOnly,
            ForeignKeys = true,
            Pooling = false,
        }.ToString());
        try
        {
            connection.Open();
            return connection;
        }
        catch (SqliteException exception)
        {
            connection.Dispose();
            var fullPath = Path.GetFullPath(path);
            var parent = Path.GetDirectoryName(fullPath)!;
            var mode = OperatingSystem.IsWindows() ? "unknown" :
                Convert.ToString((int)File.GetUnixFileMode(parent), 8);
            throw new InvalidDataException(
                $"The Parsimony evidence artifact '{fullPath}' could not be opened read-only " +
                $"(SQLite {exception.SqliteErrorCode}/{exception.SqliteExtendedErrorCode}, " +
                $"file exists={File.Exists(fullPath)}, parent mode={mode}).",
                exception);
        }
    }

    private static void RequireIdentity(SqliteConnection connection)
    {
        if (ReadPragma(connection, "application_id") != EvidenceSchema.ApplicationId ||
            ReadPragma(connection, "user_version") != EvidenceSchema.Version)
            throw new InvalidDataException("The evidence artifact has an unsupported file identity or schema. " +
                "Delete this Parsimony evidence and run the measure again; Motif rebuilds it.");
    }

    private static long ReadPragma(SqliteConnection connection, string name) =>
        Convert.ToInt64(Scalar(connection, $"PRAGMA {name};"), System.Globalization.CultureInfo.InvariantCulture);

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string GuidText(Guid value) => value.ToString("D").ToLowerInvariant();
    private static object NullableGuid(Guid? value) => value is { } guid ? GuidText(guid) : DBNull.Value;
    private static string Digest(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    private static bool IsLowerSha256(string value) => value.Length == 64 && value.All(character =>
        (character is >= '0' and <= '9') || (character is >= 'a' and <= 'f'));
}
