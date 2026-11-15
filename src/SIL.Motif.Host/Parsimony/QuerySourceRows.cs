using System.Globalization;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Query;

namespace SIL.Motif.Host.Parsimony;

/// <summary>Reads the stems, uses and objects sources from a closed grammar-facts artifact in whole-table passes.</summary>
internal static class QuerySourceRows
{
    /// <summary>The most uses rows kept; target kinds that would pass it are left out whole and named as not measured.</summary>
    internal const int MaximumUses = 250_000;

    // A light facts file keeps only the environment and natural-class statement references.
    private static readonly string[] LightMissingKinds = ["exception-feature", "category", "slot", "inflection-class"];

    private const string None = "none";
    private const string KeySeparator = "‖";

    private static readonly (string Kind, string Source, string Table, string Column)[] Targets =
    [
        ("environment", "environment", "environment", "guid"), ("naturalClass", "natural-class", "natural_class", "guid"),
        ("exceptionFeature", "exception-feature", "exception_feature", "guid"), ("category", "category", "category", "guid"),
        ("slot", "slot", "affix_slot", "guid"), ("inflectionClass", "inflection-class", "inflection_class", "guid"),
    ];

    /// <summary>One row per stem MSA; its own part-of-speech rows give <c>category</c>, <c>none</c> when it has no such row.</summary>
    public static IReadOnlyList<QueryRow> ReadStems(SqliteConnection connection, IReadOnlyDictionary<string, string> formOfAllomorph,
        IReadOnlyDictionary<string, string> lexemeOfEntry, IReadOnlyDictionary<string, int> allomorphCount)
    {
        if (!Has(connection, "msa")) return [];
        var categoryNames = Map(connection, "category", "SELECT guid, COALESCE(NULLIF(abbreviation, ''), NULLIF(name, ''), guid) FROM category");
        var positions = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (Has(connection, "msa_category"))
        {
            var filter = HasColumn(connection, "msa_category", "role") ? " WHERE role='pos'" : "";
            foreach (var (msa, category) in Pairs(connection, "SELECT msa_guid, category_guid FROM msa_category" + filter + " ORDER BY msa_guid, ordinal"))
                AddDistinct(positions, msa, categoryNames.GetValueOrDefault(category, category));
        }
        var featureNames = Map(connection, "exception_feature", "SELECT guid, COALESCE(NULLIF(abbreviation, ''), NULLIF(name, ''), guid) FROM exception_feature");
        var carried = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (Has(connection, "msa_exception_feature"))
            foreach (var (msa, feature) in Pairs(connection, "SELECT msa_guid, target_guid FROM msa_exception_feature WHERE role='required' ORDER BY msa_guid, ordinal"))
                AddDistinct(carried, msa, featureNames.GetValueOrDefault(feature, feature));
        var glosses = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Has(connection, "sense_text") && Has(connection, "sense"))
            foreach (var (entry, gloss) in Pairs(connection, "SELECT s.entry_guid, t.text FROM sense s JOIN sense_text t ON t.sense_guid=s.sense_guid " +
                         "WHERE t.kind='gloss' AND trim(t.text)<>'' ORDER BY s.entry_guid, s.sense_guid, t.ordinal"))
                glosses.TryAdd(entry, gloss.Trim());
        var msaCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (_, entry) in Pairs(connection, "SELECT msa_guid, entry_guid FROM msa"))
            msaCounts[entry] = msaCounts.GetValueOrDefault(entry) + 1;
        var rows = new List<QueryRow>();
        foreach (var (msa, entry) in Pairs(connection, "SELECT msa_guid, entry_guid FROM msa WHERE kind='stem' ORDER BY msa_guid"))
        {
            var form = lexemeOfEntry.TryGetValue(entry, out var lexeme) && formOfAllomorph.TryGetValue(lexeme, out var text) ? text : "";
            rows.Add(new QueryRow(Id(msa),
            [
                [form], [Id(entry)], positions.TryGetValue(msa, out var own) ? [.. own] : [None],
                carried.TryGetValue(msa, out var tags) ? [.. tags] : [], [glosses.GetValueOrDefault(entry, None)],
                [Number(msaCounts.GetValueOrDefault(entry))], [Number(Math.Max(0, allomorphCount.GetValueOrDefault(entry) - 1))],
            ]));
        }
        return rows;
    }

    /// <summary>Whether the artifact was written with <c>--facts-light</c>.</summary>
    public static bool IsLight(SqliteConnection connection) => HasColumn(connection, "artifact_meta", "light_omissions");

    /// <summary>
    /// One row per reference to an environment, natural class, exception feature, category, slot or inflection class.
    /// Target kinds are kept whole in target order while they fit <paramref name="maximum"/>; the rest are returned as missing.
    /// </summary>
    public static (IReadOnlyList<QueryRow> Rows, IReadOnlyList<string> CappedKinds) ReadUses(SqliteConnection connection,
        int maximum = MaximumUses)
    {
        if (!Has(connection, "statement_reference")) return ([], []);
        var kept = new List<string>();
        var capped = new List<string>();
        var total = 0L;
        foreach (var (kind, count) in Pairs(connection, "SELECT target_kind, COUNT(*) FROM statement_reference " +
                     "WHERE target_kind IN ('environment','naturalClass','exceptionFeature','category','slot','inflectionClass') " +
                     "GROUP BY target_kind ORDER BY target_kind"))
        {
            total += long.Parse(count, CultureInfo.InvariantCulture);
            if (total <= maximum) kept.Add(kind); else capped.Add(SourceKind(kind));
        }
        var rows = new List<QueryRow>();
        if (kept.Count == 0) return (rows, capped);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT target_kind, target_guid, referrer_kind, referrer_guid, role, parser_effect, ordinal FROM statement_reference " +
            $"WHERE target_kind IN ({string.Join(",", kept.Select(kind => "'" + kind + "'"))}) " +
            "ORDER BY target_kind, target_guid, referrer_kind, referrer_guid, role, ordinal";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var kind = SourceKind(reader.GetString(0));
            var target = Id(reader.GetString(1));
            var user = Id(reader.GetString(3));
            rows.Add(new QueryRow(kind + "/" + target + "/" + reader.GetString(2) + "/" + user + "/" + reader.GetString(4) + "/" +
                    reader.GetInt32(6).ToString(CultureInfo.InvariantCulture),
                [[kind], [target], [reader.GetString(2)], [user], [reader.GetString(4)], [reader.GetString(5)]]));
        }
        return (rows, capped);
    }

    /// <summary>Names what the facts did not measure, given the uses target kinds the row cap left out.</summary>
    public static QueryCoverage Coverage(SqliteConnection connection, IReadOnlyList<string> cappedKinds)
    {
        var light = IsLight(connection);
        var kinds = new List<string>();
        var classes = new List<string>();
        if (light)
        {
            kinds.AddRange(LightMissingKinds);
            classes.AddRange(LightMissingObjectClasses);
        }
        kinds.AddRange(cappedKinds.Where(kind => !kinds.Contains(kind)));
        return new QueryCoverage(light, kinds, classes);
    }

    private static readonly string[] LightMissingObjectClasses = LightMissingKinds;

    /// <summary>One row per statement-like grammar object with its reference counts.</summary>
    public static IReadOnlyList<QueryRow> ReadObjects(SqliteConnection connection)
    {
        var light = IsLight(connection);
        var references = new Dictionary<(string Kind, string Guid), (long Uses, long Applied)>();
        if (Has(connection, "statement_reference"))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT target_kind, target_guid, COUNT(*), SUM(parser_effect='applied') FROM statement_reference " +
                "WHERE target_kind IN ('environment','naturalClass','exceptionFeature','category','slot','inflectionClass') GROUP BY target_kind, target_guid";
            using var reader = command.ExecuteReader();
            while (reader.Read()) references[(reader.GetString(0), reader.GetString(1))] = (reader.GetInt64(2), reader.GetInt64(3));
        }
        var carriers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var users = new Dictionary<string, long>(StringComparer.Ordinal);
        if (Has(connection, "msa_exception_feature") && Has(connection, "msa"))
            foreach (var (feature, entry, kind, role) in Quads(connection, "SELECT f.target_guid, m.entry_guid, m.kind, f.role FROM msa_exception_feature f JOIN msa m ON m.msa_guid=f.msa_guid"))
            {
                if (kind == "stem")
                {
                    if (role != "required") continue;
                    if (!carriers.TryGetValue(feature, out var set)) carriers.Add(feature, set = new(StringComparer.Ordinal));
                    set.Add(entry);
                }
                else users[feature] = users.GetValueOrDefault(feature) + 1;
            }
        if (Has(connection, "rewrite_rhs_rule_feature"))
            foreach (var (feature, count) in Pairs(connection, "SELECT target_guid, COUNT(*) FROM (SELECT DISTINCT target_guid, rule_guid, polarity FROM rewrite_rhs_rule_feature WHERE target_kind='exceptionFeature') GROUP BY target_guid"))
                users[feature] = users.GetValueOrDefault(feature) + long.Parse(count, CultureInfo.InvariantCulture);
        if (Has(connection, "compound_rule_exception_feature"))
            foreach (var (feature, count) in Pairs(connection, "SELECT target_guid, COUNT(*) FROM (SELECT DISTINCT target_guid, rule_guid, side FROM compound_rule_exception_feature) GROUP BY target_guid"))
                users[feature] = users.GetValueOrDefault(feature) + long.Parse(count, CultureInfo.InvariantCulture);
        var keys = new Dictionary<string, (string? Left, string? Right)>(StringComparer.Ordinal);
        if (Has(connection, "environment_side"))
            foreach (var (environment, side, key) in Triples(connection, "SELECT environment_guid, side, canonical_key FROM environment_side"))
                keys[environment] = side == "left" ? (key, keys.GetValueOrDefault(environment).Right) : (keys.GetValueOrDefault(environment).Left, key);
        var rows = new List<QueryRow>();
        foreach (var (kind, source, table, column) in Targets)
        {
            if (!Has(connection, table)) continue;
            var hasStatus = kind == "environment";
            var labelSql = kind switch
            {
                "environment" => "COALESCE(NULLIF(name,''), representation)",
                "category" or "inflectionClass" => "COALESCE(NULLIF(name,''), NULLIF(abbreviation,''), guid)",
                _ => "COALESCE(NULLIF(name,''), guid)",
            };
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {column}, {labelSql}, {(hasStatus ? "parse_status" : "''")} FROM {table} ORDER BY {column}";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var guid = reader.GetString(0);
                var unmeasured = light && LightMissingObjectClasses.Contains(source);
                var (uses, applied) = references.GetValueOrDefault((kind, guid));
                var feature = kind == "exceptionFeature";
                var key = hasStatus && reader.GetString(2) == "valid" && keys.TryGetValue(guid, out var sides) &&
                    sides is { Left: { } left, Right: { } right } ? SideKey(left, right) : null;
                rows.Add(new QueryRow(Id(guid),
                [
                    [source], [reader.IsDBNull(1) ? "" : reader.GetString(1)], unmeasured ? [] : [Number(uses)], unmeasured ? [] : [Number(applied)],
                    feature ? [Number(carriers.TryGetValue(guid, out var set) ? set.Count : 0)] : [],
                    feature ? [Number(users.GetValueOrDefault(guid))] : [], key is null ? [] : [key],
                ]));
            }
        }
        return rows;
    }

    // The Measure groups on the pair of side keys; the joined key must separate pairs exactly as the pair does.
    private static string SideKey(string left, string right) => left.Contains(KeySeparator, StringComparison.Ordinal) ||
        right.Contains(KeySeparator, StringComparison.Ordinal)
        ? "\0" + left.Length.ToString(CultureInfo.InvariantCulture) + KeySeparator + left + KeySeparator + right
        : left + KeySeparator + right;

    private static string SourceKind(string kind) => Targets.First(t => t.Kind == kind).Source;
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Id(string value) => Guid.TryParse(value, out var guid) ? CanonicalId.FromGuid(guid).Value : value;

    private static void AddDistinct(Dictionary<string, List<string>> target, string key, string value)
    {
        if (!target.TryGetValue(key, out var list)) target.Add(key, list = []);
        if (!list.Contains(value, StringComparer.Ordinal)) list.Add(value);
    }

    private static bool Has(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name";
        command.Parameters.AddWithValue("$name", table);
        return command.ExecuteScalar() is not null;
    }

    private static bool HasColumn(SqliteConnection connection, string table, string column)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM pragma_table_info($table) WHERE name=$name";
        command.Parameters.AddWithValue("$table", table);
        command.Parameters.AddWithValue("$name", column);
        return command.ExecuteScalar() is not null;
    }

    private static Dictionary<string, string> Map(SqliteConnection connection, string table, string sql)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Has(connection, table)) return map;
        foreach (var (key, value) in Pairs(connection, sql)) map[key] = value;
        return map;
    }

    private static IEnumerable<(string, string)> Pairs(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        while (reader.Read()) yield return (reader.GetString(0), reader.GetValue(1) is string s ? s : Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture)!);
    }

    private static IEnumerable<(string, string, string)> Triples(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        while (reader.Read()) yield return (reader.GetString(0), reader.GetString(1), reader.GetString(2));
    }

    private static IEnumerable<(string, string, string, string)> Quads(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        while (reader.Read()) yield return (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
    }
}
