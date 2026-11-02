using Microsoft.Data.Sqlite;
using SIL.Motif.Host.Parsimony;
using Xunit;

namespace SIL.Motif.Tests.Worker;

public sealed class UnconditionedAllomorphOrderQueryTests : IDisposable
{
    // The correlated form the grouped query replaced; it defines the meaning the new query must keep.
    private const string CorrelatedQuery = """
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

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public UnconditionedAllomorphOrderQueryTests()
    {
        _connection.Open();
        Run("ATTACH DATABASE ':memory:' AS facts;");
        Run("CREATE TABLE facts.compiled_output(output_id INTEGER PRIMARY KEY, kind TEXT, key TEXT, " +
            "owner_output_id INTEGER, bucket TEXT, compiled_order INTEGER, has_phone_condition INTEGER);");
        Run("CREATE TABLE facts.compiled_allomorph_order(owner_output_id INTEGER, source_entry_guid TEXT, " +
            "source_msa_guid TEXT, source_allomorph_guid TEXT, output_id INTEGER, compiled_order INTEGER);");
        Run("CREATE TABLE facts.allomorph(guid TEXT PRIMARY KEY, is_abstract INTEGER);");
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void TheGroupedQueryReturnsTheRowsOfTheCorrelatedQueryForTiesAndSeveralOwners()
    {
        // Owner 1: two outputs tie for the highest order, one of them phone-conditioned, one not.
        Owner(1, "entry-a");
        Allomorph(10, 1, "a-1", order: 1, phone: 1);
        Allomorph(11, 1, "a-2", order: 3, phone: 0);
        Allomorph(12, 1, "a-3", order: 3, phone: 1);
        // Owner 2: a single output is both the lowest and the highest order.
        Owner(2, "entry-b");
        Allomorph(20, 2, "b-1", order: 5, phone: 0);
        // Owner 3: the highest order belongs to a conditioned output, so no unconditioned output is final.
        Owner(3, "entry-c");
        Allomorph(30, 3, "c-1", order: 1, phone: 0);
        Allomorph(31, 3, "c-2", order: 2, phone: 1);
        // Owner 4: an output with no order does not count toward the maximum.
        Owner(4, "entry-d");
        Allomorph(40, 4, "d-1", order: 2, phone: 0);
        Allomorph(41, 4, "d-2", order: null, phone: 0);
        // Owner 5: an unknown phone condition is never final, whatever its order.
        Owner(5, "entry-e");
        Allomorph(50, 5, "e-1", order: 4, phone: null);
        // A rule output of the same owner does not raise the allomorph maximum.
        Run("INSERT INTO facts.compiled_output VALUES (90, 'morph_rule', 'rule-1', 3, 'Morphology', 99, 0);");

        var expected = Read(CorrelatedQuery);
        var actual = Read(ParsimonyQuerySession.UnconditionedAllomorphCompilerOrderSql);

        Assert.Equal(expected, actual);
        Assert.Equal(8, actual.Count);
        Assert.Equal(["a-2", "b-1", "d-1"], actual.Where(IsFinal).Select(AllomorphOf).ToArray());
    }

    private static string AllomorphOf(string row) => row.Split('|')[4];

    private static bool IsFinal(string row) => row.Split('|')[7] == "1";

    private void Owner(int id, string key) =>
        Run($"INSERT INTO facts.compiled_output VALUES ({id}, 'lex_entry', '{key}', NULL, 'Morphology', NULL, NULL);");

    private void Allomorph(int outputId, int ownerId, string guid, int? order, int? phone)
    {
        var orderSql = order?.ToString() ?? "NULL";
        var phoneSql = phone?.ToString() ?? "NULL";
        Run($"INSERT INTO facts.compiled_output VALUES ({outputId}, 'allomorph', 'out-{guid}', {ownerId}, 'Morphology', " +
            $"{orderSql}, {phoneSql});");
        Run($"INSERT INTO facts.allomorph VALUES ('{guid}', 0);");
        Run($"INSERT INTO facts.compiled_allomorph_order VALUES ({ownerId}, 'entry-{ownerId}', 'msa-{ownerId}', " +
            $"'{guid}', {outputId}, {orderSql});");
    }

    private void Run(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private List<string> Read(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
        {
            var cells = new string[reader.FieldCount];
            for (var i = 0; i < cells.Length; i++) cells[i] = reader.IsDBNull(i) ? "~" : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)!;
            rows.Add(string.Join('|', cells) + "|");
        }
        return rows;
    }
}
