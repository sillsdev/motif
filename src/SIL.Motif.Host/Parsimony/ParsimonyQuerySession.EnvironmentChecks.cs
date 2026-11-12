namespace SIL.Motif.Host.Parsimony;

public sealed partial class ParsimonyQuerySession
{
    internal EnvironmentDuplicateInput ReadEnvironmentDuplicateInput()
    {
        RequireSections("allomorphs", "environments");
        return new EnvironmentDuplicateInput(ReadEnvironmentSides(), ReadEnvironmentUses());
    }

    private IReadOnlyList<EnvironmentSideFact> ReadEnvironmentSides()
    {
        var rows = new List<EnvironmentSideFact>();
        using var command = NewCommand();
        command.CommandText = """
            SELECT e.guid, e.name, e.representation, e.parse_status, l.canonical_key, r.canonical_key
            FROM facts.environment AS e
            LEFT JOIN facts.environment_side AS l ON l.environment_guid=e.guid AND l.side='left'
            LEFT JOIN facts.environment_side AS r ON r.environment_guid=e.guid AND r.side='right'
            ORDER BY e.guid;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
            rows.Add(new EnvironmentSideFact(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        return rows;
    }

    // Both roles count as use: an environment used only as an infix Position is still used.
    private IReadOnlyList<EnvironmentUseFact> ReadEnvironmentUses()
    {
        var rows = new List<EnvironmentUseFact>();
        using var command = NewCommand();
        command.CommandText = """
            SELECT environment_guid, allomorph_guid, role
            FROM facts.allomorph_environment
            ORDER BY environment_guid, allomorph_guid, role, ordinal;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
            rows.Add(new EnvironmentUseFact(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return rows;
    }
}
