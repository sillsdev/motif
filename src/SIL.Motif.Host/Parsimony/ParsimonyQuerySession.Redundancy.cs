namespace SIL.Motif.Host.Parsimony;

public sealed partial class ParsimonyQuerySession
{
    // The lexeme form is the entry's last allomorph by ordinal (see ReadLexicon); its form is the first.
    private const string LexemeFormsCte = """
        WITH lex AS (
            SELECT a.entry_guid AS entry_guid, a.guid AS allomorph_guid,
                (SELECT f.form FROM facts.allomorph_form AS f WHERE f.allomorph_guid=a.guid ORDER BY f.ordinal LIMIT 1) AS form
            FROM facts.allomorph AS a
            WHERE a.ordinal=(SELECT MAX(x.ordinal) FROM facts.allomorph AS x WHERE x.entry_guid=a.entry_guid))
        """;

    /// <summary>The project's orthographic fold rules for near-duplicate lexemes (<c>[parsimony] fold</c>); none unless set.</summary>
    public IReadOnlyList<SIL.Motif.Contract.Parsimony.FoldRule> LexemeFold { get; set; } = [];

    /// <summary>Reads each entry's lexeme form: O(entries) index probes, no pairing and no parse.</summary>
    internal LexemeFormInput ReadLexemeForms()
    {
        RequireSections("entries", "allomorphs");
        long count;
        using (var command = NewCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM facts.lex_entry;";
            count = (long)command.ExecuteScalar()!;
        }
        var forms = new List<LexemeFormFact>();
        using (var command = NewCommand())
        {
            command.CommandText = LexemeFormsCte + " SELECT entry_guid, allomorph_guid, form FROM lex WHERE form IS NOT NULL AND form<>'';";
            using var reader = command.ExecuteReader();
            while (reader.Read()) forms.Add(new LexemeFormFact(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }
        return new LexemeFormInput(count, forms, LexemeFold);
    }

    /// <summary>Reads each variant link with the main and variant lexeme forms: O(variant links) index probes.</summary>
    internal VariantShapeInput ReadVariantShapes()
    {
        RequireSections("entries", "allomorphs", "environments");
        long count;
        using (var command = NewCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM facts.lex_entry;";
            count = (long)command.ExecuteScalar()!;
        }
        var variants = new List<VariantShapeFact>();
        using (var command = NewCommand())
        {
            command.CommandText = LexemeFormsCte + """
                 SELECT v.variant_entry_guid, vl.allomorph_guid, COALESCE(t.type_guid, ''), ml.form, vl.form,
                     (SELECT group_concat(e.environment_guid, ',') FROM facts.allomorph_environment AS e
                      WHERE e.allomorph_guid=vl.allomorph_guid AND e.role='phone')
                 FROM facts.entry_variant AS v
                 LEFT JOIN facts.sense AS s ON v.component_kind='sense' AND s.sense_guid=v.component_guid
                 JOIN lex AS ml ON ml.entry_guid=COALESCE(s.entry_guid, v.component_guid)
                 JOIN lex AS vl ON vl.entry_guid=v.variant_entry_guid
                 LEFT JOIN facts.entry_variant_type AS t ON t.ref_guid=v.ref_guid AND t.ordinal=0
                 WHERE v.component_kind<>'unresolved' AND ml.form IS NOT NULL AND ml.form<>''
                     AND vl.form IS NOT NULL AND vl.form<>''
                 ORDER BY v.variant_entry_guid, v.ref_guid;
                 """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
                variants.Add(new VariantShapeFact(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.GetString(3), reader.GetString(4),
                    reader.IsDBNull(5) ? [] : reader.GetString(5).Split(',')));
        }
        return new VariantShapeInput(count, variants);
    }

    /// <summary>
    /// Counts, for each tag a rule or affix requires, the stems in that requirer's category scope and how many carry
    /// the tag. Three grouped reads, O(MSAs + rules); no word is parsed.
    /// </summary>
    internal TagPolarityInput ReadTagPolarity()
    {
        RequireSections("categories", "entries", "features", "msas", "phonology");
        var stemsByCategory = new Dictionary<string, long>(StringComparer.Ordinal);
        long stems = 0;
        using (var command = NewCommand())
        {
            command.CommandText = """
                SELECT COALESCE(c.category_guid, ''), COUNT(*) FROM facts.msa AS m
                LEFT JOIN facts.msa_category AS c ON c.msa_guid=m.msa_guid AND c.role='pos'
                WHERE m.kind='stem' GROUP BY 1;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                stemsByCategory[reader.GetString(0)] = reader.GetInt64(1);
                stems += reader.GetInt64(1);
            }
        }
        var carriers = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);
        using (var command = NewCommand())
        {
            command.CommandText = """
                SELECT f.target_guid, COALESCE(c.category_guid, ''), COUNT(DISTINCT m.msa_guid)
                FROM facts.msa_exception_feature AS f
                JOIN facts.msa AS m ON m.msa_guid=f.msa_guid AND m.kind='stem'
                LEFT JOIN facts.msa_category AS c ON c.msa_guid=m.msa_guid AND c.role='pos'
                WHERE f.role='required' GROUP BY 1, 2;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!carriers.TryGetValue(reader.GetString(0), out var byCategory))
                    carriers[reader.GetString(0)] = byCategory = new Dictionary<string, long>(StringComparer.Ordinal);
                byCategory[reader.GetString(1)] = reader.GetInt64(2);
            }
        }
        var requirers = new Dictionary<(string Tag, string Kind, string Guid, string Polarity), HashSet<string>>();
        using (var command = NewCommand())
        {
            command.CommandText = """
                SELECT f.target_guid, f.rule_guid, f.polarity, COALESCE(p.category_guid, '')
                FROM facts.rewrite_rhs_rule_feature AS f
                LEFT JOIN facts.rewrite_rhs_pos AS p ON p.rule_guid=f.rule_guid AND p.rhs_ordinal=f.rhs_ordinal
                WHERE f.target_kind='exceptionFeature';
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read()) Scope(requirers, (reader.GetString(0), "rule", reader.GetString(1), reader.GetString(2)),
                reader.GetString(3));
        }
        using (var command = NewCommand())
        {
            command.CommandText = """
                SELECT f.target_guid, m.msa_guid, COALESCE(c.category_guid, '')
                FROM facts.msa_exception_feature AS f
                JOIN facts.msa AS m ON m.msa_guid=f.msa_guid AND m.kind<>'stem'
                LEFT JOIN facts.msa_category AS c ON c.msa_guid=m.msa_guid AND c.role IN ('pos', 'from_pos')
                WHERE f.role IN ('required', 'from_required');
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read()) Scope(requirers, (reader.GetString(0), "msa", reader.GetString(1), "required"),
                reader.GetString(2));
        }
        var rows = new List<TagPolarityRow>(requirers.Count);
        foreach (var (key, scope) in requirers)
        {
            var wholeProject = scope.Contains("");
            long hosts = 0, withTag = 0;
            carriers.TryGetValue(key.Tag, out var byCategory);
            if (wholeProject)
            {
                hosts = stems;
                withTag = byCategory?.Values.Sum() ?? 0;
            }
            else
                foreach (var category in scope)
                {
                    hosts += stemsByCategory.GetValueOrDefault(category);
                    if (byCategory is not null) withTag += byCategory.GetValueOrDefault(category);
                }
            rows.Add(new TagPolarityRow(key.Tag, key.Kind, key.Guid, key.Polarity, hosts, withTag));
        }
        return new TagPolarityInput(stems, rows);
    }

    private static void Scope(Dictionary<(string Tag, string Kind, string Guid, string Polarity), HashSet<string>> map,
        (string Tag, string Kind, string Guid, string Polarity) key, string category)
    {
        if (!map.TryGetValue(key, out var scope)) map[key] = scope = new HashSet<string>(StringComparer.Ordinal);
        scope.Add(category);
    }
}
