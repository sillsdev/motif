namespace SIL.Motif.Host.Parsimony;

public sealed partial class ParsimonyQuerySession
{
    /// <summary>Reads every stem MSA that has no part-of-speech row, and whether its entry's senses have glosses.</summary>
    internal StemMsaInput ReadStemMsaCategories()
    {
        RequireSections("entries", "msas");
        long count;
        using (var command = NewCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM facts.msa WHERE kind='stem';";
            count = (long)command.ExecuteScalar()!;
        }
        var missing = new List<(string Msa, string Entry)>();
        using (var command = NewCommand())
        {
            command.CommandText = """
                SELECT m.msa_guid, m.entry_guid FROM facts.msa AS m
                WHERE m.kind='stem' AND NOT EXISTS (
                    SELECT 1 FROM facts.msa_category AS c WHERE c.msa_guid=m.msa_guid AND c.role='pos');
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read()) missing.Add((reader.GetString(0), reader.GetString(1)));
        }
        var glossed = new HashSet<string>(StringComparer.Ordinal);
        if (missing.Count > 0)
        {
            // One pass over the glosses, because the sense table has no entry index to probe per MSA.
            using var command = NewCommand();
            command.CommandText = """
                SELECT DISTINCT s.entry_guid FROM facts.sense AS s
                JOIN facts.sense_text AS t ON t.sense_guid=s.sense_guid
                WHERE t.kind='gloss' AND trim(t.text)<>'';
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read()) glossed.Add(reader.GetString(0));
        }
        return new StemMsaInput(count, Array.AsReadOnly(missing
            .Select(item => new UncategorisedStemMsa(item.Msa, item.Entry, glossed.Contains(item.Entry))).ToArray()));
    }
}
