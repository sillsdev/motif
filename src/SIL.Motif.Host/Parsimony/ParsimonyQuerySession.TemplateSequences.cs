using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Parsimony;

namespace SIL.Motif.Host.Parsimony;

internal sealed record TemplateMeasureSlot(string Guid, string Name, string Side, int Ordinal,
    int? CompiledOrder, bool? Optional, IReadOnlySet<string> MsaGuids);

internal sealed record TemplateMeasure(string Guid, string CategoryGuid, string Name, bool Disabled,
    IReadOnlyList<TemplateMeasureSlot> Slots);

internal sealed record TemplateMeasureAnalysis(ParsimonyApprovedMorphSequenceViewRow Evidence,
    IReadOnlyList<string> RootCategoryGuids);

internal sealed record TemplateSequenceMeasureFacts(IReadOnlyList<TemplateMeasure> Templates,
    IReadOnlyDictionary<string, string?> CategoryParents,
    IReadOnlyDictionary<string, IReadOnlySet<string>> MsaSlots,
    IReadOnlyDictionary<string, string> MsaKinds,
    IReadOnlyDictionary<string, IReadOnlyList<string>> MsaCategories,
    IReadOnlyList<TemplateMeasureAnalysis> Analyses);

public sealed partial class ParsimonyQuerySession
{
    internal IReadOnlySet<string> ReadCompiledMorphRuleMsas()
    {
        RequireSections("compiled_mappings");
        var result = new HashSet<string>(StringComparer.Ordinal);
        using var command = NewCommand();
        command.CommandText = "SELECT DISTINCT source_guid FROM facts.compiled_mapping " +
            "WHERE source_kind='msa' AND source_guid IS NOT NULL " +
            "AND output_id IN (SELECT output_id FROM facts.compiled_output WHERE kind='morph_rule') " +
            "ORDER BY source_guid;";
        using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add(reader.GetString(0));
        return result;
    }

    internal IReadOnlyDictionary<string, string> ReadAdhocProhibitionLoaderReasons()
    {
        RequireSections("adhoc_prohibitions", "load_accounting");
        var reasons = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        using var command = NewCommand();
        command.CommandText = """
            SELECT p.prohibition_guid, f.reason_code
            FROM facts.adhoc_prohibition AS p
            JOIN facts.load_fact AS f ON f.subject_guid=p.prohibition_guid
             AND f.subject_kind=CASE p.kind WHEN 'allomorph' THEN 'allomorphCoOccurrence'
                                            ELSE 'morphemeCoOccurrence' END
            WHERE f.pipeline_stage=CASE
                WHEN EXISTS (SELECT 1 FROM facts.load_fact AS compact
                    WHERE compact.subject_guid=p.prohibition_guid
                      AND compact.subject_kind=f.subject_kind AND compact.pipeline_stage='compact')
                THEN 'compact' ELSE 'compile' END
            ORDER BY p.prohibition_guid, f.reason_code;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var guid = reader.GetString(0);
            if (!reasons.TryGetValue(guid, out var values))
                reasons.Add(guid, values = new SortedSet<string>(StringComparer.Ordinal));
            values.Add(reader.GetString(1));
        }
        return reasons.ToDictionary(pair => pair.Key, pair => string.Join(", ", pair.Value),
            StringComparer.Ordinal);
    }

    internal TemplateSequenceMeasureFacts ReadTemplateSequenceFacts()
    {
        RequireSections("categories", "templates", "msas", "entries", "allomorphs");
        var categoryParents = ReadCategoryParents();
        var msaSlots = ReadMsaSlots();
        var msaKinds = ReadMsaKinds();
        var templates = ReadTemplates(msaSlots);
        var analyses = ReadApprovedSequences();
        var msaCategories = ReadPartOfSpeechCategories();
        var cases = analyses.Select(analysis => new TemplateMeasureAnalysis(analysis,
            Array.AsReadOnly(analysis.Morphs.Where(morph => morph.MsaKind == "stem")
                .SelectMany(morph => msaCategories.GetValueOrDefault(morph.MsaGuid ?? string.Empty) ?? [])
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()))).ToArray();
        return new TemplateSequenceMeasureFacts(templates, categoryParents, msaSlots, msaKinds, msaCategories,
            Array.AsReadOnly(cases));
    }

    internal IReadOnlyList<ParsimonyApprovedMorphSequenceViewRow> ReadApprovedMorphSequences(
        string? analysisGuid = null) => ReadTemplateSequenceFacts().Analyses
        .Select(item => item.Evidence)
        .Where(item => analysisGuid is null || item.AnalysisGuid == analysisGuid)
        .ToArray();

    /// <summary>Reads one slot with its inflectional MSA memberships and template positions.</summary>
    public ParsimonySlotContextViewRow? ReadSlotContext(string guid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guid);
        RequireSections("categories", "templates", "msas");
        string? name = null;
        string? categoryGuid = null;
        bool optional = false;
        using (var command = NewCommand())
        {
            command.CommandText = "SELECT name, category_guid, optional FROM facts.affix_slot WHERE guid=$guid;";
            command.Parameters.AddWithValue("$guid", guid);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            name = reader.GetString(0);
            categoryGuid = reader.GetString(1);
            optional = reader.GetInt32(2) != 0;
        }
        var msaGuids = new List<string>();
        using (var command = NewCommand())
        {
            command.CommandText = """
                SELECT s.msa_guid FROM facts.msa_slot AS s
                JOIN facts.msa AS m ON m.msa_guid=s.msa_guid
                WHERE s.slot_guid=$guid AND s.role='slot' AND m.kind='inflectional'
                ORDER BY s.msa_guid;
                """;
            command.Parameters.AddWithValue("$guid", guid);
            using var reader = command.ExecuteReader();
            while (reader.Read()) msaGuids.Add(reader.GetString(0));
        }
        var templateUses = new List<ParsimonyTemplateSlotUse>();
        using (var command = NewCommand())
        {
            command.CommandText = """
                SELECT t.guid, t.name, t.category_guid, s.side, s.ordinal, s.compiled_order
                FROM facts.template_slot AS s
                JOIN facts.affix_template AS t ON t.guid=s.template_guid
                WHERE s.slot_guid=$guid
                ORDER BY t.guid, CASE s.side WHEN 'prefix' THEN 0 ELSE 1 END,
                         CASE WHEN s.compiled_order IS NULL THEN 1 ELSE 0 END,
                         CASE WHEN s.side='prefix' THEN -s.compiled_order ELSE s.compiled_order END, s.ordinal;
                """;
            command.Parameters.AddWithValue("$guid", guid);
            using var reader = command.ExecuteReader();
            while (reader.Read())
                templateUses.Add(new ParsimonyTemplateSlotUse(reader.GetString(0), reader.GetString(1),
                    reader.GetString(2), reader.GetString(3), reader.GetInt32(4),
                    reader.IsDBNull(5) ? null : reader.GetInt32(5)));
        }
        return new ParsimonySlotContextViewRow(guid, name!, categoryGuid!,
            optional, Array.AsReadOnly(msaGuids.ToArray()), Array.AsReadOnly(templateUses.ToArray()));
    }

    private Dictionary<string, string?> ReadCategoryParents()
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        using var command = NewCommand();
        command.CommandText = "SELECT guid, parent_guid FROM facts.category ORDER BY guid;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            result.Add(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
        return result;
    }

    private Dictionary<string, IReadOnlySet<string>> ReadMsaSlots()
    {
        var values = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        using var command = NewCommand();
        command.CommandText = """
            SELECT s.msa_guid, s.slot_guid
            FROM facts.msa_slot AS s
            JOIN facts.msa AS m ON m.msa_guid=s.msa_guid
            WHERE s.role='slot' AND m.kind='inflectional'
            ORDER BY s.slot_guid, s.msa_guid;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var msaGuid = reader.GetString(0);
            var slotGuid = reader.GetString(1);
            if (!values.TryGetValue(slotGuid, out var msas)) values.Add(slotGuid, msas = new HashSet<string>(StringComparer.Ordinal));
            msas.Add(msaGuid);
        }
        return values.ToDictionary(pair => pair.Key, pair => (IReadOnlySet<string>)pair.Value,
            StringComparer.Ordinal);
    }

    private Dictionary<string, string> ReadMsaKinds()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        using var command = NewCommand();
        command.CommandText = "SELECT msa_guid, kind FROM facts.msa ORDER BY msa_guid;";
        using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add(reader.GetString(0), reader.GetString(1));
        return result;
    }

    private IReadOnlyList<TemplateMeasure> ReadTemplates(
        IReadOnlyDictionary<string, IReadOnlySet<string>> msaSlots)
    {
        var rows = new Dictionary<string, TemplateMeasureBuilder>(StringComparer.Ordinal);
        using var command = NewCommand();
        command.CommandText = """
            SELECT t.guid, t.category_guid, t.name, t.disabled,
                   s.slot_guid, a.name, s.side, s.ordinal, s.compiled_order, a.optional
            FROM facts.affix_template AS t
            LEFT JOIN facts.template_slot AS s ON s.template_guid=t.guid
            LEFT JOIN facts.affix_slot AS a ON a.guid=s.slot_guid
            ORDER BY t.guid, s.compiled_order, s.side, s.ordinal;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var guid = reader.GetString(0);
            if (!rows.TryGetValue(guid, out var template))
            {
                template = new TemplateMeasureBuilder(guid, reader.GetString(1), reader.GetString(2),
                    reader.GetInt32(3) != 0);
                rows.Add(guid, template);
            }
            if (reader.IsDBNull(4)) continue;
            var slotGuid = reader.GetString(4);
            template.Slots.Add(new TemplateMeasureSlot(slotGuid, reader.IsDBNull(5) ? slotGuid : reader.GetString(5),
                reader.GetString(6), reader.GetInt32(7), reader.IsDBNull(8) ? null : reader.GetInt32(8),
                reader.IsDBNull(9) ? null : reader.GetInt32(9) != 0, msaSlots.GetValueOrDefault(slotGuid) ??
                new HashSet<string>(StringComparer.Ordinal)));
        }
        return Array.AsReadOnly(rows.Values.Select(builder => builder.Build())
            .OrderBy(template => template.Guid, StringComparer.Ordinal).ToArray());
    }

    private Dictionary<string, IReadOnlyList<string>> ReadPartOfSpeechCategories()
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        using var command = NewCommand();
        command.CommandText = """
            SELECT msa_guid, category_guid FROM facts.msa_category
            WHERE role='pos' ORDER BY msa_guid, ordinal, category_guid;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var msaGuid = reader.GetString(0);
            if (!result.TryGetValue(msaGuid, out var categories)) result.Add(msaGuid, categories = []);
            categories.Add(reader.GetString(1));
        }
        return result.ToDictionary(pair => pair.Key,
            pair => (IReadOnlyList<string>)Array.AsReadOnly(pair.Value.Distinct(StringComparer.Ordinal).ToArray()),
            StringComparer.Ordinal);
    }

    /// <summary>The evidence-scope id for a requested scope, as the evidence artifact names it.</summary>
    private static string ScopeId(ParsimonyEvidenceScopeKind scope) => scope switch
    {
        ParsimonyEvidenceScopeKind.DefaultSelection => "default-selection",
        ParsimonyEvidenceScopeKind.ProjectApproved => "project-approved",
        _ => throw new ArgumentOutOfRangeException(nameof(scope)),
    };

    /// <summary>Scopes an Approved-analysis read to the requested evidence scope, binding its id.</summary>
    private string ApprovedScopeClause(SqliteCommand command, string wordformColumn = "a.wordform_guid")
    {
        ThrowIfScopeUnavailable();
        if (_inputs.EvidenceScope == ParsimonyEvidenceScopeKind.ProjectApproved) return string.Empty;
        command.Parameters.AddWithValue("$scope", ScopeId(_inputs.EvidenceScope));
        // EXISTS, not a JOIN: a wordform with several scope words must still yield each analysis once.
        return " AND EXISTS (SELECT 1 FROM evidence.scope_words AS sw " +
            $"WHERE sw.wordform_guid={wordformColumn} AND sw.scope_id=$scope)";
    }

    /// <summary>Throws when the requested evidence scope was not captured complete.</summary>
    private void ThrowIfScopeUnavailable()
    {
        if (_inputs.EvidenceScope == ParsimonyEvidenceScopeKind.ProjectApproved) return;
        using var command = NewCommand();
        command.CommandText = "SELECT status, reason_code FROM evidence.scope_descriptor WHERE scope_id=$scope;";
        command.Parameters.AddWithValue("$scope", ScopeId(_inputs.EvidenceScope));
        using var reader = command.ExecuteReader();
        string? status = null;
        string? reasonCode = null;
        if (reader.Read())
        {
            status = reader.GetString(0);
            reasonCode = reader.IsDBNull(1) ? null : reader.GetString(1);
        }
        if (status == "complete") return;
        throw new ParsimonyScopeUnavailableException(reasonCode switch
        {
            "default_selection_not_configured" => "the Default Selection is not saved.",
            "no_approved_words" => "the Default Selection has no Approved words.",
            _ => "the Default Selection is unavailable.",
        });
    }

    private IReadOnlyList<ParsimonyApprovedMorphSequenceViewRow> ReadApprovedSequences()
    {
        var analyses = new Dictionary<string, ApprovedSequenceBuilder>(StringComparer.Ordinal);
        using (var command = NewCommand())
        {
            var scopeClause = ApprovedScopeClause(command);
            command.CommandText = $"""
                SELECT a.analysis_guid, a.wordform_guid, m.ordinal, m.morph_guid, m.msa_guid, m.entry_guid,
                       msa.kind, morph.morph_type
                FROM evidence.analyses AS a
                LEFT JOIN evidence.analysis_morphs AS m ON m.analysis_guid=a.analysis_guid
                LEFT JOIN facts.msa AS msa ON msa.msa_guid=m.msa_guid
                LEFT JOIN facts.allomorph AS morph ON morph.guid=m.morph_guid
                WHERE a.opinion='approved'{scopeClause}
                ORDER BY a.analysis_guid, m.ordinal;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var analysisGuid = reader.GetString(0);
                if (!analyses.TryGetValue(analysisGuid, out var analysis))
                {
                    analysis = new ApprovedSequenceBuilder(analysisGuid, reader.GetString(1));
                    analyses.Add(analysisGuid, analysis);
                }
                if (!reader.IsDBNull(2))
                    analysis.Morphs.Add(new ApprovedMorphBuilder(reader.GetInt32(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetString(4),
                        reader.IsDBNull(5) ? null : reader.GetString(5),
                        reader.IsDBNull(6) ? null : reader.GetString(6),
                        reader.IsDBNull(7) ? null : reader.GetString(7)));
            }
        }

        ReadWordformForms(analyses);
        ReadMorphText(analyses, "analysis_morph_forms", "form", (morph, writingSystem, value) =>
            morph.Forms.Add(writingSystem, value));
        ReadMorphText(analyses, "analysis_morph_texts", "text", (morph, writingSystem, value) =>
            morph.Glosses.Add(writingSystem, value), "gloss");
        return Array.AsReadOnly(analyses.Values.Select(builder => builder.Build())
            .OrderBy(item => item.AnalysisGuid, StringComparer.Ordinal).ToArray());
    }

    private void ReadWordformForms(IReadOnlyDictionary<string, ApprovedSequenceBuilder> analyses)
    {
        using var command = NewCommand();
        var scopeClause = ApprovedScopeClause(command);
        command.CommandText = $"""
            SELECT a.analysis_guid, f.form
            FROM evidence.analyses AS a
            JOIN evidence.wordform_forms AS f ON f.wordform_guid=a.wordform_guid
            WHERE a.opinion='approved'{scopeClause}
            ORDER BY a.analysis_guid, f.writing_system;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (analyses.TryGetValue(reader.GetString(0), out var analysis) && analysis.Wordform.Length == 0)
                analysis.Wordform = reader.GetString(1);
    }

    private void ReadMorphText(IReadOnlyDictionary<string, ApprovedSequenceBuilder> analyses, string table,
        string valueColumn, Action<ApprovedMorphBuilder, string, string> add, string? kind = null)
    {
        using var command = NewCommand();
        var kindFilter = kind is null ? string.Empty : " AND f.kind=$kind";
        var scopeClause = ApprovedScopeClause(command);
        command.CommandText = $"""
            SELECT f.analysis_guid, f.morph_ordinal, f.writing_system, f.{valueColumn}
            FROM evidence.{table} AS f
            JOIN evidence.analyses AS a ON a.analysis_guid=f.analysis_guid
            WHERE a.opinion='approved'{kindFilter}{scopeClause}
            ORDER BY f.analysis_guid, f.morph_ordinal, f.writing_system;
            """;
        if (kind is not null) command.Parameters.AddWithValue("$kind", kind);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!analyses.TryGetValue(reader.GetString(0), out var analysis)) continue;
            var morph = analysis.Morphs.FirstOrDefault(item => item.Ordinal == reader.GetInt32(1));
            if (morph is not null) add(morph, reader.GetString(2), reader.GetString(3));
        }
    }

    private sealed class TemplateMeasureBuilder(string guid, string categoryGuid, string name, bool disabled)
    {
        public List<TemplateMeasureSlot> Slots { get; } = [];

        public TemplateMeasure Build() => new(guid, categoryGuid, name, disabled,
            Array.AsReadOnly(Slots.ToArray()));
    }

    private sealed class ApprovedSequenceBuilder(string analysisGuid, string wordformGuid)
    {
        public string AnalysisGuid { get; } = analysisGuid;
        public string WordformGuid { get; } = wordformGuid;
        public string Wordform { get; set; } = string.Empty;
        public List<ApprovedMorphBuilder> Morphs { get; } = [];

        public ParsimonyApprovedMorphSequenceViewRow Build() => new(AnalysisGuid, WordformGuid,
            Wordform.Length == 0 ? WordformGuid : Wordform,
            Array.AsReadOnly(Morphs.OrderBy(morph => morph.Ordinal).Select(morph => morph.Build()).ToArray()));
    }

    private sealed class ApprovedMorphBuilder(int ordinal, string? morphGuid, string? msaGuid, string? entryGuid,
        string? msaKind, string? morphType)
    {
        public int Ordinal { get; } = ordinal;
        public string? MorphGuid { get; } = morphGuid;
        public string? MsaGuid { get; } = msaGuid;
        public string? EntryGuid { get; } = entryGuid;
        public string? MsaKind { get; } = msaKind;
        public string? MorphType { get; } = morphType;
        public SortedDictionary<string, string> Forms { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, string> Glosses { get; } = new(StringComparer.Ordinal);

        public ParsimonyApprovedMorph Build() => new(Ordinal, MorphGuid, MsaGuid, EntryGuid, MorphType, MsaKind,
            new SortedDictionary<string, string>(Forms, StringComparer.Ordinal),
            new SortedDictionary<string, string>(Glosses, StringComparer.Ordinal));
    }
}
