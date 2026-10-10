using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.SampleProjects;

namespace SIL.Motif.EvalSets;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
                throw new InvalidDataException("Usage: build|build-project --set <dir> --start <start> --out <dir> | verify --set <dir> [--out <dir>]");
            if (args[0] is not ("build" or "build-project" or "verify" or "measure-output"))
                throw new InvalidDataException($"Unknown eval-set command '{args[0]}'.");
            var options = ReadOptions(args.Skip(1).ToArray());
            var setDirectory = Path.GetFullPath(Required(options, "set"));
            var languagePath = Path.Combine(setDirectory, "language", "language.yaml");
            if (!File.Exists(languagePath))
                throw new FileNotFoundException("The evaluation set has no language/language.yaml file.", languagePath);
            var languageText = await File.ReadAllTextAsync(languagePath);
            var language = JsonSerializer.Deserialize<EvaluationLanguage>(languageText, JsonOptions)
                ?? throw new InvalidDataException("The language specification is empty.");
            var data = EvaluationSetGenerator.Generate(language, setDirectory, writeArtifacts: args[0] == "build");

            if (args[0] == "measure-output")
            {
                var result = await OutputMeasurement.RunAsync(languageText, Required(options, "start"),
                    Required(options, "proposal"), Required(options, "out"),
                    options.GetValueOrDefault("words-root", setDirectory));
                Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
                return 0;
            }

            if (args[0] is "build" or "build-project")
            {
                var start = Required(options, "start");
                var outputRoot = Path.GetFullPath(Required(options, "out"));
                EnsureBuildOutputIsOutsideSet(setDirectory, outputRoot);
                var result = EvaluationProjectBuilder.Build(languageText, outputRoot, start);
                Console.WriteLine(JsonSerializer.Serialize(new { projectPath = result.ProjectPath }, JsonOptions));
                return 0;
            }

            if (args[0] == "verify")
            {
                var outputRoot = options.TryGetValue("out", out var configuredOutput)
                    ? Path.GetFullPath(configuredOutput)
                    : Path.Combine(Path.GetTempPath(), "motif-eval-sets", Guid.NewGuid().ToString("N"));
                EnsureBuildOutputIsOutsideSet(setDirectory, outputRoot);
                var result = await EvaluationSetVerifier.VerifyAsync(languageText, language, data, outputRoot,
                    CancellationToken.None, options.GetValueOrDefault("start", "gold-grammar"));
                Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
                return 0;
            }

            throw new InvalidDataException($"Unknown eval-set command '{args[0]}'.");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static Dictionary<string, string> ReadOptions(IReadOnlyList<string> args)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Count; index++)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Count)
                throw new InvalidDataException($"Unknown or incomplete option '{args[index]}'.");
            var name = args[index][2..];
            if (name is not ("set" or "start" or "out" or "proposal" or "words-root") || !result.TryAdd(name, args[++index]))
                throw new InvalidDataException($"Unknown or repeated option '--{name}'.");
        }
        return result;
    }

    private static string Required(IReadOnlyDictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidDataException($"Option '--{name}' is required.");

    private static void EnsureBuildOutputIsOutsideSet(string setDirectory, string outputRoot)
    {
        var relative = Path.GetRelativePath(setDirectory, outputRoot);
        if (relative == "." || (!relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                                relative != ".." && !Path.IsPathRooted(relative)))
            throw new InvalidDataException("Build output must be outside the committed evaluation set.");
    }
}

internal sealed record EvaluationLanguage(
    string Id,
    string Tier,
    int Seed,
    LanguageMetadata Language,
    string[] Phonemes,
    NaturalClass[] NaturalClasses,
    FeatureDefinition[] FeatureDefinitions,
    EnvironmentDefinition[] Environments,
    PartOfSpeech[] PartsOfSpeech,
    Stem[] Stems,
    Affix[] Affixes,
    AffixSlot[] AffixSlots,
    AffixTemplate[] AffixTemplates,
    PhonologicalRule[] PhonologicalRules,
    WordPattern[] Patterns,
    Defect[] Defects)
{
    public string Title { get; init; } = "";
    public string[] Teaches { get; init; } = [];
    public string Summary { get; init; } = "";
    public string Disclaimer { get; init; } = "";
    public string Description { get; init; } = "";
    public FeatureDefinition[] PhonologicalFeatureDefinitions { get; init; } = [];
    public Dictionary<string, FeatureAssignment[]> PhonemeFeatures { get; init; } = new(StringComparer.Ordinal);
    public ReduplicationRule[] Reduplication { get; init; } = [];
}

internal sealed record LanguageMetadata(string Name, string Tag);
internal sealed record NaturalClass(string Id, string Name, string Abbreviation, string[] Phonemes);
internal sealed record FeatureDefinition(string Id, string Name, string Abbreviation, FeatureValue[] Values);
internal sealed record FeatureValue(string Id, string Name, string Abbreviation);
internal sealed record FeatureAssignment(string FeatureId, string ValueId);
internal sealed record EnvironmentDefinition(string Id, string Name, string Representation);
internal sealed record PartOfSpeech(string Id, string Name);
internal sealed record Stem(string Id, string Form, string PartOfSpeech, string Gloss,
    string? InflectionClass = null, FeatureAssignment[]? Features = null);
internal sealed record Affix(string Id, string PartOfSpeech, string[] Slots, string Gloss,
    Allomorph[] Allomorphs, FeatureAssignment[]? Features = null);
internal sealed record Allomorph(string Id, string Form, string? Environment,
    string MorphType = "suffix", string? PositionEnvironment = null, string[]? InflectionClasses = null,
    FeatureAssignment[]? RequiredFeatures = null);
internal sealed record AffixSlot(string Id, string Name, string PartOfSpeech, bool Optional);
internal sealed record AffixTemplate(string Id, string Name, string PartOfSpeech,
    string[] PrefixSlots, string[] SuffixSlots, bool Final);
internal sealed record PhonologicalRule(string Id, string Name, string Input, string Output, string Environment);
internal sealed record ReduplicationRule(string Id, string Input, string Output);
internal sealed record WordPattern(string Id, string PartOfSpeech, string[] Affixes, string Partition);
internal sealed record Defect(string Id, Patch[] Patch);
internal sealed record Patch(string Op, string? AllomorphId = null, string? Form = null,
    string? FeatureId = null, string? EnvironmentId = null, string? AffixId = null, string? StemId = null,
    string? TemplateId = null, string? FirstSlotId = null, string? SecondSlotId = null,
    string? SlotId = null, bool? Optional = null, int? Count = null, string[]? Slots = null,
    string? Side = null, string? Gloss = null, AffixPatch? Affix = null, Allomorph? Allomorph = null,
    string? RuleId = null, string? Environment = null);
internal sealed record AffixPatch(string Id, string PartOfSpeech, string[] Slots, string Gloss,
    FeatureAssignment[]? Features = null);

internal sealed record GeneratedMorph(string IdentityKey, string Form, string Gloss);
internal sealed record GeneratedWord(string Word, IReadOnlyList<GeneratedMorph> Morphs);
internal sealed record GeneratedSetData(
    IReadOnlyList<GeneratedWord> Train,
    IReadOnlyList<GeneratedWord> Heldout,
    IReadOnlyList<string> Negatives);

internal static class EvaluationSetGenerator
{
    public static GeneratedSetData Generate(
        EvaluationLanguage language, string setDirectory, bool writeArtifacts = true)
    {
        Validate(language);
        var train = GeneratePartition(language, "train");
        var heldout = GeneratePartition(language, "heldout");
        var trainCombinations = Combinations(language, "train");
        var heldoutCombinations = Combinations(language, "heldout");
        if (trainCombinations.Overlaps(heldoutCombinations))
            throw new InvalidDataException("A held-out stem and affix sequence also occurs in training data.");

        var negatives = GenerateNegatives(language, heldout);
        EnsureUniqueWords(train, heldout, negatives);
        var trainWords = train.Select(item => item.Word).ToArray();
        var heldoutWords = heldout.Select(item => item.Word).ToArray();
        var goldLines = train.Concat(heldout).Select(item =>
        {
            var analysis = item.Morphs.Select(morph => new[] { morph.Form, morph.Gloss }).ToArray();
            return JsonSerializer.Serialize(new { word = item.Word, analyses = new[] { analysis } }, ProgramJson.Options);
        }).ToArray();
        if (writeArtifacts)
        {
            Directory.CreateDirectory(Path.Combine(setDirectory, "words"));
            Directory.CreateDirectory(Path.Combine(setDirectory, "gold"));
            File.WriteAllLines(Path.Combine(setDirectory, "words", "train.txt"), trainWords);
            File.WriteAllLines(Path.Combine(setDirectory, "words", "heldout.txt"), heldoutWords);
            File.WriteAllLines(Path.Combine(setDirectory, "words", "negative.txt"), negatives);
            File.WriteAllLines(Path.Combine(setDirectory, "gold", "analyses.jsonl"), goldLines);
        }
        else
        {
            AssertGeneratedFile(Path.Combine(setDirectory, "words", "train.txt"), trainWords);
            AssertGeneratedFile(Path.Combine(setDirectory, "words", "heldout.txt"), heldoutWords);
            AssertGeneratedFile(Path.Combine(setDirectory, "words", "negative.txt"), negatives);
            AssertGeneratedFile(Path.Combine(setDirectory, "gold", "analyses.jsonl"), goldLines);
        }
        return new GeneratedSetData(train, heldout, negatives);
    }

    private static void AssertGeneratedFile(string path, IReadOnlyList<string> expected)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Generated evaluation data is missing; run the build command first.", path);
        var actual = File.ReadAllLines(path);
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
            throw new InvalidDataException($"'{path}' does not match the language specification; regenerate it with build.");
    }

    private static IReadOnlyList<GeneratedWord> GeneratePartition(EvaluationLanguage language, string partition)
    {
        var result = new List<GeneratedWord>();
        var stemsById = language.Stems.ToDictionary(stem => stem.Id, StringComparer.Ordinal);
        var orderedStems = language.Stems.ToArray();
        var random = new Random(language.Seed);
        random.Shuffle(orderedStems);
        foreach (var pattern in language.Patterns.Where(pattern => pattern.Partition == partition))
        foreach (var stem in orderedStems.Where(stem => stem.PartOfSpeech == pattern.PartOfSpeech))
            result.Add(Realize(language, stemsById[stem.Id], pattern.Affixes));
        return result;
    }

    private static GeneratedWord Realize(EvaluationLanguage language, Stem stem, IReadOnlyList<string> affixIds,
        string? wrongAllomorph = null)
    {
        var form = stem.Form;
        var morphs = new List<GeneratedMorph> { new("stem/" + stem.Id, stem.Form, stem.Gloss) };
        for (var index = 0; index < affixIds.Count; index++)
        {
            var affix = language.Affixes.SingleOrDefault(candidate => candidate.Id == affixIds[index])
                ?? throw new InvalidDataException($"Pattern names unknown affix '{affixIds[index]}'.");
            var candidates = affix.Allomorphs.AsEnumerable();
            var forceAlternative = false;
            if (wrongAllomorph is not null && index == 0 && affix.Id == wrongAllomorph)
            {
                var correct = SelectAllomorph(language, stem, form, affix);
                candidates = candidates.Where(candidate => candidate.Id != correct.Id);
                forceAlternative = true;
            }
            var selected = candidates.FirstOrDefault(allomorph => forceAlternative ||
                    MatchesAllomorph(language, stem, form, allomorph))
                ?? throw new InvalidDataException($"No allomorph of '{affix.Id}' fits '{form}'.");
            form = selected.MorphType switch
            {
                "prefix" => selected.Form + form,
                "suffix" => form + selected.Form,
                _ => throw new InvalidDataException($"Word generation does not support '{selected.MorphType}' in {language.Tier}.")
            };
            morphs.Add(new GeneratedMorph("affix/" + affix.Id + "/" + selected.Id, selected.Form, affix.Gloss));
        }
        form = ApplyPhonologicalRules(language, form);
        return new GeneratedWord(form, morphs);
    }

    private static Allomorph SelectAllomorph(
        EvaluationLanguage language, Stem stem, string form, Affix affix) =>
        affix.Allomorphs.FirstOrDefault(allomorph => MatchesAllomorph(language, stem, form, allomorph))
        ?? throw new InvalidDataException($"No allomorph of '{affix.Id}' fits '{form}'.");

    private static bool MatchesAllomorph(
        EvaluationLanguage language, Stem stem, string form, Allomorph allomorph) =>
        EnvironmentMatches(language, form, allomorph.Environment) &&
        (allomorph.RequiredFeatures ?? []).All(required =>
            (stem.Features ?? []).Any(feature => feature.FeatureId == required.FeatureId &&
                                                 feature.ValueId == required.ValueId));

    private static string ApplyPhonologicalRules(EvaluationLanguage language, string form)
    {
        foreach (var rule in language.PhonologicalRules)
        {
            var naturalClass = RuleContextClass(language, rule.Environment, out var followsInput);
            var elements = new List<string>();
            var enumerator = StringInfo.GetTextElementEnumerator(form);
            while (enumerator.MoveNext()) elements.Add(enumerator.GetTextElement());
            for (var index = 0; index < elements.Count; index++)
            {
                if (elements[index] != rule.Input) continue;
                var contextIndex = followsInput ? index + 1 : index - 1;
                if (contextIndex >= 0 && contextIndex < elements.Count &&
                    naturalClass.Phonemes.Contains(elements[contextIndex], StringComparer.Ordinal))
                    elements[index] = rule.Output;
            }
            form = string.Concat(elements);
        }
        return form;
    }

    private static NaturalClass RuleContextClass(
        EvaluationLanguage language, string representation, out bool followsInput)
    {
        var expression = representation.Trim();
        followsInput = expression.StartsWith("/ _ [", StringComparison.Ordinal);
        var precedesInput = expression.StartsWith("/ [", StringComparison.Ordinal) && expression.EndsWith(" _", StringComparison.Ordinal);
        if (!followsInput && !precedesInput)
            throw new InvalidDataException($"Rule environment '{representation}' is outside the supported one-class context subset.");
        var open = expression.IndexOf('[', StringComparison.Ordinal);
        var close = expression.IndexOf(']', open + 1);
        if (close < 0 || expression[(close + 1)..].Trim() is not ("" or "_"))
            throw new InvalidDataException($"Rule environment '{representation}' needs one natural-class context.");
        var abbreviation = expression[(open + 1)..close];
        return language.NaturalClasses.SingleOrDefault(candidate => candidate.Abbreviation == abbreviation)
            ?? throw new InvalidDataException($"Rule environment '{representation}' names unknown class '{abbreviation}'.");
    }

    private static bool EnvironmentMatches(EvaluationLanguage language, string form, string? environmentId)
    {
        if (environmentId is null) return true;
        var environment = language.Environments.SingleOrDefault(candidate => candidate.Id == environmentId)
            ?? throw new InvalidDataException($"An allomorph names unknown environment '{environmentId}'.");
        var expression = environment.Representation.Trim();
        if (expression.Length < 5 || expression[0] != '/' || expression[^1] != '_')
            throw new InvalidDataException(
                $"Environment '{environment.Id}' is outside the generator's /[NaturalClass] _ subset.");
        var className = expression[2..^2];
        var naturalClass = language.NaturalClasses.SingleOrDefault(candidate => candidate.Abbreviation == className)
            ?? throw new InvalidDataException($"Environment '{environment.Id}' names unknown class '{className}'.");
        var final = LastTextElement(form);
        return naturalClass.Phonemes.Contains(final, StringComparer.Ordinal);
    }

    private static string LastTextElement(string value)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(value);
        var last = "";
        while (enumerator.MoveNext()) last = enumerator.GetTextElement();
        return last;
    }

    private static IReadOnlyList<string> GenerateNegatives(
        EvaluationLanguage language, IReadOnlyList<GeneratedWord> heldout)
    {
        var result = new List<string>();
        foreach (var pattern in language.Patterns.Where(pattern => pattern.Partition == "heldout" && pattern.Affixes.Length > 1))
        foreach (var stem in language.Stems.Where(stem => stem.PartOfSpeech == pattern.PartOfSpeech))
        {
            var reversed = pattern.Affixes.Reverse().ToArray();
            result.Add(Realize(language, stem, reversed).Word);
        }

        foreach (var pattern in language.Patterns.Where(pattern => pattern.Partition == "heldout" && pattern.Affixes.Length > 0))
        foreach (var stem in language.Stems.Where(stem => stem.PartOfSpeech == pattern.PartOfSpeech))
        {
            var correct = Realize(language, stem, pattern.Affixes);
            var firstAffix = language.Affixes.Single(item => item.Id == pattern.Affixes[0]);
            if (firstAffix.Allomorphs.Length > 1)
            {
                result.Add(Realize(language, stem, pattern.Affixes, firstAffix.Id).Word);
            }
            else
            {
                var extra = "qx";
                if (!language.Phonemes.Contains("q", StringComparer.Ordinal) ||
                    !language.Phonemes.Contains("x", StringComparer.Ordinal) ||
                    firstAffix.Allomorphs.Any(item => item.Form.Contains(extra, StringComparison.Ordinal)))
                    throw new InvalidDataException("A singleton-allomorph set needs unused q and x phonemes for negatives.");
                result.Add(correct.Word + extra);
            }
        }

        var distinct = result.Distinct(StringComparer.Ordinal).ToArray();
        if (distinct.Length < 50)
            throw new InvalidDataException($"The set produced only {distinct.Length} plausible negative words.");
        return distinct;
    }

    private static HashSet<string> Combinations(EvaluationLanguage language, string partition) =>
        language.Patterns.Where(pattern => pattern.Partition == partition)
            .SelectMany(pattern => language.Stems.Where(stem => stem.PartOfSpeech == pattern.PartOfSpeech)
                .Select(stem => stem.Id + "/" + string.Join("+", pattern.Affixes)))
            .ToHashSet(StringComparer.Ordinal);

    private static void EnsureUniqueWords(
        IReadOnlyList<GeneratedWord> train, IReadOnlyList<GeneratedWord> heldout, IReadOnlyList<string> negatives)
    {
        var positive = train.Concat(heldout).Select(item => item.Word).ToArray();
        if (positive.Distinct(StringComparer.Ordinal).Count() != positive.Length)
            throw new InvalidDataException("Generated positive words are not unique.");
        if (negatives.Intersect(positive, StringComparer.Ordinal).Any())
            throw new InvalidDataException("A negative word is also a generated positive word.");
    }

    private static void Validate(EvaluationLanguage language)
    {
        if (language.Tier is not ("T0" or "T1" or "T2" or "T3" or "T4"))
            throw new InvalidDataException($"Unknown tier '{language.Tier}'.");
        if (language.Reduplication.Length > 0)
            throw new InvalidDataException("Reduplication generation is not implemented for the current tiers.");
        if (language.Stems.Length is < 30 or > 80)
            throw new InvalidDataException("A language needs 30 to 80 stems.");
        if (language.Patterns.Length == 0 || language.Patterns.Any(pattern => pattern.Partition is not ("train" or "heldout")))
            throw new InvalidDataException("Patterns need a train or heldout partition.");
        if (!language.Patterns.Any(pattern => pattern.Partition == "train") ||
            !language.Patterns.Any(pattern => pattern.Partition == "heldout"))
            throw new InvalidDataException("The set needs training and held-out patterns.");
        if (language.Tier == "T0" && language.Affixes.SelectMany(item => item.Allomorphs).Any(item => item.Environment is not null))
            throw new InvalidDataException("T0 allomorphs cannot have conditioned environments.");
        if (language.Tier == "T1" && !language.Affixes.SelectMany(item => item.Allomorphs).Any(item => item.Environment is not null))
            throw new InvalidDataException("T1 needs at least one conditioned allomorph.");
        if (language.Tier == "T2" && (!language.Affixes.SelectMany(item => item.Allomorphs)
                .Any(item => (item.RequiredFeatures ?? []).Length > 0) ||
            !language.AffixSlots.Any(slot => !slot.Optional)))
            throw new InvalidDataException("T2 needs feature-conditioned allomorphs and an obligatory slot.");
        if (language.Tier == "T3" && language.PhonologicalRules.Length == 0)
            throw new InvalidDataException("T3 needs at least one phonological rule.");
        if (language.PhonologicalRules.Length > 1)
            throw new InvalidDataException("The current generator supports one phonological rule per language.");
        if (language.Affixes.Any(affix => affix.Allomorphs.Length == 0))
            throw new InvalidDataException("Every affix needs at least one allomorph.");
        foreach (var pattern in language.Patterns)
        foreach (var affixId in pattern.Affixes)
        {
            var affix = language.Affixes.SingleOrDefault(item => item.Id == affixId)
                ?? throw new InvalidDataException($"Pattern '{pattern.Id}' names unknown affix '{affixId}'.");
            if (affix.PartOfSpeech != pattern.PartOfSpeech)
                throw new InvalidDataException($"Pattern '{pattern.Id}' uses an affix from another category.");
        }
        var train = language.Patterns.Where(pattern => pattern.Partition == "train").ToArray();
        if (train.Sum(pattern => language.Stems.Count(stem => stem.PartOfSpeech == pattern.PartOfSpeech)) is < 150 or > 400)
            throw new InvalidDataException("The generated training list must contain 150 to 400 words.");
        if (language.Patterns.Where(pattern => pattern.Partition == "heldout")
                .Sum(pattern => language.Stems.Count(stem => stem.PartOfSpeech == pattern.PartOfSpeech)) is < 50 or > 150)
            throw new InvalidDataException("The generated held-out list must contain 50 to 150 words.");
        if (language.Seed < 0)
            throw new InvalidDataException("The language seed cannot be negative.");
    }
}

internal static class ProgramJson
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}

/// <summary>Counts a PanGloss verification of every committed positive and negative form.</summary>
public sealed record EvaluationVerificationResult(
    string SetId, string Tier, int TrainWords, int HeldoutWords, int NegativeWords,
    int GoldAnalysesMatched, int NegativeParses, string ParserPath);

/// <summary>Checks generated examples against the LibLCM project and real PanGloss output.</summary>
public static class EvaluationSetVerifier
{
    /// <summary>
    /// Builds the gold grammar and requires a matching ordered morphology for every positive word and no
    /// analyses for any negative word. The caller supplies a disposable output root.
    /// </summary>
    public static async Task<EvaluationVerificationResult> VerifySetAsync(
        string setDirectory, string outputRoot, CancellationToken cancellationToken = default)
    {
        var setPath = Path.GetFullPath(setDirectory);
        var languageSpecYaml = await File.ReadAllTextAsync(
            Path.Combine(setPath, "language", "language.yaml"), cancellationToken);
        var language = JsonSerializer.Deserialize<EvaluationLanguage>(languageSpecYaml, ProgramJson.Options)
            ?? throw new InvalidDataException("The language specification is empty.");
        var data = EvaluationSetGenerator.Generate(language, setPath, writeArtifacts: false);
        return await VerifyAsync(languageSpecYaml, language, data, outputRoot, cancellationToken);
    }

    internal static async Task<EvaluationVerificationResult> VerifyAsync(
        string languageSpecYaml, EvaluationLanguage language, GeneratedSetData data, string outputRoot,
        CancellationToken cancellationToken, string start = "gold-grammar")
    {
        var parserPath = PanGlossExecutable.TryLocate()
            ?? throw new InvalidOperationException(PanGlossExecutable.NotFoundMessage);
        if (start != "gold-grammar" && !start.StartsWith("gold-minus:", StringComparison.Ordinal))
            throw new InvalidDataException("Verification start must be gold-grammar or a declared gold-minus defect.");
        var build = EvaluationProjectBuilder.Build(languageSpecYaml, outputRoot, start);
        var positive = data.Train.Concat(data.Heldout).ToArray();
        var words = positive.Select(item => item.Word).Concat(data.Negatives).ToArray();
        using var invoker = new PanGlossInvoker(parserPath);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Batch(build.ProjectPath, words, TimeSpan.FromSeconds(1),
                PerWordStepLimit: StepCap.Default) { CollectAnalyses = true },
            "eval-set-verify:" + language.Id, linked.Token, TimeSpan.FromMinutes(10));
        if (outcome is not PanGlossOutcome.Completed completed)
            throw new InvalidOperationException($"PanGloss verification did not complete: {outcome.Message}");

        var rows = BatchTsvParser.Parse(completed.Output);
        if (rows.Count != words.Length)
            throw new InvalidDataException($"PanGloss returned {rows.Count} rows for {words.Length} words.");
        var morphology = ParseMorphEvidence.Read(completed.MorphologyOutput ?? "", words);
        var formIds = build.FormIds.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
        var failures = new List<string>();
        var matched = 0;
        for (var index = 0; index < positive.Length; index++)
        {
            var word = positive[index];
            var expectedIds = word.Morphs.Select(morph => build.FormIds[morph.IdentityKey]).ToArray();
            var found = morphology[index].Analyses.Any(analysis =>
                analysis.Morphs.Select(morph => morph.Form is not null && formIds.ContainsKey(morph.Form)
                        ? formIds[morph.Form] : "")
                    .SequenceEqual(word.Morphs.Select(morph => morph.IdentityKey), StringComparer.Ordinal));
            if (rows[index].Outcome != WordOutcome.Analysed || !found)
                failures.Add($"{word.Word} expected {string.Join(" + ", expectedIds)}; outcome={rows[index].Outcome}");
            else
                matched++;
        }
        var negativeStart = positive.Length;
        var negativeParses = 0;
        for (var index = 0; index < data.Negatives.Count; index++)
        {
            var row = rows[negativeStart + index];
            var evidence = morphology[negativeStart + index];
            if (row.Outcome != WordOutcome.NoAnalysis || evidence.Analyses.Count != 0)
            {
                negativeParses++;
                failures.Add($"negative {row.Word} outcome={row.Outcome} analyses={evidence.Analyses.Count}");
            }
        }
        if (rows.Any(row => row.Outcome is not (WordOutcome.Analysed or WordOutcome.NoAnalysis)))
            throw new InvalidDataException("PanGloss verification contains incomplete or unavailable words.");
        if (failures.Count > 0 && start == "gold-grammar")
            throw new InvalidDataException("PanGloss verification failed: " + string.Join("; ", failures.Take(12)));
        if (failures.Count == 0 && start != "gold-grammar")
            throw new InvalidDataException($"Defect '{start}' changes no committed positive or negative parse.");

        return new EvaluationVerificationResult(language.Id, language.Tier, data.Train.Count,
            data.Heldout.Count, data.Negatives.Count, matched, negativeParses, parserPath);
    }
}
