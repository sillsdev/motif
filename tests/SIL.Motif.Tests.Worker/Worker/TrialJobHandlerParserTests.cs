using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Worker;

[Collection(LcmCacheTestCollection.Name)]
public sealed class TrialJobHandlerParserTests : IDisposable
{
    private readonly TrialJobHandlerTests _fixture;

    public TrialJobHandlerParserTests(PristineProjectFixture pristine, ITestOutputHelper output) =>
        _fixture = new TrialJobHandlerTests(pristine, output);

    public void Dispose() => _fixture.Dispose();

    [RealParserFact]
    public Task ATrialOfBothProhibitionKindsPersistsTheDraftAndMatchesApply() =>
        _fixture.AssertProhibitionTrialParityAsync();
}
