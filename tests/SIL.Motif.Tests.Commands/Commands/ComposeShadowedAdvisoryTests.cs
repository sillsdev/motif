using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>A compose that edits or adds beside an alternate PanGloss reports shadowed is advised, never refused.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class ComposeShadowedAdvisoryTests(PristineProjectFixture pristine) : IDisposable
{
    private const string Version = "0.1.0";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.ComposeShadowedAdvisoryTests",
        Guid.NewGuid().ToString("N"));

    private sealed record Project(string Path, string Vernacular, Guid Entry, Guid Shadowed, Guid Free);

    // One entry with two alternate stem forms: 'ba' (the shadowed one) and 'bo' (never reported).
    private Project NewProject(string draft)
    {
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
        var morphType = cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem);
        var forms = new List<IMoStemAllomorph>();
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            foreach (var text in new[] { "ba", "bo" })
            {
                var form = cache.ServiceLocator.GetInstance<IMoStemAllomorphFactory>().Create();
                entry.AlternateFormsOS.Add(form);
                form.MorphTypeRA = morphType;
                form.Form.set_String(cache.DefaultVernWs, text);
                forms.Add(form);
            }
        });
        var project = new Project(cache.ProjectId.Path, cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs),
            entry.Guid, forms[0].Guid, forms[1].Guid);
        new FwDataProjectLoader().Save(cache);
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(project.Path, Version, draft, "advisories")).Succeeded);
        return project;
    }

    private void StoreReport(Project project, ShadowedAlternatesReport? report)
    {
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project.Path), _root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        if (report is null) return;
        var token = JsonSerializer.Serialize(captured.Value!.Token, MotifJson.CreateOptions());
        var saved = ProjectStoreCommand.Run(project.Path, MotifProductVersion.CurrentText, (database, _) =>
        {
            new GrammarCheckRepository(database).Save(token, "selection", "parser", new GrammarCheckResponse([], true)
                { ShadowedAlternates = report }, captured.Value.SourceLastWriteUtc);
            return SIL.Motif.Contract.Commands.CommandOutcome<string>.Success("seeded");
        });
        Assert.True(saved.Succeeded, saved.Refusal?.Message);
    }

    private static ShadowedAlternatesReport Report(Project project, string tier) => new(true,
        [new ShadowedAlternate(tier, "entry", "eA", "ba", ["bal"], ["unconstrained_earlier"],
            [project.Shadowed.ToString("D")], tier == ShadowedAlternateTiers.Definite ? [project.Shadowed.ToString("D")] : [])
            { EntryGuid = project.Entry.ToString("D") }], []);

    private static string EditForm(Project project, Guid target, string expected) => $$$"""
        {"intents":[{"verb":"compose-edit-allomorph-form","intent":{"entry":"{{{CanonicalId.FromGuid(project.Entry).Value}}}",
          "target":"{{{CanonicalId.FromGuid(target).Value}}}","writingSystem":"{{{project.Vernacular}}}",
          "expectedForm":"{{{expected}}}","newForm":"{{{expected}}}x"}}]}
        """;

    private static string AddForm(Project project) => $$$"""
        {"intents":[{"verb":"compose-author-alternate-form","intent":{"entry":"{{{CanonicalId.FromGuid(project.Entry).Value}}}",
          "form":"bu","formWs":"{{{project.Vernacular}}}","morphType":"{{{CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem).Value}}}"}}]}
        """;

    [Fact]
    public void EditingADefinitelyShadowedAlternateIsAdvisedWithTheShadowingAlternate()
    {
        var project = NewProject("edit-shadowed");
        StoreReport(project, Report(project, ShadowedAlternateTiers.Definite));

        var batch = ProposalCommands.ComposeBatch(new(project.Path, Version, "edit-shadowed",
            EditForm(project, project.Shadowed, "ba")));

        Assert.True(batch.Succeeded, batch.Refusal?.Message);
        var advisory = Assert.Single(batch.Value!.Advisories!);
        Assert.Equal(ComposeAdvisoryCodes.ShadowedAlternate, advisory.Code);
        Assert.Equal([0], advisory.Intents);
        Assert.Contains("shadowed by 'bal'", advisory.Message);
        Assert.Contains("unconstrained_earlier", advisory.Message);
    }

    [Fact]
    public void EditingAMaybeShadowedAlternateIsAdvisedMoreSoftly()
    {
        var project = NewProject("edit-maybe");
        StoreReport(project, Report(project, ShadowedAlternateTiers.Maybe));

        var batch = ProposalCommands.ComposeBatch(new(project.Path, Version, "edit-maybe",
            EditForm(project, project.Shadowed, "ba")));

        Assert.True(batch.Succeeded, batch.Refusal?.Message);
        var advisory = Assert.Single(batch.Value!.Advisories!);
        Assert.Equal(ComposeAdvisoryCodes.ShadowedAlternate, advisory.Code);
        Assert.Contains("could not prove", advisory.Message);
        Assert.DoesNotContain("no word can select", advisory.Message);
    }

    [Fact]
    public void EditingAnAlternateThePanGlossReportDoesNotMarkCarriesNoAdvisory()
    {
        var project = NewProject("edit-free");
        StoreReport(project, Report(project, ShadowedAlternateTiers.Definite));

        var batch = ProposalCommands.ComposeBatch(new(project.Path, Version, "edit-free",
            EditForm(project, project.Free, "bo")));

        Assert.True(batch.Succeeded, batch.Refusal?.Message);
        Assert.Null(batch.Value!.Advisories);
    }

    [Fact]
    public void AddingAnAlternateBesideADefinitelyShadowedOneIsAdvised()
    {
        var project = NewProject("add-beside");
        StoreReport(project, Report(project, ShadowedAlternateTiers.Definite));

        var batch = ProposalCommands.ComposeBatch(new(project.Path, Version, "add-beside", AddForm(project)));

        Assert.True(batch.Succeeded, batch.Refusal?.Message);
        var advisory = Assert.Single(batch.Value!.Advisories!);
        Assert.Equal(ComposeAdvisoryCodes.ShadowedAlternate, advisory.Code);
        Assert.Contains("'bal'", advisory.Message);
    }

    [Fact]
    public void WithoutAStoredReportComposeCarriesNoAdvisory()
    {
        var none = NewProject("no-report");
        StoreReport(none, null);

        var batch = ProposalCommands.ComposeBatch(new(none.Path, Version, "no-report", EditForm(none, none.Shadowed, "ba")));

        Assert.True(batch.Succeeded, batch.Refusal?.Message);
        Assert.Null(batch.Value!.Advisories);
    }

    [Fact]
    public void ACorruptStoredReportLeavesComposeWorkingWithOneAdvisoryNamingTheReason()
    {
        var project = NewProject("corrupt");
        StoreReport(project, Report(project, ShadowedAlternateTiers.Definite));
        var corrupted = ProjectStoreCommand.Run(project.Path, MotifProductVersion.CurrentText, (database, _) =>
        {
            using var connection = database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE GrammarChecks SET ResponseGzip = X'0102';";
            command.ExecuteNonQuery();
            return SIL.Motif.Contract.Commands.CommandOutcome<string>.Success("corrupted");
        });
        Assert.True(corrupted.Succeeded, corrupted.Refusal?.Message);

        var batch = ProposalCommands.ComposeBatch(new(project.Path, Version, "corrupt", EditForm(project, project.Free, "bo")));

        Assert.True(batch.Succeeded, batch.Refusal?.Message);
        var advisory = Assert.Single(batch.Value!.Advisories!);
        Assert.Equal(ComposeAdvisoryCodes.ShadowedAlternate, advisory.Code);
        Assert.Contains("could not be read", advisory.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
