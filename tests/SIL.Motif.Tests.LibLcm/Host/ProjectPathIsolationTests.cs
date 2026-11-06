using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Lexicon;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Host;

[Collection(LcmCacheParallelCollections.Group0)]
public sealed class ProjectPathIsolationTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavingASameNamedCandidateCannotChangeTheOpenSource(bool projectSharing)
    {
        var (livePath, root) = CreateProject(projectSharing);
        var loader = new FwDataProjectLoader();
        using var live = loader.LoadCache(livePath);
        loader.Save(live);
        var before = File.ReadAllBytes(livePath);
        var candidateRoot = Path.Combine(root, "candidate");
        Guid candidateId;
        string candidatePath;
        using (var candidate = new ScratchCacheFactory(loader).CreateFromFileCopy(livePath, candidateRoot))
        {
            candidatePath = candidate.ProjectId.Path;
            output.WriteLine($"Live: {live.ProjectId.Path} ({live.ServiceLocator.DataSetup.GetType().Name})");
            output.WriteLine($"Candidate: {candidatePath} ({candidate.ServiceLocator.DataSetup.GetType().Name})");
            Assert.Equal(live.ProjectId.Name, candidate.ProjectId.Name);
            Assert.NotEqual(livePath, candidatePath);
            candidateId = Guid.Empty;
            NonUndoableUnitOfWorkHelper.Do(candidate.ActionHandlerAccessor, () =>
                candidateId = candidate.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString("privatecandidate", candidate.DefaultVernWs)).Guid);
            loader.Save(candidate);
            loader.Save(live);
            Assert.Equal(before, File.ReadAllBytes(livePath));
        }
        Assert.Contains(candidateId.ToString(), File.ReadAllText(candidatePath));
        Assert.DoesNotContain(candidateId.ToString(), File.ReadAllText(livePath));
    }

    [Fact]
    public void AuthorTextPlanningAndSavingItsTrialCandidateLeaveBothSourcesUnchanged()
    {
        var (livePath, root) = CreateProject(projectSharing: true);
        var loader = new FwDataProjectLoader();
        using var live = loader.LoadCache(livePath);
        var liveBefore = File.ReadAllBytes(livePath);
        using var privateProject = new ScratchCacheFactory(loader).CreateFromFileCopy(livePath,
            Path.Combine(root, "private"));
        var privateBefore = File.ReadAllBytes(privateProject.ProjectId.Path);
        var intent = AuthorTextIntentParser.Parse(JsonSerializer.SerializeToElement(new
        {
            title = "Private text", ws = privateProject.WritingSystemFactory.GetStrFromWs(privateProject.DefaultVernWs),
            paragraphs = new[] { new { segments = new[] { new { text = "alpha beta.", notes = new[] { "note" } } } } },
        }));
        var operations = AuthorTextDraftBuilder.Build(privateProject, intent);
        var proposal = new Proposal(new Dictionary<string, string> { ["texts"] = "1.0", ["analysis"] = "1.0" },
            CanonicalId.Mint(), null, operations);
        using var scratch = DryRunScratch.Adopt(new ScratchCacheFactory(loader).CreateFromFileCopy(
            privateProject.ProjectId.Path, Path.Combine(root, "trial")), "private Trial Candidate");
        ProposalDryRunner.Run(scratch, proposal);
        var candidate = scratch.PeekCache();
        var candidatePath = candidate.ProjectId.Path;
        loader.Save(candidate, candidatePath);
        loader.Save(privateProject, privateProject.ProjectId.Path);
        loader.Save(live, livePath);

        Assert.Equal(liveBefore, File.ReadAllBytes(livePath));
        Assert.Equal(privateBefore, File.ReadAllBytes(privateProject.ProjectId.Path));
        using var reopened = loader.LoadScratchCache(candidatePath);
        var text = reopened.ServiceLocator.GetInstance<ITextRepository>().GetObject(operations.Last().EntityId!.Value.ToGuid());
        Assert.Equal("alpha beta.", ((IStTxtPara)Assert.Single(text.ContentsOA.ParagraphsOS)).Contents.Text);
        Assert.Equal(2, reopened.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances().Count());
        Assert.True(LcmSettings.IsProjectSharingEnabled(Path.GetDirectoryName(livePath)));
        Assert.True(LcmSettings.IsProjectSharingEnabled(Path.GetDirectoryName(candidatePath)));
    }

    [Fact]
    public void SaveRefusesADifferentRequestedPathBeforeCommitting()
    {
        using var live = pristine.NewScratch();
        using var candidate = pristine.NewScratch();
        var liveBefore = File.ReadAllBytes(live.ProjectId.Path);
        var candidateBefore = File.ReadAllBytes(candidate.ProjectId.Path);
        AddWordform(candidate);

        var failure = Assert.Throws<InvalidOperationException>(() =>
            new FwDataProjectLoader().Save(candidate, live.ProjectId.Path));

        Assert.Contains(live.ProjectId.Path, failure.Message);
        Assert.Contains(candidate.ProjectId.Path, failure.Message);
        Assert.Equal(liveBefore, File.ReadAllBytes(live.ProjectId.Path));
        Assert.Equal(candidateBefore, File.ReadAllBytes(candidate.ProjectId.Path));
    }

    [Fact]
    public void ChangingTheCachePathCannotRedirectEitherSaveEntryPoint()
    {
        using var cache = pristine.NewScratch();
        var originalPath = cache.ProjectId.Path;
        var otherPath = pristine.CopyProjectFile();
        var originalBefore = File.ReadAllBytes(originalPath);
        var otherBefore = File.ReadAllBytes(otherPath);
        AddWordform(cache);
        cache.ProjectId.Path = otherPath;
        try
        {
            var loader = new FwDataProjectLoader();
            Assert.Throws<InvalidOperationException>(() => loader.Save(cache));
            Assert.Throws<InvalidOperationException>(() => loader.Save(cache, otherPath));
            Assert.Equal(originalBefore, File.ReadAllBytes(originalPath));
            Assert.Equal(otherBefore, File.ReadAllBytes(otherPath));
        }
        finally { cache.ProjectId.Path = originalPath; }
    }

    [Fact]
    public void SaveRefusesANameSharedBackendEvenWhenItsReportedPathMatches()
    {
        var (path, root) = CreateProject(projectSharing: true);
        using var cache = OpenSharedPeer(path, root);
        var before = File.ReadAllBytes(path);
        AddWordform(cache);

        Assert.Throws<InvalidOperationException>(() => new FwDataProjectLoader().Save(cache, path));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void SavingACandidateCannotSendChangesToAnAlreadyRunningSharedPeer()
    {
        var (path, root) = CreateProject(projectSharing: true);
        var before = File.ReadAllBytes(path);
        using (var live = OpenSharedPeer(path, root))
        {
            using var candidate = new ScratchCacheFactory().CreateFromFileCopy(path, Path.Combine(root, "candidate"));
            AddWordform(candidate);
            new FwDataProjectLoader().Save(candidate);
            live.ActionHandlerAccessor.Commit();
            Assert.Empty(live.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances());
        }
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void ASeparateCommandRefusesTheExactFileHeldByASharedPeer()
    {
        var (path, root) = CreateProject(projectSharing: true);
        using var live = OpenSharedPeer(path, root);
        var before = File.ReadAllBytes(path);
        var start = CliProcess.CreateStartInfo(Path.Combine(root, "worker"), null, true,
            "project", "word-forming", "--project", path, "--add", "0123456789", "--json");

        var result = ToolProcess.Run(start);

        Assert.True(result.ExitCode != 0, result.Error + result.Output);
        Assert.Contains("project.in-use", result.Output + result.Error);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    private static LcmCache OpenSharedPeer(string path, string root)
    {
        var progress = new LcmThreadedProgress();
        return LcmCache.CreateCacheFromLocalProjectFile(path, null,
            new HeadlessLcmUi(progress.SynchronizeInvoke), new LcmDirectories(root, root),
            new LcmSettings(), progress);
    }

    private static void AddWordform(LcmCache cache) => NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor,
        () => cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
            .Create(TsStringUtils.MakeString("privatecandidate", cache.DefaultVernWs)));

    private (string Path, string Root) CreateProject(bool projectSharing)
    {
        var sourcePath = pristine.CopyProjectFile();
        var root = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(sourcePath))!, "isolation");
        var name = "isolation-" + Guid.NewGuid().ToString("N");
        var folder = Path.Combine(root, name);
        Directory.CreateDirectory(root);
        Directory.Move(Path.GetDirectoryName(sourcePath)!, folder);
        var livePath = Path.Combine(folder, name + ".fwdata");
        File.Move(Path.Combine(folder, Path.GetFileName(sourcePath)), livePath);
        var settings = new ProjectLexiconSettings { ProjectSharing = projectSharing };
        var settingsPath = LexiconSettingsFileHelper.GetProjectLexiconSettingsPath(folder);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        new ProjectLexiconSettingsDataMapper(new FileSettingsStore(settingsPath)).Write(settings);
        return (livePath, root);
    }
}
