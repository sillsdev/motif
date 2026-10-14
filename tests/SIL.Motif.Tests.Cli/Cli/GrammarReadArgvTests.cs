using System.Security.Cryptography;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class GrammarReadArgvTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-grammar-read-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ReadsSixAuthoredKindsByCanonicalIdAndReadsTheCapturedBaselineBack()
    {
        Guid phonemeId = Guid.Empty;
        Guid homonymousPhonemeId = Guid.Empty;
        Guid naturalClassId = Guid.Empty;
        Guid environmentId = Guid.Empty;
        Guid childCategoryId = Guid.Empty;
        Guid featureId = Guid.Empty;
        Guid valueId = Guid.Empty;
        using var project = new WalkthroughProject(pristine, _root, configure: (cache, seed, _) =>
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var services = cache.ServiceLocator;
                var phonology = cache.LangProject.PhonologicalDataOA;
                if (phonology.PhonemeSetsOS.Count == 0)
                    phonology.PhonemeSetsOS.Add(services.GetInstance<IPhPhonemeSetFactory>().Create());
                var phoneme = services.GetInstance<IPhPhonemeFactory>().Create();
                phonology.PhonemeSetsOS[0].PhonemesOC.Add(phoneme);
                phoneme.Name.set_String(cache.DefaultVernWs, "voiceless uvular stop");
                var code = services.GetInstance<IPhCodeFactory>().Create();
                phoneme.CodesOS.Add(code);
                code.Representation.set_String(cache.DefaultVernWs, "q");
                phonemeId = phoneme.Guid;

                var homonymousPhoneme = services.GetInstance<IPhPhonemeFactory>().Create();
                phonology.PhonemeSetsOS[0].PhonemesOC.Add(homonymousPhoneme);
                homonymousPhoneme.Name.set_String(cache.DefaultVernWs, "voiceless uvular stop");
                var homonymousCode = services.GetInstance<IPhCodeFactory>().Create();
                homonymousPhoneme.CodesOS.Add(homonymousCode);
                homonymousCode.Representation.set_String(cache.DefaultVernWs, "qʰ");
                homonymousPhonemeId = homonymousPhoneme.Guid;

                var naturalClass = services.GetInstance<IPhNCSegmentsFactory>().Create();
                phonology.NaturalClassesOS.Add(naturalClass);
                naturalClass.Name.set_String(cache.DefaultAnalWs, "Vowels");
                naturalClass.Abbreviation.set_String(cache.DefaultAnalWs, "V");
                naturalClass.SegmentsRC.Add(phoneme);
                naturalClassId = naturalClass.Guid;

                var environment = services.GetInstance<IPhEnvironmentFactory>().Create();
                phonology.EnvironmentsOS.Add(environment);
                environment.Name.set_String(cache.DefaultAnalWs, "Before vowels");
                environment.StringRepresentation = TsStringUtils.MakeString("/ _ [V]", cache.DefaultVernWs);
                environmentId = environment.Guid;
                var entry = services.GetInstance<ILexEntryRepository>().GetObject(seed.FirstEntryId);
                entry.SensesOS[0].Definition.set_String(cache.DefaultAnalWs, "first sense definition");
                ((IMoStemAllomorph)entry.LexemeFormOA).PhoneEnvRC.Add(environment);
                var homographicEntry = services.GetInstance<ILexEntryRepository>().GetObject(seed.SecondEntryId);
                homographicEntry.LexemeFormOA.Form.set_String(cache.DefaultVernWs, SeededProject.FirstForm);
                entry.HomographNumber = 0;
                homographicEntry.HomographNumber = 0;

                var parentCategory = services.GetInstance<IPartOfSpeechRepository>().GetObject(seed.PartOfSpeechId);
                var childCategory = services.GetInstance<IPartOfSpeechFactory>().Create();
                parentCategory.SubPossibilitiesOS.Add(childCategory);
                childCategory.Name.set_String(cache.DefaultAnalWs, "Count noun");
                childCategory.Abbreviation.set_String(cache.DefaultAnalWs, "n.");
                childCategoryId = childCategory.Guid;

                var feature = services.GetInstance<IFsClosedFeatureFactory>().Create();
                cache.LangProject.MsFeatureSystemOA.FeaturesOC.Add(feature);
                feature.Name.set_String(cache.DefaultAnalWs, "Number");
                feature.Abbreviation.set_String(cache.DefaultAnalWs, "Num");
                var value = services.GetInstance<IFsSymFeatValFactory>().Create();
                feature.ValuesOC.Add(value);
                value.Name.set_String(cache.DefaultAnalWs, "plural");
                featureId = feature.Guid;
                valueId = value.Guid;
            });
        });

        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project.FwDataPath), _root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);

        using (var phoneme = Read(project.FwDataPath, "phoneme", CanonicalId.FromGuid(phonemeId).Value))
        {
            var item = OnlyItem(phoneme, "phoneme", phonemeId);
            Assert.Equal("voiceless uvular stop", item.GetProperty("name").GetString());
            var representation = Assert.Single(item.GetProperty("representations").EnumerateArray());
            Assert.Equal("q", representation.GetProperty("text").GetString());
            Assert.Equal(NewLangProjFixture.VernacularTag, representation.GetProperty("writingSystem").GetString());
        }

        using (var naturalClass = Read(project.FwDataPath, "natural_class", CanonicalId.FromGuid(naturalClassId).Value))
        {
            var item = OnlyItem(naturalClass, "natural_class", naturalClassId);
            Assert.Equal("Vowels", item.GetProperty("name").GetString());
            Assert.Equal("V", item.GetProperty("abbreviation").GetString());
            var member = Assert.Single(item.GetProperty("members").EnumerateArray());
            Assert.Equal(CanonicalId.FromGuid(phonemeId).Value, member.GetProperty("id").GetString());
            Assert.Equal("voiceless uvular stop", member.GetProperty("name").GetString());
        }

        using (var environment = Read(project.FwDataPath, "environment", CanonicalId.FromGuid(environmentId).Value))
        {
            var item = OnlyItem(environment, "environment", environmentId);
            Assert.Equal("Before vowels", item.GetProperty("name").GetString());
            Assert.Equal("/ _ [V]", item.GetProperty("notation").GetString());
        }

        using (var category = Read(project.FwDataPath, "part_of_speech", CanonicalId.FromGuid(childCategoryId).Value))
        {
            var item = OnlyItem(category, "part_of_speech", childCategoryId);
            Assert.Equal("Count noun", item.GetProperty("name").GetString());
            Assert.Equal("n.", item.GetProperty("abbreviation").GetString());
            Assert.Equal(CanonicalId.FromGuid(project.Seed.PartOfSpeechId).Value,
                item.GetProperty("parent").GetProperty("id").GetString());
            Assert.Equal("SeededNoun", item.GetProperty("parent").GetProperty("name").GetString());
        }

        using (var feature = Read(project.FwDataPath, "inflection_feature", CanonicalId.FromGuid(featureId).Value))
        {
            var item = OnlyItem(feature, "inflection_feature", featureId);
            Assert.Equal("Number", item.GetProperty("name").GetString());
            Assert.Equal("Num", item.GetProperty("abbreviation").GetString());
            var value = Assert.Single(item.GetProperty("values").EnumerateArray());
            Assert.Equal(CanonicalId.FromGuid(valueId).Value, value.GetProperty("id").GetString());
            Assert.Equal("plural", value.GetProperty("name").GetString());
        }

        using (var lexicalEntry = Read(project.FwDataPath, "lexical_entry",
                   CanonicalId.FromGuid(project.Seed.FirstEntryId).Value))
        {
            var item = OnlyItem(lexicalEntry, "lexical_entry", project.Seed.FirstEntryId);
            Assert.Equal(SeededProject.FirstForm, item.GetProperty("headword").GetString());
            var form = item.GetProperty("lexemeForm");
            Assert.Equal(CanonicalId.FromGuid(project.Seed.FirstLexemeFormId).Value, form.GetProperty("id").GetString());
            Assert.Equal(SeededProject.FirstForm, form.GetProperty("form").GetString());
            Assert.Equal(NewLangProjFixture.VernacularTag, form.GetProperty("writingSystem").GetString());
            var formEnvironment = Assert.Single(form.GetProperty("environments").EnumerateArray());
            Assert.Equal(CanonicalId.FromGuid(environmentId).Value, formEnvironment.GetProperty("id").GetString());
            Assert.Equal("/ _ [V]", formEnvironment.GetProperty("notation").GetString());
            var sense = Assert.Single(item.GetProperty("senses").EnumerateArray());
            Assert.Equal(CanonicalId.FromGuid(project.Seed.FirstSenseId).Value, sense.GetProperty("id").GetString());
            Assert.Equal(SeededProject.FirstGloss, sense.GetProperty("gloss").GetString());
            Assert.Equal("first sense definition", sense.GetProperty("definition").GetString());
            Assert.Equal(NewLangProjFixture.AnalysisTag, sense.GetProperty("definitionWritingSystem").GetString());
            Assert.Equal(CanonicalId.FromGuid(project.Seed.PartOfSpeechId).Value,
                sense.GetProperty("grammaticalInfo").GetProperty("partOfSpeech").GetProperty("id").GetString());
        }

        var humanLexicalEntry = CliInProcess.Run(_root, null, developerCommands: true,
            "grammar", "read", "--project", project.FwDataPath, "--kind", "lexical_entry", "--id",
            CanonicalId.FromGuid(project.Seed.FirstEntryId).Value);
        Assert.True(humanLexicalEntry.ExitCode == 0, humanLexicalEntry.Error);
        Assert.Contains("Definition: first sense definition (en)", humanLexicalEntry.Output, StringComparison.Ordinal);

        using (var query = Read(project.FwDataPath, "lexical_entry", query: SeededProject.FirstGloss))
        {
            Assert.Equal(CanonicalId.FromGuid(project.Seed.FirstEntryId).Value,
                Assert.Single(query.RootElement.GetProperty("items").EnumerateArray()).GetProperty("id").GetString());
        }

        var duplicatePhonemeIds = new[] { phonemeId, homonymousPhonemeId }
            .Select(id => CanonicalId.FromGuid(id).Value).Order(StringComparer.Ordinal).ToArray();
        using (var firstPhonemePage = Read(project.FwDataPath, "phoneme", query: "voiceless uvular stop", limit: 1))
        using (var secondPhonemePage = Read(project.FwDataPath, "phoneme", query: "voiceless uvular stop", offset: 1, limit: 1))
        {
            Assert.Equal(2, firstPhonemePage.RootElement.GetProperty("total").GetInt32());
            Assert.Equal(1, firstPhonemePage.RootElement.GetProperty("returned").GetInt32());
            Assert.Equal(1, secondPhonemePage.RootElement.GetProperty("returned").GetInt32());
            Assert.Equal(duplicatePhonemeIds[0], Assert.Single(firstPhonemePage.RootElement.GetProperty("items").EnumerateArray())
                .GetProperty("id").GetString());
            Assert.Equal(duplicatePhonemeIds[1], Assert.Single(secondPhonemePage.RootElement.GetProperty("items").EnumerateArray())
                .GetProperty("id").GetString());
        }

        var duplicateEntryIds = new[] { project.Seed.FirstEntryId, project.Seed.SecondEntryId }
            .Select(id => CanonicalId.FromGuid(id).Value).Order(StringComparer.Ordinal).ToArray();
        using (var firstEntryPage = Read(project.FwDataPath, "lexical_entry", limit: 1))
        using (var secondEntryPage = Read(project.FwDataPath, "lexical_entry", offset: 1, limit: 1))
        {
            Assert.Equal(2, firstEntryPage.RootElement.GetProperty("total").GetInt32());
            Assert.Equal(1, firstEntryPage.RootElement.GetProperty("returned").GetInt32());
            Assert.Equal(1, secondEntryPage.RootElement.GetProperty("returned").GetInt32());
            Assert.Equal(duplicateEntryIds[0], Assert.Single(firstEntryPage.RootElement.GetProperty("items").EnumerateArray())
                .GetProperty("id").GetString());
            Assert.Equal(duplicateEntryIds[1], Assert.Single(secondEntryPage.RootElement.GetProperty("items").EnumerateArray())
                .GetProperty("id").GetString());
        }

        var liveBeforeRead = Hash(project.FwDataPath);
        using (var live = new FwDataProjectLoader().LoadScratchCache(project.FwDataPath))
        {
            var phoneme = (IPhPhoneme)live.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(phonemeId);
            NonUndoableUnitOfWorkHelper.Do(live.ActionHandlerAccessor,
                () => phoneme.Name.set_String(live.DefaultVernWs, "live-only changed name"));
            new FwDataProjectLoader().Save(live);
        }
        File.SetLastWriteTimeUtc(project.FwDataPath, captured.Value!.SourceLastWriteUtc.AddMinutes(1).UtcDateTime);
        var liveAfterSave = Hash(project.FwDataPath);
        using (var baselineRead = Read(project.FwDataPath, "phoneme", CanonicalId.FromGuid(phonemeId).Value))
        {
            var item = OnlyItem(baselineRead, "phoneme", phonemeId, expectStale: true);
            Assert.Equal("voiceless uvular stop", item.GetProperty("name").GetString());
            Assert.True(baselineRead.RootElement.GetProperty("isStale").GetBoolean());
            Assert.True(baselineRead.RootElement.GetProperty("projectLastWriteUtc").GetDateTimeOffset() >
                baselineRead.RootElement.GetProperty("sourceLastWriteUtc").GetDateTimeOffset());
        }
        Assert.NotEqual(liveBeforeRead, liveAfterSave);
        Assert.Equal(liveAfterSave, Hash(project.FwDataPath));

        var invalidKind = GrammarReadQuery.Query(new GrammarReadRequest(project.FwDataPath, (GrammarReadKind)999));
        Assert.False(invalidKind.Succeeded);
        Assert.Equal("grammar-read.invalid-request", invalidKind.Refusal!.Code);
        AssertInvalidRequest(new GrammarReadRequest(project.FwDataPath, GrammarReadKind.phoneme, Limit: 0));
        AssertInvalidRequest(new GrammarReadRequest(project.FwDataPath, GrammarReadKind.phoneme, Limit: 101));
        AssertInvalidRequest(new GrammarReadRequest(project.FwDataPath, GrammarReadKind.phoneme, Offset: -1));
        AssertInvalidRequest(new GrammarReadRequest(project.FwDataPath, GrammarReadKind.phoneme, Offset: 100_001));
        AssertInvalidRequest(new GrammarReadRequest(project.FwDataPath, GrammarReadKind.phoneme,
            Id: CanonicalId.FromGuid(phonemeId).Value, Query: "q"));
        AssertInvalidRequest(new GrammarReadRequest(project.FwDataPath, GrammarReadKind.phoneme,
            Id: "not-canonical"));

        var unknownCliKind = CliInProcess.Run(_root, null, developerCommands: true,
            "grammar", "read", "--project", project.FwDataPath, "--kind", "unknown-kind", "--json");
        Assert.NotEqual(0, unknownCliKind.ExitCode);
        Assert.Contains("grammar read", unknownCliKind.Output + unknownCliKind.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CanonicallyEquivalentQueriesFindNamesAndRichForms()
    {
        const string decomposed = "cafe\u0301";
        using var project = new WalkthroughProject(pristine, _root, configure: (cache, seed, _) =>
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var category = cache.ServiceLocator.GetInstance<IPartOfSpeechRepository>().GetObject(seed.PartOfSpeechId);
                category.Name.set_String(cache.DefaultAnalWs, decomposed);
                var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(seed.FirstEntryId);
                entry.LexemeFormOA.Form.set_String(cache.DefaultVernWs, decomposed);
            });
        });
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project.FwDataPath), _root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);

        foreach (var (kind, id) in new[]
                 {
                     ("part_of_speech", project.Seed.PartOfSpeechId),
                     ("lexical_entry", project.Seed.FirstEntryId),
                 })
        {
            using var composed = Read(project.FwDataPath, kind, query: "caf\u00e9");
            using var normalized = Read(project.FwDataPath, kind, query: decomposed);
            OnlyItem(composed, kind, id);
            OnlyItem(normalized, kind, id);
            Assert.Equal(normalized.RootElement.GetProperty("items").GetRawText(),
                composed.RootElement.GetProperty("items").GetRawText());
        }
    }

    [Fact]
    public void EmptyProjectReturnsAnEmptyPageForAllSixKinds()
    {
        Directory.CreateDirectory(_root);
        var cache = NewLangProjFixture.CreateCache(_root);
        string projectPath;
        try
        {
            new FwDataProjectLoader().Save(cache);
            projectPath = cache.ProjectId.Path;
        }
        finally
        {
            if (!cache.IsDisposed) cache.Dispose();
        }

        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), _root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        foreach (var kind in Kinds)
        {
            using var response = Read(projectPath, kind);
            Assert.Equal(kind, response.RootElement.GetProperty("kind").GetString());
            Assert.Equal(0, response.RootElement.GetProperty("total").GetInt32());
            Assert.Empty(response.RootElement.GetProperty("items").EnumerateArray());
        }
    }

    [Fact]
    public void ReadingWithoutACapturedBaselineIsRefused()
    {
        using var project = new WalkthroughProject(pristine, _root);

        var outcome = GrammarReadQuery.Query(new GrammarReadRequest(project.FwDataPath, GrammarReadKind.lexical_entry));

        Assert.False(outcome.Succeeded);
        Assert.Equal("grammar-read.baseline-missing", outcome.Refusal!.Code);
    }

    private static readonly string[] Kinds =
    [
        "phoneme", "natural_class", "environment", "part_of_speech", "inflection_feature", "lexical_entry",
    ];

    private JsonDocument Read(string projectPath, string kind, string? id = null, string? query = null,
        int? offset = null, int? limit = null)
    {
        var arguments = new List<string> { "grammar", "read", "--project", projectPath, "--kind", kind };
        if (id is not null) arguments.AddRange(["--id", id]);
        if (query is not null) arguments.AddRange(["--query", query]);
        if (offset is not null) arguments.AddRange(["--offset", offset.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        if (limit is not null) arguments.AddRange(["--limit", limit.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        arguments.Add("--json");
        var result = CliInProcess.Run(_root, null, developerCommands: true, arguments.ToArray());
        Assert.True(result.ExitCode == 0, result.Output);
        return JsonDocument.Parse(result.Output);
    }

    private static JsonElement OnlyItem(JsonDocument document, string kind, Guid expectedId, bool expectStale = false)
    {
        Assert.Equal(kind, document.RootElement.GetProperty("kind").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("total").GetInt32());
        var item = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(kind, item.GetProperty("kind").GetString());
        Assert.Equal(CanonicalId.FromGuid(expectedId).Value, item.GetProperty("id").GetString());
        Assert.NotEqual(JsonValueKind.Null, document.RootElement.GetProperty("baseline").ValueKind);
        Assert.Equal(expectStale, document.RootElement.GetProperty("isStale").GetBoolean());
        return item;
    }

    private static void AssertInvalidRequest(GrammarReadRequest request)
    {
        var outcome = GrammarReadQuery.Query(request);
        Assert.False(outcome.Succeeded);
        Assert.Equal("grammar-read.invalid-request", outcome.Refusal!.Code);
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
