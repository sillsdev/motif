namespace SIL.Motif.Tests.Worker;

/// <summary>
/// The v8 compiled-output rows that fixtures need before an allomorph order or a lineage row can point at them.
/// </summary>
/// <remarks>
/// A compiled output is named by its key, so a fixture ensures the owner and output rows first and then refers to
/// them by key. Each statement is concatenated with the order or mapping row it supports.
/// </remarks>
internal static class CompiledOutputSql
{
    internal const string EnsureOwner =
        "INSERT OR IGNORE INTO compiled_output(output_id, kind, key, owner_output_id, stratum_key, bucket, " +
        "compiled_order, identity_quality, realization_kind, has_phone_condition, has_morph_gate, gate_signature, " +
        "is_unconditioned) VALUES ((SELECT COALESCE(MAX(output_id), 0) + 1 FROM compiled_output), 'lex_entry', " +
        "$owner, NULL, NULL, $bucket, NULL, 'structural', NULL, NULL, NULL, NULL, NULL);";

    internal const string EnsureAllomorphOutput =
        "INSERT OR IGNORE INTO compiled_output(output_id, kind, key, owner_output_id, stratum_key, bucket, " +
        "compiled_order, identity_quality, realization_kind, has_phone_condition, has_morph_gate, gate_signature, " +
        "is_unconditioned) VALUES ((SELECT COALESCE(MAX(output_id), 0) + 1 FROM compiled_output), 'allomorph', " +
        "$output, (SELECT output_id FROM compiled_output WHERE key=$owner), NULL, $bucket, $order, 'structural', " +
        "'segments', $phone, $gate, CASE WHEN $gate=1 THEN 'mpr=1;xmpr=;fs=[];stem=' ELSE 'mpr=;xmpr=;fs=[];stem=' END, " +
        "CASE WHEN $phone=0 AND $gate=0 THEN 1 ELSE 0 END);";

    // The same allomorph row with its gate text supplied, for a fixture whose gates must differ.
    internal const string EnsureAllomorphOutputWithSignature =
        "INSERT OR IGNORE INTO compiled_output(output_id, kind, key, owner_output_id, stratum_key, bucket, " +
        "compiled_order, identity_quality, realization_kind, has_phone_condition, has_morph_gate, gate_signature, " +
        "is_unconditioned) VALUES ((SELECT COALESCE(MAX(output_id), 0) + 1 FROM compiled_output), 'allomorph', " +
        "$output, (SELECT output_id FROM compiled_output WHERE key=$owner), NULL, $bucket, $order, 'structural', " +
        "'segments', $phone, $gate, $signature, CASE WHEN $phone=0 AND $gate=0 THEN 1 ELSE 0 END);";

    internal const string EnsureRuleOutput =
        "INSERT OR IGNORE INTO compiled_output(output_id, kind, key, owner_output_id, stratum_key, bucket, " +
        "compiled_order, identity_quality, realization_kind, has_phone_condition, has_morph_gate, gate_signature, " +
        "is_unconditioned) VALUES ((SELECT COALESCE(MAX(output_id), 0) + 1 FROM compiled_output), 'morph_rule', " +
        "$output, NULL, NULL, 'Morphology', NULL, 'structural', NULL, NULL, NULL, NULL, NULL);";

    internal const string OrderRow =
        "INSERT INTO compiled_allomorph_order(owner_output_id, source_entry_guid, source_msa_guid, " +
        "source_allomorph_key, source_allomorph_guid, output_id, compiled_order) VALUES " +
        "((SELECT output_id FROM compiled_output WHERE key=$owner), $entry, $msa, $sourceKey, $allomorph, " +
        "(SELECT output_id FROM compiled_output WHERE key=$output), $order);";

    // A candidate with no compiled output: its order is NULL and it names no output row.
    internal const string CandidateOrderRow =
        "INSERT INTO compiled_allomorph_order(owner_output_id, source_entry_guid, source_msa_guid, " +
        "source_allomorph_key, source_allomorph_guid, output_id, compiled_order) VALUES " +
        "((SELECT output_id FROM compiled_output WHERE key=$owner), $entry, $msa, $sourceKey, $allomorph, NULL, NULL);";

    internal const string MappingRow =
        "INSERT INTO compiled_mapping(source_kind, source_guid, source_key, output_id, relation_role, " +
        "source_ordinal, identity_quality) VALUES ($kind, $guid, $key, " +
        "(SELECT output_id FROM compiled_output WHERE key=$output), $role, 0, $quality);";
}
