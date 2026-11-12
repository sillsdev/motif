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

    /// <summary>
    /// Reads every lexical entry with its lexeme form, allomorph forms and senses. The lexeme form is the entry's
    /// last allomorph by ordinal, because the snapshot orders alternate forms first (pg-snapshot lexicon.rs).
    /// </summary>
    /// <param name="withMsas">Whether to read each entry's MSA signatures as well.</param>
    internal LexiconInput ReadLexicon(bool withMsas)
    {
        RequireSections(withMsas ? new[] { "entries", "allomorphs", "msas" } : new[] { "entries", "allomorphs" });
        var entries = new Dictionary<string, LexiconEntryBuilder>(StringComparer.Ordinal);
        using (var command = NewCommand())
        {
            command.CommandText = "SELECT guid, lexeme_morph_type FROM facts.lex_entry;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) entries[reader.GetString(0)] = new LexiconEntryBuilder(reader.GetString(0), reader.GetString(1));
        }
        var allomorphs = new Dictionary<string, (LexiconEntryBuilder Entry, int Ordinal)>(StringComparer.Ordinal);
        using (var command = NewCommand())
        {
            command.CommandText = "SELECT guid, entry_guid, ordinal FROM facts.allomorph;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (entries.TryGetValue(reader.GetString(1), out var entry))
                {
                    allomorphs[reader.GetString(0)] = (entry, reader.GetInt32(2));
                    entry.AddAllomorph(reader.GetString(0), reader.GetInt32(2));
                }
        }
        using (var command = NewCommand())
        {
            command.CommandText = "SELECT allomorph_guid, ordinal, writing_system, form FROM facts.allomorph_form;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (allomorphs.TryGetValue(reader.GetString(0), out var owner))
                    owner.Entry.AddForm(reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3));
        }
        var senses = new Dictionary<string, LexiconSenseBuilder>(StringComparer.Ordinal);
        using (var command = NewCommand())
        {
            command.CommandText = "SELECT sense_guid, entry_guid FROM facts.sense;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (entries.TryGetValue(reader.GetString(1), out var entry))
                    entry.Senses.Add(senses[reader.GetString(0)] = new LexiconSenseBuilder(reader.GetString(0)));
        }
        using (var command = NewCommand())
        {
            command.CommandText = "SELECT sense_guid, ordinal, writing_system, text FROM facts.sense_text WHERE kind='gloss';";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (senses.TryGetValue(reader.GetString(0), out var sense))
                    sense.Glosses.Add((reader.GetInt32(1), new LexiconTextFact(reader.GetString(2), reader.GetString(3))));
        }
        if (withMsas) ReadMsaSignatures(entries);
        return new LexiconInput(Array.AsReadOnly(entries.Values.Select(entry => entry.Build()).ToArray()));
    }

    private void ReadMsaSignatures(Dictionary<string, LexiconEntryBuilder> entries)
    {
        var parts = new Dictionary<string, (LexiconEntryBuilder Entry, string Kind, List<string> Details)>(StringComparer.Ordinal);
        using (var command = NewCommand())
        {
            command.CommandText = "SELECT msa_guid, entry_guid, kind FROM facts.msa;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (entries.TryGetValue(reader.GetString(1), out var entry))
                    parts[reader.GetString(0)] = (entry, reader.GetString(2), []);
        }
        foreach (var (sql, tag) in new[]
                 {
                     ("SELECT msa_guid, role, category_guid FROM facts.msa_category;", "category"),
                     ("SELECT msa_guid, role, slot_guid FROM facts.msa_slot;", "slot"),
                 })
        {
            using var command = NewCommand();
            command.CommandText = sql;
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (parts.TryGetValue(reader.GetString(0), out var part))
                    part.Details.Add($"{tag}:{reader.GetString(1)}:{reader.GetString(2)}");
        }
        foreach (var (entry, kind, details) in parts.Values)
        {
            details.Sort(StringComparer.Ordinal);
            entry.MsaSignatures.Add(kind + "|" + string.Join("|", details));
        }
    }

    private sealed class LexiconEntryBuilder(string guid, string morphType)
    {
        private readonly List<(string Guid, int Ordinal)> _allomorphs = [];
        private readonly List<(string Guid, int Ordinal, string WritingSystem, string Form)> _forms = [];

        public List<LexiconSenseBuilder> Senses { get; } = [];
        public List<string> MsaSignatures { get; } = [];

        public void AddAllomorph(string allomorph, int ordinal) => _allomorphs.Add((allomorph, ordinal));

        public void AddForm(string allomorph, int ordinal, string writingSystem, string form) =>
            _forms.Add((allomorph, ordinal, writingSystem, form));

        public LexiconEntryFact Build()
        {
            var lexeme = _allomorphs.Count == 0 ? null : _allomorphs.MaxBy(item => item.Ordinal).Guid;
            var lexemeForms = _forms.Where(item => item.Guid == lexeme).OrderBy(item => item.Ordinal)
                .Select(item => new LexiconTextFact(item.WritingSystem, item.Form)).ToArray();
            var senses = Senses.OrderBy(item => item.Guid, StringComparer.Ordinal).Select(item => item.Build()).ToArray();
            return new LexiconEntryFact(guid, morphType, lexeme, lexemeForms,
                _forms.Select(item => item.Form).ToArray(), senses,
                MsaSignatures.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        }
    }

    private sealed class LexiconSenseBuilder(string guid)
    {
        public string Guid => guid;
        public List<(int Ordinal, LexiconTextFact Gloss)> Glosses { get; } = [];

        public LexiconSenseFact Build() =>
            new(guid, Glosses.OrderBy(item => item.Ordinal).Select(item => item.Gloss).ToArray());
    }
}
