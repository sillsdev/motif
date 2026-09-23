using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// The small, known body of lexical data every test built on <see cref="NewLangProjFixture"/> starts
/// from: two entries, each with a lexeme form, a sense, a gloss and a stem MSA sharing one part of
/// speech.
/// </summary>
/// <remarks>
/// <para>
/// Two of everything rather than one, because a recurring shape in these tests is "operate on the
/// first, then prove a second is unaffected" — a single entry cannot express that, and a test that
/// silently loses its control still passes.
/// </para>
/// <para>
/// The guids are returned rather than looked up again, so a test names the object it means instead of
/// taking whatever sorts first. That is the substantive difference from reading a sample project: what
/// each test operates on is decided here, in writing, and cannot change underneath it.
/// </para>
/// </remarks>
public sealed record SeededProject(
    Guid FirstEntryId,
    Guid SecondEntryId,
    Guid FirstLexemeFormId,
    Guid SecondLexemeFormId,
    Guid FirstSenseId,
    Guid SecondSenseId,
    Guid PartOfSpeechId,
    int NoteFieldFlid,
    int PriorityFieldFlid)
{
    /// <summary>The vernacular form written into the first entry's lexeme form.</summary>
    public const string FirstForm = "motifa";

    /// <summary>The vernacular form written into the second entry's lexeme form.</summary>
    public const string SecondForm = "motifb";

    /// <summary>The analysis-language gloss on the first sense.</summary>
    public const string FirstGloss = "first seeded gloss";

    /// <summary>The analysis-language gloss on the second sense.</summary>
    public const string SecondGloss = "second seeded gloss";

    /// <summary>The LibLCM class both seeded custom fields are defined on.</summary>
    public const string CustomFieldOwnerClass = "LexEntry";

    /// <summary>A <c>MultiUnicode</c> custom field, analysis-writing-system selector.</summary>
    public const string NoteFieldName = "MotifSeededNote";

    /// <summary>An <c>Integer</c> custom field, sharing <see cref="CustomFieldOwnerClass"/> with <see cref="NoteFieldName"/>.</summary>
    public const string PriorityFieldName = "MotifSeededPriority";

    /// <summary>The analysis-language title <see cref="SeedText"/> writes onto the seeded Text.</summary>
    public const string TextTitle = "Seeded Text";

    /// <summary>The vernacular surface form <see cref="SeedText"/> gives its approved analysis.</summary>
    public const string AnalysedWordForm = "motifanalysed";

    /// <summary>The vernacular surface form <see cref="SeedText"/> leaves without any chosen analysis.</summary>
    public const string UnanalysedWordForm = "motifunanalysed";

    /// <summary>The punctuation form <see cref="SeedText"/> places after the analysed word.</summary>
    public const string PunctuationForm = ".";

    /// <summary>
    /// Writes the seed into <paramref name="cache"/> and returns the identity of everything it made.
    /// </summary>
    /// <remarks>
    /// Two phases, mirroring ADR 0005: the custom-field definitions go in their own non-undoable unit of
    /// work first, then the lexical data in a second, so the schema mutation is never left inside an open
    /// task the way Flexicon's was.
    /// </remarks>
    public static SeededProject Seed(LcmCache cache)
    {
        var services = cache.ServiceLocator;
        var vernWs = cache.DefaultVernWs;
        var analWs = cache.DefaultAnalWs;

        var stemType = services.GetInstance<IMoMorphTypeRepository>()
            .GetObject(MoMorphTypeTags.kguidMorphStem);

        var mdc = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        int noteFlid = 0;
        int priorityFlid = 0;

        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            // Both on the same owning class so a flid drift in the second field's slot is possible to detect.
            noteFlid = mdc.AddCustomField(
                CustomFieldOwnerClass, NoteFieldName, CellarPropertyType.MultiUnicode, 0,
                string.Empty, WritingSystemServices.kwsAnal, Guid.Empty);
            priorityFlid = mdc.AddCustomField(
                CustomFieldOwnerClass, PriorityFieldName, CellarPropertyType.Integer, 0);
        });

        ILexEntry first = null!;
        ILexEntry second = null!;
        IPartOfSpeech pos = null!;

        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            pos = services.GetInstance<IPartOfSpeechFactory>().Create();
            cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(pos);
            pos.Name.set_String(analWs, "SeededNoun");

            first = MakeEntry(cache, stemType, FirstForm, FirstGloss, pos, vernWs, analWs);
            second = MakeEntry(cache, stemType, SecondForm, SecondGloss, pos, vernWs, analWs);
        });

        return new SeededProject(
            FirstEntryId: first.Guid,
            SecondEntryId: second.Guid,
            FirstLexemeFormId: first.LexemeFormOA!.Guid,
            SecondLexemeFormId: second.LexemeFormOA!.Guid,
            FirstSenseId: first.SensesOS[0].Guid,
            SecondSenseId: second.SensesOS[0].Guid,
            PartOfSpeechId: pos.Guid,
            NoteFieldFlid: noteFlid,
            PriorityFieldFlid: priorityFlid);
    }

    /// <summary>
    /// Writes one Text into <paramref name="cache"/>: a title, two paragraphs each holding one segment,
    /// an approved analysis (two morph bundles, in order, drawn from <paramref name="seed"/>'s two
    /// entries) followed by a punctuation form in the first segment, and one unanalysed wordform alone
    /// in the second. Exercises every shape <see cref="SIL.Motif.Host.Texts.InterlinearTextReader"/> must
    /// tell apart.
    /// </summary>
    public static SeededText SeedText(LcmCache cache, SeededProject seed)
    {
        var services = cache.ServiceLocator;
        var vernWs = cache.DefaultVernWs;
        var analWs = cache.DefaultAnalWs;

        var entryRepository = services.GetInstance<ILexEntryRepository>();
        var firstEntry = entryRepository.GetObject(seed.FirstEntryId);
        var secondEntry = entryRepository.GetObject(seed.SecondEntryId);
        var pos = services.GetInstance<IPartOfSpeechRepository>().GetObject(seed.PartOfSpeechId);

        IText text = null!;
        IStTxtPara firstParagraph = null!;
        IStTxtPara secondParagraph = null!;
        ISegment firstSegment = null!;
        ISegment secondSegment = null!;
        IWfiWordform analysedWordform = null!;
        IWfiWordform unanalysedWordform = null!;
        IWfiAnalysis analysis = null!;

        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            text = services.GetInstance<ITextFactory>().Create();
            text.Name.set_String(analWs, TextTitle);
            var contents = services.GetInstance<IStTextFactory>().Create();
            text.ContentsOA = contents;

            firstParagraph = services.GetInstance<IStTxtParaFactory>().Create();
            contents.ParagraphsOS.Add(firstParagraph);
            firstParagraph.Contents = TsStringUtils.MakeString($"{AnalysedWordForm}{PunctuationForm}", vernWs);
            firstSegment = EnsureOneSegment(services, firstParagraph);

            analysedWordform = services.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(AnalysedWordForm, vernWs));
            analysis = services.GetInstance<IWfiAnalysisFactory>().Create();
            analysedWordform.AnalysesOC.Add(analysis);
            analysis.CategoryRA = pos;

            var firstBundle = services.GetInstance<IWfiMorphBundleFactory>().Create();
            analysis.MorphBundlesOS.Add(firstBundle);
            firstBundle.MorphRA = firstEntry.LexemeFormOA;
            firstBundle.MsaRA = firstEntry.MorphoSyntaxAnalysesOC.First();
            firstBundle.SenseRA = firstEntry.SensesOS[0];

            var secondBundle = services.GetInstance<IWfiMorphBundleFactory>().Create();
            analysis.MorphBundlesOS.Add(secondBundle);
            secondBundle.MorphRA = secondEntry.LexemeFormOA;
            secondBundle.MsaRA = secondEntry.MorphoSyntaxAnalysesOC.First();
            secondBundle.SenseRA = secondEntry.SensesOS[0];

            cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.approves);
            firstSegment.AnalysesRS.Add(analysis);

            var punctuation = services.GetInstance<IPunctuationFormFactory>().Create();
            punctuation.Form = TsStringUtils.MakeString(PunctuationForm, vernWs);
            firstSegment.AnalysesRS.Add(punctuation);

            secondParagraph = services.GetInstance<IStTxtParaFactory>().Create();
            contents.ParagraphsOS.Add(secondParagraph);
            secondParagraph.Contents = TsStringUtils.MakeString(UnanalysedWordForm, vernWs);
            secondSegment = EnsureOneSegment(services, secondParagraph);

            unanalysedWordform = services.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(UnanalysedWordForm, vernWs));
            secondSegment.AnalysesRS.Add(unanalysedWordform);
        });

        return new SeededText(
            TextId: text.Guid,
            FirstParagraphId: firstParagraph.Guid,
            SecondParagraphId: secondParagraph.Guid,
            FirstSegmentId: firstSegment.Guid,
            SecondSegmentId: secondSegment.Guid,
            AnalysedWordformId: analysedWordform.Guid,
            UnanalysedWordformId: unanalysedWordform.Guid,
            ApprovedAnalysisId: analysis.Guid);
    }

    // Setting Contents auto-creates one spanning Segment; reuse it instead of adding a second.
    private static ISegment EnsureOneSegment(ILcmServiceLocator services, IStTxtPara paragraph)
    {
        if (paragraph.SegmentsOS.Count > 0) return paragraph.SegmentsOS[0];

        var segment = services.GetInstance<ISegmentFactory>().Create();
        paragraph.SegmentsOS.Add(segment);
        return segment;
    }

    private static ILexEntry MakeEntry(
        LcmCache cache, IMoMorphType stemType, string form, string gloss, IPartOfSpeech pos, int vernWs, int analWs)
    {
        var services = cache.ServiceLocator;

        var entry = services.GetInstance<ILexEntryFactory>().Create();

        var lexemeForm = services.GetInstance<IMoStemAllomorphFactory>().Create();
        entry.LexemeFormOA = lexemeForm;
        lexemeForm.MorphTypeRA = stemType;
        lexemeForm.Form.set_String(vernWs, form);

        var msa = services.GetInstance<IMoStemMsaFactory>().Create();
        entry.MorphoSyntaxAnalysesOC.Add(msa);
        msa.PartOfSpeechRA = pos;

        var sense = services.GetInstance<ILexSenseFactory>().Create();
        entry.SensesOS.Add(sense);
        sense.Gloss.set_String(analWs, gloss);
        sense.MorphoSyntaxAnalysisRA = msa;

        return entry;
    }
}

/// <summary>
/// Identity of everything <see cref="SeededProject.SeedText"/> wrote, for a test to name what it means
/// instead of rediscovering it by re-reading the project.
/// </summary>
public sealed record SeededText(
    Guid TextId,
    Guid FirstParagraphId,
    Guid SecondParagraphId,
    Guid FirstSegmentId,
    Guid SecondSegmentId,
    Guid AnalysedWordformId,
    Guid UnanalysedWordformId,
    Guid ApprovedAnalysisId);
