namespace SIL.Motif.Host.Parsimony;

public sealed partial class ParsimonyQuerySession
{
    internal LiteralHostInput ReadLiteralHostInput()
    {
        RequireSections("allomorphs", "environments", "patterns");
        var rulesRead = IsSectionReadable("phonology");
        var sides = ReadContextSides(rulesRead);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var command = NewCommand())
        {
            command.CommandText = """
                SELECT guid, coalesce(nullif(trim(name), ''), representation) FROM facts.environment
                UNION ALL SELECT guid, name FROM facts.phonological_rule;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read()) names[reader.GetString(0)] = reader.GetString(1);
        }
        return new LiteralHostInput(sides, names, ReadEnvironmentUses(), rulesRead);
    }

    // One row per node of every environment side and, when readable, every rewrite-rule left or right context.
    private IReadOnlyList<ContextSideFact> ReadContextSides(bool rulesRead)
    {
        var sides = new List<ContextSideFact>();
        using var command = NewCommand();
        command.CommandText = """
            SELECT r.root_id, r.owner_kind, r.owner_guid, r.role, r.ordinal,
                   n.node_id, n.parent_node_id, n.ordinal, n.kind,
                   CASE WHEN n.kind='phoneme' THEN coalesce(ph.basic_ipa_symbol, ph.name) ELSE n.token_text END
            FROM facts.pattern_root AS r
            JOIN facts.pattern_node AS n ON n.root_id=r.root_id
            LEFT JOIN facts.phoneme AS ph ON ph.guid=n.phoneme_guid
            WHERE (r.owner_kind='environment' AND r.role IN ('environment_left', 'environment_right'))
               OR ($rules=1 AND r.owner_kind='phonologicalRule'
                   AND r.role IN ('rewrite_left_context', 'rewrite_right_context'))
            ORDER BY r.root_id, n.node_id;
            """;
        command.Parameters.AddWithValue("$rules", rulesRead ? 1 : 0);
        using var reader = command.ExecuteReader();
        long? current = null;
        var key = (Kind: "", Guid: "", Side: "", Ordinal: 0);
        var nodes = new List<ContextNodeFact>();
        void Flush()
        {
            if (current is not null) sides.Add(new ContextSideFact(key.Kind, key.Guid, key.Side, key.Ordinal, nodes));
            nodes = [];
        }
        while (reader.Read())
        {
            if (reader.GetInt64(0) != current)
            {
                Flush();
                current = reader.GetInt64(0);
                key = (reader.GetString(1), reader.GetString(2),
                    reader.GetString(3).Contains("left", StringComparison.Ordinal) ? "left" : "right",
                    reader.GetInt32(4));
            }
            nodes.Add(new ContextNodeFact(reader.GetInt64(5), reader.IsDBNull(6) ? null : reader.GetInt64(6),
                reader.GetInt32(7), reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetString(9)));
        }
        Flush();
        return sides;
    }
}
