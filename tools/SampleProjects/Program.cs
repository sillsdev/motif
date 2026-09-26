using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.LcmUtils;

namespace SIL.Motif.SampleProjects;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private static int Main(string[] args)
    {
        if (args.Length != 3 || args[0] != "build")
        {
            Console.Error.WriteLine("Usage: SIL.Motif.SampleProjects build <sample.json> <output-root>");
            return 2;
        }

        try
        {
            var spec = JsonSerializer.Deserialize<SampleSpec>(File.ReadAllText(args[1]), JsonOptions)
                ?? throw new InvalidDataException("The sample specification is empty.");
            var result = SampleBuilder.Build(spec, Path.GetFullPath(args[2]));
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}

internal sealed record SampleSpec(
    string Id,
    string Title,
    LanguageSpec Language,
    string[] Phonemes,
    PartOfSpeechSpec[] PartsOfSpeech,
    StemSpec[] Stems,
    TextSpec[] Texts);

internal sealed record LanguageSpec(string Name, string Tag);
internal sealed record PartOfSpeechSpec(string Id, string Name);
internal sealed record StemSpec(string Id, string Form, string PartOfSpeech, string Gloss);
internal sealed record TextSpec(string Id, string Title, string[] Sentences);
internal sealed record BuildResult(string ProjectPath, string BackupPath, BuiltText[] Texts);
internal sealed record BuiltText(string Id, string Guid);

internal static class SampleBuilder
{
    private const string BootstrapVernacularTag = "fr";
    private const string ParserParametersXml =
        "<ParserParameters><HC><NoDefaultCompounding>true</NoDefaultCompounding><Strata /></HC></ParserParameters>";

    public static BuildResult Build(SampleSpec spec, string outputRoot)
    {
        Validate(spec);
        Directory.CreateDirectory(outputRoot);
        var projectName = ProjectName(spec.Id);
        var projectFolder = Path.Combine(outputRoot, projectName);
        var templatesFolder = Path.Combine(outputRoot, "Templates");
        Directory.CreateDirectory(projectFolder);
        Directory.CreateDirectory(templatesFolder);
        FwDataProjectLoader.Init();

        var fwDataPath = Path.Combine(projectFolder, projectName + ".fwdata");
        var progress = new LcmThreadedProgress();
        using (var cache = LcmCache.CreateCacheWithNewBlankLangProj(
                   new SampleProjectId(fwDataPath), "en", BootstrapVernacularTag, "en",
                   new HeadlessLcmUi(progress.SynchronizeInvoke),
                   new LcmDirectories(outputRoot, templatesFolder), new LcmSettings()))
        {
            Seed(cache, spec);
            cache.ServiceLocator.WritingSystemManager.Save();
            new FwDataProjectLoader().Save(cache);
        }

        var backupPath = Path.Combine(outputRoot, spec.Id + "-fixed.fwbackup");
        WriteBackup(fwDataPath, projectFolder, backupPath);
        return new BuildResult(fwDataPath, backupPath,
            spec.Texts.Select(text => new BuiltText(text.Id, Ids.Create(spec.Id, "text/" + text.Id).ToString("D"))).ToArray());
    }

    private static void Validate(SampleSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Id) || string.IsNullOrWhiteSpace(spec.Language.Tag))
            throw new InvalidDataException("Sample id and vernacular writing-system tag are required.");
        if (spec.Phonemes.Length == 0 || spec.PartsOfSpeech.Length == 0)
            throw new InvalidDataException("At least one phoneme and part of speech are required.");
        if (spec.Stems.Length == 0 || spec.Texts.Length == 0)
            throw new InvalidDataException("At least one stem and Text are required.");
        var declaredCharacters = spec.Phonemes
            .SelectMany(phoneme => phoneme.Normalize(NormalizationForm.FormD).EnumerateRunes())
            .Select(rune => rune.ToString())
            .Append("+")
            .ToHashSet(StringComparer.Ordinal);
        var partIds = spec.PartsOfSpeech.Select(part => part.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var stem in spec.Stems)
        {
            if (!partIds.Contains(stem.PartOfSpeech))
                throw new InvalidDataException($"Stem '{stem.Id}' names an unknown part of speech.");
            if (string.IsNullOrWhiteSpace(stem.Form) || string.IsNullOrWhiteSpace(stem.Gloss))
                throw new InvalidDataException($"Stem '{stem.Id}' needs a form and gloss.");
            ValidateVernacularCharacters(stem.Form, $"Stem '{stem.Id}' form '{stem.Form}'", declaredCharacters);
        }
        foreach (var text in spec.Texts)
        {
            if (text.Sentences.Length == 0 || text.Sentences.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException($"Text '{text.Id}' needs at least one sentence.");
            var unknownWords = text.Sentences.SelectMany(sentence => sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Where(word => !spec.Stems.Any(stem => stem.Form == word)).Distinct(StringComparer.Ordinal).ToArray();
            if (unknownWords.Length > 0)
                throw new InvalidDataException($"Text '{text.Id}' uses unknown stems: {string.Join(", ", unknownWords)}.");
            foreach (var word in text.Sentences.SelectMany(sentence =>
                         sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
                ValidateVernacularCharacters(word, $"Text '{text.Id}' word '{word}'", declaredCharacters);
        }
    }

    private static void ValidateVernacularCharacters(
        string form,
        string description,
        HashSet<string> declaredCharacters)
    {
        foreach (var rune in form.Normalize(NormalizationForm.FormD).EnumerateRunes())
        {
            if (!declaredCharacters.Contains(rune.ToString()))
                throw new InvalidDataException($"{description} uses undeclared character '{rune}'.");
        }
    }

    private static void Seed(LcmCache cache, SampleSpec spec)
    {
        var services = cache.ServiceLocator;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            SetVernacularWritingSystem(cache, spec.Language.Tag);
            cache.LangProject.MorphologicalDataOA.ParserParameters = ParserParametersXml;
            var positions = new Dictionary<string, IPartOfSpeech>(StringComparer.Ordinal);
            foreach (var part in spec.PartsOfSpeech)
            {
                var possibilityList = cache.LangProject.PartsOfSpeechOA;
                var position = services.GetInstance<IPartOfSpeechFactory>().Create(
                    Ids.Create(spec.Id, "part-of-speech/" + part.Id), possibilityList);
                position.Name.set_String(cache.DefaultAnalWs, part.Name);
                positions.Add(part.Id, position);
            }

            foreach (var stem in spec.Stems)
                AddStem(cache, spec.Id, stem, positions[stem.PartOfSpeech]);
            AddPhonemes(cache, spec);
            AddTexts(cache, spec);
        });
    }

    private static void SetVernacularWritingSystem(LcmCache cache, string tag)
    {
        cache.ServiceLocator.WritingSystemManager.GetOrSet(tag, out var vernacular);
        var writingSystems = cache.ServiceLocator.WritingSystems;
        foreach (var existing in writingSystems.VernacularWritingSystems
                     .Where(system => system.LanguageTag != tag).ToArray())
            writingSystems.VernacularWritingSystems.Remove(existing);
        foreach (var existing in writingSystems.CurrentVernacularWritingSystems
                     .Where(system => system.LanguageTag != tag).ToArray())
            writingSystems.CurrentVernacularWritingSystems.Remove(existing);
        if (!writingSystems.VernacularWritingSystems.Contains(vernacular))
            writingSystems.VernacularWritingSystems.Add(vernacular);
        writingSystems.CurrentVernacularWritingSystems.Remove(vernacular);
        writingSystems.CurrentVernacularWritingSystems.Insert(0, vernacular);
    }

    private static void AddStem(LcmCache cache, string sampleId, StemSpec stem, IPartOfSpeech partOfSpeech)
    {
        var services = cache.ServiceLocator;
        var entry = services.GetInstance<ILexEntryFactory>().Create(
            Ids.Create(sampleId, "stem/" + stem.Id + "/entry"), cache.LangProject.LexDbOA);
        var form = services.GetInstance<IMoStemAllomorphFactory>().Create(
            Ids.Create(sampleId, "stem/" + stem.Id + "/form"));
        entry.LexemeFormOA = form;
        form.MorphTypeRA = services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem);
        form.Form.set_String(cache.DefaultVernWs, stem.Form);

        var msa = services.GetInstance<IMoStemMsaFactory>().Create(
            Ids.Create(sampleId, "stem/" + stem.Id + "/msa"));
        entry.MorphoSyntaxAnalysesOC.Add(msa);
        msa.PartOfSpeechRA = partOfSpeech;

        var sense = services.GetInstance<ILexSenseFactory>().Create(
            Ids.Create(sampleId, "stem/" + stem.Id + "/sense"));
        entry.SensesOS.Add(sense);
        sense.Gloss.set_String(cache.DefaultAnalWs, stem.Gloss);
        sense.MorphoSyntaxAnalysisRA = msa;
    }

    private static void AddPhonemes(LcmCache cache, SampleSpec spec)
    {
        var services = cache.ServiceLocator;
        var phonemeSets = cache.LangProject.PhonologicalDataOA.PhonemeSetsOS;
        if (phonemeSets.Count == 0)
            phonemeSets.Add(services.GetInstance<IPhPhonemeSetFactory>().Create(
                Ids.Create(spec.Id, "phonology/phoneme-set")));
        var phonemeSet = phonemeSets[0];
        foreach (var phonemeText in spec.Phonemes)
        {
            var phoneme = services.GetInstance<IPhPhonemeFactory>().Create(
                Ids.Create(spec.Id, "phoneme/" + phonemeText));
            phonemeSet.PhonemesOC.Add(phoneme);
            phoneme.Name.set_String(cache.DefaultVernWs, phonemeText);
            var code = services.GetInstance<IPhCodeFactory>().Create(
                Ids.Create(spec.Id, "phoneme/" + phonemeText + "/code"));
            phoneme.CodesOS.Add(code);
            code.Representation.set_String(cache.DefaultVernWs, phonemeText);
        }
        var boundary = services.GetInstance<IPhBdryMarkerFactory>().Create(
            Ids.Create(spec.Id, "phonology/morpheme-boundary"));
        phonemeSet.BoundaryMarkersOC.Add(boundary);
        boundary.Name.set_String(cache.DefaultVernWs, "+");
        var boundaryCode = services.GetInstance<IPhCodeFactory>().Create(
            Ids.Create(spec.Id, "phonology/morpheme-boundary/code"));
        boundary.CodesOS.Add(boundaryCode);
        boundaryCode.Representation.set_String(cache.DefaultVernWs, "+");
    }

    private static void AddTexts(LcmCache cache, SampleSpec spec)
    {
        var services = cache.ServiceLocator;
        var wordformFactory = services.GetInstance<IWfiWordformFactory>();
        var identifiedWordformFactory = (ILcmFactory<IWfiWordform>)wordformFactory;
        var wordforms = new Dictionary<string, IWfiWordform>(StringComparer.Ordinal);
        foreach (var source in spec.Texts)
        {
            var text = services.GetInstance<ITextFactory>().Create(
                cache, Ids.Create(spec.Id, "text/" + source.Id));
            text.Name.set_String(cache.DefaultAnalWs, source.Title);
            var contents = services.GetInstance<IStTextFactory>().Create(
                Ids.Create(spec.Id, "text/" + source.Id + "/contents"));
            text.ContentsOA = contents;
            foreach (var (sentence, index) in source.Sentences.Select((value, index) => (value, index)))
            {
                var paragraph = services.GetInstance<IStTxtParaFactory>().Create(
                    Ids.Create(spec.Id, $"text/{source.Id}/paragraph/{index}"));
                contents.ParagraphsOS.Add(paragraph);
                var segment = services.GetInstance<ISegmentFactory>().Create(
                    Ids.Create(spec.Id, $"text/{source.Id}/segment/{index}"));
                paragraph.SegmentsOS.Add(segment);
                paragraph.Contents = TsStringUtils.MakeString(sentence, cache.DefaultVernWs);
                foreach (var form in sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!wordforms.TryGetValue(form, out var wordform))
                    {
                        var objectKey = "wordform/" + form.Normalize(NormalizationForm.FormC);
                        wordform = identifiedWordformFactory.Create(Ids.Create(spec.Id, objectKey));
                        wordform.Form.set_String(cache.DefaultVernWs, form);
                        wordforms.Add(form, wordform);
                    }
                    segment.AnalysesRS.Add(wordform);
                }
            }
        }
    }

    private static void WriteBackup(string fwDataPath, string projectFolder, string backupPath)
    {
        using var archive = ZipFile.Open(backupPath, ZipArchiveMode.Create);
        archive.CreateEntryFromFile(fwDataPath, Path.GetFileName(fwDataPath), CompressionLevel.Optimal);
        foreach (var file in Directory.EnumerateFiles(projectFolder, "*", SearchOption.AllDirectories)
                     .Where(path => path.Contains(Path.DirectorySeparatorChar + "WritingSystemStore" + Path.DirectorySeparatorChar,
                         StringComparison.OrdinalIgnoreCase)))
        {
            var entryName = Path.GetRelativePath(projectFolder, file).Replace(Path.DirectorySeparatorChar, '/');
            archive.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
        }
    }

    private static string ProjectName(string id) =>
        string.Concat(id.Select(character => char.IsAsciiLetterOrDigit(character) ? character : '_'));
}

internal static class Ids
{
    private static readonly byte[] NamespaceBytes = Convert.FromHexString("b0931bb59a7b5ca48aad39366a7be803");

    public static Guid Create(string sampleId, string objectKey)
    {
        var name = Encoding.UTF8.GetBytes(sampleId + "/" + objectKey);
        var input = new byte[NamespaceBytes.Length + name.Length];
        NamespaceBytes.CopyTo(input, 0);
        name.CopyTo(input, NamespaceBytes.Length);
        var digest = SHA1.HashData(input);
        digest[6] = (byte)((digest[6] & 0x0f) | 0x50);
        digest[8] = (byte)((digest[8] & 0x3f) | 0x80);
        var hex = Convert.ToHexString(digest.AsSpan(0, 16)).ToLowerInvariant();
        var text = $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..32]}";
        return Guid.ParseExact(text, "D");
    }
}

internal sealed class SampleProjectId(string fwDataPath) : IProjectIdentifier
{
    public string Name { get; } = System.IO.Path.GetFileNameWithoutExtension(fwDataPath);
    public string UiName => Name;
    public string Path { get; set; } = System.IO.Path.GetFullPath(fwDataPath);
    public string Handle => Path;
    public string PipeHandle => Path;
    public string ProjectFolder => System.IO.Path.GetDirectoryName(Path)!;
    public BackendProviderType Type => BackendProviderType.kXML;
}
