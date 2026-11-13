using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace SIL.Motif.Host.Parsimony;

public sealed partial class ParsimonyQuerySession
{
    /// <summary>
    /// Reads every exception feature with the entries that carry it on a stem MSA and the affix MSAs and
    /// rules that use it, in four whole-table reads.
    /// </summary>
    internal ExceptionFeatureInput ReadExceptionFeatureInput()
    {
        RequireSections("categories", "compound_rules", "entries", "features", "msas", "patterns", "phonology");
        var carriers = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var affixUsers = new Dictionary<string, List<FeatureAffixUser>>(StringComparer.Ordinal);
        var ruleUsers = new Dictionary<string, List<FeatureRuleUser>>(StringComparer.Ordinal);
        using (var command = NewCommand())
        {
            command.CommandText = """
                SELECT f.target_guid, m.entry_guid, m.kind, f.msa_guid, f.role
                FROM facts.msa_exception_feature AS f
                JOIN facts.msa AS m ON m.msa_guid=f.msa_guid
                ORDER BY f.target_guid, m.entry_guid, f.msa_guid, f.role;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var feature = reader.GetString(0);
                var isStem = reader.GetString(2) == "stem";
                if (isStem)
                {
                    if (reader.GetString(4) != "required") continue;
                    var list = GetOrAdd(carriers, feature);
                    // The read is ordered by entry, so only a repeat of the last entry can be a duplicate.
                    if (list.Count == 0 || list[^1] != reader.GetString(1)) list.Add(reader.GetString(1));
                    continue;
                }
                GetOrAdd(affixUsers, feature).Add(new FeatureAffixUser(reader.GetString(3), reader.GetString(1),
                    reader.GetString(4)));
            }
        }
        using (var command = NewCommand())
        {
            command.CommandText = """
                SELECT DISTINCT r.target_guid, r.rule_guid, p.name, r.polarity
                FROM facts.rewrite_rhs_rule_feature AS r
                LEFT JOIN facts.phonological_rule AS p ON p.guid=r.rule_guid
                WHERE r.target_kind='exceptionFeature'
                ORDER BY r.target_guid, r.rule_guid, r.polarity;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
                GetOrAdd(ruleUsers, reader.GetString(0)).Add(new FeatureRuleUser(reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3)));
        }
        var compoundUsers = new Dictionary<string, List<FeatureCompoundUser>>(StringComparer.Ordinal);
        using (var command = NewCommand())
        {
            command.CommandText = """
                SELECT DISTINCT f.target_guid, f.rule_guid, c.name, f.side
                FROM facts.compound_rule_exception_feature AS f
                LEFT JOIN facts.compound_rule AS c ON c.guid=f.rule_guid
                ORDER BY f.target_guid, f.rule_guid, f.side;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
                GetOrAdd(compoundUsers, reader.GetString(0)).Add(new FeatureCompoundUser(reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3)));
        }
        var features = new List<ExceptionFeatureFact>();
        using (var command = NewCommand())
        {
            command.CommandText = "SELECT guid, name, abbreviation FROM facts.exception_feature ORDER BY guid;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var guid = reader.GetString(0);
                features.Add(new ExceptionFeatureFact(guid, reader.GetString(1), reader.GetString(2),
                    carriers.TryGetValue(guid, out var entries) ? entries : [],
                    affixUsers.TryGetValue(guid, out var affixes) ? affixes : [],
                    ruleUsers.TryGetValue(guid, out var rules) ? rules : [],
                    compoundUsers.TryGetValue(guid, out var compounds) ? compounds : []));
            }
        }
        return new ExceptionFeatureInput(features, new FeatureSlotCount(
            ScalarCount("SELECT COUNT(*) FROM facts.inflection_class"), features.Count, ScalarCount("SELECT COUNT(*) FROM facts.lex_entry_infl_type")));
    }

    private static T GetOrAdd<T>(Dictionary<string, T> map, string key) where T : new()
    {
        if (!map.TryGetValue(key, out var value)) map.Add(key, value = new T());
        return value;
    }
}

/// <summary>An exception feature with its stem carriers and the affix MSAs and rules that use it.</summary>
/// <param name="Carriers">The distinct entries carrying the feature as required on a stem MSA, by GUID.</param>
internal sealed record ExceptionFeatureFact(string Guid, string Name, string Abbreviation,
    IReadOnlyList<string> Carriers, IReadOnlyList<FeatureAffixUser> AffixUsers, IReadOnlyList<FeatureRuleUser> RuleUsers,
    IReadOnlyList<FeatureCompoundUser> CompoundUsers);

/// <summary>A non-stem MSA that names the feature in one of the three required roles.</summary>
internal sealed record FeatureAffixUser(string MsaGuid, string EntryGuid, string Role);

/// <summary>A phonological rule whose right-hand side requires or excludes the feature.</summary>
internal sealed record FeatureRuleUser(string RuleGuid, string? Name, string Polarity);

/// <summary>A compound rule that requires the feature on its left or right constituent.</summary>
internal sealed record FeatureCompoundUser(string RuleGuid, string? Name, string Side);

/// <summary>The three counts PanGloss sums against its 64-slot feature cap.</summary>
internal sealed record FeatureSlotCount(long InflectionClasses, long ExceptionFeatures, long InflectionTypes)
{
    /// <summary>PanGloss's cap on productivity-restriction feature slots.</summary>
    public const int Cap = 64;

    /// <summary>The sum PanGloss compares with the cap.</summary>
    public long Total => InflectionClasses + ExceptionFeatures + InflectionTypes;
}

/// <summary>Every exception feature and the slot count the cap line reports.</summary>
internal sealed record ExceptionFeatureInput(IReadOnlyList<ExceptionFeatureFact> Features, FeatureSlotCount Slots);
