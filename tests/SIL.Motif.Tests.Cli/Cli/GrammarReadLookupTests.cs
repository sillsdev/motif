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
        Assert.Equal(4, root.GetProperty("nextOffset").GetInt32());

        using var last = Read(project.FwDataPath, "lexical_entry", "--limit", "4", "--offset", "4");
        Assert.Equal(4, last.RootElement.GetProperty("returned").GetInt32());
        Assert.False(last.RootElement.TryGetProperty("nextOffset", out _));

        using var defaults = Read(project.FwDataPath, "lexical_entry");
        Assert.Equal(25, defaults.RootElement.GetProperty("limit").GetInt32());
        Assert.False(defaults.RootElement.TryGetProperty("nextOffset", out _));
    }

    [Fact]
    public void TextOutputNamesTheNextOffsetAndTheTotalWhenTheLastPageIsNotShown()
    {
        using var project = CaptureProject();

        var text = CliInProcess.Run(_root, null, developerCommands: true,
            "grammar", "read", "--project", project.FwDataPath, "--kind", "lexical_entry", "--limit", "3");
        Assert.True(text.ExitCode == 0, text.Error);
        Assert.Contains("(3 of 8;", text.Output, StringComparison.Ordinal);
        Assert.Contains("Next page: --offset 3 --limit 3", text.Output, StringComparison.Ordinal);

        var every = CliInProcess.Run(_root, null, developerCommands: true,
            "grammar", "read", "--project", project.FwDataPath, "--kind", "lexical_entry", "--limit", "3", "--offset", "6");
        Assert.DoesNotContain("Next page", every.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void PageNumberIsAnotherWayToStateTheOffsetAndConflictsWithIt()
    {
        using var project = CaptureProject();

        using var second = Read(project.FwDataPath, "lexical_entry", "--limit", "3", "--page", "2");
        Assert.Equal(3, second.RootElement.GetProperty("offset").GetInt32());
        Assert.Equal(6, second.RootElement.GetProperty("nextOffset").GetInt32());

        var both = CliInProcess.Run(_root, null, developerCommands: true,
            "grammar", "read", "--project", project.FwDataPath, "--kind", "lexical_entry", "--page", "2", "--offset", "3");
        Assert.NotEqual(0, both.ExitCode);
        var zero = CliInProcess.Run(_root, null, developerCommands: true,
            "grammar", "read", "--project", project.FwDataPath, "--kind", "lexical_entry", "--page", "0");
        Assert.NotEqual(0, zero.ExitCode);
    }
}
