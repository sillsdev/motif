using System.Diagnostics;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Worker;

/// <summary>P-env-literal-host over facts a stand-in parser wrote and the test patched with the defect.</summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class ParsimonyEnvironmentLiteralHostTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    private const string Measure = "P-env-literal-host";
    private const string Entry = "10000000-0000-0000-0000-000000000001";

    private static string Env(int index) => $"40000000-0000-0000-0000-{index:D12}";

    private static string Rule(int index) => $"50000000-0000-0000-0000-{index:D12}";

    private static string Allomorph(int index) => $"60000000-0000-0000-0000-{index:D12}";

    // Tokens: "#" at an edge is an anchor, "[X]" a natural class, anything else one literal segment.
    private static Node[] Tokens(params string[] tokens)
    {
        var nodes = new List<Node>();
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (token == "#") nodes.Add(new Node(index == 0 ? "leftAnchor" : "rightAnchor"));
            else if (token.StartsWith('[')) nodes.Add(new Node("naturalClass"));
            else
            {
                nodes.Add(new Node("literalSegments", token));
                nodes.Add(new Node("phoneme", null, nodes.Count - 1));
            }
        }
        return nodes.ToArray();
    }

    private static void Environment(FactsWriter facts, int index, string left, string right, int users = 1,
        string role = "phone")
    {
        var sides = new[] { ("left", left), ("right", right) };
        facts.Environment(Env(index), $"/ {left} _ {right}", $"key-{index}-l", $"key-{index}-r");
        foreach (var (side, text) in sides)
            if (text.Length > 0)
                facts.PatternRoot("environment", Env(index), "environment_" + side, 0,
                    Tokens(text.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
        for (var user = 0; user < users; user++)
            facts.Allomorph(Allomorph(index * 10 + user), Entry, $"pim{index}x{user}", (role, Env(index)));
    }

    private static void RewriteRule(FactsWriter facts, int index, string left, string right)
    {
        long? Root(string role, string text) => text.Length == 0 ? null : facts.PatternRoot("phonologicalRule",
            Rule(index), role, 0, Sequence(text.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
        var leftRoot = Root("rewrite_left_context", left);
        var rightRoot = Root("rewrite_right_context", right);
        facts.RewriteRule(Rule(index), $"rule {index}", leftRoot, rightRoot);
    }

    // A rule context is a sequence node whose children are the phonemes, classes and repetitions in order.
    private static Node[] Sequence(string[] tokens) => [new Node("sequence"), .. tokens.Select(token => token switch
    {
        "[C]" => new Node("naturalClass", Parent: 0),
        "(V)*" => new Node("iteration", Parent: 0),
        "+" => new Node("boundary", "+", 0),
        _ => new Node("phoneme", Parent: 0, PhonemeGuid: "70000000-0000-0000-0000-000000000001"),
    })];

    private static void Prepare(FactsWriter facts)
    {
        facts.WritingSystem();
        facts.Entry(Entry);
        facts.Phoneme("70000000-0000-0000-0000-000000000001", "p");
    }

    // The shapes a reviewer must not see flagged.
    private static void WriteCleanControls(FactsWriter facts)
    {
        Environment(facts, 1, "[V]", "");
        Environment(facts, 2, "# [C]", "");
        Environment(facts, 3, "n", "");
        Environment(facts, 4, "y", "");
        Environment(facts, 5, "-", "");
        Environment(facts, 6, "", "[V]");
        Environment(facts, 7, "[F-C] [F-V] [F-C] [F-V]", "");
        Environment(facts, 8, "p i", "[V]");
        RewriteRule(facts, 1, "p i", "[C]");
        RewriteRule(facts, 2, "p i m (V)*", "");
    }

    [Fact]
    public async Task P_env_literal_host_word_listing_flags_each_literal_side_and_names_its_users()
    {
        using var harness = new PatchedFactsHarness(pristine);
        await harness.RunAsync(Measure, (connection, transaction) =>
        {
            var facts = new FactsWriter(connection, transaction);
            Prepare(facts);
            WriteCleanControls(facts);
            Environment(facts, 20, "# p i m a", "");
            Environment(facts, 21, "", "p a m #", users: 1, role: "position");
            Environment(facts, 22, "x y", "-");
            Environment(facts, 23, "p i m", "");
            RewriteRule(facts, 30, "p i m", "[C]");
            RewriteRule(facts, 31, "", "p i m a p i m");
        }, (session, bundleId) =>
        {
            var result = MeasureRunner.Execute(Measure, session, bundleId);

            Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
            var flagged = result.Findings.Select(finding => finding.AttachesTo.Identity.Split('/')[2]).Order().ToArray();
            Assert.Equal(new[] { Env(20), Env(21), Env(22), Env(23), Rule(30), Rule(31) }.Order(), flagged);
            var infix = Assert.Single(result.Findings, finding => finding.AttachesTo.Identity.Contains(Env(21)));
            Assert.Contains(infix.Limitations, text => text.Contains("infix position"));
            var rule = Assert.Single(result.Findings, finding => finding.AttachesTo.Identity.Contains(Rule(31)));
            Assert.Contains(rule.Limitations, text => text.Contains("7 literal segments"));
        });
    }

    [Fact]
    public async Task P_env_literal_host_clean_control_has_no_finding()
    {
        using var harness = new PatchedFactsHarness(pristine);
        await harness.RunAsync(Measure, (connection, transaction) =>
        {
            var facts = new FactsWriter(connection, transaction);
            Prepare(facts);
            WriteCleanControls(facts);
        }, (session, bundleId) =>
        {
            var result = MeasureRunner.Execute(Measure, session, bundleId);

            Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
            Assert.Empty(result.Findings);
            Assert.True(result.Run.EligibleItems >= 12, $"inspected {result.Run.EligibleItems} sides");
        });
    }

    [Fact]
    public async Task P_env_literal_host_runs_in_under_a_second_at_CEB_scale()
    {
        using var harness = new PatchedFactsHarness(pristine);
        await harness.RunAsync(Measure, (connection, transaction) =>
        {
            var facts = new FactsWriter(connection, transaction);
            Prepare(facts);
            for (var index = 0; index < 1500; index++)
                Environment(facts, index, index % 100 == 0 ? "# p i m a" : "[C] [V]", "[V]", users: 0);
            for (var index = 0; index < 26000; index++)
                facts.Allomorph(Allomorph(10_000_000 + index), Entry, $"f{index}", ("phone", Env(index % 1500)));
            for (var index = 0; index < 150; index++) RewriteRule(facts, index, "p i m", "[C]");
        }, (session, bundleId) =>
        {
            var clock = Stopwatch.StartNew();
            var result = MeasureRunner.Execute(Measure, session, bundleId);
            clock.Stop();
            output.WriteLine($"P-env-literal-host at 3150 sides, 26000 allomorphs: {clock.ElapsedMilliseconds} ms");

            Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
            Assert.Equal(15 + 150, result.Findings.Count);
            Assert.True(clock.ElapsedMilliseconds < 1000, $"took {clock.ElapsedMilliseconds} ms");
        });
    }
}
