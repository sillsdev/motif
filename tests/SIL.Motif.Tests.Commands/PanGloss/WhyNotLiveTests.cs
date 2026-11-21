using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.PanGloss;

/// <summary>Runs only against a real parser; it is a no-op until that parser declares the why-not capability.</summary>
[Collection(LcmCacheParallelCollections.Group1)]
[Trait("MotifTestLevel", "System")]
public sealed class WhyNotLiveTests(PristineProjectFixture pristine)
{
    [RealParserFact]
    public async Task ARealParserThatDeclaresWhyNotPassesMotifsDetection()
    {
        using var description = await FakePanGlossSurfaceTests.Describe(PanGlossExecutable.TryLocate()!);
        if (!description.RootElement.GetProperty("interfaces").TryGetProperty("whyNot", out _)) return;
        var capabilities = PanGlossSurface.ReadDescription(description.RootElement.GetRawText(), out _)!;
        Assert.Null(capabilities.WhyNotRefusal());
        Assert.NotEmpty(capabilities.WhyNot!.Kinds);
    }

    [RealParserFact]
    public async Task ARealParserExplainsAnUnmetEnvironmentOnAMotifProject()
    {
        var executable = PanGlossExecutable.TryLocate()!;
        using (var description = await FakePanGlossSurfaceTests.Describe(executable))
            if (!description.RootElement.GetProperty("interfaces").TryGetProperty("whyNot", out _)) return;

        string path, spec, environmentGuid;
        using (var cache = pristine.NewScratch())
        {
            path = cache.ProjectId.Path;
            IPhEnvironment environment = null!;
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                environment = cache.ServiceLocator.GetInstance<IPhEnvironmentFactory>().Create();
                cache.LangProject.PhonologicalDataOA.EnvironmentsOS.Add(environment);
                environment.StringRepresentation = TsStringUtils.MakeString("/ _ b", cache.DefaultVernWs);
                ((IMoStemAllomorph)cache.ServiceLocator.GetInstance<IMoFormRepository>()
                    .GetObject(pristine.Seed.FirstLexemeFormId)).PhoneEnvRC.Add(environment);
            });
            environmentGuid = environment.Guid.ToString();
            var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
            spec = entry.Guid + ":" + entry.MorphoSyntaxAnalysesOC.First().Guid;
            RealParserProject.PrepareForParsing(cache, "m", "o", "t", "i", "f", "a", "b");
        }

        Assert.True(WhyNotCandidateSpec.TryParse(spec, out var candidate, out var error), error);
        using var invoker = new PanGlossInvoker(executable);
        var outcome = await invoker.RunAsync(new PanGlossRequest.WhyNot(path, SeededProject.FirstForm, [candidate!]),
            "why-not live", CancellationToken.None, TimeSpan.FromSeconds(60));
        var completed = Assert.IsType<PanGlossOutcome.Completed>(outcome);
        Assert.True(WhyNotProjection.TryProject(completed.Output, SeededProject.FirstForm, "baseline", [candidate!],
            completed.WhyNotKinds ?? [], out var response, out var problem), problem);
        var rejection = response!.Candidates.Single().Rejection!;
        Assert.Equal("environment_unmet", rejection.Kind);
        Assert.Equal(environmentGuid, rejection.Environment!.FieldWorksGuid);
        Assert.Equal("/ _ b", rejection.Environment.Text);
    }
}
