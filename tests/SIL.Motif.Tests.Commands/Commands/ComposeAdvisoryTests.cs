using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>Composed batches carry advisories for patterns that usually belong in a different model; never refusals.</summary>
[Collection(LcmCacheParallelCollections.Group2)]
public sealed class ComposeAdvisoryTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";

    private sealed record Project(string Path, string Vernacular, string Analysis, string[] Entries, string[] Stems,
        string? InflectionClass);

    private Project NewProject(string draft, int extraEntries = 0, bool withClass = false)
    {
        using var cache = pristine.NewScratch();
        var entries = cache.ServiceLocator.GetInstance<ILexEntryRepository>();
        var seeded = new[] { entries.GetObject(pristine.Seed.FirstEntryId), entries.GetObject(pristine.Seed.SecondEntryId) };
        var created = new List<ILexEntry>();
        IMoInflClass? inflectionClass = null;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            for (var index = 0; index < extraEntries; index++)
            {
                var entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
                var form = cache.ServiceLocator.GetInstance<IMoStemAllomorphFactory>().Create();
                entry.LexemeFormOA = form;
                form.Form.set_String(cache.DefaultVernWs, $"advised{index}");
                created.Add(entry);
            }
            if (!withClass) return;
            var category = seeded[0].MorphoSyntaxAnalysesOC.OfType<IMoStemMsa>().First().PartOfSpeechRA;
            inflectionClass = cache.ServiceLocator.GetInstance<IMoInflClassFactory>().Create();
            category.InflectionClassesOC.Add(inflectionClass);
            inflectionClass.Name.set_String(cache.DefaultAnalWs, "Lone class");
            foreach (var msa in seeded.Select(entry => entry.MorphoSyntaxAnalysesOC.OfType<IMoStemMsa>().First()))
                msa.PartOfSpeechRA = category;
        });
        var project = new Project(cache.ProjectId.Path,
            cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs),
            cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs),
            seeded.Concat(created).Select(entry => CanonicalId.FromGuid(entry.Guid).Value).ToArray(),
            seeded.Select(entry => CanonicalId.FromGuid(entry.MorphoSyntaxAnalysesOC.OfType<IMoStemMsa>().First().Guid).Value)
                .ToArray(),
            inflectionClass is null ? null : CanonicalId.FromGuid(inflectionClass.Guid).Value);
        new FwDataProjectLoader().Save(cache);
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(project.Path, Version, draft, "advisories")).Succeeded);
        return project;
    }

    private static string AddMsaBatch(Project project, IEnumerable<string> targets) => $$$"""
        {"intents":[
          {"verb":"compose-author-part-of-speech","as":"verb","intent":{"name":"Verb","abbreviation":"v","ws":"{{{project.Analysis}}}"}},
          {"verb":"compose-add-morphosyntax-analysis",
           "forEach":{"targets":[{{{string.Join(",", targets.Select(target => $"\"{target}\""))}}}]},
           "intent":{"entry":"{target.id}","msa":{"kind":"stem","category":"@verb"},"gloss":"verb use","analysisWs":"{{{project.Analysis}}}"}}
        ]}
        """;

    [Fact]
    public void FiveIntentsAddingTheSameAnalysisAreAdvisedTowardAClassOrAffix()
    {
        var project = NewProject("repeated", extraEntries: 3);

        var batch = ProposalCommands.ComposeBatch(new(project.Path, Version, "repeated", AddMsaBatch(project, project.Entries)));

        Assert.True(batch.Succeeded, batch.Refusal?.Message);
        var advisory = Assert.Single(batch.Value!.Advisories!);
        Assert.Equal(ComposeAdvisoryCodes.RepeatedCondition, advisory.Code);
        Assert.Equal([1, 2, 3, 4, 5], advisory.Intents);
        Assert.Contains("inflection class", advisory.Message);
        Assert.Same(batch.Value.Advisories, batch.Value.Bounded(1).Advisories);
    }

    [Fact]
    public void FewerThanFiveRepeatsCarryNoAdvisory()
    {
        var project = NewProject("few");

        var batch = ProposalCommands.ComposeBatch(new(project.Path, Version, "few", AddMsaBatch(project, project.Entries)));

        Assert.True(batch.Succeeded, batch.Refusal?.Message);
        Assert.Null(batch.Value!.Advisories);
    }

    [Fact]
    public void AnEnvironmentThatSpellsOneWholeWordIsAdvisedTowardNaturalClasses()
    {
        var project = NewProject("word");

        var batch = ProposalCommands.ComposeBatch(new(project.Path, Version, "word", """
            {"intents":[
              {"verb":"compose-author-phoneme","intent":{"name":"a","representations":["zz"]},"as":"ph"},
              {"verb":"compose-author-environment","intent":{"name":"only zz","left":[{"boundary":"word"},{"phoneme":"@ph:phonemes"}],"right":[{"boundary":"word"}]}},
              {"verb":"compose-author-environment","intent":{"name":"after zz","left":[{"phoneme":"@ph:phonemes"}],"right":[]}}
            ]}
            """));

        Assert.True(batch.Succeeded, batch.Refusal?.Message);
        var advisory = Assert.Single(batch.Value!.Advisories!);
        Assert.Equal(ComposeAdvisoryCodes.WholeWordEnvironment, advisory.Code);
        Assert.Equal([1], advisory.Intents);
        Assert.Contains("only zz", advisory.Message);
    }

    [Fact]
    public void AClassLeftWithOneStemIsAdvisedEvenWhenTheLastIntentAssignsIt()
    {
        var project = NewProject("lone", withClass: true);

        var batch = ProposalCommands.ComposeBatch(new(project.Path, Version, "lone", $$$"""
            {"intents":[{"verb":"compose-edit-stem-inflection-class",
              "intent":{"target":"{{{project.Stems[0]}}}","expectedInflectionClass":null,"inflectionClass":"{{{project.InflectionClass}}}"}}]}
            """));

        Assert.True(batch.Succeeded, batch.Refusal?.Message);
        var advisory = Assert.Single(batch.Value!.Advisories!);
        Assert.Equal(ComposeAdvisoryCodes.OneStemInflectionClass, advisory.Code);
        Assert.Contains("Lone class", advisory.Message);
    }

    [Fact]
    public void AClassWithTwoStemsCarriesNoAdvisory()
    {
        var project = NewProject("pair", withClass: true);

        var batch = ProposalCommands.ComposeBatch(new(project.Path, Version, "pair", $$$"""
            {"intents":[{"verb":"compose-edit-stem-inflection-class",
              "forEach":{"targets":["{{{project.Stems[0]}}}","{{{project.Stems[1]}}}"]},
              "intent":{"target":"{target}","expectedInflectionClass":null,"inflectionClass":"{{{project.InflectionClass}}}"}}]}
            """));

        Assert.True(batch.Succeeded, batch.Refusal?.Message);
        Assert.Null(batch.Value!.Advisories);
    }
}
