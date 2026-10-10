using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Parsimony;

namespace SIL.Motif.Host.Parsimony;

public sealed partial class ParsimonyQuerySession
{
    /// <summary>Returns all non-abstract allomorphs with forms and exact authored environment lists.</summary>
    public IReadOnlyList<AllomorphMeasureFact> ReadAllomorphsForMeasure()
    {
        RequireSections("allomorphs", "environments");
        var builders = new Dictionary<string, AllomorphMeasureBuilder>(StringComparer.Ordinal);
        using (var command = NewCommand())
        {
            command.CommandText = """
                SELECT a.guid, a.entry_guid, a.ordinal, a.morph_type, a.is_abstract,
                       f.writing_system, f.ordinal, f.form
                FROM facts.allomorph AS a
                LEFT JOIN facts.allomorph_form AS f ON f.allomorph_guid=a.guid
                WHERE a.is_abstract=0
                ORDER BY a.entry_guid, a.guid, f.writing_system, f.ordinal;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var guid = reader.GetString(0);
                if (!builders.TryGetValue(guid, out var builder))
                {
                    builder = new AllomorphMeasureBuilder(guid, reader.GetString(1), reader.GetInt32(2),
                        reader.GetString(3), reader.GetInt32(4) != 0);
                    builders.Add(guid, builder);
                }
                if (!reader.IsDBNull(5))
                    builder.AddForm(reader.GetString(5), reader.GetInt32(6), reader.GetString(7));
            }
        }
        using (var command = NewCommand())
        {
            command.CommandText = """
                SELECT e.allomorph_guid, e.role, e.ordinal, e.environment_guid
                FROM facts.allomorph_environment AS e
                JOIN facts.allomorph AS a ON a.guid=e.allomorph_guid
                WHERE a.is_abstract=0
                ORDER BY e.allomorph_guid, e.role, e.ordinal, e.environment_guid;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (builders.TryGetValue(reader.GetString(0), out var builder))
                    builder.Environments.Add(new AllomorphEnvironmentFact(reader.GetString(1),
                        reader.GetInt32(2), reader.GetString(3)));
        }
        return Array.AsReadOnly(builders.Values.Where(builder => builder.Forms.Count > 0)
            .Select(builder => builder.Build()).OrderBy(item => item.EntryGuid, StringComparer.Ordinal)
            .ThenBy(item => item.Guid, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// The sections whose complete state makes <c>statement_reference</c> complete. An absent row is unused only
    /// when all of them are complete.
    /// </summary>
    public static IReadOnlyList<string> StatementReferenceSections { get; } =
    [
        "environments", "phonology", "patterns", "allomorphs", "msas", "entries", "templates", "compound_rules",
        "affix_processes", "adhoc_prohibitions", "load_accounting",
    ];

    /// <summary>Reads environment and natural-class use from the facts artifact, not from Text evidence.</summary>
    public IReadOnlyList<ParsimonyStatementUsageViewRow> ReadStatementUsage(string? statementKind = null)
    {
        RequireSections([.. StatementReferenceSections]);
        if (statementKind is not (null or "environment" or "natural-class"))
            throw new ArgumentException("Statement kind must be 'environment' or 'natural-class'.", nameof(statementKind));
        var rows = new List<ParsimonyStatementUsageViewRow>();
        if (statementKind is null or "environment") ReadStatementUsageFor(rows, "environment");
        if (statementKind is null or "natural-class") ReadStatementUsageFor(rows, "natural-class");
        return Array.AsReadOnly(rows.OrderBy(row => row.StatementKind, StringComparer.Ordinal)
            .ThenBy(row => row.StatementGuid, StringComparer.Ordinal).ToArray());
    }

    /// <summary>Reads one allomorph and its approved analyses through the connection-local view.</summary>
    public ParsimonyAllomorphViewRow? ReadAllomorphContext(string guid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guid);
        RequireSections("allomorphs", "environments");
        AllomorphMeasureFact? fact = ReadAllomorphsForMeasure().FirstOrDefault(item => item.Guid == guid);
        if (fact is null)
        {
            using var command = NewCommand();
            command.CommandText = """
                SELECT a.guid, a.entry_guid, a.ordinal, a.morph_type, a.is_abstract
                FROM facts.allomorph AS a WHERE a.guid=$guid;
                """;
            command.Parameters.AddWithValue("$guid", guid);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            fact = new AllomorphMeasureFact(reader.GetString(0), reader.GetString(1), reader.GetInt32(2),
                reader.GetString(3), reader.GetInt32(4) != 0,
                new SortedDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal), []);
        }
        var witnesses = ReadApprovedWitnesses("m.morph_guid", guid);
        var environments = ReadEnvironmentUse(guid);
        return new ParsimonyAllomorphViewRow(fact.Guid, fact.EntryGuid, fact.Ordinal, fact.MorphType,
            fact.IsAbstract, fact.Forms, environments, witnesses);
    }

    /// <summary>Reads one complete prohibition and its grouped-source availability.</summary>
    public ParsimonyAdhocViewRow? ReadAdhocContext(string guid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guid);
        var item = ReadAdhocProhibitions().FirstOrDefault(prohibition =>
            StringComparer.Ordinal.Equals(prohibition.Guid, guid));
        return item is null ? null : new ParsimonyAdhocViewRow(new ParsimonyAdhocProhibition(item.Guid,
            item.Kind, item.Disabled, item.Adjacency, item.PrimaryGuid, item.PrimaryTargetKind,
            item.Others.Select(target => new ParsimonyAdhocTarget(target.Guid, target.Kind)).ToArray(), item.Loaded),
            GroupedAdhocFactsAvailable);
    }

    /// <summary>Reads one MSA and its authored categories, slots, and Approved witnesses.</summary>
    public ParsimonyAffixViewRow? ReadAffixContext(string guid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guid);
        RequireSections("entries", "msas", "templates", "allomorphs");
        string? entryGuid = null;
        string? msaKind = null;
        using (var command = NewCommand())
        {
            command.CommandText = "SELECT entry_guid, kind FROM facts.msa WHERE msa_guid=$guid;";
            command.Parameters.AddWithValue("$guid", guid);
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                entryGuid = reader.GetString(0);
                msaKind = reader.GetString(1);
            }
        }
        if (entryGuid is null || msaKind is null) return null;
        var categories = ReadStringColumn("SELECT category_guid FROM facts.msa_category WHERE msa_guid=$guid " +
            "ORDER BY role, ordinal", guid);
        var slots = ReadStringColumn("SELECT slot_guid FROM facts.msa_slot WHERE msa_guid=$guid ORDER BY role, ordinal", guid);
        return new ParsimonyAffixViewRow(guid, entryGuid, msaKind, categories, slots,
            ReadApprovedWitnesses("m.msa_guid", guid));
    }

    /// <summary>Reads one natural class with the fact tables that name its use and compiler state.</summary>
    public ParsimonyNaturalClassViewRow? ReadNaturalClassContext(string guid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guid);
        RequireSections("features", "patterns", "environments", "load_accounting");
        string? kind = null;
        string? name = null;
        using (var command = NewCommand())
        {
            command.CommandText = "SELECT kind, name FROM facts.natural_class WHERE guid=$guid;";
            command.Parameters.AddWithValue("$guid", guid);
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                kind = reader.GetString(0);
                name = reader.GetString(1);
            }
        }
        if (kind is null || name is null) return null;
        var members = ReadStringColumn("SELECT phoneme_guid FROM facts.natural_class_member " +
            "WHERE natural_class_guid=$guid ORDER BY ordinal", guid);
        var effective = ReadStringColumn("SELECT phoneme_guid FROM facts.natural_class_effective_member " +
            "WHERE natural_class_guid=$guid AND phoneme_guid IS NOT NULL ORDER BY table_key, member_key", guid);
        var environmentReferences = ScalarLong("SELECT COUNT(*) FROM facts.environment_natural_class " +
            "WHERE natural_class_guid=$guid AND result='resolved'", guid);
        var patternReferences = ScalarLong("""
            SELECT COUNT(*) FROM facts.pattern_node AS n
            JOIN facts.pattern_root AS r ON r.root_id=n.root_id
            WHERE n.natural_class_guid=$guid AND r.source_kind='authored';
            """, guid);
        var load = ReadLoadState(guid);
        return new ParsimonyNaturalClassViewRow(guid, kind, name, members, effective,
            environmentReferences, patternReferences, load.Loaded, load.Disposition);
    }

    /// <summary>Reads templates with compiled prefixes in surface order and suffixes in declared order.</summary>
    public IReadOnlyList<ParsimonyTemplateViewRow> ReadTemplateOrder(string? templateGuid, string? categoryGuid)
    {
        RequireSections("categories", "templates");
        if ((templateGuid is null) == (categoryGuid is null))
            throw new ArgumentException("Supply exactly one template GUID or category GUID.");
        var rows = new List<ParsimonyTemplateViewRow>();
        using var command = NewCommand();
        command.CommandText = """
            SELECT t.guid, t.category_guid, t.name, t.disabled,
                   s.slot_guid, s.side, s.ordinal, s.compiled_order, a.optional
            FROM facts.affix_template AS t
            LEFT JOIN facts.template_slot AS s ON s.template_guid=t.guid
            LEFT JOIN facts.affix_slot AS a ON a.guid=s.slot_guid
            WHERE ($template IS NOT NULL AND t.guid=$template)
               OR ($category IS NOT NULL AND t.category_guid=$category)
            ORDER BY t.guid, CASE s.side WHEN 'prefix' THEN 0 ELSE 1 END,
                     CASE WHEN s.compiled_order IS NULL THEN 1 ELSE 0 END,
                     CASE WHEN s.side='prefix' THEN -s.compiled_order ELSE s.compiled_order END, s.ordinal;
            """;
        command.Parameters.AddWithValue("$template", (object?)templateGuid ?? DBNull.Value);
        command.Parameters.AddWithValue("$category", (object?)categoryGuid ?? DBNull.Value);
        using var reader = command.ExecuteReader();
        string? currentGuid = null;
        string? currentCategory = null;
        string? currentName = null;
        bool disabled = false;
        List<ParsimonyTemplateSlot>? slots = null;
        void Commit()
        {
            if (currentGuid is not null && currentCategory is not null && currentName is not null && slots is not null)
                rows.Add(new ParsimonyTemplateViewRow(currentGuid, currentCategory, currentName, disabled,
                    Array.AsReadOnly(slots.ToArray())));
        }
        while (reader.Read())
        {
            var guid = reader.GetString(0);
            if (guid != currentGuid)
            {
                Commit();
                currentGuid = guid;
                currentCategory = reader.GetString(1);
                currentName = reader.GetString(2);
                disabled = reader.GetInt32(3) != 0;
                slots = [];
            }
            if (!reader.IsDBNull(4))
                slots!.Add(new ParsimonyTemplateSlot(reader.GetString(4), reader.GetString(5), reader.GetInt32(6),
                    reader.IsDBNull(7) ? null : reader.GetInt32(7), !reader.IsDBNull(8) && reader.GetInt32(8) != 0));
        }
        Commit();
        return Array.AsReadOnly(rows.ToArray());
    }

    public IReadOnlyList<ParsimonyApprovedWitness> ReadApprovedWitnesses(string identityColumn, string guid)
    {
        if (identityColumn is not ("m.morph_guid" or "m.msa_guid"))
            throw new ArgumentOutOfRangeException(nameof(identityColumn));
        using var command = NewCommand();
        var scopeClause = ApprovedScopeClause(command, "m.wordform_guid");
        command.CommandText = $"""
            SELECT m.wordform_guid, m.analysis_guid, m.ordinal, f.writing_system, f.form
            FROM temp.approved_morphs AS m
            LEFT JOIN evidence.analysis_morph_forms AS f
              ON f.analysis_guid=m.analysis_guid AND f.morph_ordinal=m.ordinal
            WHERE {identityColumn}=$guid{scopeClause}
            ORDER BY m.wordform_guid, m.analysis_guid, m.ordinal, f.writing_system;
            """;
        command.Parameters.AddWithValue("$guid", guid);
        using var reader = command.ExecuteReader();
        var result = new List<ParsimonyApprovedWitness>();
        (string Wordform, string Analysis, int Ordinal)? current = null;
        SortedDictionary<string, string>? forms = null;
        void Commit()
        {
            if (current is { } key && forms is not null)
                result.Add(new ParsimonyApprovedWitness(key.Wordform, key.Analysis, key.Ordinal,
                    new SortedDictionary<string, string>(forms, StringComparer.Ordinal)));
        }
        while (reader.Read())
        {
            var key = (reader.GetString(0), reader.GetString(1), reader.GetInt32(2));
            if (current != key)
            {
                Commit();
                current = key;
                forms = new SortedDictionary<string, string>(StringComparer.Ordinal);
            }
            if (!reader.IsDBNull(3)) forms![reader.GetString(3)] = reader.GetString(4);
        }
        Commit();
        return Array.AsReadOnly(result.ToArray());
    }

    private void ReadStatementUsageFor(List<ParsimonyStatementUsageViewRow> rows, string statementKind)
    {
        // Every reference to one target is adjacent in statement_reference, so each count is one index range.
        var table = statementKind == "environment" ? "facts.environment" : "facts.natural_class";
        using var command = NewCommand();
        command.CommandText = $"""
            SELECT s.guid, s.name,
                   (SELECT COUNT(*) FROM facts.statement_reference AS r
                    WHERE r.target_kind=$kind AND r.target_guid=s.guid),
                   (SELECT COUNT(*) FROM facts.statement_reference AS r
                    WHERE r.target_kind=$kind AND r.target_guid=s.guid AND r.parser_effect='applied')
            FROM {table} AS s ORDER BY s.guid;
            """;
        command.Parameters.AddWithValue("$kind", TargetKindOf(statementKind));
        using var reader = command.ExecuteReader();
        var summaries = new List<(string Guid, string Name, long References, long Applied)>();
        while (reader.Read())
            summaries.Add((reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3)));
        reader.Close();
        foreach (var item in summaries)
        {
            var load = ReadLoadState(item.Guid);
            rows.Add(new ParsimonyStatementUsageViewRow(statementKind, item.Guid, item.Name, item.References,
                item.Applied, load.Loaded, load.Disposition, ClassifyUsage(item.References, item.Applied)));
        }
    }

    /// <summary>Lists each referrer of one statement, with the effect the compiler gave that reference.</summary>
    public IReadOnlyList<(string ReferrerKind, string ReferrerGuid, string ParserEffect)> ReadStatementReferrers(
        string statementKind, string statementGuid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statementGuid);
        using var command = NewCommand();
        command.CommandText = """
            SELECT referrer_kind, referrer_guid, parser_effect FROM facts.statement_reference
            WHERE target_kind=$kind AND target_guid=$guid
            GROUP BY referrer_kind, referrer_guid, parser_effect
            ORDER BY referrer_kind, referrer_guid, parser_effect;
            """;
        command.Parameters.AddWithValue("$kind", TargetKindOf(statementKind));
        command.Parameters.AddWithValue("$guid", statementGuid);
        using var reader = command.ExecuteReader();
        var result = new List<(string ReferrerKind, string ReferrerGuid, string ParserEffect)>();
        while (reader.Read())
            result.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return Array.AsReadOnly(result.ToArray());
    }

    private static string TargetKindOf(string statementKind) => statementKind switch
    {
        "environment" => "environment",
        "natural-class" => "naturalClass",
        _ => throw new ArgumentException("Statement kind must be 'environment' or 'natural-class'.", nameof(statementKind)),
    };

    private (bool? Loaded, string? Disposition) ReadLoadState(string guid)
    {
        using var command = NewCommand();
        command.CommandText = """
            SELECT pipeline_stage, disposition, loaded
            FROM facts.load_fact WHERE subject_guid=$guid AND pipeline_stage IN ('compile','compact')
            ORDER BY CASE pipeline_stage WHEN 'compact' THEN 1 ELSE 0 END DESC,
                     subject_kind, context_key, decision_ordinal;
            """;
        command.Parameters.AddWithValue("$guid", guid);
        using var reader = command.ExecuteReader();
        var states = new List<(string Disposition, bool? Loaded)>();
        var finalStage = -1;
        while (reader.Read())
        {
            var stage = reader.GetString(0) == "compact" ? 1 : 0;
            if (stage > finalStage)
            {
                finalStage = stage;
                states.Clear();
            }
            if (stage == finalStage)
                states.Add((reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetInt32(2) == 1));
        }
        if (states.Count == 0) return (null, null);
        var known = states.Where(state => state.Loaded.HasValue).Select(state => state.Loaded!.Value)
            .Distinct().ToArray();
        bool? loaded = known.Length == 1 && states.All(state => state.Loaded.HasValue) ? known[0] : null;
        var disposition = string.Join(",", states.Select(state => state.Disposition)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        return (loaded, disposition);
    }

    private IReadOnlyList<ParsimonyEnvironmentReference> ReadEnvironmentUse(string guid)
    {
        using var command = NewCommand();
        command.CommandText = """
            SELECT a.role, a.ordinal, a.environment_guid, u.compiled, u.result
            FROM facts.allomorph_environment AS a
            LEFT JOIN facts.environment_usage AS u
              ON u.allomorph_guid=a.allomorph_guid AND u.role=a.role AND u.ordinal=a.ordinal
             AND u.environment_guid=a.environment_guid
            WHERE a.allomorph_guid=$guid
            ORDER BY a.role, a.ordinal, u.compile_context_key;
            """;
        command.Parameters.AddWithValue("$guid", guid);
        using var reader = command.ExecuteReader();
        var values = new Dictionary<(string Role, int Ordinal, string Environment), ParsimonyEnvironmentReference>();
        while (reader.Read())
        {
            var key = (reader.GetString(0), reader.GetInt32(1), reader.GetString(2));
            values[key] = new ParsimonyEnvironmentReference(key.Item1, key.Item2, key.Item3,
                reader.IsDBNull(3) ? null : reader.GetInt32(3) == 1,
                reader.IsDBNull(4) ? null : reader.GetString(4));
        }
        return Array.AsReadOnly(values.Values.OrderBy(item => item.Role, StringComparer.Ordinal)
            .ThenBy(item => item.Ordinal).ThenBy(item => item.EnvironmentGuid, StringComparer.Ordinal).ToArray());
    }

    private IReadOnlyList<string> ReadStringColumn(string sql, string guid)
    {
        using var command = NewCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$guid", guid);
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read()) values.Add(reader.GetString(0));
        return Array.AsReadOnly(values.ToArray());
    }

    /// <summary>
    /// Returns a readable form or name for a grammar object, wordform, prohibition, or Approved morph sequence.
    /// Returns <see langword="null"/> when the attached artifacts hold no readable description.
    /// </summary>
    public string? DescribeObject(string guid)
    {
        using var command = NewCommand();
        command.CommandText = """
            SELECT coalesce(
              (SELECT group_concat(form, ' / ') FROM (SELECT form FROM facts.entry_citation_form
                 WHERE entry_guid=$guid ORDER BY ordinal, writing_system)),
              (SELECT group_concat(form, ' / ') FROM (SELECT form FROM facts.allomorph_form
                 WHERE allomorph_guid=$guid ORDER BY ordinal, writing_system)),
              (SELECT f.form FROM facts.allomorph AS a JOIN facts.allomorph_form AS f ON f.allomorph_guid=a.guid
                 WHERE a.entry_guid=$guid ORDER BY a.ordinal, f.ordinal, f.writing_system LIMIT 1),
              (SELECT CASE WHEN trim(t.name) <> '' THEN t.name ELSE 'unnamed template in ' ||
                  coalesce(nullif(trim(c.name), ''), nullif(trim(c.abbreviation), ''), 'unknown category') END
                 FROM facts.affix_template AS t LEFT JOIN facts.category AS c ON c.guid=t.category_guid
                 WHERE t.guid=$guid),
              (SELECT name FROM facts.affix_slot WHERE guid=$guid),
              (SELECT coalesce(name, abbreviation) FROM facts.category WHERE guid=$guid),
              (SELECT form FROM evidence.wordform_forms WHERE wordform_guid=$guid
                 ORDER BY writing_system LIMIT 1),
              (SELECT coalesce(nullif(name, ''), nullif(representation, '')) FROM facts.environment WHERE guid=$guid),
              (SELECT coalesce(nullif(display_name, ''), nullif(name, '')) FROM facts.natural_class WHERE guid=$guid));
            """;
        command.Parameters.AddWithValue("$guid", guid);
        return command.ExecuteScalar() as string ?? DescribeProhibition(guid) ?? DescribeApprovedSequence(guid);
    }

    /// <summary>Names an ad hoc prohibition by kind and items, such as "Morpheme co-prohibition: ba".</summary>
    private string? DescribeProhibition(string prohibitionGuid)
    {
        using var header = NewCommand();
        header.CommandText = "SELECT kind, primary_guid, target_kind FROM facts.adhoc_prohibition " +
            "WHERE prohibition_guid=$guid;";
        header.Parameters.AddWithValue("$guid", prohibitionGuid);
        using var headerReader = header.ExecuteReader();
        if (!headerReader.Read()) return null;
        var kind = headerReader.GetString(0);
        var targets = new List<(string Guid, string Kind)> { (headerReader.GetString(1), headerReader.GetString(2)) };
        headerReader.Close();

        using var others = NewCommand();
        others.CommandText = "SELECT target_guid, target_kind FROM facts.adhoc_other " +
            "WHERE prohibition_guid=$guid ORDER BY ordinal;";
        others.Parameters.AddWithValue("$guid", prohibitionGuid);
        using (var reader = others.ExecuteReader())
            while (reader.Read()) targets.Add((reader.GetString(0), reader.GetString(1)));

        var label = kind == "allomorph" ? "Allomorph co-prohibition" : "Morpheme co-prohibition";
        var names = targets.Select(target => (target.Kind == "msa" ? DescribeMsa(target.Guid)
            : DescribeObject(target.Guid)) ?? "(unnamed)").ToArray();
        return names.All(name => name == "(unnamed)") ? $"{label} (unnamed)" : $"{label}: {string.Join(", ", names)}";
    }

    internal string? DescribeMsa(string guid)
    {
        using var command = NewCommand();
        command.CommandText = """
            SELECT m.kind || ' MSA for ' || coalesce(
              (SELECT form FROM facts.entry_citation_form WHERE entry_guid=m.entry_guid
                 ORDER BY ordinal, writing_system LIMIT 1),
              (SELECT f.form FROM facts.allomorph AS a JOIN facts.allomorph_form AS f ON f.allomorph_guid=a.guid
                 WHERE a.entry_guid=m.entry_guid ORDER BY a.ordinal, f.ordinal, f.writing_system LIMIT 1),
              'unnamed entry') || coalesce(
              (SELECT ' (' || t.text || ')' FROM facts.sense AS s JOIN facts.sense_text AS t
                 ON t.sense_guid=s.sense_guid WHERE s.msa_guid=m.msa_guid AND t.kind='gloss'
                 ORDER BY t.ordinal, t.writing_system LIMIT 1), '')
            FROM facts.msa AS m WHERE m.msa_guid=$guid;
            """;
        command.Parameters.AddWithValue("$guid", guid);
        return command.ExecuteScalar() as string;
    }

    private string? DescribeApprovedSequence(string analysisGuid)
    {
        using var command = NewCommand();
        command.CommandText = """
            SELECT coalesce((SELECT f.form FROM evidence.wordform_forms AS f
                WHERE f.wordform_guid=a.wordform_guid ORDER BY f.writing_system LIMIT 1), a.wordform_guid)
            FROM evidence.analyses AS a
            WHERE a.analysis_guid=$guid AND a.opinion='approved';
            """;
        command.Parameters.AddWithValue("$guid", analysisGuid);
        var word = command.ExecuteScalar() as string;
        if (word is null) return null;

        using var morphs = NewCommand();
        morphs.CommandText = """
            SELECT coalesce(
                (SELECT f.form FROM evidence.analysis_morph_forms AS f
                 WHERE f.analysis_guid=m.analysis_guid AND f.morph_ordinal=m.ordinal
                 ORDER BY f.writing_system LIMIT 1),
                (SELECT f.form FROM facts.allomorph_form AS f
                 WHERE f.allomorph_guid=m.morph_guid ORDER BY f.writing_system LIMIT 1),
                m.morph_guid, m.entry_guid, m.msa_guid, 'unknown') ||
                coalesce(' (' || (SELECT t.text FROM evidence.analysis_morph_texts AS t
                    WHERE t.analysis_guid=m.analysis_guid AND t.morph_ordinal=m.ordinal AND t.kind='gloss'
                    ORDER BY t.writing_system LIMIT 1) || ')', '')
            FROM evidence.analysis_morphs AS m
            WHERE m.analysis_guid=$guid ORDER BY m.ordinal;
            """;
        morphs.Parameters.AddWithValue("$guid", analysisGuid);
        using var reader = morphs.ExecuteReader();
        var sequence = new List<string>();
        while (reader.Read()) sequence.Add(reader.GetString(0));
        return sequence.Count == 0 ? word : $"{word}: {string.Join(" + ", sequence)}";
    }

    private long ScalarLong(string sql, string? guid)
    {
        using var command = NewCommand();
        command.CommandText = sql;
        if (guid is not null) command.Parameters.AddWithValue("$guid", guid);
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private void RequireSections(params string[] required)
    {
        var missing = required.Where(section => ReadSectionStatus(section) != "complete").ToArray();
        if (missing.Length != 0)
            throw new ParsimonyQueryUnavailableException(missing);
    }

    // Only an applied reference makes a statement used for parsing; every other reference leaves it unused.
    private static string ClassifyUsage(long references, long applied) => (references, applied) switch
    {
        (0, _) => "defined-but-unreferenced",
        (_, > 0) => "referenced-and-applied",
        _ => "referenced-but-not-applied",
    };

    private sealed class AllomorphMeasureBuilder(string guid, string entryGuid, int ordinal, string morphType,
        bool isAbstract)
    {
        private readonly SortedDictionary<string, SortedDictionary<int, string>> _forms =
            new(StringComparer.Ordinal);

        public string Guid { get; } = guid;
        public string EntryGuid { get; } = entryGuid;
        public int Ordinal { get; } = ordinal;
        public string MorphType { get; } = morphType;
        public bool IsAbstract { get; } = isAbstract;
        public List<AllomorphEnvironmentFact> Environments { get; } = [];
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Forms => new SortedDictionary<string, IReadOnlyList<string>>(
            _forms.ToDictionary(pair => pair.Key,
                pair => (IReadOnlyList<string>)Array.AsReadOnly(pair.Value.Values.ToArray()), StringComparer.Ordinal),
            StringComparer.Ordinal);

        public void AddForm(string writingSystem, int index, string form)
        {
            if (!_forms.TryGetValue(writingSystem, out var values))
                _forms.Add(writingSystem, values = new SortedDictionary<int, string>());
            values.Add(index, form);
        }

        public AllomorphMeasureFact Build() => new(Guid, EntryGuid, Ordinal, MorphType, IsAbstract, Forms,
            Array.AsReadOnly(Environments.OrderBy(item => item.Role, StringComparer.Ordinal)
                .ThenBy(item => item.Ordinal).ThenBy(item => item.EnvironmentGuid, StringComparer.Ordinal).ToArray()));
    }
}

/// <summary>One eligible allomorph and its exact authored forms and environment edges.</summary>
public sealed record AllomorphMeasureFact(string Guid, string EntryGuid, int Ordinal, string MorphType,
    bool IsAbstract, IReadOnlyDictionary<string, IReadOnlyList<string>> Forms,
    IReadOnlyList<AllomorphEnvironmentFact> Environments);

/// <summary>One ordered condition edge authored on an allomorph.</summary>
public sealed record AllomorphEnvironmentFact(string Role, int Ordinal, string EnvironmentGuid);

/// <summary>Names the fact sections a fixed query could not read.</summary>
public sealed class ParsimonyQueryUnavailableException(IReadOnlyList<string> sections)
    : InvalidOperationException($"Required grammar-facts sections are unavailable: {string.Join(", ", sections)}.")
{
    public IReadOnlyList<string> Sections { get; } = sections;
}

/// <summary>Names the evidence scope a measure needs but the attached evidence did not capture complete.</summary>
/// <remarks>The message is a short phrase that completes "Not checked: &lt;measure&gt; — ".</remarks>
public sealed class ParsimonyScopeUnavailableException(string reason) : InvalidOperationException(reason)
{
}
