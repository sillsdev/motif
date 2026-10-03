using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Texts;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Worker;

/// <summary>
/// Pins the stored Text words of a Baseline: the runner's refresh replaces them with exactly what a fresh load of
/// the refreshed Baseline builds, a read touches only the requested Texts and the wordforms they use, and a row
/// that is damaged or belongs to another Baseline is refused rather than served.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class BaselineRefreshTextWordsProjectionTests : IDisposable
{
    private readonly PristineProjectFixture _pristine;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.BaselineRefreshTextWordsProjectionTests",
        Guid.NewGuid().ToString("N"));

    public BaselineRefreshTextWordsProjectionTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task RefreshStoresWhatAFreshLoadOfTheRefreshedBaselineBuilds()
    {
        using var cache = _pristine.NewScratch();
        var firstText = SeededProject.SeedText(cache, _pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var (project, database) = Open(cache);
        using var owned = database;
        var repository = new BaselineRepository(database);
        var refresh = new BaselineRefresh(repository, Path.Combine(_root, "managed"));
        var projectKey = ProjectWorkspaceKey.Compute(project);

        var firstToken = await refresh.RefreshAsync(cache, project, CancellationToken.None);

        Assert.Equal(1, Count(database, "BaselineTextWords", projectKey));
        var secondText = SeededProject.SeedText(cache, _pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var secondToken = await refresh.RefreshAsync(cache, project, CancellationToken.None);
        var current = repository.GetCurrentTextWords(projectKey, [firstText.TextId, secondText.TextId])!;
        using var fresh = new FwDataProjectLoader().LoadScratchCache(current.Baseline.FwDataPath);
        var expected = TextWordsProjectionBuilder.Build(fresh, CancellationToken.None);
        var stored = repository.GetCurrentTextWords(
            projectKey, expected.Texts.Select(text => text.TextId).ToArray())!;

        Assert.NotEqual(firstToken.BundleDigest, secondToken.BundleDigest);
        Assert.Equal(secondToken.BundleDigest, current.Baseline.Token.BundleDigest);
        var rtl = Assert.Single(current.WritingSystems, ws => ws.Id == SeededProject.RightToLeftTag);
        Assert.Equal(SeededProject.MissingFont, rtl.FontFamily);
        Assert.Equal("Ar", rtl.Abbreviation);
        Assert.Equal(SeededProject.RightToLeftSizePoints, rtl.StyleSizes["Normal"]);
        Assert.Equal(new SIL.Motif.Contract.Responses.WritingSystemStyleFont(SeededProject.MissingFont,
            SeededProject.FontFeatures), rtl.StyleFonts["Normal"]);
        Assert.Equal(new HashSet<Guid> { firstText.TextId, secondText.TextId },
            current.Projection.Texts.Select(text => text.TextId).ToHashSet());
        Assert.Equal(Serialize(expected), Serialize(stored.Projection));
        Assert.Equal(2, Count(database, "BaselineTextWords", projectKey));
        Assert.Equal(expected.Wordforms.Count, Count(database, "BaselineTextWordforms", projectKey));
        Assert.Equal(0, CountOtherDigest(database, "BaselineTextWords", projectKey, secondToken.BundleDigest));
        Assert.Equal(0, CountOtherDigest(database, "BaselineTextWordforms", projectKey, secondToken.BundleDigest));
    }

    [Fact]
    public void ProjectionCarriesParagraphSegmentParseAndOccurrenceIdentity()
    {
        using var cache = _pristine.NewScratch();
        var text = SeededProject.SeedText(cache, _pristine.Seed);
        SIL.LCModel.Infrastructure.NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var paragraph = cache.ServiceLocator.GetInstance<IStTxtParaRepository>().GetObject(text.FirstParagraphId);
            var rules = SIL.LCModel.Core.Text.TsStringUtils.MakePropsBldr();
            rules.SetStrPropValue((int)SIL.LCModel.Core.KernelInterfaces.FwTextPropType.ktptNamedStyle, "Heading 1");
            paragraph.StyleRules = rules.GetTextProps();
        });

        var projected = TextWordsProjectionBuilder.Build(cache, CancellationToken.None);

        var line = Assert.Single(Assert.Single(projected.Texts, item => item.TextId == text.TextId)
            .Lines, item => item.SegmentId == text.FirstSegmentId);
        Assert.Equal(text.FirstParagraphId, line.ParagraphId);
        Assert.Equal("Heading 1", line.SentenceStyle);
        Assert.False(line.ParseIsCurrent);
        var word = Assert.Single(line.Tokens, token => token.WordformId == text.AnalysedWordformId);
        Assert.Equal(0, word.OccurrenceIndex);
        Assert.Equal(text.ApprovedAnalysisId, word.AnalysisId);
        var punctuation = Assert.Single(line.Tokens, token => token.WordformId is null);
        Assert.Equal(1, punctuation.OccurrenceIndex);
        Assert.Null(punctuation.AnalysisId);

        var wordform = Assert.Single(projected.Wordforms, item => item.WordformId == text.AnalysedWordformId);
        var analysis = Assert.Single(wordform.Analyses);
        Assert.Equal(text.ApprovedAnalysisId, analysis.AnalysisId);
        Assert.Equal("approved", analysis.Opinion);
        var identity = analysis.Identity ?? throw new InvalidOperationException(
            "The projected analysis has no approved morphology identity.");
        Assert.Equal(2, identity.Morphs.Count);
        Assert.Equal(_pristine.Seed.FirstLexemeFormId.ToString("D"), identity.Morphs[0].Form);
        Assert.False(string.IsNullOrWhiteSpace(analysis.Morphs[0].Entry));
        Assert.Equal(SeededProject.FirstGloss, analysis.Morphs[0].Gloss);
        Assert.False(string.IsNullOrWhiteSpace(analysis.Morphs[0].Category));
    }

    [Fact]
    public async Task AReadReturnsOnlyTheRequestedTextsAndTheWordformsTheyUse()
    {
        var (projectKey, repository, database, first, second) = await RefreshTwoTexts();
        using var owned = database;

        var read = repository.GetCurrentTextWords(projectKey, [second, Guid.NewGuid(), second])!;

        var text = Assert.Single(read.Projection.Texts);
        Assert.Equal(second, text.TextId);
        var used = text.Lines.SelectMany(line => line.Tokens).Select(token => token.WordformId)
            .OfType<Guid>().ToHashSet();
        Assert.Equal(used, read.Projection.Wordforms.Select(wordform => wordform.WordformId).ToHashSet());
        Assert.Empty(repository.GetCurrentTextWords(projectKey, [])!.Projection.Texts);
        Assert.DoesNotContain(read.Projection.Texts, candidate => candidate.TextId == first);
    }

    [Theory]
    [InlineData("UPDATE BaselineTextWords SET BundleDigest = 'sha256:other' WHERE ProjectKey = $project;")]
    [InlineData("UPDATE BaselineTextWordforms SET BundleDigest = 'sha256:other' WHERE ProjectKey = $project;")]
    [InlineData("UPDATE BaselineTextWords SET TextJson = '{}' WHERE ProjectKey = $project;")]
    [InlineData("UPDATE BaselineTextWords SET TextJson = 'not json' WHERE ProjectKey = $project;")]
    [InlineData("UPDATE BaselineTextWordforms SET WordformJson = '{}' WHERE ProjectKey = $project;")]
    [InlineData("UPDATE BaselineTextWordforms SET WordformJson = json_set(WordformJson, " +
        "'$.Analyses[0].Identity.SourceAnalysisId', 'wrong') WHERE ProjectKey = $project;")]
    [InlineData("DELETE FROM BaselineTextWordforms WHERE ProjectKey = $project;")]
    [InlineData("UPDATE BaselineTextWords SET TextJson = json_set(TextJson, '$.Analyses', json('[]')) " +
        "WHERE ProjectKey = $project;")]
    public async Task ADamagedOrForeignRowIsRefusedRatherThanServed(string damage)
    {
        var (projectKey, repository, database, first, _) = await RefreshTwoTexts();
        using var owned = database;
        Execute(database, damage, projectKey);

        Assert.Throws<InvalidDataException>(() => repository.GetCurrentTextWords(projectKey, [first]));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async Task<(string ProjectKey, BaselineRepository Repository, MotifDatabase Database, Guid First,
        Guid Second)> RefreshTwoTexts()
    {
        using var cache = _pristine.NewScratch();
        var first = SeededProject.SeedText(cache, _pristine.Seed);
        var second = SeededProject.SeedText(cache, _pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var (project, database) = Open(cache);
        var repository = new BaselineRepository(database);
        await new BaselineRefresh(repository, Path.Combine(_root, "managed"))
            .RefreshAsync(cache, project, CancellationToken.None);
        return (ProjectWorkspaceKey.Compute(project), repository, database, first.TextId, second.TextId);
    }

    private static (ProjectLocator Project, MotifDatabase Database) Open(LcmCache cache)
    {
        var projectPath = cache.ProjectId.Path;
        var project = new ProjectLocator(projectPath, Path.GetFileNameWithoutExtension(projectPath));
        return (project, new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0)).OpenOwned(project));
    }

    private static string Serialize(TextWordsProjection value) =>
        JsonSerializer.Serialize(value, MotifJson.CreateOptions());

    private static long Count(MotifDatabase database, string table, string projectKey) =>
        Scalar(database, $"SELECT COUNT(*) FROM {table} WHERE ProjectKey = $project;", projectKey);

    private static long CountOtherDigest(MotifDatabase database, string table, string projectKey, string digest) =>
        Scalar(database, $"SELECT COUNT(*) FROM {table} WHERE ProjectKey = $project AND BundleDigest <> '{digest}';",
            projectKey);

    private static long Scalar(MotifDatabase database, string sql, string projectKey)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$project", projectKey);
        return (long)command.ExecuteScalar()!;
    }

    private static void Execute(MotifDatabase database, string sql, string projectKey)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$project", projectKey);
        Assert.True(command.ExecuteNonQuery() > 0);
    }
}
