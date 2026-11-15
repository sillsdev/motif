using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Query;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Worker.ParsimonyFactsSessionHarness;

namespace SIL.Motif.Tests.Worker;

/// <summary>Each saved query returns the same items as the Measure it expresses, over the same facts file.</summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class SavedQueryEquivalenceTests(PristineProjectFixture pristine) : IDisposable
{
    private static readonly string[] Sections =
    [
        "entries", "msas", "allomorphs", "environments", "categories", "compound_rules", "features", "patterns", "phonology",
        .. ParsimonyQuerySession.StatementReferenceSections,
    ];
    private readonly ParsimonyFactsSessionHarness _harness = new(pristine);

    public void Dispose() => _harness.Dispose();

    private static string G(int number) => $"a0000000-0000-0000-0000-{number:D12}";
    private static string Canonical(string guid) => CanonicalId.FromGuid(System.Guid.Parse(guid)).Value;

    private static void Reference(SqliteConnection connection, string kind, string target, string effect, int ordinal) =>
        Insert(connection, "INSERT INTO statement_reference(target_kind, target_guid, referrer_kind, referrer_guid, role, ordinal, parser_effect) " +
            "VALUES ($p0, $p1, 'allomorph', $p2, 'phone_env', $p3, $p4);", kind, target, G(9000 + ordinal), ordinal, effect);

    private static void Seed(SqliteConnection connection)
    {
        WritingSystem(connection);
        Insert(connection, "INSERT INTO category(guid, sibling_ordinal, name, abbreviation) VALUES ($p0, 0, 'Noun', 'n');", G(1));
        // P-msa-no-category: two stem MSAs lack a part of speech, one only has a from-category, one is fine.
        foreach (var (entry, msa, form) in new[] { (10, 110, "alpa"), (11, 111, "beta"), (12, 112, "gama"), (13, 113, "delo") })
        {
            Entry(connection, G(entry), entry);
            Allomorph(connection, G(entry + 1000), G(entry), 0, "stem", form);
            Msa(connection, G(msa), G(entry), "stem");
        }
        Insert(connection, "INSERT INTO msa_category(msa_guid, role, ordinal, category_guid) VALUES ($p0, 'pos', 0, $p1);", G(113), G(1));
        Insert(connection, "INSERT INTO msa_category(msa_guid, role, ordinal, category_guid) VALUES ($p0, 'from_pos', 0, $p1);", G(112), G(1));
        // P-statement-unused and P-env-duplicate.
        void Env(int number, string representation, string left, string right) =>
            Insert(connection, "INSERT INTO environment(guid, name, representation, parse_status) VALUES ($p0, '', $p1, 'valid');", G(number), representation);
        Env(20, "/ _ a", "k-empty", "k-a"); Env(21, "/  _  a", "k-empty", "k-a"); Env(22, "/ _ b", "k-empty", "k-b"); Env(23, "/ _ c", "k-empty", "k-c");
        foreach (var (number, left, right) in new[] { (20, "k-empty", "k-a"), (21, "k-empty", "k-a"), (22, "k-empty", "k-b") })
        {
            Insert(connection, "INSERT INTO environment_side(environment_guid, side, canonical_key, shape) VALUES ($p0, 'left', $p1, 'empty');", G(number), left);
            Insert(connection, "INSERT INTO environment_side(environment_guid, side, canonical_key, shape) VALUES ($p0, 'right', $p1, 'complex');", G(number), right);
        }
        Insert(connection, "INSERT INTO natural_class(guid, kind, name) VALUES ($p0, 'segments', 'V');", G(30));
        Insert(connection, "INSERT INTO natural_class(guid, kind, name) VALUES ($p0, 'segments', 'C');", G(31));
        Reference(connection, "environment", G(20), "applied", 1);
        Reference(connection, "environment", G(22), "ignored", 2);
        Reference(connection, "naturalClass", G(30), "applied", 3);
        // P-feature-few-carriers.
        Feature(connection, G(40), "single"); Feature(connection, G(41), "unused"); Feature(connection, G(42), "wide"); Feature(connection, G(43), "ruled");
        var carrier = 500;
        void Carriers(int feature, int count)
        {
            for (var index = 0; index < count; index++, carrier++)
            {
                Entry(connection, G(carrier), carrier);
                Msa(connection, G(carrier + 10000), G(carrier), "stem");
                MsaFeature(connection, G(carrier + 10000), G(feature));
            }
        }
        Carriers(40, 1); Carriers(42, 4); Carriers(43, 2);
        Entry(connection, G(60), 60, "suffix"); Msa(connection, G(160), G(60), "inflectional");
        MsaFeature(connection, G(160), G(40)); MsaFeature(connection, G(160), G(42), 1);
        Rule(connection, G(70), "lowering", G(43), "excluded");
        // P-lexeme-near-duplicate: case, a project fold rule, an exact repeat (not a finding) and a lone form.
        foreach (var (entry, form) in new[] { (80, "Kala"), (81, "kala"), (82, "kaliya"), (83, "kalya"), (84, "same"), (85, "same"), (86, "lone") })
        {
            Entry(connection, G(entry), entry);
            Allomorph(connection, G(entry + 1000), G(entry), 0, "stem", form);
        }
    }

    private static IReadOnlySet<string> Identities(ParsimonyMeasureResult result) =>
        result.Findings.Select(f => f.AttachesTo.Identity).ToHashSet(StringComparer.Ordinal);

    [Fact]
    public async Task EachSavedQueryReturnsTheItemsOfItsMeasure()
    {
        string facts = "";
        await _harness.RunAsync(path => { facts = path; EditFacts(path, Sections, Seed); }, (session, bundle) =>
        {
            session.LexemeFold = [new FoldRule("iy", "y")];
            var snapshot = QueryFactsReader.Read(facts, "qaa");
            QueryResult Run(string measure, Func<QueryRecord, QueryRecord>? tweak = null)
            {
                Assert.True(SavedQueries.TryGet(measure, out var query));
                query = (tweak?.Invoke(query) ?? query) with { Examples = 1000 };
                return query.From == "forms" ? FormQuery.Run(query, snapshot.Forms, snapshot.Graphemes)
                    : FormQuery.RunRows(query, query.From switch
                    {
                        "stems" => snapshot.Stems!,
                        "uses" => snapshot.Uses!,
                        _ => snapshot.Objects!,
                    }, snapshot.Graphemes);
            }

            // P-msa-no-category: the MSA identity ends in the MSA guid; a from-category row is not a part of speech.
            var noCategory = Identities(MeasureRunner.Execute("P-msa-no-category", session, bundle))
                .Select(identity => Canonical(identity.Split('/')[^1])).Order().ToArray();
            Assert.Equal(noCategory, Run("P-msa-no-category").Items!.Select(item => item.Id).Order().ToArray());
            Assert.All([110, 111, 112], n => Assert.Contains(Canonical(G(n)), noCategory));
            Assert.DoesNotContain(Canonical(G(113)), noCategory);

            // P-statement-unused.
            var unused = MeasureRunner.Execute("P-statement-unused", session, bundle).Findings
                .Select(f => Canonical(f.FindingId.Split(':')[^1])).Order().ToArray();
            Assert.Equal(unused, Run("P-statement-unused").Items!.Select(item => item.Id).Order().ToArray());
            Assert.Equal(4, unused.Length);

            // P-env-duplicate: one group of the two spellings that share both side keys.
            var duplicates = Identities(MeasureRunner.Execute("P-env-duplicate", session, bundle));
            var groups = Run("P-env-duplicate").Groups;
            var members = groups.Select(g => ParsimonyFindingIdentity.DuplicateEnvironmentsIdentity(
                g.Samples!.Select(s => FromCanonical(s.Id)).Order(StringComparer.Ordinal))).ToHashSet(StringComparer.Ordinal);
            Assert.Equal(duplicates, members);
            Assert.Single(members);

            // P-feature-few-carriers.
            var features = Identities(MeasureRunner.Execute("P-feature-few-carriers", session, bundle))
                .Select(identity => Canonical(identity.Split('/')[^1])).Order().ToArray();
            Assert.Equal(features, Run("P-feature-few-carriers").Items!.Select(item => item.Id).Order().ToArray());
            Assert.Equal(2, features.Length);

            // P-lexeme-near-duplicate, with the project's fold rule resolved as the command resolves it.
            var near = Identities(MeasureRunner.Execute("P-lexeme-near-duplicate", session, bundle));
            var expected = Run("P-lexeme-near-duplicate", q => q with { Fold = QueryFold.ResolveProject(q.Fold, [new FoldRule("iy", "y")]) })
                .Groups.Select(g => ParsimonyFindingIdentity.NearDuplicateLexemesIdentity(
                    g.Examples.Select(e => e.Entry).Order(StringComparer.Ordinal).ToArray())).ToHashSet(StringComparer.Ordinal);
            Assert.Equal(near, expected);
            Assert.Equal(2, expected.Count);
            return 0;
        });
    }

    private static readonly Lazy<Dictionary<string, string>> Fixtures = new(() =>
        Enumerable.Range(0, 20000).Select(G).ToDictionary(Canonical, g => g, StringComparer.Ordinal));

    private static string FromCanonical(string id) => Fixtures.Value[id];
}
