using System.Text.Json;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Gdr;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Worker.Store;

/// <summary>
/// Reads the newest stored parse outcome of example words from the Assessment rows a Trial or Assessment already wrote
/// (ADR 0119). It never parses. Newest first, at most <see cref="AssessmentCap"/> Assessments are tried, and each is asked
/// only for the words still unsettled, through the <c>(AssessmentId, Word)</c> index: O(examples log rows) per Assessment.
/// A word-set binding is followed to the Assessment that owns the rows, as every other word read does.
/// </summary>
public sealed class GdrExampleEvidenceRepository(MotifDatabase database) : IGdrExampleEvidence
{
    /// <summary>How many of the newest word Assessments are tried before the remaining words are called unverified.</summary>
    public const int AssessmentCap = 25;

    private readonly MotifDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    /// <inheritdoc />
    public IReadOnlyDictionary<string, GdrExampleReading> Read(IReadOnlyCollection<string> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        var found = new Dictionary<string, GdrExampleReading>(StringComparer.Ordinal);
        if (words.Count == 0) return found;
        using var connection = _database.OpenConnection();
        var assessments = new List<string>();
        using (var list = connection.CreateCommand())
        {
            list.CommandText = "SELECT AssessmentId FROM Assessments WHERE SubjectKind = 'words' AND Kind IN ($a, $b, $c) " +
                "ORDER BY SavedUtc DESC, AssessmentId DESC LIMIT $cap;";
            list.Parameters.AddWithValue("$a", AssessmentKind.ParseTime.ToStoredKind());
            list.Parameters.AddWithValue("$b", AssessmentKind.Correctness.ToStoredKind());
            list.Parameters.AddWithValue("$c", AssessmentKind.ObjectTiming.ToStoredKind());
            list.Parameters.AddWithValue("$cap", AssessmentCap);
            using var reader = list.ExecuteReader();
            while (reader.Read()) assessments.Add(reader.GetString(0));
        }

        var remaining = new HashSet<string>(words, StringComparer.Ordinal);
        foreach (var assessment in assessments)
        {
            if (remaining.Count == 0) break;
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT aw.Word, aw.Outcome, pa.MorphemeGuidsJson
                FROM AssessedWords aw
                LEFT JOIN ParsedAnalyses pa ON pa.AssessedWordId = aw.AssessedWordId
                WHERE aw.AssessmentId = COALESCE((SELECT OwnerAssessmentId FROM AssessmentWordSetBindings
                    WHERE AssessmentId = $id), $id)
                  AND aw.Word IN (SELECT value FROM json_each($forms))
                ORDER BY aw.AssessedWordId, pa.OrdinalIndex;
                """;
            command.Parameters.AddWithValue("$id", assessment);
            command.Parameters.AddWithValue("$forms", JsonSerializer.Serialize(remaining));
            var rows = new Dictionary<string, (string Outcome, List<IReadOnlyList<Guid>> Analyses)>(StringComparer.Ordinal);
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                {
                    var word = reader.GetString(0);
                    if (!rows.TryGetValue(word, out var row)) rows[word] = row = (reader.GetString(1), []);
                    if (!reader.IsDBNull(2))
                        row.Analyses.Add(JsonSerializer.Deserialize<string[]>(reader.GetString(2))!
                            .Select(id => Guid.TryParse(id, out var guid) ? guid : Guid.Empty).ToArray());
                }
            foreach (var (word, row) in rows)
            {
                var parsed = row.Outcome == "analysed";
                var conclusive = parsed || row.Outcome == "no-analysis";
                // A timed-out, capped or skipped word settles nothing, so an older Assessment may still answer it.
                if (!conclusive) continue;
                found[word] = new GdrExampleReading(assessment, parsed, true, row.Analyses);
                remaining.Remove(word);
            }
        }
        return found;
    }
}
