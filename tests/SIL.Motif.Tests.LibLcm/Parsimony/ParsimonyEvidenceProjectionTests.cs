using Microsoft.Data.Sqlite;
using System.Text;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.LiveHost.HumanJudgments;
using Xunit;

namespace SIL.Motif.Tests.Parsimony;

[Collection(LcmCacheTestCollection.Name)]
public sealed class ParsimonyEvidenceProjectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), nameof(ParsimonyEvidenceProjectionTests),
        Guid.NewGuid().ToString("N"));
    private readonly LcmCache _cache;
    private readonly SeededProject _seed;
    private readonly SeededText _text;
    private readonly Guid _outsideWordform;
    private readonly Guid _disapprovedAnalysis;
    private readonly Guid _unknownAnalysis;
    private readonly Guid _variantEntry;
    private readonly Guid _variantWordform;
    private readonly Guid _variantForm;
    private readonly Guid _inflType;

    public ParsimonyEvidenceProjectionTests(PristineProjectFixture pristine)
    {
        Directory.CreateDirectory(_root);
        _cache = pristine.NewScratch();
        _seed = pristine.Seed;
        _text = SeededProject.SeedText(_cache, _seed);
        var ids = SeedEvidence(_cache, _seed, _text);
        (_outsideWordform, _disapprovedAnalysis, _unknownAnalysis, _variantEntry, _variantWordform,
            _variantForm, _inflType) = ids;
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void CapturesOpinionsAndMorphIdentitiesForWordformsOutsideTexts()
    {
        var projection = BuildProjection(ParsimonyEvidenceScopeKind.ProjectApproved);
        var wordform = Assert.Single(projection.Wordforms, item => item.Guid == _outsideWordform);
        Assert.Contains(wordform.Analyses, item => item.Guid == _disapprovedAnalysis && item.Opinion == "disapproved");
        Assert.Contains(wordform.Analyses, item => item.Guid == _unknownAnalysis && item.Opinion == "unknown");
        Assert.DoesNotContain(projection.Occurrences, item => item.WordformGuid == _outsideWordform);
        Assert.All(Assert.Single(wordform.Analyses, item => item.Guid == _unknownAnalysis).Morphs,
            morph => Assert.Null(morph.MorphGuid));
        Assert.All(Assert.Single(wordform.Analyses, item => item.Guid == _unknownAnalysis).Morphs,
            morph => Assert.Null(morph.MsaGuid));

        var variant = Assert.Single(projection.Wordforms, item => item.Guid == _variantWordform);
        var morph = Assert.Single(Assert.Single(variant.Analyses, item => item.Opinion == "approved").Morphs);
        Assert.Equal(_variantForm, morph.MorphGuid);
        Assert.Equal(_variantEntry, morph.EntryGuid);
        Assert.Equal(_seed.FirstSenseId, morph.SenseGuid);
        Assert.Equal(_inflType, morph.InflTypeGuid);
        Assert.Contains(projection.Occurrences, occurrence => occurrence.WordformGuid == _text.AnalysedWordformId &&
            occurrence.SelectedAnalysisGuid is not null);
    }

    [Fact]
    public void ProjectApprovedAndSelectionDenominatorsStaySeparate()
    {
        var selectedWords = new[] { "café", "typed-only" };
        var selection = SIL.Motif.Host.Corpus.Selection.Create("Default",
            selectedWords.Select(word => word.Normalize(NormalizationForm.FormD)));
        var binding = new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.DefaultSelection,
            new ParsimonySelectionSnapshot("Default", [_text.TextId], ["typed-only"], selection.Words,
                selection.Sha256));
        var evidence = EvidenceProjectionBuilder.Build(_cache, TextProjection(), binding, CancellationToken.None);
        var path = Path.Combine(_root, "evidence.sqlite");
        EvidenceWriter.Write(path, evidence, "{}", "sha256:" + new string('1', 64), "sha256:" + new string('2', 64));

        using var connection = Open(path);
        Assert.Equal(1L, Scalar(connection,
            "SELECT COUNT(DISTINCT text_guid) FROM scope_texts WHERE scope_id='default-selection';"));
        Assert.Equal(1L, Scalar(connection,
            "SELECT COUNT(DISTINCT wordform_guid) FROM scope_words WHERE scope_id='default-selection' AND wordform_guid IS NOT NULL;"));
        Assert.Equal(2L, Scalar(connection,
            $"SELECT COUNT(DISTINCT scope_id) FROM scope_words WHERE wordform_guid='{_text.AnalysedWordformId:D}' AND scope_id IN ('project-approved', 'default-selection');"));
        Assert.Equal(1L, Scalar(connection,
            "SELECT COUNT(*) FROM scope_words WHERE scope_id='default-selection' AND form='typed-only' AND wordform_guid IS NULL AND source_kind='typed';"));
        Assert.True(Convert.ToInt64(Scalar(connection,
            "SELECT COUNT(DISTINCT wordform_guid) FROM scope_words WHERE scope_id='project-approved' AND wordform_guid IS NOT NULL;"))
            > Convert.ToInt64(Scalar(connection,
                "SELECT COUNT(DISTINCT wordform_guid) FROM scope_words WHERE scope_id='default-selection' AND wordform_guid IS NOT NULL;")));
    }

    [Fact]
    public void PreservesWritingSystemsOpinionsAndOneOccurrenceAcrossAlternateForms()
    {
        var projection = BuildProjection(ParsimonyEvidenceScopeKind.ProjectApproved);
        var textWordform = Assert.Single(projection.Wordforms, item => item.Guid == _text.AnalysedWordformId);
        Assert.Equal(2, textWordform.Analyses.Count(item => item.Opinion == "approved"));
        Assert.Equal("cafe\u0301", Assert.Single(textWordform.Forms, form => form.WritingSystem ==
            _cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultAnalWs)).Text);
        Assert.Equal("cafe\u0301", Assert.Single(textWordform.Forms, form => form.WritingSystem ==
            _cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultAnalWs)).TextNfd);
        Assert.True(textWordform.Forms.Select(form => form.WritingSystem).Distinct().Count() >= 2);

        var path = Path.Combine(_root, "forms.sqlite");
        EvidenceWriter.Write(path, projection, "{}", "sha256:" + new string('3', 64), "sha256:" + new string('4', 64));
        using var connection = Open(path);
        Assert.Equal((long)projection.Occurrences.Count,
            Convert.ToInt64(Scalar(connection, "SELECT COUNT(*) FROM occurrences;")));
        Assert.Equal((long)projection.Occurrences.Count,
            Convert.ToInt64(Scalar(connection, "SELECT COUNT(*) FROM occurrences WHERE text='motifanalysed';")) +
            Convert.ToInt64(Scalar(connection, "SELECT COUNT(*) FROM occurrences WHERE text<>'motifanalysed';")));
        Assert.Equal(1L, Convert.ToInt64(Scalar(connection,
            "SELECT COUNT(*) FROM occurrence_analyses WHERE role='chosen' AND analysis_guid IS NOT NULL;")));
        Assert.Equal(_text.ApprovedAnalysisId.ToString("D").ToLowerInvariant(), Scalar(connection,
            "SELECT analysis_guid FROM occurrence_analyses WHERE role='chosen';"));
        Assert.Equal(0L, Convert.ToInt64(Scalar(connection,
            $"SELECT COUNT(*) FROM analyses WHERE analysis_guid='{_unknownAnalysis:D}' AND opinion='approved';")));
        Assert.Equal(1L, Convert.ToInt64(Scalar(connection,
            $"SELECT COUNT(*) FROM occurrences WHERE wordform_guid='{_text.AnalysedWordformId:D}';")));
        Assert.Equal(2L, Convert.ToInt64(Scalar(connection,
            $"SELECT COUNT(*) FROM occurrences AS o JOIN occurrence_forms AS f USING (text_guid, segment_guid, token_ordinal) WHERE o.wordform_guid='{_text.AnalysedWordformId:D}';")));
        Assert.Equal(1L, Convert.ToInt64(Scalar(connection,
            "SELECT COUNT(*) FROM evidence_capability WHERE capability='phonological-boundaries' AND status='unavailable';")));
    }

    [Fact]
    public void KeepsPunctuationAsAnOccurrenceWithoutInventingAWordform()
    {
        var projection = BuildProjection(ParsimonyEvidenceScopeKind.ProjectApproved);
        var punctuation = Assert.Single(projection.Occurrences, item => item.WordformGuid is null);
        Assert.Equal(SeededProject.PunctuationForm, punctuation.Text);
        Assert.Empty(punctuation.Forms);
        var path = Path.Combine(_root, "punctuation.sqlite");

        EvidenceWriter.Write(path, projection, "{}", "sha256:" + new string('5', 64),
            "sha256:" + new string('6', 64));

        using var connection = Open(path);
        Assert.Equal(1L, Convert.ToInt64(Scalar(connection,
            $"SELECT COUNT(*) FROM occurrences WHERE text='.' AND wordform_guid IS NULL AND segment_guid='{punctuation.SegmentGuid:D}';")));
        Assert.Equal(0L, Convert.ToInt64(Scalar(connection,
            $"SELECT COUNT(*) FROM occurrence_forms WHERE text_guid='{punctuation.TextGuid:D}' AND segment_guid='{punctuation.SegmentGuid:D}' AND token_ordinal={punctuation.Ordinal};")));
    }

    [Fact]
    public void CapturesNotebookRevisionAndReadableValueIntoRepeatableEvidence()
    {
        var recordGuid = Guid.NewGuid();
        var projectId = CanonicalId.FromGuid(_cache.LangProject.Guid).Value;
        var revisionId = CanonicalId.FromGuid(recordGuid).Value;
        var judgment = new HumanJudgment(projectId, CanonicalId.FromGuid(Guid.NewGuid()).Value, revisionId, [],
            new DispositionJudgment(new ProjectJudgmentSubject(projectId), "P-test", ParsimonyDispositionKind.Keep,
                "sha256:" + new string('7', 64), "contract-v1", "sample subject", "Sample measure"),
            "a captured reason");
        var readable = HumanJudgmentCodec.Format(judgment);
        var fieldId = NotebookJudgmentFixture.InitializeReservedField(_cache);
        NotebookJudgmentFixture.AddRecord(_cache, recordGuid, fieldId,
            TsStringUtils.MakeString(readable, _cache.DefaultAnalWs));

        var projection = BuildProjection(ParsimonyEvidenceScopeKind.ProjectApproved);
        var path = Path.Combine(_root, "judgments.sqlite");
        var rebuild = Path.Combine(_root, "judgments-rebuild.sqlite");
        EvidenceWriter.Write(path, projection, "{}", "sha256:" + new string('8', 64),
            "sha256:" + new string('9', 64));
        EvidenceWriter.Write(rebuild, BuildProjection(ParsimonyEvidenceScopeKind.ProjectApproved), "{}",
            "sha256:" + new string('8', 64), "sha256:" + new string('9', 64));

        using var connection = Open(path);
        Assert.Equal(projectId, Scalar(connection,
            "SELECT source_project_id FROM human_judgment_metadata WHERE singleton=1;"));
        Assert.Equal(projection.HumanJudgments.Digest, Scalar(connection,
            "SELECT projection_digest FROM human_judgment_metadata WHERE singleton=1;"));
        Assert.Equal(readable, Scalar(connection,
            $"SELECT physical_value FROM human_judgment_revisions WHERE record_guid='{revisionId}';"));
        Assert.Equal(HumanJudgmentCodec.LogicalDigest(judgment), Scalar(connection,
            $"SELECT content_digest FROM human_judgment_revisions WHERE record_guid='{revisionId}';"));
        Assert.Equal("keep", Scalar(connection,
            $"SELECT disposition FROM human_judgment_subjects WHERE revision_id='{revisionId}';"));
        using var rebuilt = Open(rebuild);
        Assert.Equal(Scalar(connection, "SELECT projection_digest FROM human_judgment_metadata;"),
            Scalar(rebuilt, "SELECT projection_digest FROM human_judgment_metadata;"));
    }

    private ParsimonyEvidenceProjection BuildProjection(ParsimonyEvidenceScopeKind kind) =>
        EvidenceProjectionBuilder.Build(_cache, TextProjection(), new ParsimonyScopeBinding(kind, null),
            CancellationToken.None);

    private SIL.Motif.Host.Texts.TextWordsProjection TextProjection() =>
        TextWordsProjectionBuilder.Build(_cache, CancellationToken.None);

    private static (Guid OutsideWordform, Guid DisapprovedAnalysis, Guid UnknownAnalysis, Guid VariantEntry,
        Guid VariantWordform, Guid VariantForm, Guid InflType) SeedEvidence(
        LcmCache cache, SeededProject seed, SeededText text)
    {
        var services = cache.ServiceLocator;
        var entry = services.GetInstance<ILexEntryRepository>().GetObject(seed.FirstEntryId);
        var textWordform = services.GetInstance<IWfiWordformRepository>().GetObject(text.AnalysedWordformId);
        IWfiAnalysis disapproved = null!;
        IWfiAnalysis unknown = null!;
        IWfiWordform outside = null!;
        IWfiWordform variantWordform = null!;
        ILexEntry variant = null!;
        ILexEntryInflType inflType = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            textWordform.Form.set_String(cache.DefaultAnalWs, TsStringUtils.MakeString("café", cache.DefaultAnalWs));
            textWordform.Form.set_String(cache.DefaultVernWs, TsStringUtils.MakeString("café-vern", cache.DefaultVernWs));
            var secondApproved = services.GetInstance<IWfiAnalysisFactory>().Create();
            textWordform.AnalysesOC.Add(secondApproved);
            AddBundle(services, secondApproved, entry.LexemeFormOA, entry.MorphoSyntaxAnalysesOC.First(),
                entry.SensesOS[0], null);
            cache.LangProject.DefaultUserAgent.SetEvaluation(secondApproved, Opinions.approves);

            outside = services.GetInstance<IWfiWordformFactory>().Create(
                TsStringUtils.MakeString("outside-text", cache.DefaultVernWs));
            disapproved = services.GetInstance<IWfiAnalysisFactory>().Create();
            outside.AnalysesOC.Add(disapproved);
            AddBundle(services, disapproved, entry.LexemeFormOA, entry.MorphoSyntaxAnalysesOC.First(),
                entry.SensesOS[0], null);
            cache.LangProject.DefaultUserAgent.SetEvaluation(disapproved, Opinions.disapproves);
            unknown = services.GetInstance<IWfiAnalysisFactory>().Create();
            outside.AnalysesOC.Add(unknown);
            var nullBundle = services.GetInstance<IWfiMorphBundleFactory>().Create();
            unknown.MorphBundlesOS.Add(nullBundle);

            var lexDb = cache.LangProject.LexDbOA;
            if (lexDb.VariantEntryTypesOA is null)
                lexDb.VariantEntryTypesOA = services.GetInstance<ICmPossibilityListFactory>().Create();
            inflType = services.GetInstance<ILexEntryInflTypeFactory>().Create();
            lexDb.VariantEntryTypesOA.PossibilitiesOS.Add(inflType);
            inflType.Name.set_String(cache.DefaultAnalWs, "Inflectional variant");
            var variantReference = entry.CreateVariantEntryAndBackRef(inflType,
                TsStringUtils.MakeString("variant-form", cache.DefaultVernWs));
            variant = variantReference.OwnerOfClass<ILexEntry>();
            variantWordform = services.GetInstance<IWfiWordformFactory>().Create(
                TsStringUtils.MakeString("variant-word", cache.DefaultVernWs));
            var variantAnalysis = services.GetInstance<IWfiAnalysisFactory>().Create();
            variantWordform.AnalysesOC.Add(variantAnalysis);
            AddBundle(services, variantAnalysis, variant.LexemeFormOA, entry.MorphoSyntaxAnalysesOC.First(),
                entry.SensesOS[0], inflType);
            cache.LangProject.DefaultUserAgent.SetEvaluation(variantAnalysis, Opinions.approves);
        });
        return (outside.Guid, disapproved.Guid, unknown.Guid, variant.Guid, variantWordform.Guid,
            variant.LexemeFormOA!.Guid, inflType.Guid);
    }

    private static void AddBundle(ILcmServiceLocator services, IWfiAnalysis analysis, IMoForm? form,
        IMoMorphSynAnalysis? msa, ILexSense? sense, ILexEntryInflType? inflType)
    {
        var bundle = services.GetInstance<IWfiMorphBundleFactory>().Create();
        analysis.MorphBundlesOS.Add(bundle);
        bundle.MorphRA = form;
        bundle.MsaRA = msa;
        bundle.SenseRA = sense;
        bundle.InflTypeRA = inflType;
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(path),
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static object Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar()!;
    }
}
