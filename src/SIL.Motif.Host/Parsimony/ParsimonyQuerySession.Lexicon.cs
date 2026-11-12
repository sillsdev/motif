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

    private const string Marks = "' -=' || char(9) || char(10) || char(13)";

    /// <summary>
    /// Reads the gloss and form pairs a gloss could echo, by letting SQLite drop every pair whose gloss does not start
    /// with the form. SQLite lower() is ASCII only, so a pair with any other character is kept for the exact check.
    /// </summary>
    internal GlossEchoInput ReadGlossEchoCandidates()
    {
        RequireSections("entries", "allomorphs");
        long glossed;
        using (var command = NewCommand())
        {
            command.CommandText = $"""
                SELECT COUNT(DISTINCT sense_guid) FROM facts.sense_text
                WHERE kind='gloss' AND trim(text, {Marks})<>'';
                """;
            glossed = (long)command.ExecuteScalar()!;
        }
        var candidates = new List<GlossEchoCandidate>();
        using (var command = NewCommand())
        {
            command.CommandText = $"""
                SELECT DISTINCT t.sense_guid, s.entry_guid,
                    (SELECT x.guid FROM facts.allomorph AS x WHERE x.entry_guid=s.entry_guid ORDER BY x.ordinal DESC LIMIT 1),
                    t.text, f.form
                FROM facts.sense_text AS t
                JOIN facts.sense AS s ON s.sense_guid=t.sense_guid
                JOIN facts.allomorph AS a ON a.entry_guid=s.entry_guid
                JOIN facts.allomorph_form AS f ON f.allomorph_guid=a.guid
                WHERE t.kind='gloss' AND (
                    (trim(f.form, {Marks})<>'' AND instr(lower(trim(t.text, {Marks})), lower(trim(f.form, {Marks})))=1)
                    OR f.form GLOB '*[^ -~]*' OR t.text GLOB '*[^ -~]*');
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
                candidates.Add(new GlossEchoCandidate(reader.GetString(1), reader.GetString(0),
                    reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        }
        return new GlossEchoInput(glossed, candidates);
    }

    /// <summary>
    /// Reads every lexical entry with its lexeme form, glosses and MSA signatures. The lexeme form is the entry's
    /// last allomorph by ordinal, because the snapshot orders alternate forms first (pg-snapshot lexicon.rs).
    /// </summary>
    internal LexiconInput ReadLexicon()
    {
        RequireSections("entries", "allomorphs", "msas");
        var entries = new Dictionary<string, LexiconEntryBuilder>(StringComparer.Ordinal);
        using (var command = NewCommand())
        {
            command.CommandText = "SELECT guid, lexeme_morph_type FROM facts.lex_entry;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) entries[reader.GetString(0)] = new LexiconEntryBuilder(reader.GetString(1));
        }
        using (var command = NewCommand())
        {
            command.CommandText = """
                SELECT a.entry_guid, a.guid, f.writing_system, f.form
                FROM facts.allomorph AS a
                LEFT JOIN facts.allomorph_form AS f ON f.allomorph_guid=a.guid
                WHERE a.ordinal=(SELECT MAX(x.ordinal) FROM facts.allomorph AS x WHERE x.entry_guid=a.entry_guid)
                ORDER BY a.entry_guid, f.ordinal;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!entries.TryGetValue(reader.GetString(0), out var entry)) continue;
                entry.Lexeme = reader.GetString(1);
                if (!reader.IsDBNull(2)) entry.Forms.Add(new LexiconTextFact(reader.GetString(2), reader.GetString(3)));
            }
        }
        using (var command = NewCommand())
        {
            command.CommandText = """
                SELECT s.entry_guid, t.writing_system, t.text FROM facts.sense AS s
                LEFT JOIN facts.sense_text AS t ON t.sense_guid=s.sense_guid AND t.kind='gloss';
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!entries.TryGetValue(reader.GetString(0), out var entry)) continue;
                entry.HasSense = true;
                if (!reader.IsDBNull(1)) entry.Glosses.Add(new LexiconTextFact(reader.GetString(1), reader.GetString(2)));
            }
        }
        using (var command = NewCommand())
        {
            command.CommandText = """
                SELECT m.entry_guid, m.kind,
                    (SELECT group_concat(v, '|') FROM (SELECT c.role || ':' || c.category_guid AS v
                        FROM facts.msa_category AS c WHERE c.msa_guid=m.msa_guid ORDER BY c.role, c.category_guid)),
                    (SELECT group_concat(v, '|') FROM (SELECT l.role || ':' || l.slot_guid AS v
                        FROM facts.msa_slot AS l WHERE l.msa_guid=m.msa_guid ORDER BY l.role, l.slot_guid))
                FROM facts.msa AS m;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (entries.TryGetValue(reader.GetString(0), out var entry))
                    entry.Msas.Add($"{reader.GetString(1)}|c={(reader.IsDBNull(2) ? "" : reader.GetString(2))}|s={(reader.IsDBNull(3) ? "" : reader.GetString(3))}");
        }
        var built = new LexiconEntryFact[entries.Count];
        var index = 0;
        foreach (var (guid, entry) in entries) built[index++] = entry.Build(guid);
        return new LexiconInput(built);
    }

    private sealed class LexiconEntryBuilder(string morphType)
    {
        public string? Lexeme { get; set; }
        public bool HasSense { get; set; }
        public List<LexiconTextFact> Forms { get; } = [];
        public List<LexiconTextFact> Glosses { get; } = [];
        public List<string> Msas { get; } = [];

        public LexiconEntryFact Build(string guid) =>
            new(guid, morphType, Lexeme, Forms, HasSense, Glosses, Msas);
    }
}
