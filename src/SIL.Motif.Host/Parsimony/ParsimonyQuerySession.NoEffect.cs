using SIL.Motif.Contract.Parsimony;

namespace SIL.Motif.Host.Parsimony;

public sealed partial class ParsimonyQuerySession
{
    private static readonly IReadOnlyDictionary<string, string> NoEffectReasons =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["msEnvPartOfSpeechNotRead"] = "Required category has no effect on parsing — legacy field.",
            ["derivationalMsaIgnoresAlloClasses"] =
                "Inflection classes have no effect on parsing for a derivational affix.",
            ["stemMsaIgnoresAlloClasses"] = "Inflection classes have no effect on parsing for a stem.",
            ["unclassifiedMsaIgnoresAlloClasses"] =
                "Inflection classes have no effect on parsing for an unclassified analysis.",
            ["allomorphNotRepresented"] =
                "No compiled object was built from this allomorph, so this gate has no effect on parsing.",
            ["circumfixSuffixClassesNotRead"] = "Suffix-half inflection classes have no effect on parsing for a circumfix.",
            ["circumfixIgnoresAllomorphGates"] = "Required features of a circumfix's halves have no effect on parsing.",
        };

    /// <summary>
    /// Returns one no-effect note for each authored allomorph gate the compiler ignored. An incomplete allomorphs
    /// section returns none, since its absent rows say nothing about the authored gates.
    /// </summary>
    public IReadOnlyList<ParsimonyNote> ReadNoEffectNotes()
    {
        if (ReadSectionStatus("allomorphs") != "complete") return [];
        ThrowIfLeaseLost();
        var gates = new List<(string Allomorph, string Entry, string Kind, string? Reason)>();
        // DISTINCT keeps one note per allomorph, gate kind and reason, so identical sentences are not repeated.
        using (var command = NewCommand())
        {
            command.CommandText = "SELECT DISTINCT g.allomorph_guid, a.entry_guid, g.gate_kind, g.reason_code " +
                "FROM facts.allomorph_gate AS g JOIN facts.allomorph AS a ON a.guid=g.allomorph_guid " +
                "WHERE g.parser_effect='ignored' ORDER BY g.allomorph_guid, g.gate_kind, g.reason_code;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                gates.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3)));
        }
        return Array.AsReadOnly(gates.Select(gate =>
        {
            var entry = DescribeObject(gate.Entry) ?? gate.Entry;
            var allomorph = DescribeObject(gate.Allomorph) ?? gate.Allomorph;
            var sentence = gate.Reason is not null && NoEffectReasons.TryGetValue(gate.Reason, out var known)
                ? known
                : $"{DescribeGateKind(gate.Kind)} has no effect on parsing.";
            return new ParsimonyNote("no-effect", ParsimonyNotes.NoEffectMeasureId,
                $"{entry} ({allomorph}): {sentence}", gate.Allomorph);
        }).ToArray());
    }

    private static string DescribeGateKind(string gateKind) => gateKind switch
    {
        "inflection_class" => "Inflection class",
        "required_features" => "Required features",
        "required_category" => "Required category",
        "stem_name" => "Stem name",
        _ => "The gate",
    };
}
