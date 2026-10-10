using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.LCModel;

namespace SIL.Motif.Host.Parsimony;

public sealed partial class ParsimonyQuerySession
{
    internal AlternationDiscoveryResult ReadAlternationFamilies() =>
        AlternationDiscovery.Analyze(ReadAlternationInput());

    internal AlternationInput ReadAlternationInput()
    {
        RequireSections("allomorphs", "entries", "msas", "features", "phonology", "environments", "patterns",
            "compiled_mappings");
        var allomorphs = ReadAllomorphsForMeasure();
        var phonemes = ReadPhonemes();
        var entries = ReadAlternationEntries(allomorphs);
        return new AlternationInput(ReadRelevantFeatureCount(), phonemes, entries,
            ReadExistingRewriteRules(), ReadSectionStatus("phonology") == "complete" &&
            ReadSectionStatus("patterns") == "complete" && ReadSectionStatus("load_accounting") == "complete");
    }

    private IReadOnlyList<PhonemeFacts> ReadPhonemes()
    {
        var featureIds = ReadRelevantFeatures();
        var featureValues = ReadFeatureValues();
        var assignments = ReadFeatureAssignments();
        var rows = new Dictionary<string, PhonemeBuilder>(StringComparer.Ordinal);
        using var command = NewCommand();
        command.CommandText = """
            SELECT p.guid, p.name, p.feature_structure_id, g.writing_system, g.grapheme
            FROM facts.phoneme AS p
            LEFT JOIN facts.phoneme_grapheme AS g ON g.phoneme_guid=p.guid
            ORDER BY p.guid, g.writing_system, g.ordinal;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var guid = reader.GetString(0);
            if (!rows.TryGetValue(guid, out var phoneme))
            {
                phoneme = new PhonemeBuilder(guid, reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetInt32(2));
                rows.Add(guid, phoneme);
            }
            if (!reader.IsDBNull(3))
                phoneme.Graphemes.Add(new PhonemeGraphemeFact(guid, reader.GetString(3), reader.GetString(4)));
        }
        return Array.AsReadOnly(rows.Values.OrderBy(item => item.Guid, StringComparer.Ordinal)
            .Select(item => item.Build(featureIds, featureValues, assignments)).ToArray());
    }

    private HashSet<string> ReadRelevantFeatures()
    {
        using var command = NewCommand();
        command.CommandText = "SELECT guid FROM facts.feature WHERE system='phonological' AND kind='closed' ORDER BY guid;";
        using var reader = command.ExecuteReader();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read()) ids.Add(reader.GetString(0));
        return ids;
    }

    private Dictionary<string, HashSet<string>> ReadFeatureValues()
    {
        using var command = NewCommand();
        command.CommandText = "SELECT feature_guid, guid FROM facts.feature_value ORDER BY feature_guid, ordinal;";
        using var reader = command.ExecuteReader();
        var values = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        while (reader.Read())
        {
            var feature = reader.GetString(0);
            if (!values.TryGetValue(feature, out var featureValues))
                values[feature] = featureValues = new HashSet<string>(StringComparer.Ordinal);
            featureValues.Add(reader.GetString(1));
        }
        return values;
    }

    private Dictionary<int, IReadOnlyList<FeatureAssignmentFact>> ReadFeatureAssignments()
    {
        using var command = NewCommand();
        command.CommandText = "SELECT fs_id, feature_guid, value_kind, value_guid, child_fs_id " +
            "FROM facts.feature_assignment ORDER BY fs_id, ordinal;";
        using var reader = command.ExecuteReader();
        var values = new Dictionary<int, List<FeatureAssignmentFact>>();
        while (reader.Read())
        {
            var fsId = reader.GetInt32(0);
            if (!values.TryGetValue(fsId, out var assignments)) values[fsId] = assignments = [];
            assignments.Add(new FeatureAssignmentFact(reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetInt32(4)));
        }
        return values.ToDictionary(pair => pair.Key,
            pair => (IReadOnlyList<FeatureAssignmentFact>)Array.AsReadOnly(pair.Value.ToArray()));
    }

    private IReadOnlyList<AlternationEntryFacts> ReadAlternationEntries(
        IReadOnlyList<AllomorphMeasureFact> allomorphs)
    {
        var writingSystems = ReadVernacularWritingSystems();
        var msas = ReadMsaGateSignatures();
        var conditions = ReadAllomorphConditions(allomorphs);
        var gates = ReadAllomorphGates();
        var entries = new List<AlternationEntryFacts>();
        foreach (var group in allomorphs.GroupBy(item => item.EntryGuid, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var allomorphFacts = group.Select(item => new AlternationAllomorphFacts(item.Guid, item.MorphType,
                item.Forms.Where(form => writingSystems.Contains(form.Key, StringComparer.Ordinal))
                    .ToDictionary(form => form.Key, form => form.Value, StringComparer.Ordinal),
                conditions.GetValueOrDefault(item.Guid) ?? new AlternationConditionFacts(false, false, false, []),
                gates.GetValueOrDefault(item.Guid)))
                .ToArray();
            var msaGates = msas.GetValueOrDefault(group.Key) ?? [];
            entries.Add(new AlternationEntryFacts(group.Key, DescribeObject(group.Key) ?? group.Key,
                Array.AsReadOnly(msaGates.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()),
                Array.AsReadOnly(allomorphFacts)));
        }
        return Array.AsReadOnly(entries.ToArray());
    }

    // Absent when the compiler did not represent the allomorph; disagreeing compiled outputs are Conflicting.
    private Dictionary<string, AlternationGateFacts?> ReadAllomorphGates()
    {
        var signatures = new Dictionary<string, string?>(StringComparer.Ordinal);
        using (var command = NewCommand())
        {
            command.CommandText = "SELECT o.source_allomorph_guid, out.gate_signature " +
                "FROM facts.compiled_allomorph_order AS o " +
                "JOIN facts.compiled_output AS out ON out.output_id=o.output_id " +
                "WHERE out.kind='allomorph' AND o.source_allomorph_guid IS NOT NULL " +
                "ORDER BY o.source_allomorph_guid, out.output_id;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var guid = reader.GetString(0);
                var signature = reader.IsDBNull(1) ? null : reader.GetString(1);
                signatures[guid] = !signatures.TryGetValue(guid, out var existing) ? signature
                    : existing == signature ? existing : null;
            }
        }
        var classes = ReadAppliedGateTargets("inflection_class");
        var features = ReadAppliedGateFeatureStructures();
        var result = new Dictionary<string, AlternationGateFacts?>(StringComparer.Ordinal);
        foreach (var (guid, signature) in signatures)
            result[guid] = signature is null
                ? new AlternationGateFacts(string.Empty, string.Empty, Conflicting: true)
                : DescribeAllomorphGate(signature, classes.GetValueOrDefault(guid) ?? [],
                    features.GetValueOrDefault(guid) ?? []) ?? new AlternationGateFacts(string.Empty, string.Empty, true);
        return result;
    }

    private Dictionary<string, List<string>> ReadAppliedGateTargets(string gateKind)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        using var command = NewCommand();
        command.CommandText = "SELECT allomorph_guid, target_guid FROM facts.allomorph_gate " +
            "WHERE gate_kind=$kind AND parser_effect='applied' AND target_guid IS NOT NULL ORDER BY allomorph_guid, ordinal;";
        command.Parameters.AddWithValue("$kind", gateKind);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var guid = reader.GetString(0);
            if (!result.TryGetValue(guid, out var targets)) result[guid] = targets = [];
            targets.Add(reader.GetString(1));
        }
        return result;
    }

    private Dictionary<string, List<int>> ReadAppliedGateFeatureStructures()
    {
        var result = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        using var command = NewCommand();
        command.CommandText = "SELECT allomorph_guid, fs_id FROM facts.allomorph_gate " +
            "WHERE gate_kind='required_features' AND parser_effect='applied' AND fs_id IS NOT NULL " +
            "ORDER BY allomorph_guid, ordinal;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var guid = reader.GetString(0);
            if (!result.TryGetValue(guid, out var fsIds)) result[guid] = fsIds = [];
            fsIds.Add(reader.GetInt32(1));
        }
        return result;
    }

    // MPR ids and FeatIds are grammar-local, with no facts table to name them, so they are described loosely.
    private AlternationGateFacts? DescribeAllomorphGate(string signature, IReadOnlyList<string> classGuids,
        IReadOnlyList<int> featureStructureIds)
    {
        var parts = signature.Split(';');
        if (parts.Length != 4 || !parts[3].StartsWith("stem=", StringComparison.Ordinal)) return null;
        var hasMpr = parts[0] != "mpr=";
        var hasExcludedMpr = parts[1] != "xmpr=";
        var hasFeatures = parts[2] is not ("fs=" or "fs=[]");
        var stem = Uri.UnescapeDataString(parts[3]["stem=".Length..]);
        var orderedClasses = classGuids.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (!hasMpr && !hasExcludedMpr && !hasFeatures && stem.Length == 0 && orderedClasses.Length == 0)
            return new AlternationGateFacts(string.Empty, string.Empty);
        var described = new List<string>();
        foreach (var classGuid in orderedClasses)
            described.Add("class " + (ReadName("SELECT coalesce(nullif(trim(name), ''), " +
                "nullif(trim(abbreviation), '')) FROM facts.inflection_class WHERE guid=$guid;", classGuid)
                ?? "inflection class"));
        if (hasMpr) described.Add("morphological rule");
        if (hasExcludedMpr) described.Add("excluded morphological rule");
        if (hasFeatures)
        {
            var assignments = featureStructureIds.SelectMany(ReadFeatureAssignmentNames).ToArray();
            described.Add(assignments.Length == 0 ? "feature condition" : "features " + string.Join(", ", assignments));
        }
        if (stem.Length > 0) described.Add("stem " + stem);
        return new AlternationGateFacts(signature, string.Join(" and ", described));
    }

    private IEnumerable<string> ReadFeatureAssignmentNames(int featureStructureId)
    {
        using var command = NewCommand();
        command.CommandText = "SELECT f.name, a.value_kind, v.name FROM facts.feature_assignment AS a " +
            "LEFT JOIN facts.feature AS f ON f.guid=a.feature_guid " +
            "LEFT JOIN facts.feature_value AS v ON v.guid=a.value_guid " +
            "WHERE a.fs_id=$fs ORDER BY a.ordinal;";
        command.Parameters.AddWithValue("$fs", featureStructureId);
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            var feature = NullableString(reader, 0) ?? "feature";
            names.Add(reader.GetString(1) == "closed" ? feature + "=" + (NullableString(reader, 2) ?? "value")
                : feature);
        }
        return names;
    }

    private IReadOnlyList<string> ReadVernacularWritingSystems()
    {
        using var command = NewCommand();
        command.CommandText = "SELECT writing_system_tag FROM facts.project_writing_system " +
            "WHERE role='vernacular' ORDER BY ordinal;";
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read()) values.Add(reader.GetString(0));
        return Array.AsReadOnly(values.ToArray());
    }

    private Dictionary<string, List<string>> ReadMsaGateSignatures()
    {
        var msaRows = new Dictionary<string, MsaGateBuilder>(StringComparer.Ordinal);
        using (var command = NewCommand())
        {
            command.CommandText = "SELECT msa_guid, entry_guid, kind FROM facts.msa ORDER BY entry_guid, msa_guid;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var msa = new MsaGateBuilder(reader.GetString(0), reader.GetString(1), reader.GetString(2));
                msaRows.Add(msa.Guid, msa);
            }
        }
        ReadMsaReferences("SELECT msa_guid, role, ordinal, category_guid FROM facts.msa_category " +
            "ORDER BY msa_guid, role, ordinal;", msaRows, (msa, reader) =>
                msa.Categories.Add((reader.GetString(1), reader.GetInt32(2), reader.GetString(3))));
        ReadMsaReferences("SELECT msa_guid, role, ordinal, slot_guid FROM facts.msa_slot " +
            "ORDER BY msa_guid, role, ordinal;", msaRows, (msa, reader) =>
                msa.Slots.Add((reader.GetString(1), reader.GetInt32(2), reader.GetString(3))));
        ReadMsaReferences("SELECT msa_guid, role, class_guid FROM facts.msa_inflection_class " +
            "ORDER BY msa_guid, role;", msaRows, (msa, reader) =>
                msa.Classes.Add((reader.GetString(1), reader.GetString(2))));
        ReadMsaReferences("SELECT msa_guid, role, stem_name_guid FROM facts.msa_stem_name " +
            "ORDER BY msa_guid, role;", msaRows, (msa, reader) =>
                msa.StemNames.Add((reader.GetString(1), reader.GetString(2))));
        ReadMsaReferences("SELECT msa_guid, role, ordinal, target_guid FROM facts.msa_exception_feature " +
            "ORDER BY msa_guid, role, ordinal;", msaRows, (msa, reader) =>
                msa.ExceptionFeatures.Add((reader.GetString(1), reader.GetInt32(2), reader.GetString(3))));
        foreach (var structure in ReadMsaFeatureStructureTexts(null))
            if (msaRows.TryGetValue(structure.MsaGuid, out var msa))
                msa.FeatureStructures.Add((structure.Role, structure.Json));
        return msaRows.Values.GroupBy(item => item.EntryGuid, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Signature)
                .Order(StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
    }

    private void ReadMsaReferences(string sql, IReadOnlyDictionary<string, MsaGateBuilder> rows,
        Action<MsaGateBuilder, SqliteDataReader> add)
    {
        using var command = NewCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (rows.TryGetValue(reader.GetString(0), out var msa)) add(msa, reader);
    }

    private Dictionary<string, AlternationConditionFacts> ReadAllomorphConditions(
        IReadOnlyList<AllomorphMeasureFact> allomorphs)
    {
        var result = new Dictionary<string, AlternationConditionFacts>(StringComparer.Ordinal);
        foreach (var allomorph in allomorphs)
        {
            var phone = new List<ResolvedEnvironmentFacts>();
            var known = true;
            foreach (var edge in allomorph.Environments)
            {
                if (edge.Role != "phone") continue;
                var resolution = ReadResolvedEnvironment(allomorph.Guid, edge);
                if (resolution is null)
                {
                    known = false;
                    continue;
                }
                if (resolution.HasRestriction)
                    phone.Add(new ResolvedEnvironmentFacts(resolution.Key, resolution.Description));
            }
            result[allomorph.Guid] = new AlternationConditionFacts(known,
                allomorph.Environments.Any(item => item.Role == "phone") && phone.Count > 0,
                allomorph.Environments.Any(item => item.Role == "position"),
                Array.AsReadOnly(phone.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray()));
        }
        return result;
    }

    private ResolvedEnvironmentResult? ReadResolvedEnvironment(string allomorphGuid,
        AllomorphEnvironmentFact edge)
    {
        string? status = null;
        string? representation = null;
        using (var command = NewCommand())
        {
            command.CommandText = "SELECT parse_status, representation FROM facts.environment WHERE guid=$guid;";
            command.Parameters.AddWithValue("$guid", edge.EnvironmentGuid);
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                status = reader.GetString(0);
                representation = reader.GetString(1);
            }
        }
        if (status != "valid") return null;
        using (var command = NewCommand())
        {
            command.CommandText = "SELECT compiled, result, resolved_environment_guid FROM facts.environment_usage " +
                "WHERE allomorph_guid=$allomorph AND role=$role AND ordinal=$ordinal " +
                "ORDER BY compile_context_key;";
            command.Parameters.AddWithValue("$allomorph", allomorphGuid);
            command.Parameters.AddWithValue("$role", edge.Role);
            command.Parameters.AddWithValue("$ordinal", edge.Ordinal);
            using var reader = command.ExecuteReader();
            var contexts = 0;
            while (reader.Read())
            {
                contexts++;
                if (reader.GetInt32(0) != 1 || reader.GetString(1) != "represented" || reader.IsDBNull(2) ||
                    reader.GetString(2) != edge.EnvironmentGuid) return null;
            }
            if (contexts == 0) return null;
        }
        var left = ReadEnvironmentPattern(edge.EnvironmentGuid, "environment_left");
        var right = ReadEnvironmentPattern(edge.EnvironmentGuid, "environment_right");
        if (left is null || right is null) return null;
        var key = JsonSerializer.Serialize(new { left = left.Key, right = right.Key });
        var hasRestriction = left.HasContent || right.HasContent;
        var description = DescribeEnvironment(left.Description, right.Description, representation!);
        return new ResolvedEnvironmentResult(key, description, hasRestriction);
    }

    private NormalizedPattern? ReadEnvironmentPattern(string environmentGuid, string role)
    {
        int? rootId = null;
        using (var command = NewCommand())
        {
            command.CommandText = "SELECT root_id FROM facts.pattern_root WHERE owner_kind='environment' " +
                "AND owner_guid=$guid AND role=$role AND source_kind='resolved_environment';";
            command.Parameters.AddWithValue("$guid", environmentGuid);
            command.Parameters.AddWithValue("$role", role);
            var value = command.ExecuteScalar();
            if (value is not null && value is not DBNull)
                rootId = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        if (rootId is null) return EmptyPattern();
        return ReadNormalizedPattern(rootId.Value);
    }

    private NormalizedPattern? ReadNormalizedPattern(int rootId)
    {
        var nodes = ReadPatternNodes(rootId);
        var normalized = new List<NormalizedPatternNode>();
        foreach (var node in nodes.Where(item => item.ParentId is null).OrderBy(item => item.Ordinal))
        {
            var item = NormalizePatternNode(node, nodes);
            if (item is null) return null;
            normalized.Add(item);
        }
        var key = JsonSerializer.Serialize(normalized.Select(item => item.Key).ToArray());
        var description = string.Join(" ", normalized.Select(item => item.Description).Where(item => item.Length > 0));
        return new NormalizedPattern(key, description, normalized.Count > 0);
    }

    private static NormalizedPattern EmptyPattern() => new("[]", string.Empty, false);

    private IReadOnlyList<PatternNodeFact> ReadPatternNodes(int rootId)
    {
        using var command = NewCommand();
        command.CommandText = "SELECT node_id, parent_node_id, ordinal, kind, min, max, phoneme_guid, " +
            "natural_class_guid, boundary_guid, token_text FROM facts.pattern_node WHERE root_id=$root " +
            "ORDER BY parent_node_id, ordinal;";
        command.Parameters.AddWithValue("$root", rootId);
        using var reader = command.ExecuteReader();
        var rows = new List<PatternNodeFact>();
        while (reader.Read())
            rows.Add(new PatternNodeFact(reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetInt32(1),
                reader.GetInt32(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5), NullableString(reader, 6), NullableString(reader, 7),
                NullableString(reader, 8), NullableString(reader, 9)));
        return Array.AsReadOnly(rows.ToArray());
    }

    private NormalizedPatternNode? NormalizePatternNode(PatternNodeFact node,
        IReadOnlyList<PatternNodeFact> nodes)
    {
        if (node.Kind is not ("sequence" or "phoneme" or "naturalClass" or "boundary" or "wordBoundary" or
            "morphemeBoundary" or "leftAnchor" or "rightAnchor")) return null;
        var children = new List<NormalizedPatternNode>();
        foreach (var child in nodes.Where(item => item.ParentId == node.Id).OrderBy(item => item.Ordinal))
        {
            var normalizedChild = NormalizePatternNode(child, nodes);
            if (normalizedChild is null) return null;
            children.Add(normalizedChild);
        }
        var classExtension = node.NaturalClassGuid is null ? null : ReadEffectiveClassExtension(node.NaturalClassGuid);
        if (node.NaturalClassGuid is not null && classExtension is null) return null;
        var storedBoundaryName = node.BoundaryGuid is null ? null :
            ReadName("SELECT name FROM facts.boundary_marker WHERE guid=$guid;", node.BoundaryGuid);
        var boundary = NormalizeBoundaryReference(node.Kind, node.BoundaryGuid, storedBoundaryName);
        var kind = boundary.Kind;
        var boundaryGuid = boundary.Guid;
        var name = node.PhonemeGuid is not null ? ReadPhonemeName(node.PhonemeGuid) :
            node.NaturalClassGuid is not null ? ReadNaturalClassName(node.NaturalClassGuid) :
            boundary.Description;
        var childKeys = children.Select(item => item.Key).ToArray();
        var key = JsonSerializer.Serialize(new
        {
            kind,
            node.Min,
            node.Max,
            node.PhonemeGuid,
            classExtension,
            boundaryGuid,
            node.TokenText,
            children = childKeys,
        });
        var description = kind switch
        {
            "sequence" => string.Join(" ", children.Select(item => item.Description).Where(item => item.Length > 0)),
            "phoneme" or "naturalClass" => name,
            "boundary" => boundary.Description,
            "wordBoundary" => "word boundary",
            "morphemeBoundary" => "morpheme boundary",
            "leftAnchor" or "rightAnchor" => "word edge",
            _ => string.Empty,
        };
        return new NormalizedPatternNode(key, description);
    }

    private IReadOnlyList<object>? ReadEffectiveClassExtension(string classGuid)
    {
        var loaded = ReadObjectLoadState(classGuid, "naturalClass");
        if (loaded != true) return null;
        using var command = NewCommand();
        command.CommandText = "SELECT table_key, member_key, phoneme_guid FROM facts.natural_class_effective_member " +
            "WHERE natural_class_guid=$guid ORDER BY table_key, member_key;";
        command.Parameters.AddWithValue("$guid", classGuid);
        using var reader = command.ExecuteReader();
        var members = new List<object>();
        while (reader.Read())
            members.Add(new { table = reader.GetString(0), member = reader.GetString(1),
                phoneme = NullableString(reader, 2) });
        return Array.AsReadOnly(members.ToArray());
    }

    private string ReadPhonemeName(string guid) => ReadName("SELECT name FROM facts.phoneme WHERE guid=$guid;", guid)
        ?? guid;

    private string ReadNaturalClassName(string guid) => ReadName(
        "SELECT coalesce(display_name, name) FROM facts.natural_class WHERE guid=$guid;", guid) ?? guid;

    internal static (string Kind, string? Guid, string Description) NormalizeBoundaryReference(
        string kind, string? guid, string? storedName)
    {
        if (kind == "boundary" && Guid.TryParse(guid, out var id))
        {
            if (id == LangProjectTags.kguidPhRuleWordBdry) return ("wordBoundary", null, "word boundary");
            if (id == LangProjectTags.kguidPhRuleMorphBdry) return ("morphemeBoundary", null, "morpheme boundary");
        }
        return kind switch
        {
            "boundary" => (kind, guid, storedName ?? "boundary marker"),
            "wordBoundary" => (kind, null, "word boundary"),
            "morphemeBoundary" => (kind, null, "morpheme boundary"),
            _ => (kind, guid, storedName ?? kind),
        };
    }

    private string? ReadName(string sql, string guid)
    {
        using var command = NewCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$guid", guid);
        return command.ExecuteScalar() as string;
    }

    private bool? ReadObjectLoadState(string guid, string subjectKind)
    {
        using var command = NewCommand();
        command.CommandText = "SELECT pipeline_stage, loaded FROM facts.load_fact WHERE subject_guid=$guid " +
            "AND subject_kind=$kind " +
            "AND pipeline_stage IN ('compile','compact') ORDER BY CASE pipeline_stage WHEN 'compact' THEN 1 ELSE 0 END DESC;";
        command.Parameters.AddWithValue("$guid", guid);
        command.Parameters.AddWithValue("$kind", subjectKind);
        using var reader = command.ExecuteReader();
        var stage = -1;
        var values = new List<bool?>();
        while (reader.Read())
        {
            var current = reader.GetString(0) == "compact" ? 1 : 0;
            if (current > stage)
            {
                stage = current;
                values.Clear();
            }
            if (current == stage) values.Add(reader.IsDBNull(1) ? null : reader.GetInt32(1) == 1);
        }
        return values.Count > 0 && values.All(value => value == true) ? true :
            values.Count > 0 && values.All(value => value == false) ? false : null;
    }

    private IReadOnlyList<ExistingRewriteRule> ReadExistingRewriteRules()
    {
        var rules = new List<ExistingRewriteRule>();
        using var command = NewCommand();
        command.CommandText = "SELECT guid, kind, effective_stratum_key FROM facts.phonological_rule ORDER BY guid;";
        using var reader = command.ExecuteReader();
        var rows = new List<(string Guid, string Kind, bool InStratum)>();
        while (reader.Read()) rows.Add((reader.GetString(0), reader.GetString(1), !reader.IsDBNull(2)));
        reader.Close();
        foreach (var row in rows)
        {
            var loaded = ReadObjectLoadState(row.Guid, "phonologicalRule");
            if (row.Kind != "rewrite" || !row.InStratum)
            {
                rules.Add(new ExistingRewriteRule(null, null, false, loaded, null, row.Guid, false));
                continue;
            }
            var roots = ReadRulePatternRoots(row.Guid);
            var input = ReadSinglePhonemeRoot(roots, "rewrite_lhs", 0);
            var outputRoots = roots.Where(root => root.Role == "rewrite_sc").ToArray();
            var changeRootId = ReadRhsChangeRootId(row.Guid);
            var output = changeRootId is int changeRoot && roots.Any(root => root.RootId == changeRoot)
                ? ReadSinglePhonemeNode(changeRoot) : null;
            var hasGates = ScalarLong("SELECT COUNT(*) FROM facts.phonological_rule_variable WHERE rule_guid=$guid;", row.Guid) > 0 ||
                ScalarLong("SELECT COUNT(*) FROM facts.rewrite_rhs_pos WHERE rule_guid=$guid;", row.Guid) > 0 ||
                ScalarLong("SELECT COUNT(*) FROM facts.rewrite_rhs_rule_feature WHERE rule_guid=$guid;", row.Guid) > 0;
            var conditionKeys = ReadRuleContextKeys(roots, outputRoots.Length == 1 ? outputRoots[0].Ordinal : 0);
            var exact = ScalarLong("SELECT COUNT(*) FROM facts.rewrite_rhs WHERE rule_guid=$guid;", row.Guid) == 1 &&
                input is not null && output is not null && !hasGates;
            rules.Add(new ExistingRewriteRule(input, output, exact, loaded, conditionKeys, row.Guid));
        }
        return Array.AsReadOnly(rules.ToArray());
    }

    private IReadOnlyList<string>? ReadRuleContextKeys(IReadOnlyList<RulePatternRoot> roots, int rhsOrdinal)
    {
        var leftRoots = roots.Where(root => root.Role == "rewrite_left_context").ToArray();
        var rightRoots = roots.Where(root => root.Role == "rewrite_right_context").ToArray();
        if (leftRoots.Length > 1 || rightRoots.Length > 1 ||
            leftRoots.Any(root => root.Ordinal != rhsOrdinal) || rightRoots.Any(root => root.Ordinal != rhsOrdinal))
            return null;
        if (leftRoots.Length == 0 && rightRoots.Length == 0) return [];

        var left = leftRoots.Length == 0 ? EmptyPattern() : ReadNormalizedPattern(leftRoots[0].RootId);
        var right = rightRoots.Length == 0 ? EmptyPattern() : ReadNormalizedPattern(rightRoots[0].RootId);
        if (left is null || right is null) return null;
        if (!left.HasContent && !right.HasContent) return [];
        return Array.AsReadOnly([JsonSerializer.Serialize(new { left = left.Key, right = right.Key })]);
    }

    private IReadOnlyList<RulePatternRoot> ReadRulePatternRoots(string guid)
    {
        using var command = NewCommand();
        command.CommandText = "SELECT root_id, role, ordinal FROM facts.pattern_root " +
            "WHERE owner_kind='phonologicalRule' AND owner_guid=$guid AND source_kind='authored' ORDER BY role, ordinal;";
        command.Parameters.AddWithValue("$guid", guid);
        using var reader = command.ExecuteReader();
        var roots = new List<RulePatternRoot>();
        while (reader.Read()) roots.Add(new RulePatternRoot(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2)));
        return Array.AsReadOnly(roots.ToArray());
    }

    private string? ReadSinglePhonemeRoot(IReadOnlyList<RulePatternRoot> roots, string role, int ordinal)
    {
        var root = roots.SingleOrDefault(item => item.Role == role && item.Ordinal == ordinal);
        return root is null ? null : ReadSinglePhonemeNode(root.RootId);
    }

    private int? ReadRhsChangeRootId(string guid)
    {
        using var command = NewCommand();
        command.CommandText = "SELECT change_root_id FROM facts.rewrite_rhs WHERE rule_guid=$guid AND ordinal=0;";
        command.Parameters.AddWithValue("$guid", guid);
        var value = command.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private string? ReadSinglePhonemeNode(int rootId)
    {
        var nodes = ReadPatternNodes(rootId);
        var top = nodes.Where(item => item.ParentId is null).OrderBy(item => item.Ordinal).ToArray();
        if (top.Length == 1 && top[0].Kind == "phoneme") return top[0].PhonemeGuid;
        if (top.Length == 1 && top[0].Kind == "sequence")
        {
            var children = nodes.Where(item => item.ParentId == top[0].Id).ToArray();
            if (children.Length == 1 && children[0].Kind == "phoneme") return children[0].PhonemeGuid;
        }
        return null;
    }

    private int ReadRelevantFeatureCount()
    {
        using var command = NewCommand();
        command.CommandText = "SELECT COUNT(*) FROM facts.feature WHERE system='phonological' AND kind='closed';";
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private string? NullableString(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal)
        ? null
        : reader.GetString(ordinal);

    private string DescribeEnvironment(string left, string right, string representation)
    {
        if (left.Length > 0 && right.Length > 0) return $"between {left} and {right}";
        if (left.Length > 0) return $"after {left}";
        if (right.Length > 0) return $"before {right}";
        return $"in environment {representation}";
    }

    private sealed class PhonemeBuilder(string guid, string name, int? featureStructureId)
    {
        public string Guid { get; } = guid;
        public string Name { get; } = name;
        public int? FeatureStructureId { get; } = featureStructureId;
        public List<PhonemeGraphemeFact> Graphemes { get; } = [];

        public PhonemeFacts Build(HashSet<string> featureIds, IReadOnlyDictionary<string, HashSet<string>> featureValues,
            IReadOnlyDictionary<int, IReadOnlyList<FeatureAssignmentFact>> assignments)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var visited = new HashSet<int>();
            var valid = FeatureStructureId.HasValue;
            void Read(int fsId)
            {
                if (!visited.Add(fsId) || !assignments.TryGetValue(fsId, out var fsAssignments)) return;
                foreach (var assignment in fsAssignments)
                {
                    if (assignment.ValueKind == "complex")
                    {
                        if (assignment.ChildFsId is { } child) Read(child);
                        continue;
                    }
                    if (!featureIds.Contains(assignment.FeatureGuid)) continue;
                    if (assignment.ValueGuid is null || !featureValues.TryGetValue(assignment.FeatureGuid,
                            out var knownValues) || !knownValues.Contains(assignment.ValueGuid) ||
                        !values.TryAdd(assignment.FeatureGuid, assignment.ValueGuid))
                        valid = false;
                }
            }
            if (FeatureStructureId is { } root) Read(root);
            var complete = valid && featureIds.All(values.ContainsKey);
            return new PhonemeFacts(Guid, Name, Array.AsReadOnly(Graphemes.ToArray()),
                new PhonemeFeatureVector(complete, values));
        }
    }

    private sealed class MsaGateBuilder(string guid, string entryGuid, string kind)
    {
        public string Guid { get; } = guid;
        public string EntryGuid { get; } = entryGuid;
        public string Kind { get; } = kind;
        public List<(string Role, int Ordinal, string Guid)> Categories { get; } = [];
        public List<(string Role, int Ordinal, string Guid)> Slots { get; } = [];
        public List<(string Role, string Guid)> Classes { get; } = [];
        public List<(string Role, string Guid)> StemNames { get; } = [];
        public List<(string Role, int Ordinal, string Guid)> ExceptionFeatures { get; } = [];
        public List<(string Role, string Json)> FeatureStructures { get; } = [];

        public string Signature => JsonSerializer.Serialize(new
        {
            Kind,
            categories = Categories.Select(item => new
                { role = item.Role, ordinal = item.Ordinal, guid = item.Guid }),
            slots = Slots.Select(item => new
                { role = item.Role, ordinal = item.Ordinal, guid = item.Guid }),
            classes = Classes.Select(item => new { role = item.Role, guid = item.Guid }),
            stemNames = StemNames.Select(item => new { role = item.Role, guid = item.Guid }),
            exceptionFeatures = ExceptionFeatures.Select(item => new
                { role = item.Role, ordinal = item.Ordinal, guid = item.Guid }),
            featureStructures = FeatureStructures.Select(item => new { role = item.Role, json = item.Json }),
        });
    }

    private sealed record FeatureAssignmentFact(string FeatureGuid, string ValueKind, string? ValueGuid,
        int? ChildFsId);

    private sealed record PatternNodeFact(int Id, int? ParentId, int Ordinal, string Kind, int? Min, int? Max,
        string? PhonemeGuid, string? NaturalClassGuid, string? BoundaryGuid, string? TokenText);

    private sealed record RulePatternRoot(int RootId, string Role, int Ordinal);

    private sealed record NormalizedPattern(string Key, string Description, bool HasContent);

    private sealed record NormalizedPatternNode(string Key, string Description);

    private sealed record ResolvedEnvironmentResult(string Key, string Description, bool HasRestriction);
}
