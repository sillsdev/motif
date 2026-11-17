using System.Text.RegularExpressions;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>
/// The project at its new path, whose sibling Motif store should be pointed at it, and the confirmation phrase
/// <see cref="StoreRelocateCommand.ConfirmationPhrase"/>. <c>MatchByName</c> states that the project is the same one
/// although Motif cannot prove it from a LibLCM project GUID.
/// </summary>
public sealed record StoreRelocateRequest(string ProjectPath, string? Confirmation = null, bool MatchByName = false);

/// <summary>
/// The store file, the project path it was registered to and the one it serves now, and when. <c>Changed</c> is
/// <c>false</c> when the store already pointed at <c>NewProjectPath</c>, so nothing was recorded.
/// <c>IdentityEvidence</c> says what showed the two paths hold one project: <c>project-guid</c> when the store's
/// Baselines or the old project file carried the LibLCM project GUID, or <c>name</c> when the caller asserted it.
/// <c>OldProjectStillExists</c> is <c>true</c> when the file at the old path remains, as after a copy.
/// </summary>
public sealed record StoreRelocateResponse(string StorePath, string OldProjectPath, string NewProjectPath,
    DateTimeOffset RelocatedUtc, bool Changed, string IdentityEvidence, bool OldProjectStillExists = false);

/// <summary>
/// Points the Motif store beside a copied or moved project at the project's new path.
/// </summary>
/// <remarks>
/// <para>
/// The store is relocated only when the new path holds the project the store belongs to: the LibLCM project GUID
/// of the new file must equal the GUID in the store's Baselines or in the file at the old path when that still
/// exists. With neither to compare the command refuses, unless the caller passes <c>MatchByName</c>, which records the
/// basis as <c>name</c>; the project name the store is bound to must match either way. A different project is refused
/// with <see cref="RefusalCodes.StoreOtherProject"/> and the store is left byte for byte as it was, pinned by `ADifferentProjectIsRefusedAndTheStoreIsLeftAsItWas`.
/// </para>
/// <para>
/// It refuses without the confirmation phrase, while another program holds the project file, and while any Motif
/// process holds the store open. The path change and a <c>StoreRelocations</c> row (old path, new path, time) commit
/// in one transaction. The refusals are pinned by `WithoutTheConfirmationPhraseNothingChangesAndThePhraseIsShown`,
/// `AHeldProjectIsRefusedAndTheStoreIsLeftAsItWas` and `AStoreAnotherMotifProcessHoldsOpenIsRefusedAndLeftAsItWas`.
/// </para>
/// </remarks>
public static partial class StoreRelocateCommand
{
    /// <summary>The phrase a caller passes to confirm the relocation.</summary>
    public const string ConfirmationPhrase = "Point this Motif store at this project's new path?";

    /// <summary>Points the store beside <see cref="StoreRelocateRequest.ProjectPath"/> at that project.</summary>
    public static CommandOutcome<StoreRelocateResponse> Run(StoreRelocateRequest request) =>
        Run(request, null);

    internal static CommandOutcome<StoreRelocateResponse> Run(StoreRelocateRequest request, TimeProvider? timeProvider)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.Confirmation, ConfirmationPhrase, StringComparison.Ordinal))
            return Refused(RefusalCodes.StoreRelocateConfirmationRequired, FailureReason.InvalidArgument,
                $"Confirm this action with: {ConfirmationPhrase}", request.ProjectPath);
        if (string.IsNullOrWhiteSpace(request.ProjectPath))
            return Refused(RefusalCodes.ProjectInvalid, FailureReason.InvalidArgument, "A project path is required.",
                request.ProjectPath);

        var located = ProjectStoreCommand.LocateProject(request.ProjectPath);
        if (!located.Succeeded) return CommandOutcome<StoreRelocateResponse>.Refused(located.Refusal!);
        var project = located.Value!;

        var storePath = ProjectDatabaseCatalog.DatabasePathFor(project);
        if (!File.Exists(storePath))
            return Refused(RefusalCodes.StoreRelocateNoStore, FailureReason.NotFound,
                $"There is no Motif store, '{Path.GetFileName(storePath)}', beside this project to relocate.",
                project.FullFwDataPath, storePath);

        // A project another program holds is changing under it; its path should not be re-pointed meanwhile.
        if (ProjectFileHeldException.IsHeld(project.FullFwDataPath))
            return Refused(RefusalCodes.ProjectInUse, FailureReason.Busy,
                ProjectStoreCommand.ProjectInUseMessage(project.FullFwDataPath, "relocate the store"),
                project.FullFwDataPath);

        var newGuid = FwDataProjectGuid.TryRead(project.FullFwDataPath);
        if (newGuid is null)
            return Refused(RefusalCodes.ProjectInvalid, FailureReason.InvalidArgument,
                $"'{Path.GetFileName(project.FullFwDataPath)}' is not a FieldWorks project file: it has no language project.",
                project.FullFwDataPath);

        var evidence = "name";
        try
        {
            var result = MotifDatabase.Relocate(storePath, project, registration =>
            {
                evidence = CheckSameProject(registration, project, newGuid, storePath, request.MatchByName);
                return evidence;
            }, timeProvider: timeProvider);
            return CommandOutcome<StoreRelocateResponse>.Success(new StoreRelocateResponse(storePath,
                result.OldFwDataPath, result.NewFwDataPath, result.RelocatedUtc, result.Changed, evidence,
                result.Changed && File.Exists(result.OldFwDataPath)));
        }
        catch (StoreRelocationRefusedException exception)
        {
            var facts = Facts(project.FullFwDataPath, storePath);
            return CommandOutcome<StoreRelocateResponse>.Refused(
                new Refusal(exception.Code, FailureReason.Refused, exception.Message, facts));
        }
        catch (MotifStoreLockException exception)
        {
            return Refused(RefusalCodes.ProjectBusy, FailureReason.Busy,
                "The project store is in use by another Motif process. Close it and try again.",
                project.FullFwDataPath, storePath, exception.Message);
        }
        catch (MotifStoreVersionException exception)
        {
            return Refused(RefusalCodes.StoreOtherVersion, FailureReason.Refused, exception.Message,
                project.FullFwDataPath, storePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException
            or NotSupportedException)
        {
            return Refused(RefusalCodes.ProjectStoreIo, FailureReason.Refused,
                $"Motif could not read or change its file for this project, '{Path.GetFileName(storePath)}'.",
                project.FullFwDataPath, storePath, exception.Message);
        }
    }

    private static string CheckSameProject(StoreRegistration registration, ProjectLocator project, string newGuid,
        string storePath, bool matchByName)
    {
        var registered = registration.Registered;
        if (!StringComparer.Ordinal.Equals(registered.FieldWorksProjectIdentity, project.FieldWorksProjectIdentity))
            throw Other(registered, project, storePath,
                $"it is registered to the project named '{registered.FieldWorksProjectIdentity}', not " +
                $"'{project.FieldWorksProjectIdentity}'");

        // The GUIDs of the project the store has measured, then of the file it was registered to if that still exists.
        var known = registration.BaselineProjectGuids.ToList();
        if (known.Count == 0 && File.Exists(registered.FullFwDataPath) &&
            !StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(registered.FullFwDataPath), project.FullFwDataPath) &&
            FwDataProjectGuid.TryRead(registered.FullFwDataPath) is { } oldGuid)
            known.Add(oldGuid);
        if (known.Count == 0)
        {
            if (matchByName) return "name";
            throw new StoreRelocationRefusedException(RefusalCodes.StoreRelocateIdentityUnproven,
                $"Motif cannot show that '{project.FullFwDataPath}' is the project this store belongs to: the store holds no " +
                $"Baseline and the project file at '{registered.FullFwDataPath}' is gone or unreadable, so there is no language " +
                $"project GUID to compare with {newGuid}. If you know it is the same project, run the command again with " +
                "--match-by-name. The store was not changed.");
        }
        if (known.Any(guid => !string.Equals(guid, newGuid, StringComparison.OrdinalIgnoreCase)))
            throw Other(registered, project, storePath,
                $"its project has the language project GUID {known.First(g => !string.Equals(g, newGuid, StringComparison.OrdinalIgnoreCase))}, " +
                $"and the project here has {newGuid}");
        return "project-guid";
    }

    private static StoreRelocationRefusedException Other(
        ProjectLocator registered, ProjectLocator project, string storePath, string why) =>
        new(RefusalCodes.StoreOtherProject,
            $"The Motif file '{Path.GetFileName(storePath)}' beside '{project.FullFwDataPath}' is not this project's: {why}. " +
            "It was not changed. Relocate a store only to the copy or move of the project it belongs to.");

    private static Dictionary<string, string> Facts(string fwDataPath, string? storePath)
    {
        var facts = new Dictionary<string, string>(StringComparer.Ordinal) { ["fwDataPath"] = fwDataPath };
        if (storePath is not null) facts[RefusalFactNames.StorePath] = storePath;
        return facts;
    }

    private static CommandOutcome<StoreRelocateResponse> Refused(string code, FailureReason reason, string message,
        string? fwDataPath, string? storePath = null, string? exceptionDetail = null)
    {
        var facts = Facts(fwDataPath ?? string.Empty, storePath);
        if (exceptionDetail is not null) facts["exceptionDetail"] = exceptionDetail;
        return CommandOutcome<StoreRelocateResponse>.Refused(new Refusal(code, reason, message, facts));
    }
}

/// <summary>Reads the LibLCM language project GUID out of a <c>.fwdata</c> file without opening the project.</summary>
internal static partial class FwDataProjectGuid
{
    [GeneratedRegex("""<rt\s[^>]*class="LangProject"[^>]*>""", RegexOptions.CultureInvariant)]
    private static partial Regex LangProjectElement();

    [GeneratedRegex(@"\bguid=""([0-9A-Fa-f-]{36})""", RegexOptions.CultureInvariant)]
    private static partial Regex GuidAttribute();

    /// <summary>The project's GUID, or <see langword="null"/> when the file has no language project.</summary>
    internal static string? TryRead(string fwDataPath)
    {
        try
        {
            using var reader = new StreamReader(fwDataPath);
            while (reader.ReadLine() is { } line)
            {
                var element = LangProjectElement().Match(line);
                if (!element.Success) continue;
                var guid = GuidAttribute().Match(element.Value);
                return guid.Success ? guid.Groups[1].Value.ToLowerInvariant() : null;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
        return null;
    }
}
