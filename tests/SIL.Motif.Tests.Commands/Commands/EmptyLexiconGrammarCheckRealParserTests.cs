using System;
using System.IO;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class EmptyLexiconGrammarCheckRealParserTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        "SIL.Motif.EmptyLexiconGrammarCheckRealParserTests", Guid.NewGuid().ToString("N"));

    [SIL.Motif.Tests.Parser.RealParserFact]
    public void MeasuredBlankLexiconHasAnAdvisoryStoredWithItsParserFindings()
    {
        Directory.CreateDirectory(_root);
        using (var cache = NewLangProjFixture.CreateCache(_root))
        {
            Assert.Equal(0, cache.ServiceLocator.GetInstance<ILexEntryRepository>().Count);
            new FwDataProjectLoader().Save(cache);
        }
        var fwDataPath = NewLangProjFixture.FwDataPath(_root);
        var managedRoot = Path.Combine(_root, "motif");
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), managedRoot);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);

        var measured = GrammarCheckQuery.Query(new GrammarCheckRequest(fwDataPath));
        Assert.True(measured.Succeeded, measured.Refusal?.Message);
        var stored = StoredGrammarCheckQuery.Query(new GrammarCheckRequest(fwDataPath));
        Assert.True(stored.Succeeded, stored.Refusal?.Message);
        Assert.Equal(JsonSerializer.Serialize(measured.Value!.Findings),
            JsonSerializer.Serialize(stored.Value!.Check!.Findings));
        Assert.Equal(JsonSerializer.Serialize(measured.Value.Advisories),
            JsonSerializer.Serialize(stored.Value.Check.Advisories));

        using var measuredJson = JsonDocument.Parse(JsonSerializer.Serialize(measured.Value));
        Assert.Equal(0, measuredJson.RootElement.GetProperty("CheckedLexemeCount").GetInt32());
        Assert.Equal(0, measuredJson.RootElement.GetProperty("CheckedRuleCount").GetInt32());
        var measuredAdvisory = Assert.Single(measuredJson.RootElement.GetProperty("Advisories").EnumerateArray());
        Assert.Equal("grammar.empty-lexicon", measuredAdvisory.GetProperty("Code").GetString());
        Assert.Equal(0, measuredAdvisory.GetProperty("LexemeCount").GetInt32());
        Assert.Equal(0, measuredAdvisory.GetProperty("StemLexemeCount").GetInt32());
        Assert.Contains("0 lexical entries and 0 stem lexemes",
            measuredAdvisory.GetProperty("Message").GetString(), StringComparison.Ordinal);

        var warnings = SIL.Motif.Commands.Catalog.WarningsCommand.Warnings(new WarningsRequest(fwDataPath));
        Assert.True(warnings.Succeeded, warnings.Refusal?.Message);
        using var warningsJson = JsonDocument.Parse(JsonSerializer.Serialize(warnings.Value));
        Assert.Equal("grammar.empty-lexicon",
            Assert.Single(warningsJson.RootElement.GetProperty("Advisories").EnumerateArray())
                .GetProperty("Code").GetString());
        Assert.Equal(0, warningsJson.RootElement.GetProperty("CheckedLexemeCount").GetInt32());
        Assert.Equal(0, warningsJson.RootElement.GetProperty("CheckedRuleCount").GetInt32());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
