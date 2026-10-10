using Microsoft.Data.Sqlite;

namespace SIL.Motif.Host.Parsimony;

public sealed partial class ParsimonyQuerySession
{
    internal UnconditionedAllomorphDiscovery ReadUnconditionedAllomorphFamilies()
    {
        RequireSections("allomorphs", "entries", "msas", "environments", "compiled_mappings");
        var allomorphs = ReadAllomorphsForMeasure().ToDictionary(item => item.Guid, StringComparer.Ordinal);
        var phoneConditions = ReadUnconditionedAllomorphPhoneConditions();
        var orders = ReadUnconditionedAllomorphCompilerOrder();
        var families = new List<UnconditionedAllomorphFamily>();
        var unknownFamilies = 0;
        var invalidConditions = 0;

        foreach (var ownerGroup in orders.GroupBy(item => new UnconditionedAllomorphFamilyIdentity(item.OwnerKey,
                     item.EntryGuid, item.MsaGuid, item.Bucket)))
        {
            var outputGroups = ownerGroup.GroupBy(item => item.OutputKey, StringComparer.Ordinal).ToArray();
            var concreteOutputs = new List<UnconditionedAllomorphCompilerOrder[]>();
            var outputIsUnknown = false;
            foreach (var output in outputGroups)
            {
                var sources = output.GroupBy(item => item.AllomorphGuid, StringComparer.Ordinal).ToArray();
                if (sources.All(source => source.All(item => item.IsAbstract == true))) continue;
                if (sources.Length != 1 || sources.Any(source => source.Any(item => item.IsAbstract is null)) ||
                    sources.Any(source => source.Any(item => item.IsAbstract == true)))
                {
                    outputIsUnknown = true;
                    break;
                }
                concreteOutputs.Add(output.ToArray());
            }
            if (outputIsUnknown)
            {
                unknownFamilies++;
                continue;
            }
            if (concreteOutputs.Count == 0) continue;

            var sourceOrders = concreteOutputs.Select(group => new
                {
                    AllomorphGuid = group.Select(item => item.AllomorphGuid).Single(),
                    Orders = group.Select(item => item.Order).Distinct().ToArray(),
                    FinalElsewhere = group.Any(item => item.IsFinalElsewhereCase),
                })
                .GroupBy(item => item.AllomorphGuid, StringComparer.Ordinal)
                .ToArray();
            if (sourceOrders.Any(group => group.SelectMany(item => item.Orders).Distinct().Count() != 1) ||
                sourceOrders.Any(group => group.Select(item => item.FinalElsewhere).Distinct().Count() != 1) ||
                sourceOrders.Any(group => !allomorphs.ContainsKey(group.Key)))
            {
                unknownFamilies++;
                continue;
            }

            var ordered = sourceOrders.Select(group =>
            {
                var item = group.First();
                var order = item.Orders.Single();
                var condition = ClassifyUnconditionedAllomorphPhoneCondition(group.Key, ownerGroup.Key.Bucket, phoneConditions);
                if (condition.State == UnconditionedAllomorphPhoneConditionState.Invalid)
                    invalidConditions += condition.Environments.Count;
                return new UnconditionedAllomorphSibling(allomorphs[group.Key], order, item.FinalElsewhere,
                    condition.State, condition.Environments);
            }).ToArray();
            var highestOrder = ordered.Max(item => item.EffectiveOrder);
            if (ordered.Any(item => item.IsFinalElsewhereCase && item.EffectiveOrder != highestOrder) ||
                ordered.Any(item => item.ConditionState == UnconditionedAllomorphPhoneConditionState.Unconditioned &&
                    item.EffectiveOrder == highestOrder && !item.IsFinalElsewhereCase))
            {
                unknownFamilies++;
                continue;
            }

            if (ordered.Any(item => item.ConditionState == UnconditionedAllomorphPhoneConditionState.Unknown))
            {
                unknownFamilies++;
                continue;
            }

            var usable = ordered.Where(item => item.ConditionState is UnconditionedAllomorphPhoneConditionState.Unconditioned or
                UnconditionedAllomorphPhoneConditionState.Conditioned).OrderBy(item => item.EffectiveOrder)
                .ThenBy(item => item.Allomorph.Guid, StringComparer.Ordinal).ToArray();
            if (!usable.Any(item => item.ConditionState == UnconditionedAllomorphPhoneConditionState.Unconditioned) ||
                !usable.Any(item => item.ConditionState == UnconditionedAllomorphPhoneConditionState.Conditioned))
                continue;

            var key = $"entry/{ownerGroup.Key.EntryGuid}/msa/{ownerGroup.Key.MsaGuid ?? "none"}/" +
                      $"bucket/{ownerGroup.Key.Bucket}/owner/{ownerGroup.Key.OwnerKey}";
            families.Add(new UnconditionedAllomorphFamily(key, ownerGroup.Key.EntryGuid, ownerGroup.Key.MsaGuid,
                ownerGroup.Key.Bucket, usable));
        }

        return new UnconditionedAllomorphDiscovery(Array.AsReadOnly(families.OrderBy(item => item.Key,
                StringComparer.Ordinal).ToArray()),
            families.Sum(item => (long)item.Siblings.Count), unknownFamilies, invalidConditions);
    }

    private IReadOnlyList<UnconditionedAllomorphCompilerOrder> ReadUnconditionedAllomorphCompilerOrder()
    {
        var rows = new List<UnconditionedAllomorphCompilerOrder>();
        using var command = NewCommand();
        command.CommandText = """
            SELECT owner.key, o.source_entry_guid, o.source_msa_guid, out.bucket, o.source_allomorph_guid,
                   out.key, o.compiled_order,
                   CASE WHEN out.has_phone_condition=0 AND out.compiled_order=(
                       SELECT MAX(u.compiled_order) FROM facts.compiled_output AS u
                       WHERE u.owner_output_id=out.owner_output_id AND u.kind='allomorph')
                   THEN 1 ELSE 0 END AS is_final_elsewhere_case,
                   a.is_abstract
            FROM facts.compiled_allomorph_order AS o
            JOIN facts.compiled_output AS out ON out.output_id=o.output_id
            JOIN facts.compiled_output AS owner ON owner.output_id=o.owner_output_id
            LEFT JOIN facts.allomorph AS a ON a.guid=o.source_allomorph_guid
            WHERE o.source_allomorph_guid IS NOT NULL AND o.compiled_order IS NOT NULL
            ORDER BY owner.key, out.bucket, o.compiled_order, out.key, o.source_allomorph_guid;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(1)) continue;
            rows.Add(new UnconditionedAllomorphCompilerOrder(reader.GetString(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.GetString(5), reader.GetInt32(6), reader.GetInt32(7) != 0,
                reader.IsDBNull(8) ? null : reader.GetInt32(8) != 0));
        }
        return Array.AsReadOnly(rows.ToArray());
    }

    private IReadOnlyList<UnconditionedAllomorphPhoneEnvironmentFact> ReadUnconditionedAllomorphPhoneConditions()
    {
        var rows = new List<UnconditionedAllomorphPhoneEnvironmentFact>();
        using var command = NewCommand();
        command.CommandText = """
            SELECT a.allomorph_guid, a.ordinal, a.environment_guid, e.name, e.representation, e.parse_status,
                   u.compile_context_key, u.compiled, u.result, u.resolved_environment_guid
            FROM facts.allomorph_environment AS a
            LEFT JOIN facts.environment AS e ON e.guid=a.environment_guid
            LEFT JOIN facts.environment_usage AS u
             ON u.allomorph_guid=a.allomorph_guid AND u.role=a.role AND u.ordinal=a.ordinal
             AND u.environment_guid=a.environment_guid
             AND u.compile_context_key='production'
            WHERE a.role='phone'
            ORDER BY a.allomorph_guid, a.ordinal, u.compile_context_key;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
            rows.Add(new UnconditionedAllomorphPhoneEnvironmentFact(reader.GetString(0), reader.GetInt32(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7) == 1,
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9)));
        return Array.AsReadOnly(rows.ToArray());
    }

    private static (UnconditionedAllomorphPhoneConditionState State, IReadOnlyList<UnconditionedAllomorphPhoneEnvironmentFact> Environments)
        ClassifyUnconditionedAllomorphPhoneCondition(string allomorphGuid, string bucket,
            IReadOnlyList<UnconditionedAllomorphPhoneEnvironmentFact> conditions)
    {
        var authored = conditions.Where(item => item.AllomorphGuid == allomorphGuid)
            .GroupBy(item => (item.Ordinal, item.EnvironmentGuid)).ToArray();
        if (authored.Length == 0)
            return (UnconditionedAllomorphPhoneConditionState.Unconditioned, []);
        var invalid = authored.SelectMany(group => group)
            .Where(item => item.ParseStatus == "invalid" || item.Outcome == "invalid").ToArray();
        if (invalid.Length > 0)
            return (UnconditionedAllomorphPhoneConditionState.Invalid, invalid);

        var context = bucket is "Morphology" or "Clitics" ? "production" : null;
        var compiled = authored.Select(group => group.Where(item => item.CompileContextKey == context).ToArray())
            .ToArray();
        var allRepresented = context is not null && compiled.All(group => group.Length == 1) &&
            compiled.SelectMany(group => group).All(item =>
            item.ParseStatus == "valid" && item.Compiled == true && item.Outcome == "represented" &&
            item.ResolvedEnvironmentGuid is not null);
        return allRepresented
            ? (UnconditionedAllomorphPhoneConditionState.Conditioned, Array.AsReadOnly(compiled.SelectMany(group => group).ToArray()))
            : (UnconditionedAllomorphPhoneConditionState.Unknown, authored.SelectMany(group => group).ToArray());
    }
}

internal sealed record UnconditionedAllomorphDiscovery(IReadOnlyList<UnconditionedAllomorphFamily> Families,
    long EligibleAllomorphs, int UnknownFamilies, int InvalidConditions);

internal sealed record UnconditionedAllomorphFamily(string Key, string EntryGuid, string? MsaGuid, string Bucket,
    IReadOnlyList<UnconditionedAllomorphSibling> Siblings);

internal sealed record UnconditionedAllomorphSibling(AllomorphMeasureFact Allomorph, int EffectiveOrder,
    bool IsFinalElsewhereCase, UnconditionedAllomorphPhoneConditionState ConditionState,
    IReadOnlyList<UnconditionedAllomorphPhoneEnvironmentFact> Environments);

internal sealed record UnconditionedAllomorphCompilerOrder(string OwnerKey, string EntryGuid, string? MsaGuid, string Bucket,
    string AllomorphGuid, string OutputKey, int Order, bool IsFinalElsewhereCase, bool? IsAbstract);

internal sealed record UnconditionedAllomorphPhoneEnvironmentFact(string AllomorphGuid, int Ordinal, string EnvironmentGuid,
    string? Name, string? Representation, string? ParseStatus, string? CompileContextKey, bool? Compiled,
    string? Outcome, string? ResolvedEnvironmentGuid);

internal enum UnconditionedAllomorphPhoneConditionState
{
    Unconditioned,
    Conditioned,
    Invalid,
    Unknown,
}

internal sealed record UnconditionedAllomorphFamilyIdentity(string OwnerKey, string EntryGuid, string? MsaGuid, string Bucket);
