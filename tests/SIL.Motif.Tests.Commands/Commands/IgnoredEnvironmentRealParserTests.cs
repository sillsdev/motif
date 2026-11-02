using System;
using System.IO;
using System.Linq;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// The pinned parser's own grammar-health report is read beside Motif's check for a form whose environment is
/// unreadable, so the record shows what each side says about the same stored environment.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class IgnoredEnvironmentRealParserTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        "SIL.Motif.IgnoredEnvironmentRealParserTests", Guid.NewGuid().ToString("N"));

    [SIL.Motif.Tests.Parser.RealParserFact]
    public void TheRealGrammarCheckWarnsAboutAnEnvironmentWithABoundaryAndNamesTheForm()
    {
        Directory.CreateDirectory(_root);
        using (var cache = NewLangProjFixture.CreateCache(_root))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var services = cache.ServiceLocator;
                var set = services.GetInstance<IPhPhonemeSetFactory>().Create();
                cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.Add(set);
                foreach (var spelling in new[] { "t", "a" })
                {
                    var phoneme = services.GetInstance<IPhPhonemeFactory>().Create();
                    set.PhonemesOC.Add(phoneme);
                    var code = services.GetInstance<IPhCodeFactory>().Create();
                    phoneme.CodesOS.Add(code);
                    code.Representation.set_String(cache.DefaultVernWs, spelling);
                }
                var entry = services.GetInstance<ILexEntryFactory>().Create(
                    services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem),
                    TsStringUtils.MakeString("tata", cache.DefaultVernWs), "gloss",
                    new SandboxGenericMSA { MsaType = MsaType.kStem });
                var environment = services.GetInstance<IPhEnvironmentFactory>().Create();
                cache.LangProject.PhonologicalDataOA.EnvironmentsOS.Add(environment);
                environment.StringRepresentation = TsStringUtils.MakeString("/ _ +a", cache.DefaultVernWs);
                ((IMoStemAllomorph)entry.LexemeFormOA).PhoneEnvRC.Add(environment);
            });
            new FwDataProjectLoader().Save(cache);
        }
        var fwDataPath = NewLangProjFixture.FwDataPath(_root);
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), Path.Combine(_root, "motif"));
        Assert.True(captured.Succeeded, captured.Refusal?.Message);

        var check = GrammarCheckQuery.Query(new GrammarCheckRequest(fwDataPath));
        Assert.True(check.Succeeded, check.Refusal?.Message);
        foreach (var finding in check.Value!.Findings)
            output.WriteLine($"{finding.Code} | {finding.Severity} | {finding.Description}");

        var warning = Assert.Single(check.Value.Findings,
            item => item.Code == GrammarFindingCodes.AllomorphEnvironmentIgnoredByParser);
        Assert.Contains("'tata'", warning.Description, StringComparison.Ordinal);
        // PanGloss 0.12.3's own report is silent about the same environment, which is why Motif checks it itself.
        Assert.DoesNotContain(check.Value.Findings,
            item => item != warning && item.Description.Contains("nvironment", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
