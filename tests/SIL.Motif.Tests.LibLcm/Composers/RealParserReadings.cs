using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Composers;

/// <summary>Saves a scratch project, asks the real parser for a word and names each reading by the forms it used.</summary>
internal static class RealParserReadings
{
    public static async Task<string[]> Parse(LcmCache cache, ITestOutputHelper output,
        IReadOnlyDictionary<CanonicalId, string> names, string word)
    {
        new FwDataProjectLoader().Save(cache);
        using var invoker = new PanGlossInvoker();
        return await Read(new PanGlossTracer(invoker), cache.ProjectId.Path, output, names, word);
    }

    public static async Task<string[]> Read(PanGlossTracer tracer, string path, ITestOutputHelper output,
        IReadOnlyDictionary<CanonicalId, string> names, string word)
    {
        var outcome = Assert.IsType<PanGlossTraceOutcome.Completed>(
            await tracer.TraceAsync(path, word, CancellationToken.None));
        var details = outcome.Details!;
        Assert.True(details.SearchCompleted, word);
        Assert.False(details.Capped || details.TimedOut || details.InvalidShape, word);
        var readings = outcome.Document!.Analyses.Select(analysis => string.Join("+", analysis.Morphs.Select(morph =>
            names.TryGetValue(CanonicalId.FromGuid(Guid.Parse(morph.FormId!)), out var name) ? name : "?" + morph.Form)))
            .Distinct().Order(StringComparer.Ordinal).ToArray();
        output.WriteLine($"{word}: {(readings.Length == 0 ? "no analysis" : string.Join(" | ", readings))}");
        return readings;
    }

    public static Dictionary<string, CanonicalId> Phonemes(LcmCache cache) =>
        cache.LangProject.PhonologicalDataOA.PhonemeSetsOS[0].PhonemesOC
            .SelectMany(phoneme => phoneme.CodesOS.Select(code =>
                (Text: code.Representation.get_String(cache.DefaultVernWs)?.Text, Phoneme: phoneme)))
            .Where(item => item.Text is not null)
            .ToDictionary(item => item.Text!, item => CanonicalId.FromGuid(item.Phoneme.Guid), StringComparer.Ordinal);

    public static CanonicalId ClassOf(LcmCache cache, string name, string abbreviation, params CanonicalId[] members) =>
        AffixTestSupport.Created(AffixTestSupport.ApplyOps(cache, AuthorNaturalClassComposer.Build(cache,
            new AuthorNaturalClassIntent(name, abbreviation, members))), PhPhonDataNaturalClassesOperationKinds.Create);
}
