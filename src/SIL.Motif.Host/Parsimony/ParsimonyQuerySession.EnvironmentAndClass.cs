using System.Text;
using Microsoft.Data.Sqlite;

namespace SIL.Motif.Host.Parsimony;

public sealed partial class ParsimonyQuerySession
{
    internal EnvironmentExcessDiscovery ReadEnvironmentExcess()
    {
        RequireSections("allomorphs", "entries", "msas", "compiled_mappings", "environments", "patterns",
            "phonology", "features");
        var inventory = ReadContextInventory();
        var orders = ReadContextOrders();
        var observationRead = ReadContextObservations(inventory.Graphemes, inventory.Phones);
        var observations = observationRead.Items;
        var groups = orders.GroupBy(item => (item.OwnerKey, item.MsaGuid, item.Bucket)).ToArray();
        var targetSites = orders.Where(item => !item.IsAbstract && item.MsaGuid is not null)
            .Select(item => (item.AllomorphGuid, item.MsaGuid!)).ToHashSet();
        var sites = new List<EnvironmentExcessSite>();
        var unsupported = 0;
        foreach (var group in groups)
        {
            var siblings = group.GroupBy(item => item.AllomorphGuid, StringComparer.Ordinal)
                .Select(items => new ContextOrder(items.Key, items.Select(item => item.Order).Distinct().ToArray(),
                    items.Select(item => item.IsAbstract).Distinct().ToArray()))
                .ToArray();
            if (siblings.Any(item => item.Orders.Length != 1 || item.Abstract.Length != 1))
            {
                unsupported++;
                continue;
            }
            var ordered = siblings.OrderBy(item => item.Orders[0]).ThenBy(item => item.Guid, StringComparer.Ordinal)
                .ToArray();
            foreach (var target in ordered.Where(item => item.Abstract[0] == false))
            {
                var condition = ReadContextCondition(target.Guid, inventory, group.Key.Bucket);
                if (!condition.Supported)
                {
                    unsupported++;
                    continue;
                }
                if (condition.Side is null) continue;
                if (condition.Universe.Count == 0) { unsupported++; continue; }
                var effective = new HashSet<string>(condition.Licensed, StringComparer.Ordinal);
                var order = target.Orders[0];
                var unknownSibling = false;
                foreach (var earlier in ordered.Where(item => item.Orders[0] < order))
                {
                    var earlierCondition = ReadContextCondition(earlier.Guid, inventory, group.Key.Bucket);
                    if (!earlierCondition.Supported)
                    {
                        unknownSibling = true;
                        break;
                    }
                    if (earlierCondition.Side is not null && earlierCondition.Side != condition.Side)
                    {
                        unknownSibling = true;
                        break;
                    }
                    var earlierLicensed = earlierCondition.Side is null ? condition.Universe : earlierCondition.Licensed;
                    effective.ExceptWith(earlierLicensed);
                }
                if (unknownSibling)
                {
                    unsupported++;
                    continue;
                }
                if (group.Key.MsaGuid is null) { unsupported++; continue; }
                var witnessed = UniqueContexts(observations.Where(item => item.AllomorphGuid == target.Guid &&
                    item.MsaGuid == group.Key.MsaGuid), condition.Side, condition.BoundaryLabels);
                if (witnessed.Ambiguous)
                {
                    unsupported++;
                    continue;
                }
                var observed = witnessed.Items.Select(item => item.ContextGuid).ToHashSet(StringComparer.Ordinal);
                if (observed.Count == 0) continue;
                var applicableUniverse = condition.Universe;
                var missing = observed.Except(effective, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                var extras = effective.Except(observed, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                var featureIntersection = ReadFeatureIntersection(observed, inventory.Phones);
                var supports = witnessed.Items.ToArray();
                sites.Add(new EnvironmentExcessSite(target.Guid, group.Key.OwnerKey, group.Key.MsaGuid,
                    group.Key.Bucket, condition.EnvironmentGuids, condition.EnvironmentNames,
                    condition.Side, applicableUniverse.Order(StringComparer.Ordinal).ToArray(),
                    effective.Order(StringComparer.Ordinal).ToArray(),
                    observed.Order(StringComparer.Ordinal).ToArray(), extras, missing,
                    Math.Max(0, applicableUniverse.Except(observed, StringComparer.Ordinal).Count()),
                    supports.Select(item => item.WordformGuid).Distinct(StringComparer.Ordinal).Count(),
                    supports.Select(item => item.StemGuid).Distinct(StringComparer.Ordinal).Count(),
                    supports.Select(item => item.Display).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                    condition.BoundaryLabels, GetContextNames(inventory),
                    FindClassesCovering(observed, inventory.ClassMembers, inventory.Phones, inventory.ClassNames),
                    featureIntersection.Assignments, featureIntersection.Extension.Order(StringComparer.Ordinal).ToArray(),
                    featureIntersection.UnknownPhonemes.Order(StringComparer.Ordinal).ToArray()));
            }
        }
        var ambiguousAlignments = observationRead.AmbiguousAlignments.Count(item =>
            targetSites.Contains((item.AllomorphGuid, item.MsaGuid)));
        return new EnvironmentExcessDiscovery(Array.AsReadOnly(sites.OrderBy(item => item.AllomorphGuid,
                StringComparer.Ordinal).ThenBy(item => item.Bucket, StringComparer.Ordinal).ToArray()), unsupported,
            ambiguousAlignments);
    }

    internal NaturalClassExcessDiscovery ReadNaturalClassExcess()
    {
        RequireSections("allomorphs", "entries", "msas", "compiled_mappings", "environments", "patterns",
            "phonology", "features");
        var inventory = ReadContextInventory();
        var observationRead = ReadContextObservations(inventory.Graphemes, inventory.Phones);
        var observations = observationRead.Items;
        var references = new List<ClassContextReference>();
        using (var command = NewCommand())
        {
            command.CommandText = """
                SELECT r.owner_guid, n.side, n.natural_class_guid, a.allomorph_guid, o.source_msa_guid,
                       COALESCE(ob.bucket, ''), c.name, n.result
                FROM facts.pattern_root AS r
                JOIN facts.pattern_node AS p ON p.root_id=r.root_id
                JOIN facts.environment AS e ON e.guid=r.owner_guid
                JOIN facts.allomorph_environment AS a ON a.environment_guid=e.guid AND a.role='phone'
                JOIN facts.allomorph AS x ON x.guid=a.allomorph_guid
                JOIN facts.compiled_allomorph_order AS o ON o.source_allomorph_guid=x.guid
                LEFT JOIN facts.compiled_output AS ob ON ob.output_id=o.output_id
                JOIN facts.environment_usage AS u ON u.allomorph_guid=x.guid AND u.role=a.role
                  AND u.ordinal=a.ordinal AND u.environment_guid=e.guid
                  AND u.compile_context_key=CASE WHEN ob.bucket IN ('Morphology','Clitics') THEN 'production' ELSE 'default' END
                  AND u.compiled=1 AND u.result='represented' AND u.resolved_environment_guid=e.guid
                JOIN facts.environment_natural_class AS n ON n.environment_guid=e.guid
                JOIN facts.natural_class AS c ON c.guid=n.natural_class_guid
                WHERE r.owner_kind='environment' AND r.source_kind='resolved_environment'
                  AND r.role IN ('environment_left','environment_right') AND p.kind='naturalClass'
                  AND p.natural_class_guid=n.natural_class_guid AND n.result='resolved'
                  AND o.compiled_order IS NOT NULL
                ORDER BY e.guid, n.side, n.natural_class_guid, x.guid, ob.bucket;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
                references.Add(new ClassContextReference(reader.GetString(0), reader.GetString(1),
                    reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetString(5), reader.GetString(6), reader.GetString(7)));
        }

        var sites = new List<NaturalClassExcessSite>();
        var unsupported = 0;
        foreach (var reference in references.Distinct())
        {
            if (reference.MsaGuid is null || reference.Result != "resolved")
            {
                unsupported++;
                continue;
            }
            if (ScalarLong("SELECT COUNT(DISTINCT environment_guid) FROM facts.allomorph_environment " +
                           "WHERE allomorph_guid=$guid AND role='phone';", reference.AllomorphGuid) != 1 ||
                !EnvironmentUsesOnlyClass(reference.EnvironmentGuid, reference.ClassGuid, reference.Side))
            {
                unsupported++;
                continue;
            }
            var observed = UniqueContexts(observations.Where(item => item.AllomorphGuid == reference.AllomorphGuid &&
                item.MsaGuid == reference.MsaGuid), reference.Side);
            if (observed.Ambiguous)
            {
                unsupported++;
                continue;
            }
            var observedGuids = observed.Items.Select(item => item.ContextGuid).ToHashSet(StringComparer.Ordinal);
            if (observedGuids.Count == 0 || observedGuids.Any(guid => !inventory.Phones.ContainsKey(guid))) continue;
            var classMembers = inventory.ClassMembers.GetValueOrDefault(reference.ClassGuid) ?? new HashSet<string>(StringComparer.Ordinal);
            var extra = classMembers.Except(observedGuids, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var missing = observedGuids.Except(classMembers, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var featureIntersection = ReadFeatureIntersection(observedGuids, inventory.Phones);
            var unknown = featureIntersection.UnknownPhonemes.Order(StringComparer.Ordinal).ToArray();
            var support = observed.Items.ToArray();
            sites.Add(new NaturalClassExcessSite(reference.ClassGuid, reference.Name,
                $"{reference.AllomorphGuid}/{reference.EnvironmentGuid}/{reference.Side}/{reference.Bucket}", reference.EnvironmentGuid,
                reference.AllomorphGuid, reference.Side, inventory.Phones.Keys.Order(StringComparer.Ordinal).ToArray(),
                observedGuids.Order(StringComparer.Ordinal).ToArray(), extra, missing,
                inventory.Phones.Keys.Except(observedGuids, StringComparer.Ordinal).Count(),
                featureIntersection.Assignments, featureIntersection.Extension.Order(StringComparer.Ordinal).ToArray(),
                unknown, support.Select(item => item.WordformGuid).Distinct(StringComparer.Ordinal).Count(),
                support.Select(item => item.StemGuid).Distinct(StringComparer.Ordinal).Count(),
                support.Select(item => item.Display).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                inventory.ClassUnknownMembers.GetValueOrDefault(reference.ClassGuid) ?? [],
                GetContextNames(inventory)));
        }
        var targetSites = references.Where(item => item.MsaGuid is not null)
            .Select(item => (item.AllomorphGuid, item.MsaGuid!)).ToHashSet();
        var ambiguousAlignments = observationRead.AmbiguousAlignments.Count(item =>
            targetSites.Contains((item.AllomorphGuid, item.MsaGuid)));
        return new NaturalClassExcessDiscovery(Array.AsReadOnly(sites.OrderBy(item => item.ClassGuid,
                StringComparer.Ordinal).ThenBy(item => item.UsageSite, StringComparer.Ordinal).ToArray()), unsupported,
            ambiguousAlignments);
    }

    private ContextInventory ReadContextInventory()
    {
        var phones = new Dictionary<string, string>(StringComparer.Ordinal);
        var classNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var graphemes = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);
        using (var command = NewCommand())
        {
            command.CommandText = """
                SELECT p.guid, p.name, g.writing_system, g.grapheme
                FROM facts.phoneme AS p LEFT JOIN facts.phoneme_grapheme AS g ON g.phoneme_guid=p.guid
                ORDER BY p.guid, g.writing_system, g.ordinal;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var guid = reader.GetString(0);
                phones[guid] = reader.GetString(1);
                if (reader.IsDBNull(2)) continue;
                if (!graphemes.TryGetValue(reader.GetString(2), out var byGuid))
                    graphemes[reader.GetString(2)] = byGuid = new(StringComparer.Ordinal);
                if (!byGuid.TryGetValue(guid, out var forms)) byGuid[guid] = forms = [];
                forms.Add(reader.GetString(3).Normalize(NormalizationForm.FormD));
            }
        }
        var classes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var unknownClassMembers = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        using (var command = NewCommand())
        {
            command.CommandText = "SELECT guid, name FROM facts.natural_class ORDER BY guid;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) classNames.Add(reader.GetString(0), reader.GetString(1));
        }
        using (var command = NewCommand())
        {
            command.CommandText = "SELECT natural_class_guid, phoneme_guid, identity_quality, member_key " +
                                  "FROM facts.natural_class_effective_member ORDER BY natural_class_guid, table_key, member_key;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var guid = reader.GetString(0);
                if (!classes.TryGetValue(guid, out var members)) classes[guid] = members = new(StringComparer.Ordinal);
                if (!reader.IsDBNull(1) && reader.GetString(2) == "sourceGuid") members.Add(reader.GetString(1));
                else
                {
                    if (!unknownClassMembers.TryGetValue(guid, out var unknown)) unknownClassMembers[guid] = unknown = [];
                    unknown.Add(reader.GetString(3));
                }
            }
        }
        var boundaries = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var command = NewCommand())
        {
            command.CommandText = "SELECT guid, name FROM facts.boundary_marker ORDER BY guid;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) boundaries.Add(reader.GetString(0), reader.GetString(1));
        }
        return new ContextInventory(phones, graphemes, classes, boundaries, unknownClassMembers, classNames);
    }

    private IReadOnlyList<ContextOrderRow> ReadContextOrders()
    {
        var rows = new List<ContextOrderRow>();
        using var command = NewCommand();
        command.CommandText = """
            SELECT COALESCE(owner.key, '') AS owner_key, o.source_msa_guid,
                   COALESCE(out.bucket, '') AS bucket, o.source_allomorph_guid, o.compiled_order, a.is_abstract
            FROM facts.compiled_allomorph_order AS o
            JOIN facts.allomorph AS a ON a.guid=o.source_allomorph_guid
            LEFT JOIN facts.compiled_output AS out ON out.output_id=o.output_id
            LEFT JOIN facts.compiled_output AS owner ON owner.output_id=o.owner_output_id
            WHERE o.source_allomorph_guid IS NOT NULL AND o.compiled_order IS NOT NULL
            ORDER BY owner_key, o.source_msa_guid, bucket, o.compiled_order, o.source_allomorph_guid;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
            rows.Add(new ContextOrderRow(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5) == 1));
        return Array.AsReadOnly(rows.ToArray());
    }

    private ContextCondition ReadContextCondition(string allomorphGuid, ContextInventory inventory, string bucket)
    {
        var environments = new List<(string Guid, string Name, string? Resolved, string? Status, bool? Compiled)>();
        if (ScalarLong("SELECT COUNT(*) FROM facts.allomorph_environment WHERE allomorph_guid=$guid AND role='position';",
                allomorphGuid) > 0)
            return ContextCondition.Unknown;
        using (var command = NewCommand())
        {
            command.CommandText = """
                SELECT a.environment_guid, e.name, u.resolved_environment_guid, e.parse_status, u.compiled
                FROM facts.allomorph_environment AS a
                LEFT JOIN facts.environment AS e ON e.guid=a.environment_guid
                LEFT JOIN facts.environment_usage AS u ON u.allomorph_guid=a.allomorph_guid AND u.role=a.role
                  AND u.ordinal=a.ordinal AND u.environment_guid=a.environment_guid
                  AND u.compile_context_key=$context
                WHERE a.allomorph_guid=$guid AND a.role='phone'
                ORDER BY a.ordinal, u.compile_context_key;
                """;
            command.Parameters.AddWithValue("$guid", allomorphGuid);
            command.Parameters.AddWithValue("$context", bucket is "Morphology" or "Clitics" ? "production" : "default");
            using var reader = command.ExecuteReader();
            while (reader.Read())
                environments.Add((reader.GetString(0), reader.IsDBNull(1) ? reader.GetString(0) : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4) == 1));
        }
        if (environments.Count == 0)
            return new ContextCondition(true, null, [], [], [], [], []);
        var alternatives = new List<ContextAlternative>();
        foreach (var environment in environments.GroupBy(item => item.Guid, StringComparer.Ordinal).Select(group => group.First()))
        {
            if (environment.Status != "valid" || environment.Compiled != true || environment.Resolved is null)
                return ContextCondition.Unknown;
            var parsed = ReadSimpleEnvironment(environment.Resolved, inventory);
            if (!parsed.Supported) return ContextCondition.Unknown;
            alternatives.Add(parsed with { EnvironmentGuid = environment.Guid, EnvironmentName = environment.Name });
        }
        var sides = alternatives.Select(item => item.Side).Where(item => item is not null).Distinct().ToArray();
        if (sides.Length != 1 || alternatives.Any(item => item.Side is null)) return ContextCondition.Unknown;
        var side = sides[0]!;
        var universe = inventory.Phones.Keys.ToHashSet(StringComparer.Ordinal);
        var explicitBoundaries = alternatives.SelectMany(item => item.Boundaries).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var boundary in explicitBoundaries) universe.Add(boundary);
        var licensed = alternatives.SelectMany(item => item.Licensed).ToHashSet(StringComparer.Ordinal);
        return new ContextCondition(true, side, licensed, universe,
            alternatives.Select(item => item.EnvironmentGuid!).ToArray(),
            alternatives.Select(item => item.EnvironmentName!).ToArray(), explicitBoundaries);
    }

    private ContextAlternative ReadSimpleEnvironment(string environmentGuid, ContextInventory inventory)
    {
        var sideAtoms = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var boundaries = new HashSet<string>(StringComparer.Ordinal);
        using var command = NewCommand();
        command.CommandText = """
            SELECT r.role, n.node_id, n.parent_node_id, n.ordinal, n.kind, n.phoneme_guid,
                   n.natural_class_guid, n.boundary_guid, n.min, n.max
            FROM facts.pattern_root AS r JOIN facts.pattern_node AS n ON n.root_id=r.root_id
            WHERE r.owner_kind='environment' AND r.owner_guid=$guid AND r.source_kind='resolved_environment'
              AND r.role IN ('environment_left','environment_right')
            ORDER BY r.role, n.parent_node_id, n.ordinal;
            """;
        command.Parameters.AddWithValue("$guid", environmentGuid);
        var nodes = new List<EnvironmentPatternNode>();
        using (var reader = command.ExecuteReader())
            while (reader.Read()) nodes.Add(new EnvironmentPatternNode(reader.GetString(0), reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2), reader.GetInt32(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetInt32(8),
                reader.IsDBNull(9) ? null : reader.GetInt32(9)));
        foreach (var side in new[] { "environment_left", "environment_right" })
        {
            var roots = nodes.Where(item => item.Role == side && item.ParentId is null).OrderBy(item => item.Ordinal).ToArray();
            if (roots.Length == 0) continue;
            var members = new HashSet<string>(StringComparer.Ordinal);
            foreach (var root in roots)
            {
                var atom = root;
                if (root.Kind == "sequence")
                {
                    var children = nodes.Where(item => item.Role == side && item.ParentId == root.Id).ToArray();
                    if (children.Length != 1) return ContextAlternative.Unknown;
                    atom = children[0];
                }
                if (atom.Kind == "phoneme" && atom.PhonemeGuid is not null && inventory.Phones.ContainsKey(atom.PhonemeGuid))
                    members.Add(atom.PhonemeGuid);
                else if (atom.Kind == "naturalClass" && atom.ClassGuid is not null &&
                         inventory.ClassMembers.TryGetValue(atom.ClassGuid, out var classMembers) && classMembers.Count > 0 &&
                         !inventory.ClassUnknownMembers.ContainsKey(atom.ClassGuid))
                    members.UnionWith(classMembers);
                else if (atom.Kind == "boundary" && atom.BoundaryGuid is not null && inventory.Boundaries.ContainsKey(atom.BoundaryGuid))
                {
                    members.Add(atom.BoundaryGuid);
                    boundaries.Add(atom.BoundaryGuid);
                }
                else return ContextAlternative.Unknown;
                if (atom.Min is < 1 || atom.Max is < 1 || (atom.Min.HasValue && atom.Min != 1) ||
                    (atom.Max.HasValue && atom.Max != 1)) return ContextAlternative.Unknown;
            }
            sideAtoms[side] = members;
        }
        if (sideAtoms.Count != 1) return ContextAlternative.Unknown;
        var selected = sideAtoms.Single();
        return new ContextAlternative(true, selected.Key == "environment_left" ? "left" : "right",
            selected.Value, boundaries, environmentGuid, null);
    }

    private bool EnvironmentUsesOnlyClass(string environmentGuid, string classGuid, string side)
    {
        var expectedRole = side == "left" ? "environment_left" : "environment_right";
        var nodes = new List<EnvironmentPatternNode>();
        using var command = NewCommand();
        command.CommandText = """
            SELECT r.role, n.node_id, n.parent_node_id, n.ordinal, n.kind, n.phoneme_guid,
                   n.natural_class_guid, n.boundary_guid, n.min, n.max
            FROM facts.pattern_root AS r JOIN facts.pattern_node AS n ON n.root_id=r.root_id
            WHERE r.owner_kind='environment' AND r.owner_guid=$guid AND r.source_kind='resolved_environment'
              AND r.role IN ('environment_left','environment_right')
            ORDER BY r.role, n.parent_node_id, n.ordinal;
            """;
        command.Parameters.AddWithValue("$guid", environmentGuid);
        using (var reader = command.ExecuteReader())
            while (reader.Read()) nodes.Add(new EnvironmentPatternNode(reader.GetString(0), reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2), reader.GetInt32(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetInt32(8),
                reader.IsDBNull(9) ? null : reader.GetInt32(9)));
        if (nodes.Count == 0 || nodes.Any(item => item.Role != expectedRole)) return false;
        var roots = nodes.Where(item => item.ParentId is null).OrderBy(item => item.Ordinal).ToArray();
        if (roots.Length == 0) return false;
        foreach (var root in roots)
        {
            var atom = root;
            if (root.Kind == "sequence")
            {
                var children = nodes.Where(item => item.ParentId == root.Id).ToArray();
                if (children.Length != 1) return false;
                atom = children[0];
            }
            if (atom.Kind != "naturalClass" || atom.ClassGuid != classGuid ||
                nodes.Any(item => item.ParentId == atom.Id) || atom.Min is < 1 || atom.Max is < 1 ||
                (atom.Min.HasValue && atom.Min != 1) || (atom.Max.HasValue && atom.Max != 1)) return false;
        }
        return true;
    }

    private ContextObservationRead ReadContextObservations(
        IReadOnlyDictionary<string, Dictionary<string, List<string>>> graphemes,
        IReadOnlyDictionary<string, string> phones)
    {
        var rows = new Dictionary<string, ObservationBuilder>(StringComparer.Ordinal);
        var ambiguousAlignments = new HashSet<AmbiguousContextAlignment>();
        using var command = NewCommand();
        var scopeClause = ApprovedScopeClause(command);
        command.CommandText = $"""
            SELECT a.analysis_guid, a.wordform_guid, wf.writing_system, wf.form, m.ordinal, m.morph_guid,
                   m.msa_guid, m.entry_guid, mm.form, COALESCE(msa.kind, ''), COALESCE(e.lexeme_morph_type, '')
            FROM evidence.analyses AS a
            JOIN evidence.wordform_forms AS wf ON wf.wordform_guid=a.wordform_guid
            JOIN evidence.analysis_morphs AS m ON m.analysis_guid=a.analysis_guid
            LEFT JOIN evidence.analysis_morph_forms AS mm ON mm.analysis_guid=m.analysis_guid
              AND mm.morph_ordinal=m.ordinal AND mm.writing_system=wf.writing_system
            LEFT JOIN facts.msa AS msa ON msa.msa_guid=m.msa_guid
            LEFT JOIN facts.lex_entry AS e ON e.guid=m.entry_guid
            WHERE a.opinion='approved'{scopeClause}
            ORDER BY a.analysis_guid, wf.writing_system, m.ordinal;
            """;
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var analysis = reader.GetString(0);
                var ws = reader.GetString(2);
                var key = analysis + "\n" + ws;
                if (!rows.TryGetValue(key, out var builder))
                    rows[key] = builder = new ObservationBuilder(analysis, reader.GetString(1), ws,
                        reader.GetString(3).Normalize(NormalizationForm.FormD));
                builder.Morphs.Add(new ObservedMorph(reader.GetInt32(4), NullableString(reader, 5),
                    NullableString(reader, 6), NullableString(reader, 7), NullableString(reader, 8),
                    reader.GetString(9) == "stem" || reader.GetString(10) == "stem"));
            }
        }
        var output = new List<ObservedMorphemeContext>();
        foreach (var row in rows.Values)
        {
            if (!graphemes.TryGetValue(row.WritingSystem, out var map) || row.Morphs.Count == 0 ||
                row.Morphs.Any(item => item.Form is null || item.MorphGuid is null)) continue;
            var wordTokens = Tokenize(row.Wordform, map, out var wordAmbiguous);
            var rowAmbiguous = wordAmbiguous;
            if (wordTokens is null)
            {
                if (rowAmbiguous) RecordAmbiguousAlignment(row, ambiguousAlignments);
                continue;
            }
            var morphTokens = new List<string[]>();
            var valid = true;
            foreach (var morph in row.Morphs.OrderBy(item => item.Ordinal))
            {
                var tokens = Tokenize(morph.Form!.Normalize(NormalizationForm.FormD), map, out var morphAmbiguous);
                rowAmbiguous |= morphAmbiguous;
                if (tokens is null) { valid = false; break; }
                morphTokens.Add(tokens);
            }
            if (rowAmbiguous) RecordAmbiguousAlignment(row, ambiguousAlignments);
            if (!valid || !morphTokens.SelectMany(item => item).SequenceEqual(wordTokens, StringComparer.Ordinal)) continue;
            var stems = row.Morphs.Where(item => item.IsStem && item.EntryGuid is not null)
                .Select(item => item.EntryGuid!).Distinct(StringComparer.Ordinal).ToArray();
            if (stems.Length != 1) continue;
            var offsets = 0;
            foreach (var (morph, tokens) in row.Morphs.OrderBy(item => item.Ordinal).Zip(morphTokens))
            {
                if (morph.MorphGuid is not null && morph.MsaGuid is not null)
                {
                    output.Add(new ObservedMorphemeContext(morph.MorphGuid, morph.MsaGuid, stems[0],
                        row.WordformGuid, row.AnalysisGuid, morph.Ordinal,
                        offsets > 0 ? wordTokens[offsets - 1] : null,
                        offsets + tokens.Length < wordTokens.Length ? wordTokens[offsets + tokens.Length] : null,
                        string.Concat(wordTokens.Select(guid => phones.GetValueOrDefault(guid, guid))),
                        row.WritingSystem));
                }
                offsets += tokens.Length;
            }
        }
        return new ContextObservationRead(Array.AsReadOnly(output.ToArray()),
            Array.AsReadOnly(ambiguousAlignments.OrderBy(item => item.AllomorphGuid, StringComparer.Ordinal)
                .ThenBy(item => item.MsaGuid, StringComparer.Ordinal).ThenBy(item => item.WordformGuid, StringComparer.Ordinal)
                .ThenBy(item => item.WritingSystem, StringComparer.Ordinal).ToArray()));
    }

    private static void RecordAmbiguousAlignment(ObservationBuilder row, ISet<AmbiguousContextAlignment> target)
    {
        foreach (var morph in row.Morphs.Where(item => item.MorphGuid is not null && item.MsaGuid is not null))
            target.Add(new AmbiguousContextAlignment(morph.MorphGuid!, morph.MsaGuid!, row.WordformGuid,
                row.WritingSystem));
    }

    private static string[]? Tokenize(string text,
        IReadOnlyDictionary<string, List<string>> graphemes, out bool ambiguous)
    {
        var forms = graphemes.SelectMany(pair => pair.Value.Select(form => (Guid: pair.Key, Form: form)))
            .Distinct().OrderByDescending(item => item.Form.Length)
            .ThenBy(item => item.Guid, StringComparer.Ordinal).ToArray();
        var memo = new Dictionary<int, List<string[]>?>();
        List<string[]>? Visit(int offset)
        {
            if (offset == text.Length) return [Array.Empty<string>()];
            if (memo.TryGetValue(offset, out var cached)) return cached;
            var results = new List<string[]>();
            foreach (var form in forms.Where(item => text.AsSpan(offset).StartsWith(item.Form.AsSpan(), StringComparison.Ordinal)))
            {
                foreach (var tail in Visit(offset + form.Form.Length) ?? [])
                {
                    results.Add([form.Guid, .. tail]);
                    if (results.Count > 1) break;
                }
                if (results.Count > 1) break;
            }
            memo[offset] = results.Count == 0 ? null : results;
            return memo[offset];
        }
        var tokenizations = Visit(0);
        ambiguous = tokenizations is { Count: > 1 };
        return tokenizations is { Count: 1 } ? tokenizations[0] : null;
    }

    private static UniqueContextResult UniqueContexts(IEnumerable<ObservedMorphemeContext> source, string side,
        IReadOnlyList<string>? boundaryGuids = null)
    {
        var rows = source.ToArray();
        var groups = rows.GroupBy(item => item.WordformGuid, StringComparer.Ordinal);
        var output = new List<ObservedContextSupport>();
        var ambiguous = false;
        foreach (var group in groups)
        {
            var boundary = boundaryGuids is { Count: 1 } ? boundaryGuids[0] : null;
            var unique = group.Select(item => (Context: side == "left" ? item.LeftGuid : item.RightGuid,
                item.StemGuid)).Distinct().ToArray();
            if (unique.Length != 1)
            {
                ambiguous = true;
                continue;
            }
            var item = group.First();
            var context = side == "left" ? item.LeftGuid : item.RightGuid;
            if (context is null) context = boundary;
            if (context is null)
            {
                ambiguous = true;
                continue;
            }
            output.Add(new ObservedContextSupport(item.WordformGuid, item.StemGuid,
                context, item.Display));
        }
        return new UniqueContextResult(Array.AsReadOnly(output.ToArray()), ambiguous);
    }

    private (string[] Assignments, HashSet<string> Extension, HashSet<string> UnknownPhonemes)
        ReadFeatureIntersection(HashSet<string> observed, IReadOnlyDictionary<string, string> phones)
    {
        var assignmentRows = new Dictionary<string, Dictionary<string, (string Kind, string? Value)>>(StringComparer.Ordinal);
        var labels = new Dictionary<(string Feature, string Value), string>();
        using (var command = NewCommand())
        {
            command.CommandText = """
                SELECT p.guid, a.feature_guid, a.value_kind, a.value_guid, f.name, v.name
                FROM facts.phoneme AS p JOIN facts.feature_structure AS s ON s.fs_id=p.feature_structure_id
                JOIN facts.feature_assignment AS a ON a.fs_id=s.fs_id
                JOIN facts.feature AS f ON f.guid=a.feature_guid
                LEFT JOIN facts.feature_value AS v ON v.guid=a.value_guid
                WHERE s.system='phonological' AND f.system='phonological' AND f.kind='closed'
                ORDER BY p.guid, a.feature_guid;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var phoneme = reader.GetString(0);
                if (!assignmentRows.TryGetValue(phoneme, out var values)) assignmentRows[phoneme] = values = new(StringComparer.Ordinal);
                values[reader.GetString(1)] = (reader.GetString(2), NullableString(reader, 3));
                if (!reader.IsDBNull(3) && !reader.IsDBNull(4) && !reader.IsDBNull(5))
                    labels[(reader.GetString(1), reader.GetString(3))] = reader.GetString(4) + "=" + reader.GetString(5);
            }
        }
        var common = new Dictionary<string, string>(StringComparer.Ordinal);
        if (observed.Count > 0)
        {
            var first = assignmentRows.GetValueOrDefault(observed.First()) ?? new(StringComparer.Ordinal);
            foreach (var pair in first)
                if (pair.Value.Kind == "closed" && pair.Value.Value is not null && observed.All(guid =>
                    assignmentRows.TryGetValue(guid, out var values) && values.TryGetValue(pair.Key, out var value) &&
                    value.Kind == "closed" && value.Value == pair.Value.Value))
                    common[pair.Key] = pair.Value.Value;
        }
        var extension = new HashSet<string>(StringComparer.Ordinal);
        var unknown = new HashSet<string>(StringComparer.Ordinal);
        foreach (var phone in phones.Keys)
        {
            var values = assignmentRows.GetValueOrDefault(phone) ?? new(StringComparer.Ordinal);
            var definiteNo = common.Any(pair => values.TryGetValue(pair.Key, out var value) &&
                value.Kind == "closed" && value.Value is not null && value.Value != pair.Value);
            if (definiteNo) continue;
            if (common.Count > 0 && common.All(pair => values.TryGetValue(pair.Key, out var value) &&
                value.Kind == "closed" && value.Value == pair.Value)) extension.Add(phone);
            else if (common.Count > 0) unknown.Add(phone);
        }
        if (common.Count == 0) extension.UnionWith(phones.Keys);
        var descriptions = common.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => labels.GetValueOrDefault((pair.Key, pair.Value), $"{pair.Key}={pair.Value}")).ToArray();
        return (descriptions, extension, unknown);
    }

    private static IReadOnlyList<string> FindClassesCovering(HashSet<string> observed,
        IReadOnlyDictionary<string, HashSet<string>> classes, IReadOnlyDictionary<string, string> phones,
        IReadOnlyDictionary<string, string> classNames) =>
        classes.Where(pair => observed.IsSubsetOf(pair.Value) && pair.Value.Any(phones.ContainsKey))
            .Select(pair => classNames.GetValueOrDefault(pair.Key, pair.Key)).Order(StringComparer.Ordinal).ToArray();

    private static IReadOnlyDictionary<string, string> GetContextNames(ContextInventory inventory) =>
        inventory.Phones.Concat(inventory.Boundaries).ToDictionary(pair => pair.Key, pair => pair.Value,
            StringComparer.Ordinal);

    private sealed record ContextInventory(Dictionary<string, string> Phones,
        Dictionary<string, Dictionary<string, List<string>>> Graphemes,
        Dictionary<string, HashSet<string>> ClassMembers, Dictionary<string, string> Boundaries,
        Dictionary<string, List<string>> ClassUnknownMembers, Dictionary<string, string> ClassNames);
    private sealed record ContextOrderRow(string OwnerKey, string? MsaGuid, string Bucket, string AllomorphGuid,
        int Order, bool IsAbstract);
    private sealed record ContextOrder(string Guid, int[] Orders, bool[] Abstract);
    private sealed record ContextCondition(bool Supported, string? Side, HashSet<string> Licensed,
        HashSet<string> Universe, string[] EnvironmentGuids, string[] EnvironmentNames, string[] BoundaryLabels)
    {
        public static ContextCondition Unknown { get; } = new(false, null, [], [], [], [], []);
    }
    private sealed record ContextAlternative(bool Supported, string? Side, HashSet<string> Licensed,
        HashSet<string> Boundaries, string? EnvironmentGuid, string? EnvironmentName)
    {
        public static ContextAlternative Unknown { get; } = new(false, null, [], [], null, null);
    }
    private sealed record EnvironmentPatternNode(string Role, int Id, int? ParentId, int Ordinal, string Kind,
        string? PhonemeGuid, string? ClassGuid, string? BoundaryGuid, int? Min, int? Max);
    private sealed record ObservedMorph(int Ordinal, string? MorphGuid, string? MsaGuid, string? EntryGuid,
        string? Form, bool IsStem);
    private sealed class ObservationBuilder(string analysisGuid, string wordformGuid, string writingSystem, string wordform)
    {
        public string AnalysisGuid { get; } = analysisGuid;
        public string WordformGuid { get; } = wordformGuid;
        public string WritingSystem { get; } = writingSystem;
        public string Wordform { get; } = wordform;
        public List<ObservedMorph> Morphs { get; } = [];
    }
    private sealed record ObservedMorphemeContext(string AllomorphGuid, string MsaGuid, string StemGuid,
        string WordformGuid, string AnalysisGuid, int Ordinal, string? LeftGuid, string? RightGuid,
        string Display, string WritingSystem)
    {
        public string Side { get; init; } = string.Empty;
    }
    private sealed record ObservedContextSupport(string WordformGuid, string StemGuid, string ContextGuid,
        string Display);
    private sealed record ContextObservationRead(IReadOnlyList<ObservedMorphemeContext> Items,
        IReadOnlyList<AmbiguousContextAlignment> AmbiguousAlignments);
    private sealed record AmbiguousContextAlignment(string AllomorphGuid, string MsaGuid,
        string WordformGuid, string WritingSystem);
    private sealed record UniqueContextResult(IReadOnlyList<ObservedContextSupport> Items, bool Ambiguous);
    private sealed record ClassContextReference(string EnvironmentGuid, string Side, string ClassGuid,
        string AllomorphGuid, string? MsaGuid, string Bucket, string Name, string Result);
}

internal sealed record EnvironmentExcessDiscovery(IReadOnlyList<EnvironmentExcessSite> Sites, int UnsupportedSites,
    int AmbiguousAlignments);
internal sealed record EnvironmentExcessSite(string AllomorphGuid, string OwnerKey, string MsaGuid, string Bucket,
    IReadOnlyList<string> EnvironmentGuids, IReadOnlyList<string> EnvironmentNames, string Side,
    IReadOnlyList<string> Universe, IReadOnlyList<string> Licensed, IReadOnlyList<string> Observed,
    IReadOnlyList<string> Extras, IReadOnlyList<string> MissingObserved,
    int Denominator, int WordTypes, int Stems, IReadOnlyList<string> Witnesses,
    IReadOnlyList<string> BoundaryGuids, IReadOnlyDictionary<string, string> ContextNames,
    IReadOnlyList<string> ExistingClassesCoveringObserved, IReadOnlyList<string> FeatureIntersection,
    IReadOnlyList<string> IntersectionExtension, IReadOnlyList<string> UnknownFeatureMembers);
internal sealed record NaturalClassExcessDiscovery(IReadOnlyList<NaturalClassExcessSite> Sites, int UnsupportedSites,
    int AmbiguousAlignments);
internal sealed record NaturalClassExcessSite(string ClassGuid, string ClassName, string UsageSite,
    string EnvironmentGuid, string AllomorphGuid, string Side, IReadOnlyList<string> Universe,
    IReadOnlyList<string> Observed, IReadOnlyList<string> Extras, IReadOnlyList<string> MissingObserved,
    int Denominator,
    IReadOnlyList<string> FeatureIntersection, IReadOnlyList<string> IntersectionExtension,
    IReadOnlyList<string> UnknownFeatureMembers, int WordTypes, int Stems,
    IReadOnlyList<string> Witnesses, IReadOnlyList<string> UnknownClassMembers,
    IReadOnlyDictionary<string, string> ContextNames);
