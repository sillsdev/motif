using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.LcmUtils;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>A seeded lexicon with undeclared letters and typed Selection words, without any Texts.</summary>
public sealed class NoTextsWarningProject : IDisposable
{
    public NoTextsWarningProject(PristineProjectFixture pristine)
    {
        using var cache = pristine.NewScratch();
        var services = cache.ServiceLocator;
        var forms = new List<(string Form, Guid AllomorphId)>();
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var first = services.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
            first.LexemeFormOA.Form.set_String(cache.DefaultVernWs, "trois");
            ((IMoStemMsa)first.MorphoSyntaxAnalysesOC.Single()).PartOfSpeechRA = null;
            TroisEntryId = first.Guid;
            TroisAllomorphId = first.LexemeFormOA.Guid;
            foreach (var spelling in RefusedForms)
            {
                var entry = services.GetInstance<ILexEntryFactory>().Create();
                var form = services.GetInstance<IMoStemAllomorphFactory>().Create();
                entry.LexemeFormOA = form;
                form.MorphTypeRA = services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem);
                form.Form.set_String(cache.DefaultVernWs, spelling.Normalize(System.Text.NormalizationForm.FormD));
                var msa = services.GetInstance<IMoStemMsaFactory>().Create();
                entry.MorphoSyntaxAnalysesOC.Add(msa);
                msa.PartOfSpeechRA = services.GetInstance<IPartOfSpeechRepository>().GetObject(pristine.Seed.PartOfSpeechId);
                var sense = services.GetInstance<ILexSenseFactory>().Create();
                entry.SensesOS.Add(sense);
                sense.MorphoSyntaxAnalysisRA = msa;
                sense.Gloss.set_String(cache.DefaultAnalWs, spelling);
                forms.Add((spelling, form.Guid));
            }
        });
        RealParserProject.PrepareForParsing(cache, "a", "b", "e", "f", "i", "l", "m", "n", "o", "r", "s", "t", "u", "v", "x");
        FwDataPath = cache.ProjectId.Path;
        Allomorphs = forms;
        ManagedRoot = Path.Combine(Path.GetTempPath(), "SIL.Motif.NoTextsWarnings", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ManagedRoot);
    }

    public static IReadOnlyList<string> RefusedForms { get; } =
        ["chassis", "échauffer", "ciel", "courir", "chat", "très", "fenêtre", "chien", "lionceaux",
         "arc-en-ciel", "chaton", "cuisiner", "chaud", "chassis", "chauffer"];
    public string FwDataPath { get; }
    public string ManagedRoot { get; }
    public Guid TroisEntryId { get; private set; }
    public Guid TroisAllomorphId { get; private set; }
    public IReadOnlyList<(string Form, Guid AllomorphId)> Allomorphs { get; }

    public void Dispose()
    {
        try { Directory.Delete(ManagedRoot, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
