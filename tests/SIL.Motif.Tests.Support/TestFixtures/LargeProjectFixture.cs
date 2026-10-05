using System.Diagnostics;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.LcmUtils;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>A process-local saved master makes realistic project sizes affordable without sharing mutable caches.</summary>
public sealed class LargeProjectFixture : IDisposable
{
    public const int EntryCount = 3000;
    public const int TextCount = 40;
    public const int OccurrenceCount = 9760;
    public const int LongestTextWordCount = 400;
    private static readonly Lazy<MasterProject> Master = new(() => CreateMaster(
        EntryCount, EntryCount, TextCount, OccurrenceCount, LongestTextWordCount));
    private static readonly Lazy<MasterProject> RepresentativeMaster = new(() => CreateMaster(
        22948, 21604, 129, 76761, 1792));
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.Tests.Large", Guid.NewGuid().ToString("N"));

    public LargeProjectFixture(bool representativeScale = false)
    {
        var master = representativeScale ? RepresentativeMaster.Value : Master.Value;
        var folder = Path.Combine(_root, NewLangProjFixture.ProjectName);
        PristineProjectFixture.CopyFieldWorksProjectFiles(master.Folder, folder);
        FwDataPath = Path.Combine(folder, NewLangProjFixture.ProjectName + ".fwdata");
        ManagedRoot = Path.Combine(_root, "motif");
        Directory.CreateDirectory(ManagedRoot);
        Words = master.Words;
        TextIds = master.TextIds;
        CreationTime = master.CreationTime;
        SelectedWordCount = master.SelectedWordCount;
        TotalOccurrenceCount = master.TotalOccurrenceCount;
        LongestTextLength = master.LongestTextLength;
    }

    public string FwDataPath { get; }
    public string ManagedRoot { get; }
    public IReadOnlyList<LargeProjectWord> Words { get; }
    public IReadOnlyList<Guid> TextIds { get; }
    public TimeSpan CreationTime { get; }
    public int SelectedWordCount { get; }
    public int TotalOccurrenceCount { get; }
    public int LongestTextLength { get; }

    private static MasterProject CreateMaster(int wordformCount, int selectedWordCount, int textCount,
        int occurrenceCount, int longestTextLength)
    {
        var clock = Stopwatch.StartNew();
        var root = Path.Combine(Path.GetTempPath(), "SIL.Motif.Tests.Large", Guid.NewGuid().ToString("N"));
        using var cache = NewLangProjFixture.CreateCache(root);
        var services = cache.ServiceLocator;
        var entries = services.GetInstance<ILexEntryFactory>();
        var forms = services.GetInstance<IMoStemAllomorphFactory>();
        var msas = services.GetInstance<IMoStemMsaFactory>();
        var senses = services.GetInstance<ILexSenseFactory>();
        var wordforms = services.GetInstance<IWfiWordformFactory>();
        var analyses = services.GetInstance<IWfiAnalysisFactory>();
        var bundles = services.GetInstance<IWfiMorphBundleFactory>();
        var stem = services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem);
        var words = new List<LargeProjectWord>(wordformCount);
        var stored = new List<IWfiAnalysis>(wordformCount);
        var lexicon = new List<(IMoStemAllomorph Form, IMoStemMsa Msa, ILexSense Sense)>(EntryCount);
        var textIds = new List<Guid>(textCount);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var pos = services.GetInstance<IPartOfSpeechFactory>().Create();
            cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(pos);
            pos.Name.set_String(cache.DefaultAnalWs, "Scale noun");
            for (var i = 0; i < EntryCount; i++)
            {
                var word = Word(i);
                var entry = entries.Create();
                var form = forms.Create();
                entry.LexemeFormOA = form;
                form.MorphTypeRA = stem;
                form.Form.set_String(cache.DefaultVernWs, word);
                var alternate = forms.Create();
                entry.AlternateFormsOS.Add(alternate);
                alternate.MorphTypeRA = stem;
                alternate.Form.set_String(cache.DefaultVernWs, word + "a");
                var msa = msas.Create();
                entry.MorphoSyntaxAnalysesOC.Add(msa);
                msa.PartOfSpeechRA = pos;
                var sense = senses.Create();
                entry.SensesOS.Add(sense);
                sense.MorphoSyntaxAnalysisRA = msa;
                sense.Gloss.set_String(cache.DefaultAnalWs, "scale gloss " + i);
                lexicon.Add((form, msa, sense));
            }
            for (var i = 0; i < wordformCount; i++)
            {
                var word = Word(i);
                var (form, msa, sense) = lexicon[i % EntryCount];
                var wordform = wordforms.Create(TsStringUtils.MakeString(word, cache.DefaultVernWs));
                var analysis = analyses.Create();
                wordform.AnalysesOC.Add(analysis);
                analysis.CategoryRA = pos;
                var bundle = bundles.Create();
                analysis.MorphBundlesOS.Add(bundle);
                bundle.MorphRA = form;
                bundle.MsaRA = msa;
                bundle.SenseRA = sense;
                var opinion = i % 3 == 0 ? Opinions.approves : i % 3 == 1 ? Opinions.disapproves : Opinions.noopinion;
                if (opinion != Opinions.noopinion) cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, opinion);
                words.Add(new(wordform.Guid, word, form.Guid, msa.Guid));
                stored.Add(analysis);
            }

            var occurrence = 0;
            for (var t = 0; t < textCount; t++)
            {
                var text = services.GetInstance<ITextFactory>().Create();
                text.Name.set_String(cache.DefaultAnalWs, $"Scale Text {t + 1:00}");
                text.ContentsOA = services.GetInstance<IStTextFactory>().Create();
                textIds.Add(text.Guid);
                var remaining = occurrenceCount - longestTextLength;
                var count = t == 0 ? longestTextLength :
                    remaining / (textCount - 1) + (t <= remaining % (textCount - 1) ? 1 : 0);
                for (var offset = 0; offset < count; offset += 8)
                {
                    var paragraph = services.GetInstance<IStTxtParaFactory>().Create();
                    text.ContentsOA.ParagraphsOS.Add(paragraph);
                    var width = Math.Min(8, count - offset);
                    var indices = Enumerable.Range(occurrence, width).Select(index => index % selectedWordCount).ToArray();
                    paragraph.Contents = TsStringUtils.MakeString(
                        string.Join(" ", indices.Select(index => words[index].Form)), cache.DefaultVernWs);
                    var segment = paragraph.SegmentsOS.FirstOrDefault();
                    if (segment is null)
                    {
                        segment = services.GetInstance<ISegmentFactory>().Create();
                        paragraph.SegmentsOS.Add(segment);
                    }
                    foreach (var index in indices) segment.AnalysesRS.Add(stored[index]);
                    occurrence += width;
                }
            }
        });
        RealParserProject.PrepareForParsing(cache, Enumerable.Range('a', 26).Select(c => ((char)c).ToString()).ToArray());
        new FwDataProjectLoader().Save(cache);
        clock.Stop();
        RegisterCleanup(root);
        return new(Path.GetDirectoryName(cache.ProjectId.Path)!, words.AsReadOnly(), textIds.AsReadOnly(), clock.Elapsed,
            selectedWordCount, occurrenceCount, longestTextLength);
    }

    private static string Word(int index) => "scale" + (char)('a' + index / 17576) +
        (char)('a' + index / 676 % 26) + (char)('a' + index / 26 % 26) + (char)('a' + index % 26);

    // A separate closure keeps cleanup from retaining the seed's LibLCM objects until process exit.
    private static void RegisterCleanup(string root) => AppDomain.CurrentDomain.ProcessExit += (_, _) => Delete(root);

    public void Dispose() => Delete(_root);

    private static void Delete(string root)
    {
        try { Directory.Delete(root, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record MasterProject(string Folder, IReadOnlyList<LargeProjectWord> Words,
        IReadOnlyList<Guid> TextIds, TimeSpan CreationTime, int SelectedWordCount, int TotalOccurrenceCount,
        int LongestTextLength);
}

/// <summary>Stable identities let deterministic fake outcomes name the seeded morphology exactly.</summary>
public sealed record LargeProjectWord(Guid WordformId, string Form, Guid AllomorphId, Guid MsaId);
