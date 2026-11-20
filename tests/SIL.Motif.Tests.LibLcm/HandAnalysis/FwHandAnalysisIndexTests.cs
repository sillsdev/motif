using System.Security.Cryptography;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.HandAnalysis;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.HandAnalysis;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group1)]
public sealed class FwHandAnalysisIndexTests : IDisposable, IHandAnalysisProjectSources
{
    private static readonly Guid ProjectId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private readonly LcmCache _cache;
    private readonly SeededProject _seed;
    private readonly string _livePath;
    private readonly string _baselinePath;
    private readonly BaselineSemanticIdentity _baseline = new("project", "sha256:" + new string('1', 64), "projection");
    private readonly ITestOutputHelper _output;

    public FwHandAnalysisIndexTests(PristineProjectFixture pristine, ITestOutputHelper output)
    {
        _output = output;
        _cache = pristine.NewScratch();
        _seed = pristine.Seed;
        _livePath = _cache.ProjectId.Path;
        _baselinePath = pristine.CopyProjectFile();
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    string? IHandAnalysisProjectSources.LiveFwData(Guid projectId) => projectId == ProjectId ? _livePath : null;

    string? IHandAnalysisProjectSources.BaselineFwData(Guid projectId, BaselineSemanticIdentity identity) =>
        projectId == ProjectId && identity == _baseline ? _baselinePath : null;

    private ILexEntry Entry(Guid id) => _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(id);

    private IWfiWordform Wordform(string form)
    {
        IWfiWordform wordform = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => wordform = _cache.ServiceLocator
            .GetInstance<IWfiWordformFactory>().Create(TsStringUtils.MakeString(form, _cache.DefaultVernWs)));
        return wordform;
    }

    private IWfiAnalysis Analysis(IWfiWordform wordform, Guid? entryId, Opinions opinion, ICmAgent? agent = null)
    {
        IWfiAnalysis analysis = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            analysis = _cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
            wordform.AnalysesOC.Add(analysis);
            if (entryId is { } id)
            {
                var entry = Entry(id);
                var bundle = _cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
                analysis.MorphBundlesOS.Add(bundle);
                bundle.MorphRA = entry.LexemeFormOA;
                bundle.MsaRA = entry.MorphoSyntaxAnalysesOC.First();
            }

            (agent ?? _cache.LangProject.DefaultUserAgent).SetEvaluation(analysis, opinion);
        });
        return analysis;
    }

    private void Save() => new FwDataProjectLoader().Save(_cache);

    private FwHandAnalysisIndex Index() => new(this);

    private async Task<IHandAnalysisSnapshot> Pin(FwHandAnalysisIndex index) =>
        (await index.PinAsync(ProjectId, null, CancellationToken.None)).Value!;

    [Fact]
    public void ApprovedDisapprovedAndParserOnlyAnalysesAreToldApart()
    {
        var word = Wordform("zzApproved");
        var approved = Analysis(word, _seed.FirstEntryId, Opinions.approves);
        Analysis(word, _seed.SecondEntryId, Opinions.disapproves);
        Analysis(Wordform("zzParser"), _seed.SecondEntryId, Opinions.approves, _cache.LangProject.DefaultParserAgent);
        Analysis(Wordform("zzNoOpinion"), _seed.FirstEntryId, Opinions.noopinion);

        var set = ApprovedReadingSet.Read(_cache);

        var only = Assert.Single(set.Analyses);
        Assert.Equal(approved.Guid, only.AnalysisId);
        Assert.NotNull(only.Signature);
        Assert.Equal(0, set.SkippedOtherHumanAgentApprovals);
    }

    [Fact]
    public void AnApprovalWithoutMorphBundlesIsCountedButNeverGivenAnIdentity()
    {
        Analysis(Wordform("zzNoBundles"), null, Opinions.approves);

        var fact = Assert.Single(ApprovedReadingSet.Read(_cache).Analyses);

        Assert.Null(fact.Signature);
        Assert.Equal(ApprovedUnsignedCause.NoMorphBundles, fact.UnsignedCause);
    }

    [Fact]
    public void ApprovalByANonDefaultHumanAgentIsSkippedAndCounted()
    {
        ICmAgent other = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            other = _cache.ServiceLocator.GetInstance<ICmAgentFactory>().Create();
            _cache.LangProject.AnalyzingAgentsOC.Add(other);
            other.Human = true;
        });
        Analysis(Wordform("zzOtherHuman"), _seed.FirstEntryId, Opinions.approves, other);

        var set = ApprovedReadingSet.Read(_cache);

        Assert.Empty(set.Analyses);
        Assert.Equal(1, set.SkippedOtherHumanAgentApprovals);
    }

    [Fact]
    public void AParserReadingWithTheSameMorphsKeepsTheApprovalAndOtherMorphsLoseIt()
    {
        Analysis(Wordform("zzKept"), _seed.FirstEntryId, Opinions.approves);
        var set = ApprovedReadingSet.Read(_cache);
        var reader = new ReadingSignatureReader(_cache);
        ParseAnalysis Parse(Guid entryId)
        {
            var entry = Entry(entryId);
            return new([new ParseMorph(entry.LexemeFormOA.Guid.ToString(), entry.MorphoSyntaxAnalysesOC.First().Guid.ToString(), null, null)]);
        }

        Assert.Single(set.KeptBy(reader.Read(Parse(_seed.FirstEntryId))));
        Assert.Empty(set.KeptBy(reader.Read(Parse(_seed.SecondEntryId))));
    }

    [Fact]
    public void ADifferentlyResolvedSenseOfTheSameMorphemeLosesTheApprovalAndAnAmbiguousOneKeepsIt()
    {
        var entry = Entry(_seed.FirstEntryId);
        var first = entry.SensesOS[0];
        ILexSense second = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            second = _cache.ServiceLocator.GetInstance<ILexSenseFactory>().Create();
            entry.SensesOS.Add(second);
            second.MorphoSyntaxAnalysisRA = first.MorphoSyntaxAnalysisRA;
        });
        var approved = Analysis(Wordform("zzSenseApproved"), _seed.FirstEntryId, Opinions.approves);
        var other = Analysis(Wordform("zzSenseOther"), _seed.FirstEntryId, Opinions.noopinion);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            approved.MorphBundlesOS[0].SenseRA = first;
            other.MorphBundlesOS[0].SenseRA = second;
        });
        var set = ApprovedReadingSet.Read(_cache);
        var reader = new ReadingSignatureReader(_cache);
        var ambiguous = reader.Read(new ParseAnalysis([new ParseMorph(entry.LexemeFormOA.Guid.ToString(),
            first.MorphoSyntaxAnalysisRA.Guid.ToString(), null, null)]));

        Assert.Equal(ReadingSenseStatus.Resolved, Assert.Single(set.Analyses).Signature!.Morphemes[0].SenseStatus);
        Assert.Equal(ReadingSenseStatus.Ambiguous, ambiguous.Morphemes[0].SenseStatus);
        Assert.Single(set.KeptBy(ambiguous));
        Assert.Empty(set.KeptBy(ReadingSignatureReader.Read(other)!));
        Assert.Single(set.KeptBy(ReadingSignatureReader.Read(approved)!));
    }

    [Fact]
    public void TheIdentityMapResolvesAFrozenIdentityAndRefusesGuessesAndStrangers()
    {
        var entry = Entry(_seed.FirstEntryId);
        var map = new ReadingIdentityMap(new ReadingSignatureReader(_cache));
        var frozen = $"{entry.LexemeFormOA.Guid}/{entry.MorphoSyntaxAnalysesOC.First().Guid}//";

        var mapped = map.ToSignature(frozen);

        Assert.NotNull(mapped.Value);
        Assert.Equal(RefusalCodes.HandAnalysisIdentityMismatch, map.ToSignature(frozen + "guess").Refusal?.Code);
        Assert.Equal(RefusalCodes.HandAnalysisIdentityMismatch, map.ToSignature(Guid.NewGuid() + "/" + Guid.NewGuid() + "//").Refusal?.Code);
        Assert.Equal(RefusalCodes.HandAnalysisIdentityMismatch, map.ToSignature("nonsense").Refusal?.Code);
        var other = new ReadingSignatureReader(_cache).Read(new ParseAnalysis([new ParseMorph(
            Entry(_seed.SecondEntryId).LexemeFormOA.Guid.ToString(), Entry(_seed.SecondEntryId).MorphoSyntaxAnalysesOC.First().Guid.ToString(), null, null)]));
        Assert.Equal(RefusalCodes.HandAnalysisIdentityMismatch, map.Match(frozen, other).Refusal?.Code);
        Assert.NotNull(map.Match(frozen, mapped.Value!).Value);
    }

    [Fact]
    public async Task APinServesApprovedReadingsPagedAndRefusesTheOtherQueries()
    {
        var word = Wordform("zzPinned");
        Analysis(word, _seed.FirstEntryId, Opinions.approves);
        Analysis(word, _seed.SecondEntryId, Opinions.approves);
        Save();
        await using var pin = await Pin(Index());
        var query = new WordQuery.Alternatives(new WordIdentity.Native(word.Guid), WordOrder.Encounter);

        var first = (await pin.WordsAsync(query, new PageRequest(1), CancellationToken.None)).Value!;
        var second = (await pin.WordsAsync(query, new PageRequest(1, first.NextCursor), CancellationToken.None)).Value!;

        Assert.Equal(2, first.Total);
        Assert.NotNull(first.NextCursor);
        Assert.Null(second.NextCursor);
        Assert.All(first.Items.Concat(second.Items), item =>
            Assert.NotNull(Assert.IsType<WordFact.Reading>(item).Signature));
        Assert.Equal(RefusalCodes.HandAnalysisInvalidCursor,
            (await pin.WordsAsync(new WordQuery.Alternatives(new WordIdentity.Native(Guid.NewGuid()), WordOrder.Encounter),
                new PageRequest(1, first.NextCursor), CancellationToken.None)).Refusal?.Code);
        Assert.Equal(RefusalCodes.HandAnalysisUnsupportedOperation,
            (await pin.WordsAsync(new WordQuery.Identity(new WordIdentity.Native(word.Guid)), new(), CancellationToken.None)).Refusal?.Code);
        Assert.Equal(RefusalCodes.HandAnalysisUnsupportedOperation,
            (await pin.LexiconAsync(null!, new(), CancellationToken.None)).Refusal?.Code);
        await pin.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => ((IApprovedReadingSource)pin).Approved);
    }

    [Fact]
    public async Task PinningDoesNotChangeTheProjectFile()
    {
        Analysis(Wordform("zzReadOnly"), _seed.FirstEntryId, Opinions.approves);
        Save();
        var before = SHA256.HashData(File.ReadAllBytes(_livePath));

        await using var pin = await Pin(Index());

        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(_livePath)));
        Assert.Single(((IApprovedReadingSource)pin).Approved.Analyses);
    }

    [Fact]
    public async Task AtABaselineReadsTheBaselineCopyNotTheLiveProject()
    {
        Analysis(Wordform("zzLiveOnly"), _seed.FirstEntryId, Opinions.approves);
        Save();
        var index = Index();

        await using var live = await Pin(index);
        await using var past = (await index.PinAtBaselineAsync(ProjectId, _baseline, CancellationToken.None)).Value!;
        var again = (await index.PinAtBaselineAsync(ProjectId, _baseline, CancellationToken.None)).Value!;

        Assert.Single(((IApprovedReadingSource)live).Approved.Analyses);
        Assert.Empty(((IApprovedReadingSource)past).Approved.Analyses);
        Assert.Equal(_baseline.SemanticSnapshotDigest, past.Stamp.HomePoint);
        Assert.Equal(past.Stamp, again.Stamp);
        Assert.NotEqual(live.Stamp.SnapshotId, past.Stamp.SnapshotId);
        Assert.Equal(past.Stamp.SnapshotId,
            (await index.PinAsync(ProjectId, past.Stamp.SnapshotId, CancellationToken.None)).Value!.Stamp.SnapshotId);
    }

    [Fact]
    public async Task ALivePinIsReusedUntilTheProjectFileChanges()
    {
        Analysis(Wordform("zzReuse"), _seed.FirstEntryId, Opinions.approves);
        Save();
        var index = Index();

        await using var first = await Pin(index);
        await using var second = await Pin(index);
        Assert.Equal(1, index.Loads);
        Assert.Equal(first.Stamp, second.Stamp);

        Analysis(Wordform("zzReuseChanged"), _seed.SecondEntryId, Opinions.approves);
        Save();
        await using var third = await Pin(index);

        Assert.Equal(2, index.Loads);
        Assert.NotEqual(first.Stamp.SnapshotId, third.Stamp.SnapshotId);
        Assert.Equal(2, ((IApprovedReadingSource)third).Approved.Analyses.Length);
    }

    [Fact]
    public async Task ASameLengthSameModificationTimeChangeInvalidatesALivePin()
    {
        var index = Index();
        await using var first = await Pin(index);
        var bytes = File.ReadAllBytes(_livePath);
        var time = File.GetLastWriteTimeUtc(_livePath);
        var at = bytes.Length - 2;
        while (!(bytes[at] == (byte)' ' && bytes[at - 1] == (byte)'"' && char.IsLetter((char)bytes[at + 1]))) at--;
        Assert.True(at > bytes.Length - 65536);
        bytes[at] = (byte)'\n';
        File.WriteAllBytes(_livePath, bytes);
        File.SetLastWriteTimeUtc(_livePath, time);

        var stale = await index.PinAsync(ProjectId, null, CancellationToken.None);

        Assert.Equal(2, index.Loads);
        Assert.NotNull(stale.Value);
    }

    [Fact]
    public async Task AProjectFileThatCannotBeReadRefusesAsUnreadable()
    {
        if (OperatingSystem.IsWindows()) return;
        var original = File.GetUnixFileMode(_livePath);
        File.SetUnixFileMode(_livePath, UnixFileMode.None);
        try
        {
            var outcome = await Index().PinAsync(ProjectId, null, CancellationToken.None);

            Assert.Equal(RefusalCodes.ProjectUnreadable, outcome.Refusal?.Code);
        }
        finally
        {
            File.SetUnixFileMode(_livePath, original);
        }
    }

    [Fact]
    public async Task AMissingBaselineProjectOrSnapshotRefusesWithRegisteredCodes()
    {
        var index = Index();
        var gone = new BaselineSemanticIdentity("project", "sha256:" + new string('2', 64), "projection");

        Assert.Equal(RefusalCodes.HandAnalysisSnapshotExpired,
            (await index.PinAtBaselineAsync(ProjectId, gone, CancellationToken.None)).Refusal?.Code);
        Assert.Equal(RefusalCodes.HandAnalysisSnapshotExpired,
            (await index.PinAsync(ProjectId, "hand-analysis/none", CancellationToken.None)).Refusal?.Code);
        var unknown = await index.PinAsync(Guid.NewGuid(), null, CancellationToken.None);
        Assert.Equal(RefusalCodes.HandAnalysisIndexUnavailable, unknown.Refusal?.Code);
        Assert.Equal(HandAnalysisRefusalFacts.Unavailable, unknown.Refusal?.Facts[HandAnalysisRefusalFacts.Cause]);
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await index.PinAsync(ProjectId, null, new CancellationToken(true)));
    }

    [Fact]
    public void RealProjectCountsOnlyWhenAPathIsSupplied()
    {
        var path = Environment.GetEnvironmentVariable("MOTIF_REAL_HAND_ANALYSIS_FWDATA");
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        var started = DateTime.UtcNow;
        using var copy = SIL.Motif.Host.Baselines.BaselineReadCache.Open(path);
        var set = ApprovedReadingSet.Read(copy.Cache);
        var line = ($"wordforms={set.WordformCount} approved={set.Analyses.Length} signed={set.SignedCount} " +
            $"unsigned={set.Analyses.Length - set.SignedCount} otherHuman={set.SkippedOtherHumanAgentApprovals} " +
            $"seconds={(DateTime.UtcNow - started).TotalSeconds:F1}");
        _output.WriteLine(line);
        Console.Out.WriteLine(line);
    }
}
