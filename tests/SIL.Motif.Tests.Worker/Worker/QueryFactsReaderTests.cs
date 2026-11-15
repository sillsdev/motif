using Microsoft.Data.Sqlite;
using SIL.Motif.Host.Parsimony;
using Xunit;

namespace SIL.Motif.Tests.Worker.Worker;

public sealed class QueryFactsReaderTests
{
    [Fact]
    public void ResolvesEntryAndSenseComponentsAndSkipsMissingLexemeAndUnresolved()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE allomorph(guid TEXT, entry_guid TEXT, ordinal INTEGER, morph_type TEXT);
                    CREATE TABLE allomorph_form(allomorph_guid TEXT, ordinal INTEGER, writing_system TEXT, form TEXT);
                    CREATE TABLE allomorph_environment(allomorph_guid TEXT, environment_guid TEXT);
                    CREATE TABLE environment(guid TEXT, representation TEXT);
                    CREATE TABLE msa(msa_guid TEXT, entry_guid TEXT, kind TEXT);
                    CREATE TABLE msa_category(msa_guid TEXT, category_guid TEXT);
                    CREATE TABLE category(guid TEXT, name TEXT, abbreviation TEXT);
                    CREATE TABLE sense(sense_guid TEXT, entry_guid TEXT);
                    CREATE TABLE entry_variant(variant_entry_guid TEXT, ref_guid TEXT, component_kind TEXT, component_guid TEXT);
                    CREATE TABLE entry_variant_type(ref_guid TEXT, ordinal INTEGER, type_guid TEXT);
                    CREATE TABLE phoneme_grapheme(writing_system TEXT, grapheme TEXT);
                    """;
                command.ExecuteNonQuery();
                void Insert(string table, params object[] values)
                {
                    using var insert = connection.CreateCommand();
                    var parameters = values.Select((_, index) => "$p" + index).ToArray();
                    insert.CommandText = $"INSERT INTO {table} VALUES ({string.Join(",", parameters)})";
                    for (var i = 0; i < values.Length; i++) insert.Parameters.AddWithValue(parameters[i], values[i]);
                    insert.ExecuteNonQuery();
                }
                Insert("allomorph", "alt", "main", 0, "stem");
                Insert("allomorph", "lex", "main", 1, "stem");
                Insert("allomorph", "v1", "variant1", 0, "stem");
                Insert("allomorph", "v2", "variant2", 0, "stem");
                Insert("allomorph", "missing", "empty", 0, "stem");
                Insert("allomorph", "lex2", "main2", 0, "stem");
                Insert("allomorph_form", "alt", 0, "x", "rana");
                Insert("allomorph_form", "lex", 0, "x", "dana");
                Insert("allomorph_form", "v1", 0, "x", "ranga");
                Insert("allomorph_form", "v2", 0, "x", "rang");
                Insert("allomorph_form", "missing", 0, "x", "unresolved");
                Insert("allomorph_form", "lex2", 0, "x", "different");
                Insert("allomorph_form", "lex", 1, "other", "wrong");
                Insert("msa", "msa", "main", "stem");
                Insert("msa_category", "msa", "category");
                Insert("category", "category", "Verb", "v");
                Insert("sense", "sense", "main");
                Insert("sense", "sense2", "main");
                Insert("entry_variant", "variant1", "ref1", "entry", "main");
                Insert("entry_variant", "variant1", "ref1", "sense", "sense2");
                Insert("entry_variant", "variant1", "ref1", "entry", "main2");
                Insert("entry_variant", "variant2", "ref2", "sense", "sense");
                Insert("entry_variant", "empty", "ref3", "unresolved", "missing");
                Insert("entry_variant_type", "ref1", 0, "type");
                Insert("phoneme_grapheme", "x", "ng");
                Insert("phoneme_grapheme", "other", "xx");
            }
            var snapshot = QueryFactsReader.Read(path, "x");
            Assert.Equal(6, snapshot.Forms.Count);
            Assert.Single(snapshot.Forms, f => f.Kind == "lexeme" && f.Form == "dana");
            Assert.Equal(["v"], Assert.Single(snapshot.Forms, f => f.Kind == "alternate" && f.Base == "dana").Category);
            Assert.Equal(2, snapshot.Forms.Count(f => f.Kind == "variant" && f.Base == "dana"));
            Assert.Single(snapshot.Forms, f => f.Kind == "variant" && f.Base == "different");
            Assert.Equal(1, snapshot.UnresolvedVariants);
            Assert.DoesNotContain(snapshot.Forms, f => f.Form == "unresolved");
            Assert.Equal(["ng"], snapshot.Graphemes);
        }
        finally { File.Delete(path); }
    }
}
