using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SIL.Motif.Generator;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// A Proposal's status names and the table of which status may move to which live in one lifecycle
/// module, so a command or the store spelling a status itself would drift from that table unnoticed.
/// </summary>
public sealed class ProposalLifecycleSourceTests
{
    private static readonly Regex StatusLiteral = new(
        "\"(withdrawn|applied|rejected|deferred|superseded|proposed)\"", RegexOptions.None);

    // Files whose matching literal is not a Proposal status, each with the reason.
    private static readonly Dictionary<string, string> NotAStatus = new(StringComparer.Ordinal)
    {
        ["SIL.Motif.Commands/Queries/TraceReadingBuilder.cs"] = "a parser trace step's outcome word",
    };

    [Fact]
    public void NoProposalStatusLiteralOutsideTheLifecycle()
    {
        var src = Path.Combine(RepoPaths.FindRepoRoot(), "src");
        var offences = new List<string>();
        foreach (var project in new[] { "SIL.Motif.Commands", "SIL.Motif.Worker.Runtime" })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(src, project), "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(src, file).Replace('\\', '/');
            if (relative.EndsWith("/ProposalLifecycle.cs", StringComparison.Ordinal) || NotAStatus.ContainsKey(relative))
                continue;
            var number = 0;
            foreach (var line in File.ReadLines(file))
            {
                number++;
                var code = line.TrimStart();
                if (code.StartsWith("//", StringComparison.Ordinal)) continue;
                if (StatusLiteral.IsMatch(line)) offences.Add($"{relative}:{number}: {code}");
            }
        }

        Assert.True(offences.Count == 0, string.Join(Environment.NewLine, offences));
    }
}
