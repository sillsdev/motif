using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>An agent fetches exactly the entry it means by exact form or morph type, and pages any large read.</summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class GrammarReadLookupTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-grammar-lookup-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private WalkthroughProject CaptureProject()
    {
        var project = new WalkthroughProject(pristine, _root, configure: (cache, _, _) =>
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var services = cache.ServiceLocator;
                void Stem(string form)
                {
                    var entry = services.GetInstance<ILexEntryFactory>().Create();
                    var lexeme = services.GetInstance<IMoStemAllomorphFactory>().Create();
                    entry.LexemeFormOA = lexeme;
                    lexeme.MorphTypeRA = services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem);
                    lexeme.Form.set_String(cache.DefaultVernWs, form);
                }

                void Prefix(string form)
                {
                    var entry = services.GetInstance<ILexEntryFactory>().Create();
                    var lexeme = services.GetInstance<IMoAffixAllomorphFactory>().Create();
                    entry.LexemeFormOA = lexeme;
                    lexeme.MorphTypeRA = services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphPrefix);
                    lexeme.Form.set_String(cache.DefaultVernWs, form);
                }

                foreach (var stem in new[] { "ka", "kaka", "kata", "zi" }) Stem(stem);
                Prefix("zi");
                Prefix("li");
            }));
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project.FwDataPath), _root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        return project;
    }

    private JsonDocument Read(string projectPath, string kind, params string[] more)
    {
        var arguments = new List<string> { "grammar", "read", "--project", projectPath, "--kind", kind };
        arguments.AddRange(more);
        arguments.Add("--json");
        var result = CliInProcess.Run(_root, null, developerCommands: true, arguments.ToArray());
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        return JsonDocument.Parse(result.Output);
    }

    private static string[] Headwords(JsonDocument document) => document.RootElement.GetProperty("items").EnumerateArray()
        .Select(item => item.GetProperty("headword").GetString()!).Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void ExactReturnsOnlyEntriesWhoseFormIsTheTextNotEveryEntryContainingIt()
    {
        using var project = CaptureProject();

        using var substring = Read(project.FwDataPath, "lexical_entry", "--query", "ka");
        Assert.Equal(3, substring.RootElement.GetProperty("total").GetInt32());

        using var exact = Read(project.FwDataPath, "lexical_entry", "--exact", "KA");
        Assert.Equal(1, exact.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(["ka"], Headwords(exact));
    }

    [Fact]
    public void ABareExactFormFindsTheAffixWhoseHeadwordCarriesTheMarkerAndAMarkedFormDoesNot()
    {
        using var project = CaptureProject();

        using var bare = Read(project.FwDataPath, "lexical_entry", "--exact", "zi");
        Assert.Equal(["zi", "zi-"], Headwords(bare));

        using var marked = Read(project.FwDataPath, "lexical_entry", "--exact", "zi-");
        Assert.Equal(["zi-"], Headwords(marked));
    }

    [Fact]
    public void ABareExactFormSaysItAlsoMatchedTheMarkedAffixAndAMarkedFormSaysNothing()
    {
        using var project = CaptureProject();

        using var bare = Read(project.FwDataPath, "lexical_entry", "--exact", "zi");
        var note = bare.RootElement.GetProperty("exactNote").GetString()!;
        Assert.Contains("zi-", note, StringComparison.Ordinal);
        Assert.Contains("marker", note, StringComparison.Ordinal);

        using var stemOnly = Read(project.FwDataPath, "lexical_entry", "--exact", "ka");
        Assert.False(stemOnly.RootElement.TryGetProperty("exactNote", out var none) && none.ValueKind == JsonValueKind.String);

        using var narrowed = Read(project.FwDataPath, "lexical_entry", "--exact", "zi", "--morph-type", "prefix");
        Assert.False(narrowed.RootElement.TryGetProperty("exactNote", out var narrowedNote) && narrowedNote.ValueKind == JsonValueKind.String);

        using var marked = Read(project.FwDataPath, "lexical_entry", "--exact", "zi-");
        Assert.False(marked.RootElement.TryGetProperty("exactNote", out var markedNote) && markedNote.ValueKind == JsonValueKind.String);

        var text = CliInProcess.Run(_root, null, developerCommands: true,
            ["grammar", "read", "--project", project.FwDataPath, "--kind", "lexical_entry", "--exact", "zi"]);
        Assert.Equal(0, text.ExitCode);
        Assert.Contains("also matched 1 marked headword(s): zi-", text.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void AtBaselineReadsTheBaselineAndAnythingElseThatIsNotAProposalIdOrHasNoTrialIsRefused()
    {
        using var project = CaptureProject();

        using var baseline = Read(project.FwDataPath, "lexical_entry", "--exact", "ka", "--at", "baseline");
        Assert.Equal("baseline", baseline.RootElement.GetProperty("at").GetString());

        var notAnId = CliInProcess.Run(_root, null, developerCommands: true,
            ["grammar", "read", "--project", project.FwDataPath, "--kind", "lexical_entry", "--at", "last-week", "--json"]);
        Assert.NotEqual(0, notAnId.ExitCode);
        Assert.Contains("grammar-read.invalid-request", notAnId.Output + notAnId.Error, StringComparison.Ordinal);

        var noTrial = CliInProcess.Run(_root, null, developerCommands: true,
            ["grammar", "read", "--project", project.FwDataPath, "--kind", "lexical_entry", "--at", "AaHnUob1K5UlJwJZtEhh_w", "--json"]);
        Assert.NotEqual(0, noTrial.ExitCode);
        Assert.Contains("trial.none", noTrial.Output + noTrial.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void MorphTypeNarrowsEntriesByNameOrCanonicalIdAndAcceptsSeveralNames()
    {
        using var project = CaptureProject();

        using var prefixes = Read(project.FwDataPath, "lexical_entry", "--morph-type", "Prefix");
        Assert.Equal(["li-", "zi-"], Headwords(prefixes));
        Assert.All(prefixes.RootElement.GetProperty("items").EnumerateArray(),
            item => Assert.Equal("prefix", item.GetProperty("morphType").GetString()));

        using var one = Read(project.FwDataPath, "lexical_entry", "--exact", "zi", "--morph-type", "prefix");
        Assert.Equal(["zi-"], Headwords(one));

        var prefixId = prefixes.RootElement.GetProperty("items")[0].GetProperty("morphTypeId").GetString()!;
        using var byId = Read(project.FwDataPath, "lexical_entry", "--morph-type", prefixId);
        Assert.Equal(["li-", "zi-"], Headwords(byId));

        using var several = Read(project.FwDataPath, "lexical_entry", "--morph-type", "prefix,suffix", "--exact", "li");
        Assert.Equal(["li-"], Headwords(several));
    }

    [Fact]
    public void ExactAppliesToNamesOfOtherKindsToo()
    {
        using var project = CaptureProject();

        using var exact = Read(project.FwDataPath, "part_of_speech", "--exact", "seedednoun");
        Assert.Equal(1, exact.RootElement.GetProperty("total").GetInt32());
        using var partial = Read(project.FwDataPath, "part_of_speech", "--exact", "seeded");
        Assert.Equal(0, partial.RootElement.GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("--exact", "ka", "--query", "ka")]
    [InlineData("--exact", "ka", "--id", "AaHnUob1K5UlJwJZtEhh_w")]
    [InlineData("--morph-type", "prefix", "--id", "AaHnUob1K5UlJwJZtEhh_w")]
    public void CombiningAnExactFormWithAQueryOrIdIsRefused(params string[] flags)
    {
        using var project = CaptureProject();
        var result = CliInProcess.Run(_root, null, developerCommands: true,
            ["grammar", "read", "--project", project.FwDataPath, "--kind", "lexical_entry", .. flags, "--json"]);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("grammar-read.invalid-request", result.Output + result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void MorphTypeIsRefusedForKindsThatHaveNone()
    {
        using var project = CaptureProject();
        var result = CliInProcess.Run(_root, null, developerCommands: true,
            "grammar", "read", "--project", project.FwDataPath, "--kind", "phoneme", "--morph-type", "prefix", "--json");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("grammar-read.invalid-request", result.Output + result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryPageStatesItsTotalAndWhereTheNextPageStartsUntilTheLast()
    {
        using var project = CaptureProject();

        using var first = Read(project.FwDataPath, "lexical_entry", "--limit", "4");
        var root = first.RootElement;
        Assert.Equal(8, root.GetProperty("total").GetInt32());
        Assert.Equal(4, root.GetProperty("returned").GetInt32());
        Assert.Equal(SIL.Motif.Contract.Responses.ResultPaging.Cursor(4), root.GetProperty("nextCursor").GetString());

        using var last = Read(project.FwDataPath, "lexical_entry", "--limit", "4", "--cursor", SIL.Motif.Contract.Responses.ResultPaging.Cursor(4));
        Assert.Equal(4, last.RootElement.GetProperty("returned").GetInt32());
        Assert.False(last.RootElement.TryGetProperty("nextCursor", out _));

        using var defaults = Read(project.FwDataPath, "lexical_entry");
        Assert.Equal(25, defaults.RootElement.GetProperty("limit").GetInt32());
        Assert.False(defaults.RootElement.TryGetProperty("nextCursor", out _));
    }

    [Fact]
    public void TextOutputNamesTheNextCursorAndTheTotalWhenTheLastPageIsNotShown()
    {
        using var project = CaptureProject();

        var text = CliInProcess.Run(_root, null, developerCommands: true,
            "grammar", "read", "--project", project.FwDataPath, "--kind", "lexical_entry", "--limit", "3");
        Assert.True(text.ExitCode == 0, text.Error);
        Assert.Contains("(3 of 8;", text.Output, StringComparison.Ordinal);
        Assert.Contains("Next page: --cursor " + SIL.Motif.Contract.Responses.ResultPaging.Cursor(3) + " --limit 3", text.Output, StringComparison.Ordinal);

        var every = CliInProcess.Run(_root, null, developerCommands: true,
            "grammar", "read", "--project", project.FwDataPath, "--kind", "lexical_entry", "--limit", "3", "--cursor", SIL.Motif.Contract.Responses.ResultPaging.Cursor(6));
        Assert.DoesNotContain("Next page", every.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void ACursorTheApiDidNotIssueIsRefused()
    {
        using var project = CaptureProject();

        var bad = CliInProcess.Run(_root, null, developerCommands: true,
            "grammar", "read", "--project", project.FwDataPath, "--kind", "lexical_entry", "--cursor", "3");
        Assert.NotEqual(0, bad.ExitCode);
        var page = CliInProcess.Run(_root, null, developerCommands: true,
            "grammar", "read", "--project", project.FwDataPath, "--kind", "lexical_entry", "--page", "2");
        Assert.NotEqual(0, page.ExitCode);
    }

    [Fact]
    public void PhonologicalRulesAreReadWithTheIntentAnEditGuardNeeds()
    {
        Guid ruleId = Guid.Empty;
        using var project = new WalkthroughProject(pristine, _root, configure: (cache, _, _) =>
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var services = cache.ServiceLocator;
                var data = cache.LangProject.PhonologicalDataOA;
                var set = services.GetInstance<IPhPhonemeSetFactory>().Create();
                data.PhonemeSetsOS.Add(set);
                var rule = services.GetInstance<IPhRegularRuleFactory>().Create();
                data.PhonRulesOS.Add(rule);
                rule.Name.set_String(cache.DefaultAnalWs, "Nasal assimilation");
                ruleId = rule.Guid;
                foreach (var letter in new[] { "n", "m" })
                {
                    var phoneme = services.GetInstance<IPhPhonemeFactory>().Create();
                    set.PhonemesOC.Add(phoneme);
                    phoneme.Name.set_String(cache.DefaultVernWs, letter);
                    var context = services.GetInstance<IPhSimpleContextSegFactory>().Create();
                    if (letter == "n") rule.StrucDescOS.Add(context);
                    else rule.RightHandSidesOS.Single().StrucChangeOS.Add(context);
                    context.FeatureStructureRA = phoneme;
                }
            }));
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project.FwDataPath), _root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);

        var id = SIL.Motif.Contract.Ids.CanonicalId.FromGuid(ruleId).Value;
        using var byId = Read(project.FwDataPath, "phonological_rule", "--id", id);
        var item = Assert.Single(byId.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("phonological_rule", item.GetProperty("kind").GetString());
        Assert.Equal(id, item.GetProperty("id").GetString());
        Assert.Equal("Nasal assimilation", item.GetProperty("name").GetString());
        Assert.Equal(1, item.GetProperty("rightHandSideCount").GetInt32());
        Assert.Single(item.GetProperty("intent").GetProperty("input").EnumerateArray());
        Assert.Single(item.GetProperty("intent").GetProperty("output").EnumerateArray());

        using var plural = Read(project.FwDataPath, "phonological_rules", "--exact", "nasal assimilation");
        Assert.Equal(1, plural.RootElement.GetProperty("total").GetInt32());
        Assert.Equal("phonological_rule", plural.RootElement.GetProperty("kind").GetString());
    }

    [Theory]
    [InlineData("prefx")]
    [InlineData("prefixes")]
    [InlineData("prefix,suffx")]
    public void AnUnknownMorphTypeIsRefusedWithTheValidNamesNotAnEmptyResult(string morphType)
    {
        using var project = CaptureProject();
        var result = CliInProcess.Run(_root, null, developerCommands: true,
            "grammar", "read", "--project", project.FwDataPath, "--kind", "lexical_entry", "--morph-type", morphType, "--json");
        Assert.NotEqual(0, result.ExitCode);
        var output = result.Output + result.Error;
        Assert.Contains("grammar-read.invalid-request", output, StringComparison.Ordinal);
        Assert.Contains("Unknown morph type", output, StringComparison.Ordinal);
        Assert.Contains("prefix", output, StringComparison.Ordinal);
        Assert.Contains("suffix", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePagesConcatenatedAreTheUnpagedListInTheSameOrder()
    {
        using var project = CaptureProject();

        using var all = Read(project.FwDataPath, "lexical_entry", "--limit", "100");
        var expected = all.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()!).ToArray();
        Assert.True(expected.Length >= 6);

        var paged = new List<string>();
        string? cursor = SIL.Motif.Contract.Responses.ResultPaging.Cursor(0);
        while (cursor is { } next)
        {
            using var page = Read(project.FwDataPath, "lexical_entry", "--limit", "2", "--cursor", next);
            paged.AddRange(page.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()!));
            cursor = page.RootElement.TryGetProperty("nextCursor", out var token) ? token.GetString() : null;
        }

        Assert.Equal(expected, paged);
    }

    [Fact]
    public void PhonologicalRulesComeBackInApplicationOrderWithTheirPositionNotSortedByName()
    {
        using var project = new WalkthroughProject(pristine, _root, configure: (cache, _, _) =>
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                foreach (var name in new[] { "Zeta", "Alpha", "Mid" })
                {
                    var rule = cache.ServiceLocator.GetInstance<IPhRegularRuleFactory>().Create();
                    cache.LangProject.PhonologicalDataOA.PhonRulesOS.Add(rule);
                    rule.Name.set_String(cache.DefaultAnalWs, name);
                }
            }));
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project.FwDataPath), _root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);

        using var rules = Read(project.FwDataPath, "phonological_rule", "--limit", "2");
        var first = rules.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(["Zeta", "Alpha"], first.Select(item => item.GetProperty("name").GetString()!).ToArray());
        Assert.Equal([1, 2], first.Select(item => item.GetProperty("position").GetInt32()).ToArray());

        using var rest = Read(project.FwDataPath, "phonological_rule", "--limit", "2", "--cursor", SIL.Motif.Contract.Responses.ResultPaging.Cursor(2));
        var last = Assert.Single(rest.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("Mid", last.GetProperty("name").GetString());
        Assert.Equal(3, last.GetProperty("position").GetInt32());
    }
}
