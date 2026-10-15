using System;
using System.Collections.Generic;
using System.Linq;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>Authored morphology identities for one synthetic Tagalog parser example.</summary>
/// <param name="Form">The authored LibLCM form identity.</param>
/// <param name="Msa">The authored LibLCM morphosyntactic analysis identity.</param>
public sealed record TagalogAffixPanelMorph(Guid Form, Guid Msa);

/// <summary>Authors a synthetic Tagalog affix-process panel in a blank LibLCM project.</summary>
/// <remarks>
/// <para>
/// This is a controlled parser example, not a lexical or grammatical description of Tagalog.
/// Its affix processes use reusable phonological input mappings and copy references.
/// </para>
/// </remarks>
/// <param name="TextId">The authored Text that contains the approved synthetic forms.</param>
/// <param name="Words">The surface forms in the order they appear in the Text.</param>
/// <param name="ExpectedMorphs">The expected form and MSA identities for each surface form.</param>
/// <param name="ControlTextId">The Text containing approved witnesses and unanalysed negative controls.</param>
/// <param name="NegativeControlWords">The negative control forms in the order they appear.</param>
/// <param name="PositiveControlMorphs">The expected form and MSA identities for approved control witnesses.</param>
/// <param name="ForbiddenMorphs">The affix and stem identities that must not analyze each control.</param>
public sealed record TagalogAffixPanelFixture(
    Guid TextId,
    IReadOnlyList<string> Words,
    IReadOnlyDictionary<string, IReadOnlyList<TagalogAffixPanelMorph>> ExpectedMorphs,
    Guid ControlTextId,
    IReadOnlyList<string> NegativeControlWords,
    IReadOnlyDictionary<string, IReadOnlyList<TagalogAffixPanelMorph>> PositiveControlMorphs,
    IReadOnlyDictionary<string, IReadOnlyList<TagalogAffixPanelMorph>> ForbiddenMorphs)
{
    /// <summary>Adds the stems, affixes, processes, and approved word analyses to <paramref name="cache"/>.</summary>
    /// <param name="cache">The blank LibLCM project to populate.</param>
    public static TagalogAffixPanelFixture Author(LcmCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        var services = cache.ServiceLocator;
        var phonology = cache.LangProject.PhonologicalDataOA;
        if (phonology.PhonemeSetsOS.Count == 0)
            phonology.PhonemeSetsOS.Add(services.GetInstance<IPhPhonemeSetFactory>().Create());
        var phonemeSet = phonology.PhonemeSetsOS[0];
        OwnedBoundaryMarkerFixture.EnsureReservedMarkers(cache, phonemeSet);

        var phonemes = AddPhonemes(cache, phonemeSet, "kamotb pasyalhikn g-");
        var consonants = AddNaturalClass(cache, phonemes, "C", "Consonants", "kmtbpsylhng");
        var vowels = AddNaturalClass(cache, phonemes, "V", "Vowels", "aoi");
        var environment = AddEnvironment(cache, "/ # [C] _");

        var rootKamot = MakeEntry(cache, MoMorphTypeTags.kguidMorphStem, "kamot", "synthetic root");
        var rootTakbo = MakeEntry(cache, MoMorphTypeTags.kguidMorphStem, "takbo", "synthetic root");
        var rootPasyal = MakeEntry(cache, MoMorphTypeTags.kguidMorphStem, "pasyal", "synthetic root");
        var rootHilik = MakeEntry(cache, MoMorphTypeTags.kguidMorphStem, "hilik", "synthetic root");
        var rootSalpok = MakeEntry(cache, MoMorphTypeTags.kguidMorphStem, "salpok", "synthetic root");
        var rootTakas = MakeEntry(cache, MoMorphTypeTags.kguidMorphStem, "takas", "synthetic root");
        var rootTaat = MakeEntry(cache, MoMorphTypeTags.kguidMorphStem, "taat", "synthetic root");
        var rootAyat = MakeEntry(cache, MoMorphTypeTags.kguidMorphStem, "ayat", "synthetic root");

        var infix = MakeEntry(cache, MoMorphTypeTags.kguidMorphInfix, "in", "synthetic infix");
        SetPosition(infix, environment);
        var mag = MakeEntry(cache, MoMorphTypeTags.kguidMorphPrefix, "mag-", "synthetic prefix");
        var nag = MakeEntry(cache, MoMorphTypeTags.kguidMorphPrefix, "nag-", "synthetic prefix");
        var suffix = MakeEntry(cache, MoMorphTypeTags.kguidMorphSuffix, "-an", "synthetic suffix");

        var cvCopy = MakeProcessEntry(cache, "CV-copy",
        [
            new CopyPart(0), new CopyPart(1), new CopyPart(0), new CopyPart(1), new CopyPart(2),
        ],
        [consonants, vowels, null]);
        var fullCopy = MakeProcessEntry(cache, "Full-copy-with-separator",
        [new CopyPart(0), new InsertPhone('-'), new CopyPart(0)],
        [null]);

        var words = new (string Word, EntryMorph[] Morphs)[]
        {
            ("kinamot", [infix, rootKamot]),
            ("kinakamot", [infix, cvCopy, rootKamot]),
            ("magtatakbo", [mag, cvCopy, rootTakbo]),
            ("magpasyal-pasyal", [mag, fullCopy, rootPasyal]),
            ("naghihilik", [nag, cvCopy, rootHilik]),
            ("sinalpok", [infix, rootSalpok]),
            ("tinakasan", [infix, rootTakas, suffix]),
        };

        var positive = MakeApprovedText(cache, words, "SYNTHETIC EXAMPLE: Tagalog affix process panel");
        var positiveControls = new (string Word, EntryMorph[] Morphs)[]
        {
            ("tataat", [cvCopy, rootTaat]),
            ("ayat", [rootAyat]),
        };
        var controls = MakeApprovedText(
            cache, positiveControls, "SYNTHETIC EXAMPLE: Tagalog positive and negative controls");
        var negativeWords = new (string Word, EntryMorph[] Morphs)[]
        {
            ("ikinamot", [infix, rootKamot]),
            ("kainmot", [infix, rootKamot]),
            ("taataat", [cvCopy, rootTaat]),
            ("inayat", [infix, rootAyat]),
        };
        MakeUnanalysedWords(cache, controls.Contents, negativeWords);
        return new TagalogAffixPanelFixture(
            positive.TextId,
            positive.Words,
            positive.ExpectedMorphs,
            controls.TextId,
            negativeWords.Select(item => item.Word).ToArray(),
            controls.ExpectedMorphs,
            negativeWords.ToDictionary(
                item => item.Word,
                item => (IReadOnlyList<TagalogAffixPanelMorph>)item.Morphs.Select(morph =>
                    new TagalogAffixPanelMorph(morph.Form.Guid, morph.Msa.Guid)).ToArray(),
                StringComparer.Ordinal));
    }

    private static Dictionary<char, IPhPhoneme> AddPhonemes(
        LcmCache cache, IPhPhonemeSet phonemeSet, string inventory)
    {
        var phonemes = new Dictionary<char, IPhPhoneme>();
        foreach (var symbol in inventory.Where(character => !char.IsWhiteSpace(character)).Distinct())
        {
            var phoneme = cache.ServiceLocator.GetInstance<IPhPhonemeFactory>().Create();
            phonemeSet.PhonemesOC.Add(phoneme);
            phoneme.Name.set_String(cache.DefaultVernWs, symbol.ToString());
            phoneme.CodesOS.Clear();
            var code = cache.ServiceLocator.GetInstance<IPhCodeFactory>().Create();
            phoneme.CodesOS.Add(code);
            code.Representation.set_String(cache.DefaultVernWs, symbol.ToString());
            phonemes.Add(symbol, phoneme);
        }

        return phonemes;
    }

    private static IPhNCSegments AddNaturalClass(
        LcmCache cache,
        IReadOnlyDictionary<char, IPhPhoneme> phonemes,
        string abbreviation,
        string name,
        string members)
    {
        var naturalClass = cache.ServiceLocator.GetInstance<IPhNCSegmentsFactory>().Create();
        cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Add(naturalClass);
        naturalClass.Abbreviation.set_String(cache.DefaultAnalWs, abbreviation);
        naturalClass.Name.set_String(cache.DefaultAnalWs, name);
        foreach (var member in members)
            naturalClass.SegmentsRC.Add(phonemes[member]);
        return naturalClass;
    }

    private static IPhEnvironment AddEnvironment(LcmCache cache, string representation)
    {
        var environment = cache.ServiceLocator.GetInstance<IPhEnvironmentFactory>().Create();
        cache.LangProject.PhonologicalDataOA.EnvironmentsOS.Add(environment);
        environment.StringRepresentation = TsStringUtils.MakeString(representation, cache.DefaultVernWs);
        return environment;
    }

    private static void SetPosition(EntryMorph affix, IPhEnvironment environment)
    {
        var allomorph = (IMoAffixAllomorph)affix.Form;
        allomorph.PositionRS.Clear();
        allomorph.PositionRS.Add(environment);
    }

    private static EntryMorph MakeProcessEntry(
        LcmCache cache,
        string label,
        IReadOnlyList<ProcessOutput> output,
        IReadOnlyList<IPhNaturalClass?> inputClasses)
    {
        var entry = MakeEntry(cache, MoMorphTypeTags.kguidMorphPrefix, string.Empty, label).Entry;
        var process = cache.ServiceLocator.GetInstance<IMoAffixProcessFactory>().Create();
        entry.LexemeFormOA = process;
        process.OutputOS.Clear();
        process.InputOS.Clear();
        process.IsAbstract = false;
        process.MorphTypeRA = cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>()
            .GetObject(MoMorphTypeTags.kguidMorphPrefix);
        process.Form.set_String(cache.DefaultVernWs, string.Empty);

        foreach (var naturalClass in inputClasses)
        {
            if (naturalClass is null)
            {
                process.InputOS.Add(cache.ServiceLocator.GetInstance<IPhVariableFactory>().Create());
                continue;
            }

            var context = cache.ServiceLocator.GetInstance<IPhSimpleContextNCFactory>().Create();
            process.InputOS.Add(context);
            context.FeatureStructureRA = naturalClass;
        }

        foreach (var step in output)
        {
            if (step is CopyPart copy)
            {
                var mapping = cache.ServiceLocator.GetInstance<IMoCopyFromInputFactory>().Create();
                process.OutputOS.Add(mapping);
                mapping.ContentRA = process.InputOS[copy.InputPart];
                continue;
            }

            var insert = cache.ServiceLocator.GetInstance<IMoInsertPhonesFactory>().Create();
            process.OutputOS.Add(insert);
            insert.ContentRS.Add(((InsertPhone)step).Phone == '-'
                ? FindPhoneme(cache, '-')
                : throw new InvalidOperationException("Unexpected inserted phone."));
        }

        return new EntryMorph(process, entry.MorphoSyntaxAnalysesOC.Single(), entry);
    }

    private static IPhPhoneme FindPhoneme(LcmCache cache, char symbol)
    {
        var phonemeSet = cache.LangProject.PhonologicalDataOA.PhonemeSetsOS[0];
        return phonemeSet.PhonemesOC.Single(phoneme =>
            phoneme.Name.get_String(cache.DefaultVernWs).Text == symbol.ToString());
    }

    private static EntryMorph MakeEntry(LcmCache cache, Guid morphType, string form, string gloss)
    {
        var services = cache.ServiceLocator;
        var entry = services.GetInstance<ILexEntryFactory>().Create(
            services.GetInstance<IMoMorphTypeRepository>().GetObject(morphType),
            TsStringUtils.MakeString(form, cache.DefaultVernWs),
            gloss,
            new SandboxGenericMSA
            {
                MsaType = morphType == MoMorphTypeTags.kguidMorphStem ? MsaType.kStem : MsaType.kUnclassified,
            });
        return new EntryMorph(entry.LexemeFormOA, entry.MorphoSyntaxAnalysesOC.Single(), entry);
    }

    private static PositiveText MakeApprovedText(
        LcmCache cache, IReadOnlyList<(string Word, EntryMorph[] Morphs)> words, string textName)
    {
        var services = cache.ServiceLocator;
        var text = services.GetInstance<ITextFactory>().Create();
        text.Name.set_String(cache.DefaultAnalWs, textName);
        var contents = services.GetInstance<IStTextFactory>().Create();
        text.ContentsOA = contents;
        var expected = new Dictionary<string, IReadOnlyList<TagalogAffixPanelMorph>>(StringComparer.Ordinal);

        foreach (var (word, morphs) in words)
        {
            var paragraph = services.GetInstance<IStTxtParaFactory>().Create();
            contents.ParagraphsOS.Add(paragraph);
            paragraph.Contents = TsStringUtils.MakeString(word, cache.DefaultVernWs);
            var segment = paragraph.SegmentsOS.Single();
            var wordform = services.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(word, cache.DefaultVernWs));
            var analysis = services.GetInstance<IWfiAnalysisFactory>().Create();
            wordform.AnalysesOC.Add(analysis);
            foreach (var morph in morphs)
            {
                var bundle = services.GetInstance<IWfiMorphBundleFactory>().Create();
                analysis.MorphBundlesOS.Add(bundle);
                bundle.MorphRA = morph.Form;
                bundle.MsaRA = morph.Msa;
            }
            cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.approves);
            segment.AnalysesRS.Add(analysis);
            expected.Add(word, morphs.Select(morph => new TagalogAffixPanelMorph(
                morph.Form.Guid, morph.Msa.Guid)).ToArray());
        }

        return new PositiveText(text.Guid, words.Select(item => item.Word).ToArray(), expected, contents);
    }

    private static void MakeUnanalysedWords(
        LcmCache cache, IStText contents, IReadOnlyList<(string Word, EntryMorph[] Morphs)> words)
    {
        var services = cache.ServiceLocator;
        foreach (var (word, _) in words)
        {
            var paragraph = services.GetInstance<IStTxtParaFactory>().Create();
            contents.ParagraphsOS.Add(paragraph);
            paragraph.Contents = TsStringUtils.MakeString(word, cache.DefaultVernWs);
            var segment = paragraph.SegmentsOS.Single();
            var wordform = services.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(word, cache.DefaultVernWs));
            segment.AnalysesRS.Add(wordform);
        }
    }

    private abstract record ProcessOutput;
    private sealed record CopyPart(int InputPart) : ProcessOutput;
    private sealed record InsertPhone(char Phone) : ProcessOutput;
    private sealed record EntryMorph(IMoForm Form, IMoMorphSynAnalysis Msa, ILexEntry Entry);
    private sealed record PositiveText(
        Guid TextId,
        IReadOnlyList<string> Words,
        IReadOnlyDictionary<string, IReadOnlyList<TagalogAffixPanelMorph>> ExpectedMorphs,
        IStText Contents);
}
