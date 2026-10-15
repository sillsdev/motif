using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class AuthorAffixProcessConformanceTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly IPartOfSpeech _category;

    public AuthorAffixProcessConformanceTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        IPartOfSpeech category = null!;
        Setup(() =>
        {
            category = _cache.ServiceLocator.GetInstance<IPartOfSpeechFactory>().Create();
            _cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(category);
            category.Name.set_String(_cache.DefaultAnalWs, "Synthetic category");
        });
        _category = category;
        Phone("k");
        Phone("a");
    }

    public void Dispose() => _cache.Dispose();

    [RealParserFact]
    public async Task AuthoredProcessReachesPinnedImporterWithRepeatedMultigraphAndLiteralHyphenMappings()
    {
        var multigraph = Phone("ts");
        var hyphen = Phone("-");
        var ordinary = Proposal(AuthorLexicalEntryComposer.Build(_cache, new AuthorLexicalEntryIntent("ka",
            _cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultVernWs), CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem),
            "STEM", _cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultAnalWs), new(LexicalEntryMsaKind.Stem, Id(_category)))));
        ProposalApplier.Apply(_cache, ordinary, ScratchDryRun.Of(_cache, ordinary).Anchor, "affix-core-parser-tests");
        var intent = Intent() with
        {
            MorphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphPrefix),
            Input = [new("stem", Remaining: true)],
            Output = [new(Copy: "stem"), new(Insert: [Id(multigraph), Id(multigraph), Id(hyphen)]), new(Copy: "stem")],
        };
        var proposal = Proposal(AuthorAffixProcessComposer.Build(_cache, intent));
        ProposalApplier.Apply(_cache, proposal, ScratchDryRun.Of(_cache, proposal).Anchor, "affix-core-parser-tests");
        new FwDataProjectLoader().Save(_cache);
        var importedPath = Path.Combine(Path.GetDirectoryName(_cache.ProjectId.Path)!, "authored-affix-process.json");
        using var invoker = new PanGlossInvoker();
        var outcome = await invoker.RunAsync(new PanGlossRequest.Import(_cache.ProjectId.Path, importedPath),
            "authored-affix-process", CancellationToken.None);
        Assert.True(outcome is PanGlossOutcome.Completed, outcome.Message);
        using var imported = JsonDocument.Parse(await File.ReadAllTextAsync(importedPath));
        var entryId = Created(proposal.Operations, LexDbEntriesOperationKinds.Create).ToGuid().ToString();
        var entry = Assert.Single(imported.RootElement.GetProperty("lexicon").GetProperty("entries").EnumerateArray(),
            item => item.GetProperty("guid").GetString() == entryId);
        var allomorph = Assert.Single(entry.GetProperty("allomorphs").EnumerateArray());
        Assert.Equal(Created(proposal.Operations, LexEntryLexemeFormOperationKinds.CreateLexemeForm).ToGuid().ToString(),
            allomorph.GetProperty("guid").GetString());
        Assert.False(allomorph.GetProperty("isAbstract").GetBoolean());
        var process = allomorph.GetProperty("process");
        Assert.Equal("variable", Assert.Single(process.GetProperty("input").EnumerateArray()).GetProperty("kind").GetString());
        var output = process.GetProperty("output").EnumerateArray().ToArray();
        Assert.Equal(new[] { "copyFromInput", "insertSegments", "insertSegments", "insertSegments", "copyFromInput" },
            output.Select(step => step.GetProperty("kind").GetString()));
        Assert.Equal(new[] { "ts", "ts", "-" }, output.Skip(1).Take(3).Select(step => step.GetProperty("text").GetString()));
        Assert.Equal(1, output[0].GetProperty("part").GetInt32());
        Assert.Equal(1, output[^1].GetProperty("part").GetInt32());
        Assert.Equal(Created(proposal.Operations, LexEntryMorphoSyntaxAnalysesOperationKinds.Create).ToGuid().ToString(),
            Assert.Single(entry.GetProperty("senses").EnumerateArray()).GetProperty("msa").GetString());
    }

    private AuthorAffixProcessIntent Intent() => new(CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphPrefix), "COPY",
        _cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultAnalWs), new(LexicalEntryMsaKind.Unclassified, Id(_category)),
        [new("stem", Remaining: true)], [new(Copy: "stem"), new(Copy: "stem")]);
    private void Setup(Action action) => UndoableUnitOfWorkHelper.Do("setup", "setup", _cache.ServiceLocator.GetInstance<IActionHandler>(), action);
    private static CanonicalId Id(ICmObject value) => CanonicalId.FromGuid(value.Guid);
    private static CanonicalId Created(IReadOnlyList<OperationEnvelope> operations, string kind) =>
        Assert.Single(operations, operation => operation.Kind == kind).EntityId!.Value;
    private static Proposal Proposal(IReadOnlyList<OperationEnvelope> operations) => new(
        new Dictionary<string, string> { ["lexical"] = "1.0", ["grammar"] = "1.0", ["lists"] = "1.0" }, CanonicalId.Mint(), null, operations);
    private IPhPhoneme Phone(string representation)
    {
        IPhPhoneme phone = null!;
        Setup(() =>
        {
            var set = _cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.FirstOrDefault();
            if (set is null)
            {
                set = _cache.ServiceLocator.GetInstance<IPhPhonemeSetFactory>().Create();
                _cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.Add(set);
            }
            phone = _cache.ServiceLocator.GetInstance<IPhPhonemeFactory>().Create();
            set.PhonemesOC.Add(phone);
            phone.CodesOS.Clear();
            var code = _cache.ServiceLocator.GetInstance<IPhCodeFactory>().Create();
            phone.CodesOS.Add(code);
            code.Representation.set_String(_cache.DefaultVernWs, representation);
        });
        return phone;
    }
}
