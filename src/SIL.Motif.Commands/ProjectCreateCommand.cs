using SIL.Motif.Projection;
using System.Globalization;
using System.Text;
using SIL.LCModel;
using SIL.LCModel.Core.WritingSystems;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.WritingSystems;

namespace SIL.Motif.Commands;

/// <summary>
/// Creates a new, blank FieldWorks project with one vernacular and one analysis writing system, so a grammar can be
/// built from nothing through Proposals.
/// </summary>
/// <remarks>
/// <para>
/// The project holds no phonemes, lexicon or texts: everything after creation goes through Proposals. It does hold an
/// empty phoneme set with FieldWorks' reserved word (<c>#</c>) and morpheme (<c>+</c>) boundary markers, because
/// without <c>+</c> the parser cannot compile the grammar at all; with them a new project can be Assessed (every
/// word unanalysed) before its first Proposal.
/// </para>
/// <para>
/// Word-forming characters go into the vernacular writing system's LDML <c>main</c> character set, which FieldWorks
/// and Motif's corpus tokeniser both consult before Unicode's letter classes; letters stay word-forming without it.
/// The command never overwrites: an existing project folder is refused, and a failed creation removes the folder it
/// made.
/// </para>
/// </remarks>
public static class ProjectCreateCommand
{
    /// <summary>The analysis writing system a request without one gets.</summary>
    public const string DefaultAnalysisTag = "en";

    private static readonly char[] PortableInvalidNameCharacters = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Creates the project with the default loader.</summary>
    public static CommandOutcome<ProjectCreatedResponse> Create(ProjectCreateRequest request) =>
        Create(request, new FwDataProjectLoader());

    /// <summary>Creates the project with an explicit loader for controlled callers.</summary>
    public static CommandOutcome<ProjectCreatedResponse> Create(ProjectCreateRequest request, FwDataProjectLoader loader)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(loader);

        if (!IsPortableProjectName(request.Name))
            return Refused("project-create.invalid-name", FailureReason.InvalidArgument,
                "A project name must be non-empty, must not start or end with a space or end with a dot, and must not " +
                "contain < > : \" / \\ | ? * or control characters, or be a reserved device name such as CON.",
                ("name", request.Name));
        var analysisTag = string.IsNullOrWhiteSpace(request.AnalysisTag) ? DefaultAnalysisTag : request.AnalysisTag.Trim();
        var vernacularTag = request.VernacularTag?.Trim() ?? "";
        foreach (var (role, tag) in new[] { ("vernacular", vernacularTag), ("analysis", analysisTag) })
        {
            if (!IetfLanguageTag.IsValid(tag))
                return Refused("project-create.invalid-writing-system", FailureReason.InvalidArgument,
                    $"'{tag}' is not a valid BCP 47 language tag for the {role} writing system.", (role, tag));
        }
        if (string.Equals(vernacularTag, analysisTag, StringComparison.OrdinalIgnoreCase))
            return Refused("project-create.invalid-writing-system", FailureReason.InvalidArgument,
                "The vernacular and analysis writing systems must differ.", ("vernacular", vernacularTag));
        if (!TryReadWordForming(request.WordFormingCharacters, out var wordForming, out var badCharacter))
            return Refused("project-create.invalid-word-forming", FailureReason.InvalidArgument,
                "Word-forming characters must be single, visible characters; whitespace and control characters " +
                "cannot form words.", ("character", badCharacter));

        string parentFolder;
        string projectFolder;
        string fwDataPath;
        try
        {
            parentFolder = Path.GetFullPath(request.ParentFolder);
            projectFolder = Path.Combine(parentFolder, request.Name);
            fwDataPath = Path.Combine(projectFolder, request.Name + ".fwdata");
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return Refused("project-create.invalid-folder", FailureReason.InvalidArgument, exception.Message,
                ("folder", request.ParentFolder));
        }
        if (!Directory.Exists(parentFolder))
            return Refused("project-create.folder-not-found", FailureReason.NotFound,
                $"The folder '{parentFolder}' does not exist. Create it first, or name an existing folder.",
                ("folder", parentFolder));
        if (Directory.Exists(projectFolder) || File.Exists(projectFolder))
            return Refused("project-create.exists", FailureReason.Refused,
                $"'{projectFolder}' already exists. Motif never overwrites a project; choose another name or folder.",
                ("projectFolder", projectFolder));

        try
        {
            using (var cache = loader.CreateBlankProject(fwDataPath, vernacularTag, analysisTag))
            {
                NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                {
                    ConfigureVernacular(cache.ServiceLocator.WritingSystems.DefaultVernacularWritingSystem,
                        request.VernacularName, wordForming);
                    SeedBoundaryMarkers(cache);
                });
                cache.ServiceLocator.WritingSystemManager.Save();
                loader.Save(cache);
            }
            return Verify(loader, fwDataPath, request.Name);
        }
        catch (ProjectWritingSystemsUnavailableException)
        {
            // A setup problem still leaves nothing behind, so the same name can be created once it is fixed.
            RemoveCreatedFolder(projectFolder);
            throw;
        }
        catch (Exception exception)
        {
            RemoveCreatedFolder(projectFolder);
            return Refused("project-create.failed", FailureReason.Refused,
                "LibLCM could not create the project, and Motif removed what it had written: " + exception.Message,
                ("projectFolder", projectFolder));
        }
    }

    private static void ConfigureVernacular(CoreWritingSystemDefinition vernacular, string? name, IReadOnlyList<string> wordForming)
    {
        if (!string.IsNullOrWhiteSpace(name))
            vernacular.Language = new LanguageSubtag(vernacular.Language, name.Trim());
        if (wordForming.Count == 0) return;
        if (!vernacular.CharacterSets.TryGet("main", out var main))
        {
            main = new CharacterSetDefinition("main");
            vernacular.CharacterSets.Add(main);
        }
        foreach (var character in wordForming)
            if (!main.Characters.Contains(character)) main.Characters.Add(character);
    }

    // FieldWorks' reserved markers, not content: the parser cannot compile a phoneme table without "+".
    private static void SeedBoundaryMarkers(LcmCache cache)
    {
        var services = cache.ServiceLocator;
        var set = services.GetInstance<IPhPhonemeSetFactory>().Create();
        cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.Add(set);
        foreach (var (guid, representation) in new[]
                 { (LangProjectTags.kguidPhRuleWordBdry, "#"), (LangProjectTags.kguidPhRuleMorphBdry, "+") })
        {
            var marker = services.GetInstance<IPhBdryMarkerFactory>().Create(guid);
            set.BoundaryMarkersOC.Add(marker);
            marker.Name.set_String(cache.DefaultVernWs, representation);
            var code = services.GetInstance<IPhCodeFactory>().Create();
            marker.CodesOS.Add(code);
            code.Representation.set_String(cache.DefaultVernWs, representation);
        }
    }

    private static CommandOutcome<ProjectCreatedResponse> Verify(FwDataProjectLoader loader, string fwDataPath, string name)
    {
        using var cache = loader.LoadCache(fwDataPath);
        var writingSystems = cache.ServiceLocator.WritingSystems;
        var vernacular = writingSystems.DefaultVernacularWritingSystem;
        var analysis = writingSystems.DefaultAnalysisWritingSystem;
        var wordForming = vernacular.CharacterSets.TryGet("main", out var main)
            ? main.Characters.ToArray()
            : [];
        return CommandOutcome<ProjectCreatedResponse>.Success(new ProjectCreatedResponse(
            fwDataPath, name, vernacular.LanguageTag, vernacular.Language?.Name ?? vernacular.LanguageTag,
            wordForming, analysis.LanguageTag));
    }

    internal static bool IsPortableProjectName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name != name.Trim() || name.EndsWith('.')) return false;
        if (name.IndexOfAny(PortableInvalidNameCharacters) >= 0 || name.Any(char.IsControl)) return false;
        var stem = name.Split('.')[0];
        return !ReservedDeviceNames.Contains(stem);
    }

    internal static bool TryReadWordForming(string? raw, out IReadOnlyList<string> characters, out string? bad)
    {
        var result = new List<string>();
        characters = result;
        bad = null;
        if (string.IsNullOrEmpty(raw)) return true;
        var elements = StringInfo.GetTextElementEnumerator(raw.Normalize(NormalizationForm.FormC));
        while (elements.MoveNext())
        {
            var element = (string)elements.Current;
            var runes = element.EnumerateRunes().ToArray();
            if (runes.Length != 1 || Rune.IsWhiteSpace(runes[0]) || Rune.IsControl(runes[0]))
            {
                bad = element;
                return false;
            }
            if (!result.Contains(element, StringComparer.Ordinal)) result.Add(element);
        }
        return true;
    }

    private static void RemoveCreatedFolder(string projectFolder)
    {
        try
        {
            if (Directory.Exists(projectFolder)) Directory.Delete(projectFolder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The refusal still names the folder, so a leftover one is visible rather than silent.
        }
    }

    private static CommandOutcome<ProjectCreatedResponse> Refused(
        string code, FailureReason reason, string message, params (string Key, string? Value)[] facts)
    {
        var factMap = facts.Where(fact => fact.Value is not null)
            .ToDictionary(fact => fact.Key, fact => fact.Value!, StringComparer.Ordinal);
        return CommandOutcome<ProjectCreatedResponse>.Refused(new Refusal(code, reason, message, factMap));
    }
}
