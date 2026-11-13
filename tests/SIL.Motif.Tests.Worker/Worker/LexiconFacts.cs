using Microsoft.Data.Sqlite;

namespace SIL.Motif.Tests.Worker;

/// <summary>
/// An authored lexicon written into a copy of the grammar facts: entries, allomorph forms, MSAs and senses.
/// It replaces whatever lexicon the copy held, so a fixture names exactly the defect it seeds.
/// </summary>
internal sealed class LexiconSeed
{
    private readonly List<(string Guid, string MorphType)> _entries = [];
    private readonly List<(string Guid, string Entry, int Ordinal, string FormClass, string[] Forms)> _allomorphs = [];
    private readonly List<(string Guid, string Entry, string Kind, string[] Categories, string[] Slots)> _msas = [];
    private readonly List<(string Guid, string Entry, string[] Glosses)> _senses = [];

    public int EntryCount => _entries.Count;

    /// <summary>Adds an entry whose lexeme form is its last allomorph, as the snapshot orders them.</summary>
    public LexiconSeed Entry(string guid, string morphType, params string[] lexemeForms)
    {
        _entries.Add((guid, morphType));
        _allomorphs.Add((guid + "-a0", guid, 0, FormClassOf(morphType), lexemeForms));
        return this;
    }

    /// <summary>Adds an entry whose lexeme form is a process with no form text.</summary>
    public LexiconSeed ProcessEntry(string guid, string morphType)
    {
        _entries.Add((guid, morphType));
        _allomorphs.Add((guid + "-a0", guid, 0, "process", []));
        return this;
    }

    /// <summary>Adds an alternate form that sorts before the lexeme form.</summary>
    public LexiconSeed Alternate(string entry, params string[] forms)
    {
        var index = _allomorphs.FindIndex(item => item.Entry == entry);
        var next = _allomorphs.Count(item => item.Entry == entry);
        // The alternate takes ordinal 0 and the lexeme form moves last.
        for (var i = 0; i < _allomorphs.Count; i++)
            if (_allomorphs[i].Entry == entry)
                _allomorphs[i] = _allomorphs[i] with { Ordinal = _allomorphs[i].Ordinal + 1 };
        _allomorphs.Add((entry + "-alt" + next, entry, 0, _allomorphs[index].FormClass, forms));
        return this;
    }

    public LexiconSeed Msa(string guid, string entry, string kind, string[]? categories = null, string[]? slots = null)
    {
        _msas.Add((guid, entry, kind, categories ?? [], slots ?? []));
        return this;
    }

    public LexiconSeed Sense(string guid, string entry, params string[] glosses)
    {
        _senses.Add((guid, entry, glosses));
        return this;
    }

    private static string FormClassOf(string morphType) => morphType switch
    {
        "prefix" or "suffix" or "infix" or "circumfix" or "proclitic" or "enclitic" or "clitic" => "affix",
        _ => "stem",
    };

    /// <summary>Replaces the lexicon in the facts file and marks the lexicon sections complete.</summary>
    public void WriteTo(string factsPath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(factsPath),
            Pooling = false,
            ForeignKeys = false,
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        foreach (var table in new[]
                 {
                     "sense_text", "sense", "msa_category", "msa_slot", "msa_inflection_class", "msa_stem_name",
                     "msa_exception_feature", "msa", "allomorph_form", "allomorph_gate", "allomorph_environment",
                     "allomorph", "entry_citation_form", "lex_entry",
                 })
            Run(connection, transaction, $"DELETE FROM {table};");
        Run(connection, transaction, "INSERT OR IGNORE INTO writing_system(tag) VALUES ('qaa'), ('en'), ('fr');");
        Insert(connection, transaction, "INSERT INTO lex_entry(guid, source_ordinal, lexeme_morph_type) VALUES ($a, $b, $c);",
            _entries.Select((item, index) => new object[] { item.Guid, index, item.MorphType }));
        var morphTypes = _entries.ToDictionary(item => item.Guid, item => item.MorphType, StringComparer.Ordinal);
        Insert(connection, transaction,
            "INSERT INTO allomorph(guid, entry_guid, ordinal, morph_type, form_class, is_abstract) VALUES ($a, $b, $c, $d, $e, 0);",
            _allomorphs.Select(item => new object[]
            {
                item.Guid, item.Entry, item.Ordinal, morphTypes[item.Entry],
                item.FormClass,
            }));
        Insert(connection, transaction,
            "INSERT INTO allomorph_form(allomorph_guid, ordinal, writing_system, form) VALUES ($a, $b, 'qaa', $c);",
            _allomorphs.SelectMany(item => item.Forms.Select((form, index) => new object[] { item.Guid, index, form })));
        Insert(connection, transaction, "INSERT INTO msa(msa_guid, entry_guid, kind) VALUES ($a, $b, $c);",
            _msas.Select(item => new object[] { item.Guid, item.Entry, item.Kind }));
        Insert(connection, transaction,
            "INSERT INTO msa_category(msa_guid, role, ordinal, category_guid) VALUES ($a, $b, $c, $d);",
            _msas.SelectMany(item => item.Categories.Select((category, index) =>
            {
                var parts = category.Split(':');
                return new object[] { item.Guid, parts.Length > 1 ? parts[0] : "pos", index, parts[^1] };
            })));
        Insert(connection, transaction,
            "INSERT INTO msa_slot(msa_guid, role, ordinal, slot_guid) VALUES ($a, 'slot', $b, $c);",
            _msas.SelectMany(item => item.Slots.Select((slot, index) => new object[] { item.Guid, index, slot })));
        Insert(connection, transaction, "INSERT INTO sense(sense_guid, entry_guid, msa_guid) VALUES ($a, $b, NULL);",
            _senses.Select(item => new object[] { item.Guid, item.Entry }));
        Insert(connection, transaction,
            "INSERT INTO sense_text(sense_guid, kind, ordinal, writing_system, text) VALUES ($a, 'gloss', $b, 'en', $c);",
            _senses.SelectMany(item => item.Glosses.Select((gloss, index) => new object[] { item.Guid, index, gloss })));
        Run(connection, transaction,
            "UPDATE artifact_section SET status='complete', reason_code=NULL WHERE section IN ('entries', 'msas', 'allomorphs');");
        transaction.Commit();
    }

    private static void Run(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void Insert(SqliteConnection connection, SqliteTransaction transaction, string sql,
        IEnumerable<object[]> rows)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        var names = new[] { "$a", "$b", "$c", "$d", "$e" }.Where(name => sql.Contains(name, StringComparison.Ordinal)).ToArray();
        foreach (var name in names) command.Parameters.Add(new SqliteParameter(name, null));
        command.Prepare();
        foreach (var row in rows)
        {
            for (var i = 0; i < names.Length; i++) command.Parameters[names[i]].Value = row[i];
            command.ExecuteNonQuery();
        }
    }
}
