using System;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// A SYNTHETIC EXAMPLE grammar for grammar findings to name, written onto a seeded project's copy, and the
/// grammar-health reports that name its objects.
/// </summary>
/// <remarks>
/// A vowel class V conditions the second seeded entry's form through the environment <c>/ _ [V]</c>; a class VC
/// with a longer abbreviation conditions the first through <c>/ [VC] _</c>, so a match on V must not catch it.
/// A regular rule's left context names V, and the phoneme u is written with the code <c>u</c>.
/// </remarks>
internal sealed record WarningGrammar(
    string FwDataPath, Guid Vowels, Guid VowelsBefore, Guid Longer, Guid Harmony, Guid U, Guid FirstMsa)
{
    /// <summary>The seeded Text, when <see cref="Author"/> was asked to write it.</summary>
    public Guid? TextId { get; init; }

    public static WarningGrammar Author(PristineProjectFixture pristine, bool withText = false)
    {
        var fwDataPath = pristine.CopyProjectFile();
        var loader = new FwDataProjectLoader();
        using var cache = loader.LoadCache(fwDataPath);
        var services = cache.ServiceLocator;
        var phonology = cache.LangProject.PhonologicalDataOA;
        var vern = cache.DefaultVernWs;
        var anal = cache.DefaultAnalWs;
        IPhNaturalClass vowels = null!, longer = null!;
        IPhEnvironment vowelsBefore = null!, longerAfter = null!;
        IPhRegularRule harmony = null!;
        IPhPhoneme u = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            if (phonology.PhonemeSetsOS.Count == 0)
                phonology.PhonemeSetsOS.Add(services.GetInstance<IPhPhonemeSetFactory>().Create());
            OwnedBoundaryMarkerFixture.EnsureReservedMarkers(cache, phonology.PhonemeSetsOS[0]);
            u = services.GetInstance<IPhPhonemeFactory>().Create();
            phonology.PhonemeSetsOS[0].PhonemesOC.Add(u);
            u.Name.set_String(vern, "u");
            u.CodesOS.Clear();
            var code = services.GetInstance<IPhCodeFactory>().Create();
            u.CodesOS.Add(code);
            code.Representation.set_String(vern, "u");

            vowels = NaturalClass(services, phonology, anal, "V", u);
            longer = NaturalClass(services, phonology, anal, "VC", u);
            vowelsBefore = Environment(services, phonology, vern, "/ _ [V]");
            longerAfter = Environment(services, phonology, vern, "/ [VC] _");
            Stem(services, pristine.Seed.SecondEntryId).PhoneEnvRC.Add(vowelsBefore);
            Stem(services, pristine.Seed.FirstEntryId).PhoneEnvRC.Add(longerAfter);

            harmony = services.GetInstance<IPhRegularRuleFactory>().Create();
            phonology.PhonRulesOS.Add(harmony);
            harmony.Name.set_String(anal, "Vowel harmony");
            if (harmony.RightHandSidesOS.Count == 0)
                harmony.RightHandSidesOS.Add(services.GetInstance<IPhSegRuleRHSFactory>().Create());
            var context = services.GetInstance<IPhSimpleContextNCFactory>().Create();
            harmony.RightHandSidesOS[0].LeftContextOA = context;
            context.FeatureStructureRA = vowels;
        });
        var firstMsa = services.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId)
            .MorphoSyntaxAnalysesOC.First().Guid;
        var text = withText ? SeededProject.SeedText(cache, pristine.Seed) : null;
        loader.Save(cache);
        return new WarningGrammar(fwDataPath, vowels.Guid, vowelsBefore.Guid, longer.Guid, harmony.Guid, u.Guid,
            firstMsa)
        {
            TextId = text?.TextId,
        };
    }

    private static IPhNaturalClass NaturalClass(ILcmServiceLocator services, IPhPhonData phonology, int anal,
        string abbreviation, IPhPhoneme member)
    {
        var naturalClass = services.GetInstance<IPhNCSegmentsFactory>().Create();
        phonology.NaturalClassesOS.Add(naturalClass);
        naturalClass.Abbreviation.set_String(anal, abbreviation);
        naturalClass.Name.set_String(anal, abbreviation + " class");
        naturalClass.SegmentsRC.Add(member);
        return naturalClass;
    }

    private static IPhEnvironment Environment(ILcmServiceLocator services, IPhPhonData phonology, int vern,
        string representation)
    {
        var environment = services.GetInstance<IPhEnvironmentFactory>().Create();
        phonology.EnvironmentsOS.Add(environment);
        environment.StringRepresentation = TsStringUtils.MakeString(representation, vern);
        return environment;
    }

    private static IMoStemAllomorph Stem(ILcmServiceLocator services, Guid entry) =>
        (IMoStemAllomorph)services.GetInstance<ILexEntryRepository>().GetObject(entry).LexemeFormOA;
}

/// <summary>A grammar-health report, schema 4, whose findings each name the given subjects.</summary>
internal static class GrammarHealthReports
{
    /// <summary>One named object: its FieldWorks class, its title, and its GUID, if PanGloss recorded one.</summary>
    internal sealed record Subject(string Kind, string Title, Guid? Guid = null);

    public static string With(params (string Code, Subject[] Subjects)[] findings) => JsonSerializer.Serialize(new
    {
        schema_version = 4, locale = "en",
        fieldworks_project = new { name = (string?)null, source = (string?)null },
        summary = findings.GroupBy(finding => finding.Code).Select(group => new
        {
            code = group.Key, group_name = group.Key, level = "warning", count = group.Count(),
        }),
        diagnostics = findings.Select(finding => new
        {
            level = "warning", code = finding.Code, group_name = finding.Code, origin = "check",
            description = finding.Code + " described", guidance = (string?)null,
                    title = finding.Code, explanation = finding.Code + " described", help_path = (string?)null, help_body = (string?)null,
                    fieldworks_places = Array.Empty<object>(), scope = finding.Subjects.Length == 0 ? "project_settings" : "object",
            subjects = finding.Subjects.Select(subject => new
            {
                kind = subject.Kind, status = "object", field = (string?)null, source_class = (string?)null, title = subject.Title, subtitle = (string?)null,
                guid = subject.Guid?.ToString("D"), internal_id = (string?)null,
                fieldworks = new { status = "unavailable", reason = "missing_project", guid = subject.Guid?.ToString("D") },
            }),
        }),
    });
}
