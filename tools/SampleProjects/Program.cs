using System.IO.Compression;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;

namespace SIL.Motif.SampleProjects;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
    };

    private static int Main(string[] args)
    {
        if (args.Length < 3 || args[0] != "build")
        {
            Console.Error.WriteLine("Usage: SIL.Motif.SampleProjects build <sample.json> <output-root> [--bugs <bugs.json> --bug <id> ...] [--check]");
            return 2;
        }

        try
        {
            string? bugsPath = null;
            var bugIds = new List<string>();
            var check = false;
            for (var index = 3; index < args.Length; index++)
            {
                if (args[index] == "--bugs" && index + 1 < args.Length)
                    bugsPath = args[++index];
                else if (args[index] == "--bug" && index + 1 < args.Length)
                    bugIds.Add(args[++index]);
                else if (args[index] == "--check")
                    check = true;
                else
                    throw new InvalidDataException($"Unknown or incomplete builder option '{args[index]}'.");
            }

            if (check && bugsPath is null)
            {
                var siblingBugsPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!, "bugs.json");
                if (File.Exists(siblingBugsPath)) bugsPath = siblingBugsPath;
            }
            if (bugIds.Count > 0 && bugsPath is null)
                throw new InvalidDataException("A bug list is required when applying bug patches.");

            var spec = JsonSerializer.Deserialize<SampleSpec>(File.ReadAllText(args[1]), JsonOptions)
                ?? throw new InvalidDataException("The sample specification is empty.");
            var bugs = bugsPath is null
                ? []
                : JsonSerializer.Deserialize<BugSpec[]>(File.ReadAllText(bugsPath), JsonOptions)
                  ?? throw new InvalidDataException("The bug list is empty.");
            var outputRoot = Path.GetFullPath(args[2]);
            var result = check
                ? CheckVariants(spec, outputRoot, bugs)
                : SampleBuilder.Build(spec, outputRoot, bugs, bugIds);
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static BuildResult CheckVariants(SampleSpec spec, string outputRoot, IReadOnlyList<BugSpec> bugs)
    {
        var checkRoot = Path.Combine(outputRoot, "check");
        var fixedBuild = CheckVariant(spec, checkRoot, "fixed", bugs, []);
        foreach (var bug in bugs)
            CheckVariant(spec, checkRoot, bug.Id, bugs, [bug.Id]);
        return fixedBuild;
    }

    private static BuildResult CheckVariant(
        SampleSpec spec, string checkRoot, string variant, IReadOnlyList<BugSpec> bugs, IReadOnlyList<string> bugIds)
    {
        var variantRoot = Path.Combine(checkRoot, variant);
        var build = SampleBuilder.Build(spec, variantRoot, bugs, bugIds);
        var capture = BaselineCaptureCommand.Capture(
            new BaselineCaptureRequest(build.ProjectPath), Path.Combine(variantRoot, "motif-root"));
        if (!capture.Succeeded)
            throw new InvalidOperationException($"[{variant}] Baseline capture failed: {capture.Refusal?.Message}");

        using var grammarTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var grammarTask = Task.Run(() => GrammarCheckQuery.Query(
            new GrammarCheckRequest(build.ProjectPath), grammarTimeout.Token));
        var words = spec.Texts.SelectMany(text => text.Sentences)
            .SelectMany(sentence => sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var wordsTask = Task.Run(() =>
        {
            using var invoker = new PanGlossInvoker();
            return invoker.RunAsync(
                    new PanGlossRequest.Batch(build.ProjectPath, words, TimeSpan.FromSeconds(3)),
                    "sample-check:" + variant, CancellationToken.None, TimeSpan.FromSeconds(20))
                .GetAwaiter().GetResult();
        });
        Task.WaitAll(grammarTask, wordsTask);
        var grammarCheck = grammarTask.Result;
        if (!grammarCheck.Succeeded)
            throw new InvalidOperationException($"[{variant}] Grammar check failed: {grammarCheck.Refusal?.Message}");

        var findings = grammarCheck.Value!.Findings;
        var errors = findings.Count(finding => finding.Severity == GrammarDiagnosticLevel.Error);
        var warnings = findings.Count(finding => finding.Severity == GrammarDiagnosticLevel.Warning);
        var information = findings.Count(finding => finding.Severity == GrammarDiagnosticLevel.Information);
        Console.Error.WriteLine(
            $"[{variant}] grammar-health errors={errors} warnings={warnings} info={information}");
        foreach (var finding in findings)
            Console.Error.WriteLine(
                $"[{variant}] finding code={finding.Code} level={finding.Severity.ToWireValue()} description={finding.Description}");

        var outcome = wordsTask.Result;
        if (outcome is not PanGlossOutcome.Completed completed)
            throw new InvalidOperationException($"[{variant}] Word parsing failed: {outcome.Message}");
        var results = BatchTsvParser.Parse(completed.Output);
        if (results.Count != words.Length)
            throw new InvalidDataException(
                $"[{variant}] PanGloss returned {results.Count} word outcomes for {words.Length} sample words.");
        foreach (var result in results)
            Console.Error.WriteLine($"[{variant}] word {result.Word} outcome={result.Outcome.ToStoredOutcome()}");
        return build;
    }
}

internal sealed record SampleSpec
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string[] Teaches { get; init; } = [];
    public string Summary { get; init; } = "";
    public string Disclaimer { get; init; } = "";
    public string Description { get; init; } = "";
    public ResearchSpec? Research { get; init; }
    public KnownGrammarWarningSpec[] KnownGrammarWarnings { get; init; } = [];
    public LanguageSpec Language { get; init; } = new("", "");
    public string[] Phonemes { get; init; } = [];
    public NaturalClassSpec[] NaturalClasses { get; init; } = [];
    public FeatureDefinitionSpec[] FeatureDefinitions { get; init; } = [];
    public InflectionClassSpec[] InflectionClasses { get; init; } = [];
    public EnvironmentSpec[] Environments { get; init; } = [];
    public PartOfSpeechSpec[] PartsOfSpeech { get; init; } = [];
    public StemSpec[] Stems { get; init; } = [];
    public AffixSpec[] Affixes { get; init; } = [];
    public AffixSlotSpec[] AffixSlots { get; init; } = [];
    public AffixTemplateSpec[] AffixTemplates { get; init; } = [];
    public PhonologicalRuleSpec[] PhonologicalRules { get; init; } = [];
    public TextSpec[] Texts { get; init; } = [];
}

internal sealed record LanguageSpec(string Name, string Tag);
internal sealed record ResearchSpec(string Document, ResearchSectionSpec[] Sections);
internal sealed record ResearchSectionSpec(string Part, string Section);
internal sealed record KnownGrammarWarningSpec(string Code, string Reason);
internal sealed record NaturalClassSpec(string Id, string Name, string Abbreviation, string[] Phonemes);
internal sealed record FeatureDefinitionSpec(string Id, string Name, string Abbreviation, FeatureValueSpec[] Values);
internal sealed record FeatureValueSpec(string Id, string Name, string Abbreviation);
internal sealed record FeatureAssignmentSpec(string FeatureId, string ValueId);
internal sealed record InflectionClassSpec(string Id, string Name, string PartOfSpeech);
internal sealed record EnvironmentSpec(string Id, string Name, string Representation);
internal sealed record PartOfSpeechSpec(string Id, string Name);
internal sealed record StemSpec(
    string Id, string Form, string PartOfSpeech, string Gloss, string? InflectionClass = null,
    FeatureAssignmentSpec[]? Features = null);
internal sealed record AffixSpec(
    string Id, string PartOfSpeech, string[] Slots, string Gloss, AllomorphSpec[] Allomorphs,
    FeatureAssignmentSpec[]? Features = null);
internal sealed record AffixPatchSpec(
    string Id, string PartOfSpeech, string[] Slots, string Gloss,
    FeatureAssignmentSpec[]? Features = null);
internal sealed record AllomorphSpec(
    string Id, string Form, string? Environment, string MorphType = "suffix",
    string? PositionEnvironment = null, string[]? InflectionClasses = null,
    FeatureAssignmentSpec[]? RequiredFeatures = null);
internal sealed record AffixSlotSpec(string Id, string Name, string PartOfSpeech, bool Optional);
internal sealed record AffixTemplateSpec(
    string Id, string Name, string PartOfSpeech, string[] PrefixSlots, string[] SuffixSlots, bool Final);
internal sealed record PhonologicalRuleSpec(string Id, string Name, string Input, string Output, string Environment);
internal sealed record TextSpec(string Id, string Title, string[] Sentences);
internal sealed record BugSpec(
    string Id, string Title, string Disclaimer, BugSymptom? Symptom, string[] Fix, PatchOperation[] Patch);
internal sealed record BugSymptom(string Kind, string[] Words, string Reason);
internal sealed record PatchOperation(
    string Op,
    string? AllomorphId = null,
    string? Form = null,
    string? EnvironmentId = null,
    string? AffixId = null,
    string? StemId = null,
    string? TemplateId = null,
    string? FirstSlotId = null,
    string? SecondSlotId = null,
    string? SlotId = null,
    bool? Optional = null,
    int? Count = null,
    string[]? Slots = null,
    string? Side = null,
    AffixPatchSpec? Affix = null,
    AllomorphSpec? Allomorph = null);
internal sealed record BuildResult(string ProjectPath, string BackupPath, BuiltText[] Texts, string[] AppliedBugs);
internal sealed record BuiltText(string Id, string Guid);

internal static class SampleBuilder
{
    private const string BootstrapVernacularTag = "fr";
    private const string ParserParametersXml =
        "<ParserParameters><HC><NoDefaultCompounding>true</NoDefaultCompounding><Strata /></HC></ParserParameters>";

    private sealed record FeatureDefinitionHandle(
        IFsClosedFeature Feature, IReadOnlyDictionary<string, IFsSymFeatVal> Values);

    public static BuildResult Build(
        SampleSpec source, string outputRoot, IReadOnlyList<BugSpec> bugs, IReadOnlyList<string> bugIds)
    {
        var spec = ApplyBugs(source, bugs, bugIds);
        Validate(spec, bugs);
        Directory.CreateDirectory(outputRoot);
        var isBroken = bugIds.Count > 0;
        var projectName = ProjectName(spec.Id) + (isBroken ? "Broken" : "");
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

        var backupKind = isBroken ? "broken" : "fixed";
        var backupPath = Path.Combine(outputRoot, spec.Id + "-" + backupKind + ".fwbackup");
        WriteBackup(fwDataPath, projectFolder, backupPath);
        return new BuildResult(fwDataPath, backupPath,
            spec.Texts.Select(text => new BuiltText(text.Id, Ids.Create(spec.Id, "text/" + text.Id).ToString("D"))).ToArray(),
            [.. bugIds]);
    }

    private static SampleSpec ApplyBugs(
        SampleSpec source, IReadOnlyList<BugSpec> bugs, IReadOnlyList<string> bugIds)
    {
        var spec = source;
        foreach (var bugId in bugIds)
        {
            var bug = bugs.SingleOrDefault(candidate => candidate.Id == bugId)
                ?? throw new InvalidDataException($"Bug '{bugId}' is not declared in the bug list.");
            foreach (var patch in bug.Patch)
                spec = ApplyPatch(spec, patch);
        }
        return spec;
    }

    private static SampleSpec ApplyPatch(SampleSpec spec, PatchOperation patch) => patch.Op switch
    {
        "setEnvironment" => SetEnvironment(spec, patch),
        "setAllomorphForm" => SetAllomorphForm(spec, patch),
        "removeStem" => spec with
        {
            Stems = spec.Stems.Where(stem => stem.Id != Required(patch.StemId, "stemId")).ToArray(),
        },
        "swapSlots" => SwapSlots(spec, patch),
        "setSlotOptional" => spec with
        {
            AffixSlots = spec.AffixSlots.Select(slot => slot.Id == Required(patch.SlotId, "slotId")
                ? slot with { Optional = patch.Optional ?? throw new InvalidDataException("setSlotOptional needs optional.") }
                : slot).ToArray(),
        },
        "setAffixSlots" => SetAffixSlots(spec, patch),
        "duplicateOptionalSlot" => DuplicateOptionalSlot(spec, patch),
        "removeAllomorph" => RemoveAllomorph(spec, patch),
        "addAffix" => AddAffix(spec, patch),
        "addAllomorph" => AddAllomorph(spec, patch),
        "duplicateTemplate" => DuplicateTemplate(spec, patch),
        _ => throw new InvalidDataException($"Unknown patch operation '{patch.Op}'."),
    };

    private static SampleSpec SwapSlots(SampleSpec spec, PatchOperation patch)
    {
        var templateId = Required(patch.TemplateId, "templateId");
        var first = Required(patch.FirstSlotId, "firstSlotId");
        var second = Required(patch.SecondSlotId, "secondSlotId");
        var side = SlotSide(patch, "swapSlots");
        return spec with
        {
            AffixTemplates = spec.AffixTemplates.Select(template =>
            {
                if (template.Id != templateId) return template;
                var slots = side == "prefix" ? template.PrefixSlots ?? [] : template.SuffixSlots ?? [];
                var firstIndex = Array.IndexOf(slots, first);
                var secondIndex = Array.IndexOf(slots, second);
                if (firstIndex < 0 || secondIndex < 0)
                    throw new InvalidDataException($"Template '{templateId}' does not contain both {side} slots.");
                var swapped = slots.ToArray();
                (swapped[firstIndex], swapped[secondIndex]) = (swapped[secondIndex], swapped[firstIndex]);
                return side == "prefix"
                    ? template with { PrefixSlots = swapped }
                    : template with { SuffixSlots = swapped };
            }).ToArray(),
        };
    }

    private static string SlotSide(PatchOperation patch, string operation)
    {
        var side = patch.Side ?? "suffix";
        if (side is not ("prefix" or "suffix"))
            throw new InvalidDataException($"{operation} side must be 'prefix' or 'suffix'.");
        return side;
    }

    private static SampleSpec SetEnvironment(SampleSpec spec, PatchOperation patch)
    {
        var allomorphId = Required(patch.AllomorphId, "allomorphId");
        var environmentId = Required(patch.EnvironmentId, "environmentId");
        var matches = spec.Affixes.SelectMany(affix => affix.Allomorphs)
            .Count(allomorph => allomorph.Id == allomorphId);
        if (matches != 1) throw new InvalidDataException($"Allomorph '{allomorphId}' does not exist exactly once.");
        return spec with
        {
            Affixes = spec.Affixes.Select(affix => affix with
            {
                Allomorphs = affix.Allomorphs.Select(allomorph => allomorph.Id == allomorphId
                    ? allomorph with { Environment = environmentId }
                    : allomorph).ToArray(),
            }).ToArray(),
        };
    }

    private static SampleSpec SetAllomorphForm(SampleSpec spec, PatchOperation patch)
    {
        var allomorphId = Required(patch.AllomorphId, "allomorphId");
        var form = Required(patch.Form, "form");
        var matches = spec.Affixes.SelectMany(affix => affix.Allomorphs)
            .Count(allomorph => allomorph.Id == allomorphId);
        if (matches != 1) throw new InvalidDataException($"Allomorph '{allomorphId}' does not exist exactly once.");
        return spec with
        {
            Affixes = spec.Affixes.Select(affix => affix with
            {
                Allomorphs = affix.Allomorphs.Select(allomorph => allomorph.Id == allomorphId
                    ? allomorph with { Form = form }
                    : allomorph).ToArray(),
            }).ToArray(),
        };
    }

    private static SampleSpec SetAffixSlots(SampleSpec spec, PatchOperation patch)
    {
        var affixId = Required(patch.AffixId, "affixId");
        var slots = patch.Slots ?? throw new InvalidDataException("setAffixSlots needs slots.");
        if (spec.Affixes.Count(affix => affix.Id == affixId) != 1)
            throw new InvalidDataException($"Affix '{affixId}' does not exist exactly once.");
        return spec with
        {
            Affixes = spec.Affixes.Select(affix => affix.Id == affixId
                ? affix with { Slots = slots }
                : affix).ToArray(),
        };
    }

    private static SampleSpec DuplicateOptionalSlot(SampleSpec spec, PatchOperation patch)
    {
        var slotId = Required(patch.SlotId, "slotId");
        var templateId = Required(patch.TemplateId, "templateId");
        var count = patch.Count ?? throw new InvalidDataException("duplicateOptionalSlot needs count.");
        if (count is < 1 or > 64) throw new InvalidDataException("duplicateOptionalSlot count must be from 1 to 64.");
        var side = SlotSide(patch, "duplicateOptionalSlot");
        var sourceSlot = spec.AffixSlots.SingleOrDefault(slot => slot.Id == slotId)
            ?? throw new InvalidDataException($"Slot '{slotId}' does not exist.");
        var copies = Enumerable.Range(1, count).Select(index => sourceSlot with
        {
            Id = $"{slotId}-copy-{index}",
            Name = sourceSlot.Name + " copy " + index,
            Optional = true,
        }).ToArray();
        var affixes = spec.Affixes.Select(affix => affix.Slots.Contains(slotId, StringComparer.Ordinal)
            ? affix with { Slots = [.. affix.Slots, .. copies.Select(copy => copy.Id)] }
            : affix).ToArray();
        var templates = spec.AffixTemplates.Select(template => template.Id == templateId
            ? side == "prefix"
                ? template with { PrefixSlots = [.. template.PrefixSlots ?? [], .. copies.Select(copy => copy.Id)] }
                : template with { SuffixSlots = [.. template.SuffixSlots ?? [], .. copies.Select(copy => copy.Id)] }
            : template).ToArray();
        return spec with
        {
            AffixSlots = [.. spec.AffixSlots, .. copies],
            Affixes = affixes,
            AffixTemplates = templates,
        };
    }

    private static SampleSpec RemoveAllomorph(SampleSpec spec, PatchOperation patch)
    {
        var allomorphId = Required(patch.AllomorphId, "allomorphId");
        if (spec.Affixes.SelectMany(affix => affix.Allomorphs)
                .Count(allomorph => allomorph.Id == allomorphId) != 1)
            throw new InvalidDataException($"Allomorph '{allomorphId}' does not exist exactly once.");
        return spec with
        {
            Affixes = spec.Affixes.Select(affix =>
            {
                if (!affix.Allomorphs.Any(allomorph => allomorph.Id == allomorphId)) return affix;
                var allomorphs = affix.Allomorphs
                    .Where(allomorph => allomorph.Id != allomorphId)
                    .ToArray();
                if (allomorphs.Length == 0)
                    throw new InvalidDataException($"Removing '{allomorphId}' would leave affix '{affix.Id}' without an allomorph.");
                return affix with { Allomorphs = allomorphs };
            }).ToArray(),
        };
    }

    private static SampleSpec AddAffix(SampleSpec spec, PatchOperation patch)
    {
        var source = patch.Affix ?? throw new InvalidDataException("addAffix needs an affix.");
        if (spec.Affixes.Any(affix => affix.Id == source.Id))
            throw new InvalidDataException($"Affix '{source.Id}' already exists.");
        var affix = new AffixSpec(source.Id, source.PartOfSpeech, source.Slots, source.Gloss, [], source.Features);
        return spec with { Affixes = [.. spec.Affixes, affix] };
    }

    private static SampleSpec AddAllomorph(SampleSpec spec, PatchOperation patch)
    {
        var affixId = Required(patch.AffixId, "affixId");
        var allomorph = patch.Allomorph ?? throw new InvalidDataException("addAllomorph needs an allomorph.");
        if (spec.Affixes.SelectMany(affix => affix.Allomorphs)
                .Any(existing => existing.Id == allomorph.Id))
            throw new InvalidDataException($"Allomorph '{allomorph.Id}' already exists.");
        if (spec.Affixes.Count(affix => affix.Id == affixId) != 1)
            throw new InvalidDataException($"Affix '{affixId}' does not exist exactly once.");
        return spec with
        {
            Affixes = spec.Affixes.Select(affix => affix.Id == affixId
                ? affix with { Allomorphs = [.. affix.Allomorphs, allomorph] }
                : affix).ToArray(),
        };
    }

    private static SampleSpec DuplicateTemplate(SampleSpec spec, PatchOperation patch)
    {
        var templateId = Required(patch.TemplateId, "templateId");
        var count = patch.Count ?? throw new InvalidDataException("duplicateTemplate needs count.");
        if (count is < 1 or > 32) throw new InvalidDataException("duplicateTemplate count must be from 1 to 32.");
        var source = spec.AffixTemplates.SingleOrDefault(template => template.Id == templateId)
            ?? throw new InvalidDataException($"Template '{templateId}' does not exist.");
        var copies = Enumerable.Range(1, count).Select(index => source with
        {
            Id = $"{templateId}-copy-{index}",
            Name = source.Name + " copy " + index,
        }).ToArray();
        if (copies.Any(copy => spec.AffixTemplates.Any(template => template.Id == copy.Id)))
            throw new InvalidDataException($"Template '{templateId}' already has a generated copy id.");
        return spec with { AffixTemplates = [.. spec.AffixTemplates, .. copies] };
    }

    private static string Required(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new InvalidDataException($"Patch operation needs {name}.") : value;

    private static void Validate(SampleSpec spec, IReadOnlyList<BugSpec> bugs)
    {
        if (string.IsNullOrWhiteSpace(spec.Id) || string.IsNullOrWhiteSpace(spec.Title) ||
            spec.Teaches.Length == 0 || string.IsNullOrWhiteSpace(spec.Summary) ||
            string.IsNullOrWhiteSpace(spec.Language.Tag) || string.IsNullOrWhiteSpace(spec.Disclaimer) ||
            string.IsNullOrWhiteSpace(spec.Description))
            throw new InvalidDataException("Sample id, title, description, disclaimer, and writing-system tag are required.");
        if (spec.Phonemes.Length == 0 || spec.PartsOfSpeech.Length == 0)
            throw new InvalidDataException("At least one phoneme and part of speech are required.");
        if (spec.Stems.Length == 0 || spec.Texts.Length == 0)
            throw new InvalidDataException("At least one stem and Text are required.");
        var declaredCharacters = spec.Phonemes
            .SelectMany(TextElements)
            .Append("+")
            .ToHashSet(StringComparer.Ordinal);
        var partIds = UniqueById(spec.PartsOfSpeech, part => part.Id, "part of speech")
            .Keys.ToHashSet(StringComparer.Ordinal);
        UniqueById(spec.Stems, stem => stem.Id, "stem");
        UniqueById(spec.Affixes, affix => affix.Id, "affix");
        UniqueById(spec.NaturalClasses, item => item.Id, "natural class");
        var featureDefinitions = UniqueById(spec.FeatureDefinitions, feature => feature.Id, "feature definition");
        foreach (var feature in featureDefinitions.Values)
            UniqueById(feature.Values, value => value.Id, $"value in feature '{feature.Id}'");
        var inflectionClasses = UniqueById(spec.InflectionClasses, item => item.Id, "inflection class");
        UniqueById(spec.Phonemes, phoneme => phoneme, "phoneme");
        UniqueById(spec.Texts, text => text.Id, "Text");
        UniqueById(spec.AffixTemplates, template => template.Id, "affix template");
        var slotById = UniqueById(spec.AffixSlots, slot => slot.Id, "affix slot");
        var environmentIds = UniqueById(spec.Environments, environment => environment.Id, "environment")
            .Keys.ToHashSet(StringComparer.Ordinal);
        foreach (var inflectionClass in inflectionClasses.Values)
        {
            if (!partIds.Contains(inflectionClass.PartOfSpeech) ||
                string.IsNullOrWhiteSpace(inflectionClass.Name))
                throw new InvalidDataException($"Inflection class '{inflectionClass.Id}' needs a name and known part of speech.");
        }
        foreach (var feature in featureDefinitions.Values)
        {
            if (string.IsNullOrWhiteSpace(feature.Name) || string.IsNullOrWhiteSpace(feature.Abbreviation) ||
                feature.Values.Length == 0)
                throw new InvalidDataException($"Feature '{feature.Id}' needs a name, abbreviation, and value.");
            foreach (var value in feature.Values)
                if (string.IsNullOrWhiteSpace(value.Name) || string.IsNullOrWhiteSpace(value.Abbreviation))
                    throw new InvalidDataException($"Value '{value.Id}' in feature '{feature.Id}' needs a name and abbreviation.");
        }
        foreach (var stem in spec.Stems)
        {
            if (!partIds.Contains(stem.PartOfSpeech))
                throw new InvalidDataException($"Stem '{stem.Id}' names an unknown part of speech.");
            if (string.IsNullOrWhiteSpace(stem.Form) || string.IsNullOrWhiteSpace(stem.Gloss))
                throw new InvalidDataException($"Stem '{stem.Id}' needs a form and gloss.");
            if (stem.InflectionClass is { } classId &&
                (!inflectionClasses.TryGetValue(classId, out var inflectionClass) ||
                 inflectionClass.PartOfSpeech != stem.PartOfSpeech))
                throw new InvalidDataException($"Stem '{stem.Id}' names an unknown or incompatible inflection class.");
            ValidateFeatureAssignments(stem.Features ?? [], featureDefinitions, $"Stem '{stem.Id}'");
            ValidateVernacularCharacters(stem.Form, $"Stem '{stem.Id}' form '{stem.Form}'", declaredCharacters);
        }
        foreach (var affix in spec.Affixes)
        {
            UniqueById(affix.Allomorphs, allomorph => allomorph.Id, $"allomorph in affix '{affix.Id}'");
            if (!partIds.Contains(affix.PartOfSpeech))
                throw new InvalidDataException($"Affix '{affix.Id}' names an unknown part of speech.");
            if (affix.Allomorphs.Length == 0 || affix.Slots.Length == 0)
                throw new InvalidDataException($"Affix '{affix.Id}' needs an allomorph and a slot.");
            foreach (var slotId in affix.Slots)
                if (!slotById.TryGetValue(slotId, out var slot) || slot.PartOfSpeech != affix.PartOfSpeech)
                    throw new InvalidDataException($"Affix '{affix.Id}' names an unknown or incompatible slot '{slotId}'.");
            foreach (var allomorph in affix.Allomorphs)
            {
                if (string.IsNullOrWhiteSpace(allomorph.Id))
                    throw new InvalidDataException($"Affix '{affix.Id}' has an allomorph without an id.");
                if (allomorph.MorphType is not ("suffix" or "prefix" or "infix"))
                    throw new InvalidDataException($"Allomorph '{allomorph.Id}' morphType must be suffix, prefix, or infix.");
                if (allomorph.MorphType == "infix" && allomorph.PositionEnvironment is null)
                    throw new InvalidDataException($"Infix allomorph '{allomorph.Id}' needs a position environment.");
                if (allomorph.PositionEnvironment is not null && !environmentIds.Contains(allomorph.PositionEnvironment))
                    throw new InvalidDataException($"Allomorph '{allomorph.Id}' names an unknown position environment.");
                foreach (var classId in allomorph.InflectionClasses ?? [])
                    if (!inflectionClasses.TryGetValue(classId, out var inflectionClass) ||
                        inflectionClass.PartOfSpeech != affix.PartOfSpeech)
                        throw new InvalidDataException($"Allomorph '{allomorph.Id}' names an unknown or incompatible inflection class.");
                ValidateFeatureAssignments(allomorph.RequiredFeatures ?? [], featureDefinitions,
                    $"Allomorph '{allomorph.Id}'");
                var allomorphCharacters = declaredCharacters.Append("-").ToHashSet(StringComparer.Ordinal);
                ValidateVernacularCharacters(allomorph.Form,
                    $"Affix '{affix.Id}' allomorph '{allomorph.Id}' form '{allomorph.Form}'", allomorphCharacters);
                if (allomorph.Environment is not null && !environmentIds.Contains(allomorph.Environment))
                    throw new InvalidDataException($"Allomorph '{allomorph.Id}' names an unknown environment.");
            }
            ValidateFeatureAssignments(affix.Features ?? [], featureDefinitions, $"Affix '{affix.Id}'");
        }
        foreach (var template in spec.AffixTemplates)
        {
            if (!partIds.Contains(template.PartOfSpeech))
                throw new InvalidDataException($"Template '{template.Id}' names an unknown part of speech.");
            foreach (var slotId in (template.PrefixSlots ?? []).Concat(template.SuffixSlots ?? []))
                if (!slotById.TryGetValue(slotId, out var slot) || slot.PartOfSpeech != template.PartOfSpeech)
                    throw new InvalidDataException($"Template '{template.Id}' names an unknown or incompatible slot '{slotId}'.");
        }
        foreach (var group in spec.AffixTemplates.GroupBy(template => template.PartOfSpeech, StringComparer.Ordinal))
            if (!group.Any(template => template.Final))
                throw new InvalidDataException($"Templates for part of speech '{group.Key}' need a final template.");
        foreach (var text in spec.Texts)
        {
            if (text.Sentences.Length == 0 || text.Sentences.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException($"Text '{text.Id}' needs at least one sentence.");
            foreach (var word in text.Sentences.SelectMany(sentence =>
                         sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
                ValidateVernacularCharacters(word, $"Text '{text.Id}' word '{word}'", declaredCharacters);
        }
        foreach (var rule in spec.PhonologicalRules)
            if (string.IsNullOrWhiteSpace(rule.Id) || string.IsNullOrWhiteSpace(rule.Name) ||
                string.IsNullOrWhiteSpace(rule.Input) || string.IsNullOrWhiteSpace(rule.Output) ||
                string.IsNullOrWhiteSpace(rule.Environment))
                throw new InvalidDataException("Phonological rules need an id, name, input, output, and environment.");
        foreach (var bug in bugs)
        {
            if (string.IsNullOrWhiteSpace(bug.Disclaimer))
                throw new InvalidDataException($"Bug '{bug.Id}' needs its synthetic-data disclaimer.");
        }
    }

    private static void ValidateFeatureAssignments(
        IEnumerable<FeatureAssignmentSpec> assignments,
        IReadOnlyDictionary<string, FeatureDefinitionSpec> features,
        string owner)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assignment in assignments)
        {
            if (!features.TryGetValue(assignment.FeatureId, out var feature) ||
                !feature.Values.Any(value => value.Id == assignment.ValueId))
                throw new InvalidDataException($"{owner} names an unknown feature value '{assignment.FeatureId}/{assignment.ValueId}'.");
            if (!seen.Add(assignment.FeatureId))
                throw new InvalidDataException($"{owner} assigns feature '{assignment.FeatureId}' more than once.");
        }
    }

    private static Dictionary<string, T> UniqueById<T>(
        IEnumerable<T> records, Func<T, string> idSelector, string description)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            var id = idSelector(record);
            if (string.IsNullOrWhiteSpace(id) || !result.TryAdd(id, record))
                throw new InvalidDataException($"The {description} id '{id}' is empty or duplicated.");
        }
        return result;
    }

    private static void ValidateVernacularCharacters(
        string form, string description, HashSet<string> declaredCharacters)
    {
        foreach (var character in TextElements(form))
            if (!declaredCharacters.Contains(character))
                throw new InvalidDataException($"{description} uses undeclared character '{character}'.");
    }

    private static IEnumerable<string> TextElements(string value)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(value.Normalize(NormalizationForm.FormC));
        while (enumerator.MoveNext())
            yield return enumerator.GetTextElement();
    }

    private static void Seed(LcmCache cache, SampleSpec spec)
    {
        var services = cache.ServiceLocator;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            SetVernacularWritingSystem(cache, spec.Language.Tag);
            cache.LangProject.Description.set_String(cache.DefaultAnalWs,
                string.IsNullOrWhiteSpace(spec.Description) ? spec.Disclaimer : spec.Description);
            cache.LangProject.MorphologicalDataOA.ParserParameters = ParserParametersXml;
            var positions = AddPartsOfSpeech(cache, spec);
            var inflectionClasses = AddInflectionClasses(cache, spec, positions);
            var features = AddFeatureDefinitions(cache, spec);
            AddStems(cache, spec, positions, inflectionClasses, features);
            var phonemes = AddPhonemes(cache, spec);
            AddNaturalClasses(cache, spec, phonemes);
            var environments = AddEnvironments(cache, spec);
            var slots = AddAffixSlots(cache, spec, positions);
            AddAffixTemplates(cache, spec, positions, slots);
            AddAffixes(cache, spec, positions, slots, environments, inflectionClasses, features);
            AddTexts(cache, spec);
        });
    }

    private static Dictionary<string, IPartOfSpeech> AddPartsOfSpeech(LcmCache cache, SampleSpec spec)
    {
        var positions = new Dictionary<string, IPartOfSpeech>(StringComparer.Ordinal);
        foreach (var part in spec.PartsOfSpeech)
        {
            var possibilityList = cache.LangProject.PartsOfSpeechOA;
            var position = cache.ServiceLocator.GetInstance<IPartOfSpeechFactory>().Create(
                Ids.Create(spec.Id, "part-of-speech/" + part.Id), possibilityList);
            position.Name.set_String(cache.DefaultAnalWs, part.Name);
            positions.Add(part.Id, position);
        }
        return positions;
    }

    private static Dictionary<string, IMoInflClass> AddInflectionClasses(
        LcmCache cache, SampleSpec spec, IReadOnlyDictionary<string, IPartOfSpeech> positions)
    {
        var result = new Dictionary<string, IMoInflClass>(StringComparer.Ordinal);
        foreach (var source in spec.InflectionClasses)
        {
            var inflectionClass = cache.ServiceLocator.GetInstance<IMoInflClassFactory>().Create(
                Ids.Create(spec.Id, "inflection-class/" + source.Id));
            var partOfSpeech = positions[source.PartOfSpeech];
            partOfSpeech.InflectionClassesOC.Add(inflectionClass);
            inflectionClass.Name.set_String(cache.DefaultAnalWs, source.Name);
            if (partOfSpeech.DefaultInflectionClassRA is null)
                partOfSpeech.DefaultInflectionClassRA = inflectionClass;
            result.Add(source.Id, inflectionClass);
        }
        return result;
    }

    private static Dictionary<string, FeatureDefinitionHandle> AddFeatureDefinitions(LcmCache cache, SampleSpec spec)
    {
        var result = new Dictionary<string, FeatureDefinitionHandle>(StringComparer.Ordinal);
        foreach (var source in spec.FeatureDefinitions)
        {
            var feature = cache.ServiceLocator.GetInstance<IFsClosedFeatureFactory>().Create(
                Ids.Create(spec.Id, "feature/" + source.Id));
            cache.LangProject.MsFeatureSystemOA.FeaturesOC.Add(feature);
            feature.Name.set_String(cache.DefaultAnalWs, source.Name);
            feature.Abbreviation.set_String(cache.DefaultAnalWs, source.Abbreviation);
            var values = new Dictionary<string, IFsSymFeatVal>(StringComparer.Ordinal);
            foreach (var valueSource in source.Values)
            {
                var value = cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create(
                    Ids.Create(spec.Id, "feature/" + source.Id + "/value/" + valueSource.Id));
                feature.ValuesOC.Add(value);
                value.Name.set_String(cache.DefaultAnalWs, valueSource.Name);
                value.Abbreviation.set_String(cache.DefaultAnalWs, valueSource.Abbreviation);
                values.Add(valueSource.Id, value);
            }
            result.Add(source.Id, new FeatureDefinitionHandle(feature, values));
        }
        return result;
    }

    private static IFsFeatStruc? CreateFeatureStructure(
        LcmCache cache,
        string sampleId,
        string identity,
        IReadOnlyList<FeatureAssignmentSpec> assignments,
        IReadOnlyDictionary<string, FeatureDefinitionHandle> features)
    {
        if (assignments.Count == 0) return null;
        var structure = cache.ServiceLocator.GetInstance<IFsFeatStrucFactory>().Create(
            Ids.Create(sampleId, identity));
        foreach (var assignment in assignments)
        {
            var feature = features[assignment.FeatureId];
            var specification = cache.ServiceLocator.GetInstance<IFsClosedValueFactory>().Create(
                Ids.Create(sampleId, identity + "/" + assignment.FeatureId));
            structure.FeatureSpecsOC.Add(specification);
            specification.FeatureRA = feature.Feature;
            specification.ValueRA = feature.Values[assignment.ValueId];
        }
        return structure;
    }

    private static void AddStems(
        LcmCache cache,
        SampleSpec spec,
        IReadOnlyDictionary<string, IPartOfSpeech> positions,
        IReadOnlyDictionary<string, IMoInflClass> inflectionClasses,
        IReadOnlyDictionary<string, FeatureDefinitionHandle> features)
    {
        foreach (var stem in spec.Stems)
            AddStem(cache, spec.Id, stem, positions[stem.PartOfSpeech], inflectionClasses, features);
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

    private static void AddStem(
        LcmCache cache,
        string sampleId,
        StemSpec stem,
        IPartOfSpeech partOfSpeech,
        IReadOnlyDictionary<string, IMoInflClass> inflectionClasses,
        IReadOnlyDictionary<string, FeatureDefinitionHandle> features)
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
        if (stem.InflectionClass is { } classId)
            msa.InflectionClassRA = inflectionClasses[classId];
        var stemFeatures = CreateFeatureStructure(cache, sampleId, "stem/" + stem.Id + "/features",
            stem.Features ?? [], features);
        if (stemFeatures is not null)
            msa.MsFeaturesOA = stemFeatures;

        var sense = services.GetInstance<ILexSenseFactory>().Create(
            Ids.Create(sampleId, "stem/" + stem.Id + "/sense"));
        entry.SensesOS.Add(sense);
        sense.Gloss.set_String(cache.DefaultAnalWs, stem.Gloss);
        sense.MorphoSyntaxAnalysisRA = msa;
    }

    private static Dictionary<string, IPhPhoneme> AddPhonemes(LcmCache cache, SampleSpec spec)
    {
        var services = cache.ServiceLocator;
        var result = new Dictionary<string, IPhPhoneme>(StringComparer.Ordinal);
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
            result.Add(phonemeText, phoneme);
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
        return result;
    }

    private static Dictionary<string, IPhNCSegments> AddNaturalClasses(
        LcmCache cache, SampleSpec spec, IReadOnlyDictionary<string, IPhPhoneme> phonemes)
    {
        var result = new Dictionary<string, IPhNCSegments>(StringComparer.Ordinal);
        foreach (var source in spec.NaturalClasses)
        {
            var naturalClass = cache.ServiceLocator.GetInstance<IPhNCSegmentsFactory>().Create(
                Ids.Create(spec.Id, "natural-class/" + source.Id));
            cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Add(naturalClass);
            naturalClass.Name.set_String(cache.DefaultAnalWs, source.Name);
            naturalClass.Abbreviation.set_String(cache.DefaultAnalWs, source.Abbreviation);
            foreach (var phoneme in source.Phonemes)
                naturalClass.SegmentsRC.Add(phonemes[phoneme]);
            result.Add(source.Id, naturalClass);
        }
        return result;
    }

    private static Dictionary<string, IPhEnvironment> AddEnvironments(LcmCache cache, SampleSpec spec)
    {
        var result = new Dictionary<string, IPhEnvironment>(StringComparer.Ordinal);
        foreach (var source in spec.Environments)
        {
            var environment = cache.ServiceLocator.GetInstance<IPhEnvironmentFactory>().Create(
                Ids.Create(spec.Id, "environment/" + source.Id));
            cache.LangProject.PhonologicalDataOA.EnvironmentsOS.Add(environment);
            environment.Name.set_String(cache.DefaultAnalWs, source.Name);
            environment.StringRepresentation = TsStringUtils.MakeString(source.Representation, cache.DefaultVernWs);
            result.Add(source.Id, environment);
        }
        return result;
    }

    private static Dictionary<string, IMoInflAffixSlot> AddAffixSlots(
        LcmCache cache, SampleSpec spec, IReadOnlyDictionary<string, IPartOfSpeech> positions)
    {
        var result = new Dictionary<string, IMoInflAffixSlot>(StringComparer.Ordinal);
        foreach (var source in spec.AffixSlots)
        {
            var slot = cache.ServiceLocator.GetInstance<IMoInflAffixSlotFactory>().Create(
                Ids.Create(spec.Id, "affix-slot/" + source.Id));
            positions[source.PartOfSpeech].AffixSlotsOC.Add(slot);
            slot.Name.set_String(cache.DefaultAnalWs, source.Name);
            slot.Optional = source.Optional;
            result.Add(source.Id, slot);
        }
        return result;
    }

    private static void AddAffixTemplates(
        LcmCache cache,
        SampleSpec spec,
        IReadOnlyDictionary<string, IPartOfSpeech> positions,
        IReadOnlyDictionary<string, IMoInflAffixSlot> slots)
    {
        foreach (var source in spec.AffixTemplates)
        {
            var template = cache.ServiceLocator.GetInstance<IMoInflAffixTemplateFactory>().Create(
                Ids.Create(spec.Id, "affix-template/" + source.Id));
            positions[source.PartOfSpeech].AffixTemplatesOS.Add(template);
            template.Name.set_String(cache.DefaultAnalWs, source.Name);
            template.Final = source.Final;
            foreach (var slotId in source.PrefixSlots ?? [])
                template.PrefixSlotsRS.Add(slots[slotId]);
            foreach (var slotId in source.SuffixSlots ?? [])
                template.SuffixSlotsRS.Add(slots[slotId]);
        }
    }

    private static void AddAffixes(
        LcmCache cache,
        SampleSpec spec,
        IReadOnlyDictionary<string, IPartOfSpeech> positions,
        IReadOnlyDictionary<string, IMoInflAffixSlot> slots,
        IReadOnlyDictionary<string, IPhEnvironment> environments,
        IReadOnlyDictionary<string, IMoInflClass> inflectionClasses,
        IReadOnlyDictionary<string, FeatureDefinitionHandle> features)
    {
        foreach (var affix in spec.Affixes)
        {
            var services = cache.ServiceLocator;
            var entry = services.GetInstance<ILexEntryFactory>().Create(
                Ids.Create(spec.Id, "affix/" + affix.Id + "/entry"), cache.LangProject.LexDbOA);
            for (var index = 0; index < affix.Allomorphs.Length; index++)
            {
                var source = affix.Allomorphs[index];
                var allomorph = services.GetInstance<IMoAffixAllomorphFactory>().Create(
                    Ids.Create(spec.Id, "affix/" + affix.Id + "/allomorph/" + source.Id));
                if (index == 0) entry.LexemeFormOA = allomorph;
                else entry.AlternateFormsOS.Add(allomorph);
                var morphType = source.MorphType switch
                {
                    "prefix" => MoMorphTypeTags.kguidMorphPrefix,
                    "infix" => MoMorphTypeTags.kguidMorphInfix,
                    _ => MoMorphTypeTags.kguidMorphSuffix,
                };
                allomorph.MorphTypeRA = services.GetInstance<IMoMorphTypeRepository>().GetObject(morphType);
                allomorph.Form.set_String(cache.DefaultVernWs, source.Form);
                if (source.Environment is not null)
                    allomorph.PhoneEnvRC.Add(environments[source.Environment]);
                if (source.PositionEnvironment is not null)
                    allomorph.PositionRS.Add(environments[source.PositionEnvironment]);
                foreach (var classId in source.InflectionClasses ?? [])
                    allomorph.InflectionClassesRC.Add(inflectionClasses[classId]);
                var environmentFeatures = CreateFeatureStructure(cache, spec.Id,
                    "affix/" + affix.Id + "/allomorph/" + source.Id + "/required-features",
                    source.RequiredFeatures ?? [], features);
                if (environmentFeatures is not null)
                    allomorph.MsEnvFeaturesOA = environmentFeatures;
            }
            var msa = services.GetInstance<IMoInflAffMsaFactory>().Create(
                entry, SandboxGenericMSA.Create(MsaType.kInfl, positions[affix.PartOfSpeech]));
            var inflectionFeatures = CreateFeatureStructure(cache, spec.Id,
                "affix/" + affix.Id + "/inflection-features", affix.Features ?? [], features);
            if (inflectionFeatures is not null)
                msa.InflFeatsOA = inflectionFeatures;
            foreach (var slotId in affix.Slots)
                msa.SlotsRC.Add(slots[slotId]);
            var sense = services.GetInstance<ILexSenseFactory>().Create(
                Ids.Create(spec.Id, "affix/" + affix.Id + "/sense"));
            entry.SensesOS.Add(sense);
            sense.Gloss.set_String(cache.DefaultAnalWs, affix.Gloss);
            sense.MorphoSyntaxAnalysisRA = msa;
        }
    }

    private static void AddTexts(LcmCache cache, SampleSpec spec)
    {
        var services = cache.ServiceLocator;
        var wordformFactory = (ILcmFactory<IWfiWordform>)services.GetInstance<IWfiWordformFactory>();
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
                        wordform = wordformFactory.Create(Ids.Create(spec.Id, objectKey));
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

    private static string ProjectName(string id) => "Motif" + string.Concat(id
        .Split('-', StringSplitOptions.RemoveEmptyEntries)
        .Select(segment => char.ToUpperInvariant(segment[0]) + segment[1..]));
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
