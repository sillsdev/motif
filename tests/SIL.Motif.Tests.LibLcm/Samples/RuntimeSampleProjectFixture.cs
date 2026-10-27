using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIL.Motif.Tests.Samples;

internal sealed record RuntimeSampleProjectFixture(
    RuntimeSampleSpec Sample,
    RuntimeSampleTokens Tokens)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public static RuntimeSampleProjectFixture Create(string id)
    {
        var symbols = Enumerable.Range('a', 26).Select(value => ((char)value).ToString()).ToArray();
        var consonant = symbols[0];
        var vowel = symbols[1];
        var secondConsonant = symbols[2];
        var prefixForm = symbols[3];
        var infixFirst = symbols[4];
        var infixSecond = symbols[5];
        var suffixForm = symbols[6];
        var vowelClassAbbreviation = "V";
        var consonantClassAbbreviation = "C";
        var nounId = id + "-noun";
        var verbId = id + "-verb";
        var nounForm = consonant + vowel;
        var verbForm = secondConsonant + vowel + consonant;
        var tokens = new RuntimeSampleTokens(
            Id: id,
            NounId: nounId,
            VerbId: verbId,
            NounName: id + " noun",
            VerbName: id + " verb",
            NounForm: nounForm,
            VerbForm: verbForm,
            PrefixForm: prefixForm,
            InfixForm: infixFirst + infixSecond,
            InfixResult: secondConsonant + infixFirst + infixSecond + vowel + consonant,
            SuffixForm: suffixForm,
            Phonemes: [consonant, vowel, secondConsonant, prefixForm, infixFirst, infixSecond, suffixForm],
            VowelClassId: id + "-vowels",
            VowelClassAbbreviation: vowelClassAbbreviation,
            BackVowelClassAbbreviation: "B",
            FrontVowelClassAbbreviation: "F",
            ConsonantClassId: id + "-consonants",
            ConsonantClassAbbreviation: consonantClassAbbreviation,
            NounTemplateId: id + "-noun-template",
            VerbTemplateId: id + "-verb-template",
            NounTemplateName: id + " noun templates",
            VerbTemplateName: id + " verb templates",
            EnvironmentId: id + "-after-vowel",
            EnvironmentRepresentation: "/[" + vowelClassAbbreviation + "]_",
            InfixEnvironmentId: id + "-infix-position",
            InfixEnvironmentRepresentation: "/ # [" + consonantClassAbbreviation + "] _",
            PrefixSlotOneId: id + "-prefix-one",
            PrefixSlotTwoId: id + "-prefix-two",
            PrefixSlotOneName: id + " prefix one",
            PrefixSlotTwoName: id + " prefix two",
            ObjectSlotId: id + "-object",
            ObjectSlotName: id + " object",
            InfixSlotId: id + "-infix-slot",
            InfixSlotName: id + " infix",
            ClassId: id + "-class-one",
            ClassName: id + " noun class one",
            FeatureId: id + "-noun-class",
            FeatureName: id + " noun class",
            FeatureValueId: id + "-class-one-value",
            FeatureValueName: id + " class one",
            PrefixAffixId: id + "-test-prefix",
            PrefixAllomorphOneId: id + "-prefix-form-one",
            PrefixAllomorphTwoId: id + "-prefix-form-two",
            TextId: id + "-example",
            TextTitle: id + " example",
            ZeroAffixId: id + "-zero-object-affix",
            ZeroConditionedAllomorphId: id + "-zero-conditioned",
            ZeroUnconditionedAllomorphId: id + "-zero-unconditioned",
            BugId: id + "-bug");

        var sample = new RuntimeSampleSpec
        {
            Id = id,
            Title = id + " runtime sample",
            Teaches = ["runtime fixture construction"],
            Summary = "A sample assembled from typed runtime values.",
            Disclaimer = "Generated test fixture data.",
            Description = "A sample assembled from typed runtime values.",
            Language = new("Runtime language", "fr"),
            Phonemes = [.. tokens.Phonemes],
            NaturalClasses =
            [
                new(tokens.VowelClassId, id + " vowels", tokens.VowelClassAbbreviation, [vowel]),
                new(tokens.ConsonantClassId, id + " consonants", tokens.ConsonantClassAbbreviation, [consonant, secondConsonant,
                    prefixForm, infixFirst, infixSecond, suffixForm]),
            ],
            PartsOfSpeech =
            [
                new(nounId, id + " noun"),
                new(verbId, id + " verb"),
            ],
            Stems =
            [
                new(nounId, nounForm, nounId, id + " noun gloss"),
                new(verbId, verbForm, verbId, id + " verb gloss"),
            ],
            Environments = [new(tokens.EnvironmentId, id + " after vowel", tokens.EnvironmentRepresentation)],
            AffixTemplates =
            [
                new(tokens.NounTemplateId, tokens.NounTemplateName, nounId, Final: true),
                new(tokens.VerbTemplateId, tokens.VerbTemplateName, verbId, Final: true),
            ],
            Texts = [new(tokens.TextId, tokens.TextTitle, [nounForm])],
        };

        return new(sample, tokens);
    }

    public static Task WriteSampleAsync(string path, RuntimeSampleSpec sample) =>
        File.WriteAllTextAsync(path, JsonSerializer.Serialize(sample, JsonOptions));

    public static Task WriteBugsAsync(string path, IReadOnlyList<RuntimeBugSpec> bugs) =>
        File.WriteAllTextAsync(path, JsonSerializer.Serialize(bugs, JsonOptions));

    public static Task WriteGenerateRequestAsync(string path, Guid entryGuid, Guid msaGuid) =>
        File.WriteAllTextAsync(path, JsonSerializer.Serialize(
            new RuntimePanGlossGenerateRequest(1,
                new(entryGuid.ToString("D"), msaGuid.ToString("D"), "MoStemMsa"),
                Array.Empty<RuntimePanGlossGenerateMorpheme>()), JsonOptions));
}

internal sealed record RuntimeSampleTokens(
    string Id,
    string NounId,
    string VerbId,
    string NounName,
    string VerbName,
    string NounForm,
    string VerbForm,
    string PrefixForm,
    string InfixForm,
    string InfixResult,
    string SuffixForm,
    IReadOnlyList<string> Phonemes,
    string VowelClassId,
    string VowelClassAbbreviation,
    string BackVowelClassAbbreviation,
    string FrontVowelClassAbbreviation,
    string ConsonantClassId,
    string ConsonantClassAbbreviation,
    string NounTemplateId,
    string VerbTemplateId,
    string NounTemplateName,
    string VerbTemplateName,
    string EnvironmentId,
    string EnvironmentRepresentation,
    string InfixEnvironmentId,
    string InfixEnvironmentRepresentation,
    string PrefixSlotOneId,
    string PrefixSlotTwoId,
    string PrefixSlotOneName,
    string PrefixSlotTwoName,
    string ObjectSlotId,
    string ObjectSlotName,
    string InfixSlotId,
    string InfixSlotName,
    string ClassId,
    string ClassName,
    string FeatureId,
    string FeatureName,
    string FeatureValueId,
    string FeatureValueName,
    string PrefixAffixId,
    string PrefixAllomorphOneId,
    string PrefixAllomorphTwoId,
    string TextId,
    string TextTitle,
    string ZeroAffixId,
    string ZeroConditionedAllomorphId,
    string ZeroUnconditionedAllomorphId,
    string BugId);

internal sealed class RuntimeSampleSpec
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public required IReadOnlyList<string> Teaches { get; set; }
    public required string Summary { get; set; }
    public required string Disclaimer { get; set; }
    public required string Description { get; set; }
    public required RuntimeLanguageSpec Language { get; set; }
    public required IReadOnlyList<string> Phonemes { get; set; }
    public List<RuntimeNaturalClassSpec> NaturalClasses { get; set; } = [];
    public List<RuntimeFeatureDefinitionSpec> FeatureDefinitions { get; set; } = [];
    public List<RuntimeInflectionClassSpec> InflectionClasses { get; set; } = [];
    public List<RuntimeEnvironmentSpec> Environments { get; set; } = [];
    public required IReadOnlyList<RuntimePartOfSpeechSpec> PartsOfSpeech { get; set; }
    public List<RuntimeStemSpec> Stems { get; set; } = [];
    public List<RuntimeAffixSpec> Affixes { get; set; } = [];
    public List<RuntimeAffixSlotSpec> AffixSlots { get; set; } = [];
    public List<RuntimeAffixTemplateSpec> AffixTemplates { get; set; } = [];
    public List<RuntimePhonologicalRuleSpec> PhonologicalRules { get; set; } = [];
    public required IReadOnlyList<RuntimeTextSpec> Texts { get; set; }
}

internal sealed record RuntimeLanguageSpec(string Name, string Tag);
internal sealed record RuntimeNaturalClassSpec(string Id, string Name, string Abbreviation,
    IReadOnlyList<string> Phonemes);
internal sealed record RuntimeFeatureDefinitionSpec(string Id, string Name, string Abbreviation,
    IReadOnlyList<RuntimeFeatureValueSpec> Values);
internal sealed record RuntimeFeatureValueSpec(string Id, string Name, string Abbreviation);
internal sealed record RuntimeFeatureAssignmentSpec(string FeatureId, string ValueId);
internal sealed record RuntimeInflectionClassSpec(string Id, string Name, string PartOfSpeech);
internal sealed record RuntimeEnvironmentSpec(string Id, string Name, string Representation);
internal sealed record RuntimePartOfSpeechSpec(string Id, string Name);
internal sealed record RuntimeStemSpec(string Id, string Form, string PartOfSpeech, string Gloss,
    string? InflectionClass = null, IReadOnlyList<RuntimeFeatureAssignmentSpec>? Features = null);
internal sealed record RuntimeAllomorphSpec(string Id, string Form, string? Environment = null,
    string? MorphType = null, string? PositionEnvironment = null,
    IReadOnlyList<string>? InflectionClasses = null,
    IReadOnlyList<RuntimeFeatureAssignmentSpec>? RequiredFeatures = null);
internal sealed record RuntimeAffixSpec(string Id, string PartOfSpeech, IReadOnlyList<string> Slots,
    string Gloss, IReadOnlyList<RuntimeAllomorphSpec> Allomorphs,
    IReadOnlyList<RuntimeFeatureAssignmentSpec>? Features = null);
internal sealed record RuntimeAffixSlotSpec(string Id, string Name, string PartOfSpeech, bool Optional);
internal sealed record RuntimeAffixTemplateSpec(string Id, string Name, string PartOfSpeech, bool Final,
    IReadOnlyList<string>? PrefixSlots = null, IReadOnlyList<string>? SuffixSlots = null);
internal sealed record RuntimePhonologicalRuleSpec(string Id, string Name, string Input, string Output,
    string Environment);
internal sealed record RuntimeTextSpec(string Id, string Title, IReadOnlyList<string> Sentences);
internal sealed record RuntimeBugSymptomSpec(string Kind, IReadOnlyList<string> Words, string Reason);
internal sealed record RuntimeBugSpec(string Id, string Title, string Disclaimer,
    RuntimeBugSymptomSpec Symptom, IReadOnlyList<string> Fix, IReadOnlyList<RuntimePatchSpec> Patch);
internal sealed record RuntimePanGlossGenerateRequest(int SchemaVersion,
    RuntimePanGlossGenerateMorpheme Root, IReadOnlyList<RuntimePanGlossGenerateMorpheme> Morphemes);
internal sealed record RuntimePanGlossGenerateMorpheme(string EntryGuid, string MsaGuid, string MsaClass);
internal sealed record RuntimeAffixPatchSpec(string Id, string PartOfSpeech, IReadOnlyList<string> Slots,
    string Gloss, IReadOnlyList<RuntimeFeatureAssignmentSpec>? Features = null);
internal sealed record RuntimePatchSpec(string Op,
    string? TemplateId = null,
    string? FirstSlotId = null,
    string? SecondSlotId = null,
    string? Side = null,
    string? SlotId = null,
    int? Count = null,
    string? AllomorphId = null,
    string? AffixId = null,
    string? Form = null,
    string? EnvironmentId = null,
    RuntimeAffixPatchSpec? Affix = null,
    RuntimeAllomorphSpec? Allomorph = null);
