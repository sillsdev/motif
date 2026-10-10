using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace SIL.Motif.Host.Parsimony;

public sealed partial class ParsimonyQuerySession
{
    // The RFC 8785 text of each MSA structure; gate signatures and feature effects compare it byte for byte.
    private IReadOnlyList<(string MsaGuid, string Role, string Json)> ReadMsaFeatureStructureTexts(string? msaGuid)
    {
        var assignments = new Dictionary<int, List<(int Ordinal, string Feature, string Kind, string? ValueGuid,
            int? ChildFsId)>>();
        var roots = new List<(string MsaGuid, string Role, int FsId)>();
        var rootIds = new HashSet<int>();
        using (var command = NewCommand())
        {
            command.CommandText = """
                SELECT s.fs_id, s.owner_guid, s.role, s.path, a.ordinal, a.feature_guid, a.value_kind,
                       a.value_guid, a.child_fs_id
                FROM facts.feature_structure AS s
                LEFT JOIN facts.feature_assignment AS a ON a.fs_id=s.fs_id
                WHERE s.owner_kind='msa' AND ($filter IS NULL OR s.owner_guid=$filter)
                ORDER BY s.owner_guid, s.role, s.fs_id, a.ordinal;
                """;
            command.Parameters.AddWithValue("$filter", (object?)msaGuid ?? DBNull.Value);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var fsId = reader.GetInt32(0);
                // The join repeats each structure row once per assignment, so a root is recorded once.
                if (reader.GetString(3) == "root" && rootIds.Add(fsId))
                    roots.Add((reader.GetString(1), reader.GetString(2), fsId));
                if (reader.IsDBNull(4)) continue;
                if (!assignments.TryGetValue(fsId, out var items))
                    assignments[fsId] = items = [];
                items.Add((reader.GetInt32(4), reader.GetString(5), reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetInt32(8)));
            }
        }

        var result = new List<(string MsaGuid, string Role, string Json)>(roots.Count);
        foreach (var root in roots)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }))
            {
                WriteFeatureStructure(writer, root.FsId, assignments);
            }
            result.Add((root.MsaGuid, root.Role, Encoding.UTF8.GetString(stream.ToArray())));
        }
        return result;
    }

    private static void WriteFeatureStructure(Utf8JsonWriter writer, int fsId,
        Dictionary<int, List<(int Ordinal, string Feature, string Kind, string? ValueGuid, int? ChildFsId)>> assignments)
    {
        writer.WriteStartObject();
        writer.WriteStartArray("values");
        if (assignments.TryGetValue(fsId, out var items))
        {
            foreach (var item in items.OrderBy(entry => entry.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("feature", item.Feature);
                writer.WriteStartObject("value");
                if (item.Kind == "closed")
                {
                    writer.WriteString("kind", "closed");
                    writer.WriteString("value", item.ValueGuid);
                }
                else
                {
                    writer.WriteString("kind", "complex");
                    writer.WritePropertyName("value");
                    WriteFeatureStructure(writer, item.ChildFsId!.Value, assignments);
                }
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
