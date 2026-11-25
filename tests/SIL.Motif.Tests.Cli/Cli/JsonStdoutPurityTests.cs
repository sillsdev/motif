using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SIL.Motif.Cli;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// With <c>--json</c>, stdout is exactly one JSON document, or empty when the command refused (the refusal goes to
/// stderr); a log line, banner or warning on stdout makes a script slice the text by hand. The sweep runs each verb
/// against a missing project, so it mostly exercises the refusal path.
/// </summary>
public sealed class JsonStdoutPurityTests
{
    private static (int Exit, string Out, string Err) Run(string root, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [RunnerOptions.RootVariable] = root,
            ["MOTIF_SUPPRESS_KICK"] = "1",
        };
        var exit = SIL.Motif.Cli.Program.Run(args, output, error, environment, static (_, _) => { });
        return (exit, output.ToString(), error.ToString());
    }

    private static void AssertOneDocumentOrNothing(string stdout, string label)
    {
        if (stdout.Length == 0) return;
        try
        {
            using var document = JsonDocument.Parse(stdout);
        }
        catch (JsonException exception)
        {
            Assert.Fail($"{label}: stdout is not exactly one JSON document ({exception.Message}): {stdout[..Math.Min(200, stdout.Length)]}");
        }
    }

    public static IEnumerable<object[]> JsonVerbs() =>
        CliVerbCatalog.All
            .Where(verb => verb.UsageLines.Any(line => line.Contains("--json", StringComparison.Ordinal)))
            .Select(verb => verb.CommandName).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(JsonVerbs))]
    public void EveryJsonVerbPrintsOnlyJsonToStdoutWhenItRefuses(string commandName)
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-json-purity", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var missing = Path.Combine(root, "absent.fwdata");
            var argv = commandName.Split(' ').Concat(["--json", "--project", missing]).ToArray();
            var plain = Run(root, argv);
            AssertOneDocumentOrNothing(plain.Out, commandName);
            var picked = Run(root, [.. argv, "--pick", "no.such.field"]);
            AssertOneDocumentOrNothing(picked.Out, commandName + " --pick");
            if (plain.Exit != 0)
            {
                Assert.Empty(picked.Out);
                Assert.Equal(plain.Exit, picked.Exit);
                Assert.Equal(plain.Err, picked.Err);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ASuccessfulJsonVerbPrintsExactlyOneDocumentAndPickKeepsTheNamedFields()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-json-purity", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var whole = Run(root, "query", "--schema", "--json");
            Assert.Equal(0, whole.Exit);
            using var document = JsonDocument.Parse(whole.Out);
            var first = document.RootElement.EnumerateObject().First().Name;

            var picked = Run(root, "query", "--schema", "--json", "--pick", first);
            Assert.Equal(0, picked.Exit);
            using var projection = JsonDocument.Parse(picked.Out);
            Assert.Equal([first], projection.RootElement.EnumerateObject().Select(p => p.Name));

            var bad = Run(root, "query", "--schema", "--json", "--pick", first + ".definitelyMissing,nope");
            Assert.NotEqual(0, bad.Exit);
            Assert.Empty(bad.Out);
            Assert.Contains("cli.pick-path", bad.Err, StringComparison.Ordinal);

            var withoutJson = Run(root, "query", "--schema", "--pick", first);
            Assert.NotEqual(0, withoutJson.Exit);
            Assert.Empty(withoutJson.Out);
            Assert.Contains("cli.pick-path", withoutJson.Err, StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
