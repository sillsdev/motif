using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Requests;

/// <summary>
/// How much of the stored-analysis comparison a grammar check runs. The comparison re-parses each wordform
/// to see whether its stored analyses still parse, so it is the one part of a grammar check whose cost grows
/// with the size of the project.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StoredAnalysisComparison>))]
public enum StoredAnalysisComparison
{
    /// <summary>Compares wordforms in order until a fixed work budget is spent, then says what was left.</summary>
    Budget,

    /// <summary>Compares every wordform, however long that takes on a large project.</summary>
    All,

    /// <summary>Skips the comparison and runs only the checks that need no parsing.</summary>
    Off,
}
