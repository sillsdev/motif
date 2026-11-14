using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Ids;

namespace SIL.Motif.Host.Parsimony;

/// <summary>Reads compact query input from a closed grammar-facts artifact.</summary>
public static class QueryFactsReader
{
    /// <summary>One decoded snapshot, including unresolved variant links omitted from the rows.</summary>
    public sealed record Snapshot(IReadOnlyList<QueryForm> Forms, IReadOnlyList<string> Graphemes,
        int UnresolvedVariants, IReadOnlyList<Stem>? Stems = null);

    /// <summary>One stem MSA retained for subsequent query sources.</summary>
    public sealed record Stem(string Id, string Entry, IReadOnlyList<string> Categories);

    /// <summary>Returns the first vernacular writing system recorded in the artifact.</summary>
    public static string DefaultWritingSystem(string factsPath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = factsPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT writing_system_tag FROM project_writing_system WHERE role='vernacular' ORDER BY ordinal LIMIT 1";
        return command.ExecuteScalar() as string ?? throw new InvalidDataException("The facts have no vernacular writing system.");
    }

    /// <summary>Reads forms and resolves entry and sense variants; distinct bases produce one row each.</summary>
    public static Snapshot Read(string factsPath, string writingSystem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(factsPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(writingSystem);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = factsPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT guid, entry_guid, ordinal, morph_type FROM allomorph ORDER BY entry_guid, ordinal";
        var allomorphs = new List<Allomorph>();
        using (var reader = command.ExecuteReader())
            while (reader.Read()) allomorphs.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3)));
        var forms = new Dictionary<string, string>(StringComparer.Ordinal);
        command.CommandText = "SELECT allomorph_guid, form FROM allomorph_form WHERE writing_system=$ws ORDER BY ordinal";
        command.Parameters.AddWithValue("$ws", writingSystem);
        using (var reader = command.ExecuteReader())
            while (reader.Read()) forms.TryAdd(reader.GetString(0), reader.GetString(1));
        var lexemes = allomorphs.GroupBy(a => a.Entry, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        var environments = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        command.Parameters.Clear();
        command.CommandText = "SELECT a.allomorph_guid, COALESCE(NULLIF(e.representation, ''), a.environment_guid) " +
            "FROM allomorph_environment a LEFT JOIN environment e ON e.guid=a.environment_guid";
        using (var reader = command.ExecuteReader())
            while (reader.Read()) Add(environments, reader.GetString(0), reader.GetString(1));
        var categories = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        command.CommandText = "SELECT m.entry_guid, COALESCE(NULLIF(c.abbreviation, ''), NULLIF(c.name, ''), mc.category_guid) " +
            "FROM msa m JOIN msa_category mc ON mc.msa_guid=m.msa_guid LEFT JOIN category c ON c.guid=mc.category_guid";
        using (var reader = command.ExecuteReader())
            while (reader.Read()) Add(categories, reader.GetString(0), reader.GetString(1));
        var stems = new List<Stem>();
        command.CommandText = "SELECT msa_guid, entry_guid FROM msa WHERE kind='stem'";
        using (var reader = command.ExecuteReader())
            while (reader.Read()) stems.Add(new Stem(reader.GetString(0), reader.GetString(1), Values(categories, reader.GetString(1))));
        var rows = new List<QueryForm>(allomorphs.Count);
        foreach (var item in allomorphs)
        {
            if (!forms.TryGetValue(item.Id, out var form) || !lexemes.TryGetValue(item.Entry, out var lexeme) ||
                !forms.TryGetValue(lexeme.Id, out var basis)) continue;
            rows.Add(new QueryForm(Id(item.Id), form, basis, item.Id == lexeme.Id ? "lexeme" : "alternate", item.Entry,
                item.MorphType, "", Values(environments, item.Id), Values(categories, item.Entry)));
        }
        command.CommandText = "SELECT v.variant_entry_guid, v.ref_guid, v.component_kind, v.component_guid, s.entry_guid, " +
            "(SELECT t.type_guid FROM entry_variant_type t WHERE t.ref_guid=v.ref_guid ORDER BY t.ordinal LIMIT 1) " +
            "FROM entry_variant v LEFT JOIN sense s ON v.component_kind='sense' AND s.sense_guid=v.component_guid";
        var unresolved = 0;
        var variantEntries = new HashSet<string>(StringComparer.Ordinal);
        var variantBases = new HashSet<(string FormId, string Base)>();
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                var entry = reader.GetString(0);
                variantEntries.Add(entry);
                var kind = reader.GetString(2);
                var main = kind == "entry" ? reader.GetString(3) : kind == "sense" && !reader.IsDBNull(4) ? reader.GetString(4) : null;
                if (main is null) { unresolved++; continue; }
                if (!lexemes.TryGetValue(entry, out var variant) || !lexemes.TryGetValue(main, out var primary) ||
                    !forms.TryGetValue(variant.Id, out var form) || !forms.TryGetValue(primary.Id, out var basis)) continue;
                if (!variantBases.Add((variant.Id, basis))) continue;
                rows.Add(new QueryForm(Id(variant.Id), form, basis, "variant", entry, variant.MorphType,
                    reader.IsDBNull(5) ? "" : reader.GetString(5), Values(environments, variant.Id), Values(categories, entry)));
            }
        rows.RemoveAll(row => row.Kind == "lexeme" && variantEntries.Contains(row.Entry));
        command.CommandText = "SELECT DISTINCT grapheme FROM phoneme_grapheme WHERE writing_system=$ws AND grapheme<>''";
        command.Parameters.AddWithValue("$ws", writingSystem);
        var graphemes = new List<string>();
        using (var reader = command.ExecuteReader()) while (reader.Read()) graphemes.Add(reader.GetString(0));
        return new Snapshot(rows, graphemes, unresolved, stems);
    }

    private static string Id(string value) => Guid.TryParse(value, out var guid) ? CanonicalId.FromGuid(guid).Value : value;

    private sealed record Allomorph(string Id, string Entry, int Ordinal, string MorphType);
    private static void Add(Dictionary<string, List<string>> target, string key, string value)
    {
        if (!target.TryGetValue(key, out var list)) target.Add(key, list = []);
        if (!list.Contains(value, StringComparer.Ordinal)) list.Add(value);
    }
    private static IReadOnlyList<string> Values(Dictionary<string, List<string>> source, string key) =>
        source.TryGetValue(key, out var values) ? values : [];
}
