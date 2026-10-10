using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.PanGloss;

[Collection(LcmCacheParallelCollections.Group1)]
[Trait("MotifTestLevel", "System")]
public sealed class UnsegmentableAllomorphRealParserTests(PristineProjectFixture pristine)
{
    /// <summary>
    /// A stem form holding '[', which the parser can neither segment nor define as a provisional letter, makes it
    /// skip that allomorph and warn on stderr, and Motif turns the warning into one finding naming the allomorph's
    /// GUID. Most other undefined letters become provisional letters, and a control character is refused fatally
    /// instead; nothing here pins either.
    /// </summary>
    [RealParserFact]
    public async Task BracketInAnAllomorphFormBecomesOneUnsegmentableFindingNamingItsGuid()
    {
        const string form = "ni[kah";
        string path;
        Guid allomorphId;
        using (var cache = pristine.NewScratch())
        {
            path = cache.ProjectId.Path;
            var allomorph = (IMoStemAllomorph)cache.ServiceLocator.GetInstance<IMoFormRepository>()
                .GetObject(pristine.Seed.FirstLexemeFormId);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                allomorph.Form.set_String(cache.DefaultVernWs, TsStringUtils.MakeString(form, cache.DefaultVernWs)));
            allomorphId = allomorph.Guid;
            RealParserProject.PrepareForParsing(cache, "n", "i", "k", "a", "h", "m", "o", "t", "f", "b");
        }

        using var invoker = new PanGlossInvoker();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Batch(path, ["nikah"], TimeSpan.FromSeconds(1)),
            "test:unsegmentable-allomorph", cancellation.Token, wallClockCap: TimeSpan.FromSeconds(30));
        var completed = Assert.IsType<PanGlossOutcome.Completed>(outcome);

        var lines = completed.StandardError.Split('\n').Select(line => line.Trim())
            .Where(line => line.StartsWith("warning:", StringComparison.OrdinalIgnoreCase)).ToArray();
        var merged = StoredParseWarnings.Merge(new GrammarCheckResponse([], false), lines);

        var finding = Assert.Single(merged.Findings);
        Assert.Equal(GrammarFindingCodes.ParseAllomorphUnsegmentable, finding.Code);
        var subject = Assert.Single(finding.Subject);
        Assert.Equal(allomorphId.ToString("D"), subject.SubjectGuid);
        Assert.Equal(form, subject.Title);
    }
}
