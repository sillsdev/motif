using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class PinnedGrammarAdviceTests(PristineProjectFixture pristine)
{
    [SIL.Motif.Tests.Parser.RealParserFact]
    public void PinnedParserSuppliesAdviceForEverySeededFinding()
    {
        var grammar = WarningGrammar.Author(pristine);
        using (var cache = new SIL.Motif.Host.LcmUtils.FwDataProjectLoader().LoadCache(grammar.FwDataPath))
            RealParserProject.PrepareForParsing(cache);
        using var invoker = new PanGlossInvoker(SIL.Motif.Host.Parser.PanGlossExecutable.TryLocate());
        var parsed = invoker.RunAsync(new PanGlossRequest.GrammarHealth(grammar.FwDataPath, "Seeded advice"),
            "seeded-advice", CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(parsed is PanGlossOutcome.Completed, parsed.ToString());
        var completed = (PanGlossOutcome.Completed)parsed;
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "seeded-advice-raw.json"), completed.Output);
        using var raw = JsonDocument.Parse(completed.Output);
        var diagnostics = raw.RootElement.GetProperty("diagnostics").EnumerateArray().ToArray();
        Assert.True(diagnostics.Length >= 3);
        Assert.Contains(diagnostics, item => item.GetProperty("code").GetString() == "provisional.letter");
        Assert.All(diagnostics, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("explanation").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("guidance").GetString()));
        });
    }

}
