using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Parsimony;

namespace SIL.Motif.Host.Parsimony;

public sealed partial class ParsimonyQuerySession
{
    private static readonly string[] NullAffixMarkers = ["^0", "*0", "&0", "∅"];

    internal long CountLoadedInflectionalAffixes()
    {
        RequireSections("msas", "entries", "allomorphs", "compiled_mappings", "load_accounting");
        using var command = NewCommand();
        command.CommandText = """
            SELECT COUNT(DISTINCT m.msa_guid)
            FROM facts.msa AS m
            WHERE m.kind='inflectional'
              AND EXISTS (
                  SELECT 1
                  FROM facts.compiled_allomorph_order AS o
                  JOIN facts.allomorph AS a ON a.guid=o.source_allomorph_guid
                  JOIN facts.compiled_mapping AS am ON am.source_kind='allomorph'
                      AND am.source_guid=a.guid AND am.output_id IN (SELECT output_id FROM facts.compiled_output WHERE kind='allomorph')
                  JOIN facts.allomorph_form AS f ON f.allomorph_guid=a.guid
                  WHERE o.source_msa_guid=m.msa_guid AND o.output_id IN (SELECT output_id FROM facts.compiled_output WHERE bucket='Morphology')
                    AND o.output_id IS NOT NULL AND o.compiled_order IS NOT NULL
                    AND a.is_abstract=0 AND a.morph_type IN ('prefix', 'suffix') AND f.form<>''
                    AND NOT EXISTS (SELECT 1 FROM facts.load_fact AS process
                        WHERE process.subject_kind='affixProcess' AND process.subject_guid=a.guid)
              );
            """;
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    internal IReadOnlyList<ParsimonyUnslottedAffixViewRow> ReadUnslottedAffixes(string? msaGuid = null)
    {
        RequireSections("categories", "templates", "msas", "entries", "allomorphs", "compiled_mappings",
            "load_accounting");
        var candidates = ReadUnslottedCandidates(msaGuid);
        if (candidates.Count == 0) return [];

        foreach (var sequence in ReadTemplateSequenceFacts().Analyses)
            AddApprovedSequenceEvidence(sequence, candidates);

        return Array.AsReadOnly(candidates.Values.Select(builder => builder.Build())
            .OrderBy(row => row.MsaGuid, StringComparer.Ordinal).ToArray());
    }

    internal IReadOnlyList<ParsimonyNullOptionalViewRow> ReadNullOptionalAffixes(string? msaGuid = null)
    {
        RequireSections("categories", "templates", "msas", "entries", "allomorphs", "compiled_mappings",
            "load_accounting", "adhoc_prohibitions");
        // Checked here, not only per row: an empty identity list would otherwise compute with no scope.
        ThrowIfScopeUnavailable();
        var identities = ReadNullLikeMsaIdentities(msaGuid);
        var rows = identities.Select(identity => BuildNullOptionalRow(identity.MsaGuid, identity.EntryGuid))
            .OrderBy(row => row.MsaGuid, StringComparer.Ordinal).ToArray();
        return Array.AsReadOnly(rows);
    }

    private Dictionary<string, UnslottedAffixBuilder> ReadUnslottedCandidates(string? msaGuid)
    {
        var result = new Dictionary<string, UnslottedAffixBuilder>(StringComparer.Ordinal);
        using var command = NewCommand();
        command.CommandText = """
            SELECT m.msa_guid, m.entry_guid
            FROM facts.msa AS m
            WHERE m.kind='inflectional'
              AND NOT EXISTS (SELECT 1 FROM facts.msa_slot AS s WHERE s.msa_guid=m.msa_guid)
              AND EXISTS (
                  SELECT 1
                  FROM facts.compiled_allomorph_order AS o
                  JOIN facts.allomorph AS a ON a.guid=o.source_allomorph_guid
                  JOIN facts.compiled_mapping AS am ON am.source_kind='allomorph'
                      AND am.source_guid=a.guid AND am.output_id IN (SELECT output_id FROM facts.compiled_output WHERE kind='allomorph')
                  JOIN facts.allomorph_form AS f ON f.allomorph_guid=a.guid
                  WHERE o.source_msa_guid=m.msa_guid AND o.output_id IN (SELECT output_id FROM facts.compiled_output WHERE bucket='Morphology')
                    AND o.output_id IS NOT NULL AND o.compiled_order IS NOT NULL
                    AND a.is_abstract=0 AND a.morph_type IN ('prefix', 'suffix') AND f.form<>''
                    AND NOT EXISTS (SELECT 1 FROM facts.load_fact AS process
                        WHERE process.subject_kind='affixProcess' AND process.subject_guid=a.guid)
              )
              AND ($filter IS NULL OR m.msa_guid=$filter)
            ORDER BY m.msa_guid;
            """;
        command.Parameters.AddWithValue("$filter", (object?)msaGuid ?? DBNull.Value);
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var guid = reader.GetString(0);
                var entryGuid = reader.GetString(1);
                result.Add(guid, new UnslottedAffixBuilder(guid, entryGuid,
                    DescribeMsa(guid) ?? "inflectional MSA"));
            }
        }

        foreach (var builder in result.Values)
        {
            builder.CategoryGuids.AddRange(ReadStringColumn(
                "SELECT category_guid FROM facts.msa_category WHERE msa_guid=$guid AND role='pos' " +
                "ORDER BY ordinal, category_guid", builder.MsaGuid));
            builder.Realizations.AddRange(ReadLoadedAffixRealizations(builder.MsaGuid));
        }
        return result;
    }

    private IReadOnlyList<ParsimonyLoadedAffixRealization> ReadLoadedAffixRealizations(string msaGuid)
    {
        var forms = new Dictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);
        var morphTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        using var command = NewCommand();
        command.CommandText = """
            SELECT DISTINCT a.guid, a.morph_type, f.writing_system, f.form
            FROM facts.compiled_allomorph_order AS o
            JOIN facts.allomorph AS a ON a.guid=o.source_allomorph_guid
            JOIN facts.compiled_mapping AS am ON am.source_kind='allomorph'
                AND am.source_guid=a.guid AND am.output_id IN (SELECT output_id FROM facts.compiled_output WHERE kind='allomorph')
            JOIN facts.allomorph_form AS f ON f.allomorph_guid=a.guid
            WHERE o.source_msa_guid=$guid AND o.output_id IN (SELECT output_id FROM facts.compiled_output WHERE bucket='Morphology')
              AND o.output_id IS NOT NULL AND o.compiled_order IS NOT NULL
              AND a.is_abstract=0 AND a.morph_type IN ('prefix', 'suffix')
              AND NOT EXISTS (SELECT 1 FROM facts.load_fact AS process
                  WHERE process.subject_kind='affixProcess' AND process.subject_guid=a.guid)
              AND EXISTS (SELECT 1 FROM facts.allomorph_form AS usable
                  WHERE usable.allomorph_guid=a.guid AND usable.form<>'')
            ORDER BY a.guid, f.writing_system, f.ordinal;
            """;
        command.Parameters.AddWithValue("$guid", msaGuid);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var guid = reader.GetString(0);
            morphTypes.TryAdd(guid, reader.GetString(1));
            if (!forms.TryGetValue(guid, out var byWritingSystem))
                forms.Add(guid, byWritingSystem = new SortedDictionary<string, string>(StringComparer.Ordinal));
            byWritingSystem.TryAdd(reader.GetString(2), reader.GetString(3));
        }
        return Array.AsReadOnly(forms.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new ParsimonyLoadedAffixRealization(pair.Key, morphTypes[pair.Key], pair.Value))
            .ToArray());
    }

    private void AddApprovedSequenceEvidence(TemplateMeasureAnalysis item,
        IReadOnlyDictionary<string, UnslottedAffixBuilder> candidates)
    {
        var evidence = item.Evidence;
        var targeted = evidence.Morphs.Where(morph => morph.MsaGuid is not null && candidates.ContainsKey(morph.MsaGuid))
            .Select(morph => candidates[morph.MsaGuid!]).Distinct().ToArray();
        if (targeted.Length == 0) return;
        foreach (var candidate in targeted) candidate.ApprovedAnalysisGuids.Add(evidence.AnalysisGuid);

        if (!TryGetRootedAffixes(item, out var rootOrdinal, out var stemEntryGuid,
                out var affixes, out var exclusion))
        {
            foreach (var candidate in targeted) candidate.AddExclusion(exclusion);
            return;
        }

        var repeatedMsas = affixes.GroupBy(morph => morph.MsaGuid!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var candidate in targeted)
        {
            var occurrences = affixes.Where(morph => morph.MsaGuid == candidate.MsaGuid).ToArray();
            if (occurrences.Length != 1)
            {
                candidate.AddExclusion("the same MSA occurs more than once in the Approved sequence");
                continue;
            }
            var morph = occurrences[0];
            var signedDistance = morph.Ordinal - rootOrdinal;
            var side = signedDistance < 0 ? "prefix" : "suffix";
            if ((morph.MorphType == "prefix" && signedDistance >= 0) ||
                (morph.MorphType == "suffix" && signedDistance <= 0))
            {
                candidate.AddExclusion("the recorded order conflicts with the affix side");
                continue;
            }
            candidate.AddPosition(side, signedDistance, evidence.AnalysisGuid, evidence.WordformGuid,
                stemEntryGuid);
        }

        foreach (var sideGroup in affixes.GroupBy(morph => morph.MorphType!, StringComparer.Ordinal))
        {
            var side = sideGroup.OrderBy(morph => morph.Ordinal).ToArray();
            var pairsSeen = new HashSet<AffixMsaPair>();
            for (var firstIndex = 0; firstIndex < side.Length; firstIndex++)
            for (var secondIndex = firstIndex + 1; secondIndex < side.Length; secondIndex++)
            {
                var left = side[firstIndex];
                var right = side[secondIndex];
                if (left.MsaGuid == right.MsaGuid) continue;
                var pair = AffixMsaPair.Create(sideGroup.Key, left.MsaGuid!, right.MsaGuid!);
                if (!pairsSeen.Add(pair)) continue;
                var isTargeted = candidates.TryGetValue(pair.FirstMsaGuid, out var firstCandidate) |
                    candidates.TryGetValue(pair.SecondMsaGuid, out var secondCandidate);
                if (!isTargeted) continue;
                if (repeatedMsas.Contains(pair.FirstMsaGuid) || repeatedMsas.Contains(pair.SecondMsaGuid))
                {
                    var excludedFirstDescription = DescribeMsa(pair.FirstMsaGuid) ?? "inflectional MSA";
                    var excludedSecondDescription = DescribeMsa(pair.SecondMsaGuid) ?? "inflectional MSA";
                    firstCandidate?.AddPairExclusion(pair, evidence.AnalysisGuid, excludedFirstDescription,
                        excludedSecondDescription);
                    secondCandidate?.AddPairExclusion(pair, evidence.AnalysisGuid, excludedFirstDescription,
                        excludedSecondDescription);
                    continue;
                }

                var witness = new ParsimonyAffixOrderWitness(evidence.AnalysisGuid, evidence.WordformGuid,
                    evidence.Wordform, stemEntryGuid, left.MsaGuid == pair.FirstMsaGuid);
                var firstDescription = DescribeMsa(pair.FirstMsaGuid) ?? "inflectional MSA";
                var secondDescription = DescribeMsa(pair.SecondMsaGuid) ?? "inflectional MSA";
                firstCandidate?.AddPairWitness(pair, witness, firstDescription, secondDescription);
                secondCandidate?.AddPairWitness(pair, witness, firstDescription, secondDescription);
            }
        }
    }

    private static bool TryGetRootedAffixes(TemplateMeasureAnalysis item, out int rootOrdinal,
        out string stemEntryGuid, out IReadOnlyList<ParsimonyApprovedMorph> affixes, out string exclusion)
    {
        var evidence = item.Evidence;
        var roots = evidence.Morphs.Where(morph => morph.MsaKind == "stem").ToArray();
        rootOrdinal = 0;
        stemEntryGuid = string.Empty;
        affixes = [];
        if (roots.Length != 1 || roots[0].EntryGuid is null)
        {
            exclusion = "the sequence does not identify exactly one root entry";
            return false;
        }
        var nonRoots = evidence.Morphs.Where(morph => morph.MsaKind != "stem").ToArray();
        if (nonRoots.Any(morph => morph.MsaKind != "inflectional" || morph.MsaGuid is null ||
                morph.MorphType is not ("prefix" or "suffix")))
        {
            exclusion = "the sequence contains an unsupported non-inflectional or non-affix morph";
            return false;
        }
        rootOrdinal = roots[0].Ordinal;
        stemEntryGuid = roots[0].EntryGuid!;
        affixes = Array.AsReadOnly(nonRoots);
        exclusion = string.Empty;
        return true;
    }

    private IReadOnlyList<NullMsaIdentity> ReadNullLikeMsaIdentities(string? msaGuid)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        using var command = NewCommand();
        command.CommandText = """
            SELECT DISTINCT m.msa_guid, m.entry_guid
            FROM facts.msa AS m
            JOIN facts.allomorph AS a ON a.entry_guid=m.entry_guid
            JOIN facts.allomorph_form AS f ON f.allomorph_guid=a.guid
            WHERE m.kind='inflectional' AND a.morph_type IN ('prefix', 'suffix')
              AND (f.form='' OR f.form IN ('^0', '*0', '&0', '∅'))
              AND ($filter IS NULL OR m.msa_guid=$filter)
            UNION
            SELECT DISTINCT m.msa_guid, m.entry_guid
            FROM facts.msa AS m
            JOIN facts.compiled_allomorph_order AS o ON o.source_msa_guid=m.msa_guid
            WHERE m.kind='inflectional' AND o.output_id IN (SELECT output_id FROM facts.compiled_output WHERE bucket='Morphology') AND o.source_allomorph_guid IS NULL
              AND o.source_allomorph_key='' AND o.output_id IS NOT NULL AND o.compiled_order IS NOT NULL
              AND ($filter IS NULL OR m.msa_guid=$filter)
            UNION
            SELECT m.msa_guid, m.entry_guid
            FROM facts.msa AS m
            WHERE m.kind='inflectional'
              AND EXISTS (SELECT 1 FROM facts.compiled_mapping AS mm
                  WHERE mm.source_kind='msa' AND mm.source_guid=m.msa_guid AND mm.output_id IN (SELECT output_id FROM facts.compiled_output WHERE kind='morph_rule'))
              AND ($filter IS NULL OR m.msa_guid=$filter)
            ORDER BY 1;
            """;
        command.Parameters.AddWithValue("$filter", (object?)msaGuid ?? DBNull.Value);
        using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add(reader.GetString(0), reader.GetString(1));
        return Array.AsReadOnly(result.Select(pair => new NullMsaIdentity(pair.Key, pair.Value)).ToArray());
    }

    private IReadOnlyList<string> ReadApprovedAnalysesForMsa(string msaGuid)
    {
        using var command = NewCommand();
        var scopeClause = ApprovedScopeClause(command);
        command.CommandText = $"""
            SELECT DISTINCT m.analysis_guid FROM evidence.analysis_morphs AS m
            JOIN evidence.analyses AS a ON a.analysis_guid=m.analysis_guid
            WHERE m.msa_guid=$guid AND a.opinion='approved'{scopeClause} ORDER BY m.analysis_guid;
            """;
        command.Parameters.AddWithValue("$guid", msaGuid);
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read()) values.Add(reader.GetString(0));
        return Array.AsReadOnly(values.ToArray());
    }

    private ParsimonyNullOptionalViewRow BuildNullOptionalRow(string msaGuid, string entryGuid)
    {
        var hasOrdinaryOutput = Exists("""
            SELECT 1
            FROM facts.compiled_allomorph_order AS o
            JOIN facts.allomorph AS a ON a.guid=o.source_allomorph_guid
            JOIN facts.compiled_mapping AS am ON am.source_kind='allomorph'
                AND am.source_guid=a.guid AND am.output_id IN (SELECT output_id FROM facts.compiled_output WHERE kind='allomorph')
            WHERE o.source_msa_guid=$guid AND o.output_id IN (SELECT output_id FROM facts.compiled_output WHERE bucket='Morphology')
              AND o.output_id IS NOT NULL AND o.compiled_order IS NOT NULL
              AND a.is_abstract=0 AND a.morph_type IN ('prefix', 'suffix')
              AND NOT EXISTS (SELECT 1 FROM facts.load_fact AS process
                  WHERE process.subject_kind='affixProcess' AND process.subject_guid=a.guid)
            LIMIT 1;
            """, msaGuid);
        var hasProcessOutput = !hasOrdinaryOutput && Exists("""
            SELECT 1
            FROM facts.compiled_allomorph_order AS o
            JOIN facts.load_fact AS process ON process.subject_guid=o.source_allomorph_guid
                AND process.subject_kind='affixProcess'
            JOIN facts.compiled_mapping AS am ON am.source_kind='allomorph'
                AND am.source_guid=o.source_allomorph_guid AND am.output_id IN (SELECT output_id FROM facts.compiled_output WHERE kind='allomorph')
            WHERE o.source_msa_guid=$guid AND o.output_id IN (SELECT output_id FROM facts.compiled_output WHERE bucket='Morphology')
              AND o.output_id IS NOT NULL AND o.compiled_order IS NOT NULL
            LIMIT 1;
            """, msaGuid);
        var syntheticNullCount = Scalar("""
            SELECT COUNT(DISTINCT o.output_id) FROM facts.compiled_allomorph_order AS o
            JOIN facts.compiled_output AS out ON out.output_id=o.output_id
            WHERE o.source_msa_guid=$guid AND out.bucket='Morphology' AND o.source_allomorph_guid IS NULL
              AND o.source_allomorph_key='' AND o.compiled_order IS NOT NULL;
            """, msaGuid);
        var realizations = ReadEmptySourceForms(msaGuid, entryGuid, hasOrdinaryOutput);
        var loadedZeroCount = realizations.Count(item => item.CompilerRecognizedZero && !item.IsAbstract);
        var hasNonzeroLoadedSibling = hasOrdinaryOutput && Exists("""
            SELECT 1
            FROM facts.compiled_allomorph_order AS o
            JOIN facts.allomorph AS a ON a.guid=o.source_allomorph_guid
            JOIN facts.compiled_mapping AS am ON am.source_kind='allomorph'
                AND am.source_guid=a.guid AND am.output_id IN (SELECT output_id FROM facts.compiled_output WHERE kind='allomorph')
            JOIN facts.allomorph_form AS f ON f.allomorph_guid=a.guid
            WHERE o.source_msa_guid=$guid AND o.output_id IN (SELECT output_id FROM facts.compiled_output WHERE bucket='Morphology')
              AND o.output_id IS NOT NULL AND o.compiled_order IS NOT NULL
              AND a.is_abstract=0 AND a.morph_type IN ('prefix', 'suffix') AND f.form<>''
              AND f.form NOT IN ('^0', '*0', '&0', '∅')
              AND NOT EXISTS (SELECT 1 FROM facts.load_fact AS process
                  WHERE process.subject_kind='affixProcess' AND process.subject_guid=a.guid)
            LIMIT 1;
            """, msaGuid);
        var loadedZeroOnly = loadedZeroCount > 0 && !hasNonzeroLoadedSibling;
        var slots = ReadZeroSlots(msaGuid);
        var featureEffects = ReadMsaFeatureEffects(msaGuid);
        var classes = ReadStringColumn("SELECT class_guid FROM facts.msa_inflection_class " +
            "WHERE msa_guid=$guid ORDER BY role, class_guid", msaGuid);
        var exceptionFeatures = ReadStringColumn("SELECT target_guid FROM facts.msa_exception_feature " +
            "WHERE msa_guid=$guid ORDER BY role, ordinal", msaGuid);
        var senses = ReadStringColumn("SELECT sense_guid FROM facts.sense WHERE msa_guid=$guid " +
            "ORDER BY sense_guid", msaGuid);
        var prohibitions = ReadMsaProhibitionReferences(msaGuid);
        var approvedAnalyses = ReadApprovedAnalysesForMsa(msaGuid);
        var conditions = ReadZeroConditions(realizations);
        var classification = ClassifyNullMsa(loadedZeroOnly, hasProcessOutput, syntheticNullCount,
            realizations, slots, hasNonzeroLoadedSibling);
        var optionalCandidate = classification == "optional-slot-candidate";
        var removable = optionalCandidate && featureEffects.Count == 0 && classes.Count == 0 &&
            exceptionFeatures.Count == 0 && conditions.Count == 0 && senses.Count == 0 &&
            prohibitions.Count == 0 && approvedAnalyses.Count == 0;
        var exclusions = BuildNullExclusions(classification, loadedZeroOnly, featureEffects, classes,
            exceptionFeatures, conditions, senses, prohibitions, approvedAnalyses, removable);
        return new ParsimonyNullOptionalViewRow(msaGuid, entryGuid,
            DescribeMsa(msaGuid) ?? "inflectional MSA", classification, realizations, slots,
            featureEffects, classes, exceptionFeatures, conditions, senses, prohibitions, approvedAnalyses,
            checked((int)syntheticNullCount), loadedZeroOnly, removable, exclusions);
    }

    private IReadOnlyList<ParsimonyZeroRealizationFact> ReadEmptySourceForms(string msaGuid, string entryGuid,
        bool hasOrdinaryOutput)
    {
        var builders = new Dictionary<string, ZeroRealizationBuilder>(StringComparer.Ordinal);
        using var command = NewCommand();
        command.CommandText = """
            SELECT DISTINCT a.guid, a.morph_type, a.is_abstract, f.writing_system, f.form
            FROM facts.allomorph AS a
            JOIN facts.allomorph_form AS f ON f.allomorph_guid=a.guid
            WHERE a.entry_guid=$entry AND a.morph_type IN ('prefix', 'suffix')
              AND NOT EXISTS (SELECT 1 FROM facts.load_fact AS process
                  WHERE process.subject_kind='affixProcess' AND process.subject_guid=a.guid)
              AND EXISTS (SELECT 1 FROM facts.allomorph_form AS empty
                  WHERE empty.allomorph_guid=a.guid
                    AND (empty.form='' OR empty.form IN ('^0', '*0', '&0', '∅')))
            ORDER BY a.guid, f.writing_system, f.ordinal;
            """;
        command.Parameters.AddWithValue("$entry", entryGuid);
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var guid = reader.GetString(0);
                if (!builders.TryGetValue(guid, out var builder))
                {
                    var loaded = hasOrdinaryOutput && IsLoadedOrdinaryAllomorph(msaGuid, guid);
                    builder = new ZeroRealizationBuilder(guid, reader.GetString(1), reader.GetInt32(2) != 0,
                        loaded, loaded ? "represented" : ReadFinalAllomorphLoadReason(guid));
                    builders.Add(guid, builder);
                }
                builder.Forms[reader.GetString(3)] = reader.GetString(4);
            }
        }
        return Array.AsReadOnly(builders.Values.OrderBy(builder => builder.Guid, StringComparer.Ordinal)
            .Select(builder => new ParsimonyZeroRealizationFact(builder.Guid, builder.MorphType,
                new SortedDictionary<string, string>(builder.Forms, StringComparer.Ordinal), builder.IsAbstract,
                builder.Loaded, builder.LoaderReason)
            {
                CompilerRecognizedZero = builder.Loaded && builder.Forms.Count > 0 &&
                    builder.Forms.Values.All(form => NullAffixMarkers.Contains(form, StringComparer.Ordinal)),
            }).ToArray());
    }

    private bool IsLoadedOrdinaryAllomorph(string msaGuid, string allomorphGuid) => Exists("""
        SELECT 1
        FROM facts.compiled_allomorph_order AS o
        JOIN facts.allomorph AS a ON a.guid=o.source_allomorph_guid
        JOIN facts.compiled_mapping AS am ON am.source_kind='allomorph'
            AND am.source_guid=o.source_allomorph_guid AND am.output_id IN (SELECT output_id FROM facts.compiled_output WHERE kind='allomorph')
        WHERE o.source_msa_guid=$msa AND o.output_id IN (SELECT output_id FROM facts.compiled_output WHERE bucket='Morphology') AND o.source_allomorph_guid=$allomorph
          AND o.output_id IS NOT NULL AND o.compiled_order IS NOT NULL
          AND NOT EXISTS (SELECT 1 FROM facts.load_fact AS process
              WHERE process.subject_kind='affixProcess' AND process.subject_guid=a.guid)
        LIMIT 1;
        """, msaGuid, allomorphGuid);

    private string? ReadFinalAllomorphLoadReason(string allomorphGuid)
    {
        using var command = NewCommand();
        command.CommandText = """
            SELECT reason_code FROM facts.load_fact
            WHERE subject_kind='allomorph' AND subject_guid=$guid
              AND context_key='lexEntryForm:morphology' AND pipeline_stage IN ('compile', 'compact')
            ORDER BY CASE pipeline_stage WHEN 'compact' THEN 1 ELSE 0 END DESC, decision_ordinal DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$guid", allomorphGuid);
        return command.ExecuteScalar() as string;
    }

    private IReadOnlyList<ParsimonyZeroSlotMembership> ReadZeroSlots(string msaGuid)
    {
        var result = new List<ParsimonyZeroSlotMembership>();
        using var command = NewCommand();
        command.CommandText = """
            SELECT s.slot_guid, coalesce(nullif(trim(a.name), ''), s.slot_guid), a.optional
            FROM facts.msa_slot AS s LEFT JOIN facts.affix_slot AS a ON a.guid=s.slot_guid
            WHERE s.msa_guid=$guid AND s.role='slot'
            ORDER BY s.ordinal, s.slot_guid;
            """;
        command.Parameters.AddWithValue("$guid", msaGuid);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            result.Add(new ParsimonyZeroSlotMembership(reader.GetString(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2) != 0));
        return Array.AsReadOnly(result.ToArray());
    }

    private IReadOnlyList<ParsimonyMsaFeatureEffect> ReadMsaFeatureEffects(string msaGuid) =>
        Array.AsReadOnly(ReadMsaFeatureStructureTexts(msaGuid)
            .OrderBy(item => item.Role, StringComparer.Ordinal)
            .Select(item => new ParsimonyMsaFeatureEffect(item.Role, item.Json)).ToArray());

    private IReadOnlyList<string> ReadMsaProhibitionReferences(string msaGuid)
    {
        var result = new SortedSet<string>(StringComparer.Ordinal);
        using var command = NewCommand();
        command.CommandText = """
            SELECT prohibition_guid FROM facts.adhoc_prohibition
            WHERE target_kind='msa' AND primary_guid=$guid
            UNION
            SELECT prohibition_guid FROM facts.adhoc_other
            WHERE target_kind='msa' AND target_guid=$guid
            ORDER BY 1;
            """;
        command.Parameters.AddWithValue("$guid", msaGuid);
        using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add(reader.GetString(0));
        return Array.AsReadOnly(result.ToArray());
    }

    private IReadOnlyList<string> ReadZeroConditions(IReadOnlyList<ParsimonyZeroRealizationFact> realizations)
    {
        var guids = realizations.Where(item => item.AllomorphGuid is not null)
            .Select(item => item.AllomorphGuid!).Distinct(StringComparer.Ordinal).ToArray();
        if (guids.Length == 0) return [];
        var result = new SortedSet<string>(StringComparer.Ordinal);
        using var command = NewCommand();
        var parameters = new List<string>(guids.Length);
        for (var index = 0; index < guids.Length; index++)
        {
            var name = "$guid" + index;
            parameters.Add(name);
            command.Parameters.AddWithValue(name, guids[index]);
        }
        command.CommandText = $"SELECT role || ':' || environment_guid FROM facts.allomorph_environment " +
            $"WHERE allomorph_guid IN ({string.Join(",", parameters)}) ORDER BY allomorph_guid, role, ordinal, environment_guid;";
        using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add(reader.GetString(0));
        return Array.AsReadOnly(result.ToArray());
    }

    private static string ClassifyNullMsa(bool loadedZeroOnly, bool processOutput, long syntheticNullCount,
        IReadOnlyList<ParsimonyZeroRealizationFact> realizations,
        IReadOnlyList<ParsimonyZeroSlotMembership> slots, bool hasNonzeroLoadedSibling)
    {
        if (hasNonzeroLoadedSibling && realizations.Any(item => item.CompilerRecognizedZero && !item.IsAbstract))
            return "zero-with-nonzero-sibling";
        if (loadedZeroOnly)
        {
            if (slots.Count == 0) return "zero-without-slot";
            if (slots.Any(slot => slot.Optional is null)) return "slot-optionality-unknown";
            if (slots.Any(slot => slot.Optional == true) && slots.Any(slot => slot.Optional == false))
                return "mixed-slot-optionality";
            return slots.All(slot => slot.Optional == true)
                ? "optional-slot-candidate"
                : "obligatory-slot-alternative";
        }
        if (processOutput) return "process-rule-excluded";
        if (syntheticNullCount > 0) return "synthetic-null-excluded";
        if (realizations.Count > 0 && realizations.All(item => item.IsAbstract)) return "abstract-form-excluded";
        return "empty-or-dropped-form-excluded";
    }

    private static IReadOnlyList<string> BuildNullExclusions(string classification, bool loadedZeroOnly,
        IReadOnlyList<ParsimonyMsaFeatureEffect> featureEffects, IReadOnlyList<string> classes,
        IReadOnlyList<string> exceptionFeatures, IReadOnlyList<string> conditions, IReadOnlyList<string> senses,
        IReadOnlyList<string> prohibitions, IReadOnlyList<string> approvedAnalyses, bool removable)
    {
        var result = new List<string>();
        if (!loadedZeroOnly) result.Add(classification switch
        {
            "process-rule-excluded" => "Process-rule outputs are outside the ordinary zero-form measure.",
            "synthetic-null-excluded" => "The compiler output has no authored allomorph identity.",
            "abstract-form-excluded" => "Abstract forms are not loaded ordinary realizations.",
            _ => "No authored null marker has a final compiled output; blank or dropped forms do not establish a loaded zero.",
        });
        if (classification == "zero-with-nonzero-sibling")
            result.Add("A nonzero loaded sibling means the MSA is not zero-only.");
        if (classification == "obligatory-slot-alternative")
            result.Add("An obligatory-slot zero is an absence alternative, not an optional-slot duplicate.");
        if (classification == "zero-without-slot") result.Add("No slot membership is available for an optionality comparison.");
        if (classification is "mixed-slot-optionality" or "slot-optionality-unknown")
            result.Add("Slot optionality is mixed or unavailable, so the absence comparison is uncertain.");
        if (featureEffects.Count > 0 || exceptionFeatures.Count > 0)
            result.Add("Feature-bearing zero morphology needs a keep or ask decision.");
        if (classes.Count > 0) result.Add("Inflection-class restrictions need review before removal.");
        if (conditions.Count > 0) result.Add("Conditioned zero realizations need review before removal.");
        if (senses.Count > 0 || prohibitions.Count > 0)
            result.Add("Incoming senses or ad hoc targets keep this realization referenced.");
        if (approvedAnalyses.Count > 0)
            result.Add("Approved morphology uses this MSA and must be preserved.");
        if (loadedZeroOnly && !removable && result.Count == 0)
            result.Add("This zero does not meet the featureless, unreferenced optional-slot cleanup subset.");
        return Array.AsReadOnly(result.Distinct(StringComparer.Ordinal).ToArray());
    }

    private bool Exists(string sql, string guid) => Exists(sql, guid, null);

    private bool Exists(string sql, string firstGuid, string? secondGuid)
    {
        using var command = NewCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$guid", firstGuid);
        if (secondGuid is not null)
        {
            command.Parameters.AddWithValue("$msa", firstGuid);
            command.Parameters.AddWithValue("$allomorph", secondGuid);
        }
        return command.ExecuteScalar() is not null;
    }

    private long Scalar(string sql, string guid)
    {
        using var command = NewCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$guid", guid);
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed record NullMsaIdentity(string MsaGuid, string EntryGuid);

    private sealed class ZeroRealizationBuilder(string guid, string morphType, bool isAbstract, bool loaded,
        string? loaderReason)
    {
        public string Guid { get; } = guid;
        public string MorphType { get; } = morphType;
        public bool IsAbstract { get; } = isAbstract;
        public bool Loaded { get; } = loaded;
        public string? LoaderReason { get; } = loaderReason;
        public SortedDictionary<string, string> Forms { get; } = new(StringComparer.Ordinal);
    }

    private sealed class UnslottedAffixBuilder(string msaGuid, string entryGuid, string description)
    {
        public string MsaGuid { get; } = msaGuid;
        public string EntryGuid { get; } = entryGuid;
        public string Description { get; } = description;
        public List<string> CategoryGuids { get; } = [];
        public List<ParsimonyLoadedAffixRealization> Realizations { get; } = [];
        public HashSet<string> ApprovedAnalysisGuids { get; } = new(StringComparer.Ordinal);
        private readonly Dictionary<PositionKey, PositionBuilder> _positions = [];
        private readonly Dictionary<AffixMsaPair, PrecedenceBuilder> _precedence = [];
        private readonly Dictionary<string, int> _exclusions = new(StringComparer.Ordinal);

        public void AddPosition(string side, int distance, string analysisGuid, string wordformGuid, string stemEntryGuid)
        {
            var key = new PositionKey(side, distance);
            if (!_positions.TryGetValue(key, out var position)) _positions.Add(key, position = new PositionBuilder());
            position.Analyses.Add(analysisGuid);
            position.Wordforms.Add(wordformGuid);
            position.Stems.Add(stemEntryGuid);
        }

        public void AddExclusion(string reason) => _exclusions[reason] = _exclusions.GetValueOrDefault(reason) + 1;

        public void AddPairWitness(AffixMsaPair pair, ParsimonyAffixOrderWitness witness,
            string firstDescription, string secondDescription)
        {
            if (!_precedence.TryGetValue(pair, out var item))
                _precedence.Add(pair, item = new PrecedenceBuilder(pair, firstDescription, secondDescription));
            item.Witnesses.TryAdd(witness.AnalysisGuid, witness);
        }

        public void AddPairExclusion(AffixMsaPair pair, string analysisGuid,
            string firstDescription, string secondDescription)
        {
            if (!_precedence.TryGetValue(pair, out var item))
                _precedence.Add(pair, item = new PrecedenceBuilder(pair, firstDescription, secondDescription));
            item.ExcludedAnalyses.Add(analysisGuid);
        }

        public ParsimonyUnslottedAffixViewRow Build() => new(MsaGuid, EntryGuid, Description,
            Array.AsReadOnly(CategoryGuids.ToArray()), Array.AsReadOnly(Realizations.ToArray()),
            ApprovedAnalysisGuids.Count, Array.AsReadOnly(ApprovedAnalysisGuids.Order(StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(_positions.OrderBy(pair => pair.Key.Side, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.Distance).Select(pair => new ParsimonyAffixPositionCount(pair.Key.Side,
                    pair.Key.Distance, pair.Value.Analyses.Count, pair.Value.Wordforms.Count, pair.Value.Stems.Count)).ToArray()),
            Array.AsReadOnly(_precedence.Values.Select(item => item.Build()).OrderBy(item => item.Side, StringComparer.Ordinal)
                .ThenBy(item => item.FirstMsaGuid, StringComparer.Ordinal)
                .ThenBy(item => item.SecondMsaGuid, StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(_exclusions.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Value} Approved analysis case(s) were excluded because {pair.Key}.").ToArray()));

        private sealed class PositionBuilder
        {
            public HashSet<string> Analyses { get; } = new(StringComparer.Ordinal);
            public HashSet<string> Wordforms { get; } = new(StringComparer.Ordinal);
            public HashSet<string> Stems { get; } = new(StringComparer.Ordinal);
        }

        private sealed class PrecedenceBuilder(AffixMsaPair pair, string firstDescription, string secondDescription)
        {
            public Dictionary<string, ParsimonyAffixOrderWitness> Witnesses { get; } = new(StringComparer.Ordinal);
            public HashSet<string> ExcludedAnalyses { get; } = new(StringComparer.Ordinal);

            public ParsimonyAffixPrecedenceCount Build()
            {
                var witnesses = Witnesses.Values.OrderBy(item => item.AnalysisGuid, StringComparer.Ordinal).ToArray();
                var firstDirection = witnesses.Where(item => item.FirstBeforeSecond).ToArray();
                var secondDirection = witnesses.Where(item => !item.FirstBeforeSecond).ToArray();
                var support = firstDirection.Length >= secondDirection.Length ? firstDirection : secondDirection;
                var supportWords = support.Select(item => item.WordformGuid).Distinct(StringComparer.Ordinal).Count();
                var supportStems = support.Select(item => item.StemEntryGuid).Distinct(StringComparer.Ordinal).Count();
                var suggested = (firstDirection.Length > 0 && secondDirection.Length == 0 ||
                                 secondDirection.Length > 0 && firstDirection.Length == 0) &&
                                supportWords >= 3 && supportStems >= 2;
                return new ParsimonyAffixPrecedenceCount(pair.Side, pair.FirstMsaGuid,
                    firstDescription, pair.SecondMsaGuid, secondDescription, firstDirection.Length, secondDirection.Length,
                    ExcludedAnalyses.Count, supportWords, supportStems, suggested, Array.AsReadOnly(witnesses));
            }
        }
    }

    private readonly record struct PositionKey(string Side, int Distance);

    private readonly record struct AffixMsaPair(string Side, string FirstMsaGuid, string SecondMsaGuid)
    {
        public static AffixMsaPair Create(string side, string first, string second) =>
            StringComparer.Ordinal.Compare(first, second) < 0
                ? new AffixMsaPair(side, first, second)
                : new AffixMsaPair(side, second, first);
    }
}
