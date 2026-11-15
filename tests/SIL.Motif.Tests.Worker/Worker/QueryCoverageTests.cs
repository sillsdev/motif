using Microsoft.Data.Sqlite;
using SIL.Motif.Host.Parsimony;
using Xunit;

namespace SIL.Motif.Tests.Worker;

/// <summary>The uses and objects sources say what light or oversized facts did not measure instead of showing zero.</summary>
public sealed class QueryCoverageTests
{
    private static SqliteConnection Open(bool light)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE artifact_meta(complete INTEGER);
            CREATE TABLE statement_reference(target_kind TEXT, target_guid TEXT, referrer_kind TEXT, referrer_guid TEXT,
                role TEXT, parser_effect TEXT, ordinal INTEGER);
            CREATE TABLE environment(guid TEXT, name TEXT, representation TEXT, parse_status TEXT);
            CREATE TABLE exception_feature(guid TEXT, name TEXT, abbreviation TEXT);
            CREATE TABLE category(guid TEXT, name TEXT, abbreviation TEXT);
            INSERT INTO environment VALUES ('a0000000-0000-0000-0000-000000000001', 'e1', '/ _ a', 'valid');
            INSERT INTO exception_feature VALUES ('a0000000-0000-0000-0000-000000000002', 'f1', 'f1');
            INSERT INTO category VALUES ('a0000000-0000-0000-0000-000000000003', 'c1', 'c1');
            INSERT INTO statement_reference VALUES ('environment', 'a0000000-0000-0000-0000-000000000001', 'allomorph', 'a0000000-0000-0000-0000-000000000009', 'phone_env', 'applied', 0);
            INSERT INTO statement_reference VALUES ('environment', 'a0000000-0000-0000-0000-000000000001', 'allomorph', 'a0000000-0000-0000-0000-00000000000a', 'phone_env', 'applied', 1);
            INSERT INTO statement_reference VALUES ('category', 'a0000000-0000-0000-0000-000000000003', 'msa', 'a0000000-0000-0000-0000-00000000000b', 'pos', 'applied', 0);
            """;
        command.ExecuteNonQuery();
        if (light)
        {
            command.CommandText = "ALTER TABLE artifact_meta ADD COLUMN light_omissions TEXT;";
            command.ExecuteNonQuery();
        }
        return connection;
    }

    [Fact]
    public void LightFactsNameTheUnmeasuredKindsAndLeaveObjectCountsEmptyNotZero()
    {
        using var connection = Open(light: true);
        var coverage = QuerySourceRows.Coverage(connection, []);
        Assert.True(coverage.Light);
        Assert.Equal(["exception-feature", "category", "slot", "inflection-class"], coverage.MissingUseKinds);
        Assert.Equal(coverage.MissingUseKinds, coverage.MissingObjectClasses);
        var objects = QuerySourceRows.ReadObjects(connection).ToDictionary(row => row.Values[0][0]);
        Assert.Equal(["2"], objects["environment"].Values[2]);
        Assert.Empty(objects["exception-feature"].Values[2]);
        Assert.Empty(objects["exception-feature"].Values[3]);
        Assert.Empty(objects["category"].Values[2]);
    }

    [Fact]
    public void FullFactsMeasureEveryClass()
    {
        using var connection = Open(light: false);
        Assert.False(QuerySourceRows.Coverage(connection, []).Light);
        var objects = QuerySourceRows.ReadObjects(connection).ToDictionary(row => row.Values[0][0]);
        Assert.Equal(["0"], objects["exception-feature"].Values[2]);
        Assert.Equal(["1"], objects["category"].Values[2]);
    }

    [Fact]
    public void TheUsesCapKeepsWholeTargetKindsAndNamesTheOnesItLeftOut()
    {
        using var connection = Open(light: false);
        var (rows, capped) = QuerySourceRows.ReadUses(connection, maximum: 2);
        Assert.Equal(["category"], rows.Select(row => row.Values[0][0]).Distinct());
        Assert.Equal(["environment"], capped);
        Assert.Equal(["environment"], QuerySourceRows.Coverage(connection, capped).MissingUseKinds);
        Assert.Equal(3, QuerySourceRows.ReadUses(connection).Rows.Count);
    }
}
