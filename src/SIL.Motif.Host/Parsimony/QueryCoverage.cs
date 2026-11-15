using System.Text.Json;

namespace SIL.Motif.Host.Parsimony;

/// <summary>
/// What the facts behind a query snapshot did not measure. A use target kind or object class listed here has no rows
/// (kinds) or no <c>uses</c> and <c>applied</c> counts (classes); it is never shown as zero.
/// </summary>
/// <param name="Light">Whether the facts were written with <c>--facts-light</c>.</param>
/// <param name="MissingUseKinds">The <c>target-kind</c> values of the uses source that have no rows.</param>
/// <param name="MissingObjectClasses">The object classes whose <c>uses</c> and <c>applied</c> are not measured.</param>
public sealed record QueryCoverage(bool Light, IReadOnlyList<string> MissingUseKinds,
    IReadOnlyList<string> MissingObjectClasses)
{
    /// <summary>Facts that omit nothing.</summary>
    public static QueryCoverage Complete { get; } = new(false, [], []);

    internal string ToJson() => JsonSerializer.Serialize(this);

    internal static QueryCoverage FromJson(string json) =>
        string.IsNullOrEmpty(json) ? Complete : JsonSerializer.Deserialize<QueryCoverage>(json) ?? Complete;
}
