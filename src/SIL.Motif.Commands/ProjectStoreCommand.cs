using SIL.Motif.Host;
using System;
using System.Collections.Generic;
using System.IO;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>
/// Runs one verb against a project's paired Motif store, and turns any failure into the CLI's contract.
/// </summary>
/// <remarks>
/// <para>
/// What a verb author would otherwise have to know: that the project file must exist before it can key a
/// workspace, that the schema constant and the product version go into the catalog together, and which
/// exception out of the store means which <see cref="FailureReason"/>. That last one is the real content
/// here — a held database and a malformed row both surface as exceptions thrown far away, and telling
/// them apart decides whether a caller is told to retry.
/// </para>
/// <para>
/// A verb supplies only what it does with an open store. Everything above is settled once.
/// </para>
/// </remarks>
public static class ProjectStoreCommand
{
    /// <summary>Opens the paired store for a project, runs the verb, and translates any failure.</summary>
    /// <param name="fwDataPath">The path to the FieldWorks project data file.</param>
    /// <param name="productVersion">The worker version used for store compatibility checks.</param>
    /// <param name="act">The verb to run against the open project store.</param>
    /// <param name="ownershipPatience">Maximum wait for the creation lock; defaults to 30 seconds.</param>
    public static CommandOutcome<T> Run<T>(string fwDataPath, string productVersion,
        Func<MotifDatabase, ProjectLocator, CommandOutcome<T>> act,
        TimeSpan? ownershipPatience = null) where T : class
    {
        ArgumentNullException.ThrowIfNull(act);

        ProjectLocator project;
        try
        {
            project = Locate(fwDataPath);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            return CommandOutcome<T>.Refused(new Refusal(
                exception is FileNotFoundException ? "project.not-found" : "project.invalid",
                FailureReason.InvalidArgument, exception.Message, Fact(fwDataPath)));
        }

        var catalog = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, ParseVersion(productVersion));
        MotifDatabase database;
        try
        {
            database = catalog.OpenOwned(project, ownershipPatience);
        }
        catch (MotifStoreLockException exception)
        {
            return CommandOutcome<T>.Refused(
                new Refusal("project.busy", FailureReason.Busy, exception.Message, Fact(fwDataPath)));
        }
        catch (IOException exception)
        {
            return CommandOutcome<T>.Refused(StoreRefusal(exception, "project.store-io", fwDataPath));
        }
        catch (UnauthorizedAccessException exception)
        {
            return CommandOutcome<T>.Refused(StoreRefusal(exception, "project.store-io", fwDataPath));
        }
        catch (NotSupportedException exception)
        {
            return CommandOutcome<T>.Refused(StoreRefusal(exception, "project.store-io", fwDataPath));
        }
        catch (InvalidDataException exception)
        {
            return CommandOutcome<T>.Refused(StoreRefusal(exception, "project.store-io", fwDataPath));
        }

        using (database)
        {
            try
            {
                return act(database, project);
            }
            catch (IOException exception)
            {
                return CommandOutcome<T>.Refused(StoreRefusal(exception, "project.operation-io", fwDataPath));
            }
            catch (UnauthorizedAccessException exception)
            {
                return CommandOutcome<T>.Refused(StoreRefusal(exception, "project.operation-io", fwDataPath));
            }
            catch (NotSupportedException exception)
            {
                return CommandOutcome<T>.Refused(StoreRefusal(exception, "project.operation-io", fwDataPath));
            }
            catch (InvalidDataException exception)
            {
                return CommandOutcome<T>.Refused(StoreRefusal(exception, "project.operation-io", fwDataPath));
            }
        }
    }

    private static Dictionary<string, string> Fact(string fwDataPath) =>
        new(StringComparer.Ordinal) { ["fwDataPath"] = fwDataPath };

    private static string MessageFor(Exception exception) =>
        string.IsNullOrWhiteSpace(exception.Message)
            ? "The project could not access required storage."
            : exception.Message;

    private static Refusal StoreRefusal(Exception exception, string ioCode, string fwDataPath) => exception switch
    {
        IOException or UnauthorizedAccessException => new Refusal(
            ioCode, FailureReason.Refused, MessageFor(exception), Fact(fwDataPath)),
        NotSupportedException => new Refusal(
            "store.unsupported", FailureReason.Refused, exception.Message, Fact(fwDataPath)),
        InvalidDataException => new Refusal(
            "store.inconsistent", FailureReason.StoreInconsistent, exception.Message, Fact(fwDataPath)),
        _ => throw new ArgumentOutOfRangeException(nameof(exception), exception.GetType(),
            "The exception is not a recognized project-store failure."),
    };

    /// A malformed product version must not stop a verb; the compatibility floor it feeds is a lower bound.
    private static Version ParseVersion(string productVersion) =>
        Version.TryParse(productVersion, out var parsed) ? parsed : MotifProductVersion.Current;

    /// The file must exist: an unresolvable path would key a second, empty workspace instead of the real one.
    private static ProjectLocator Locate(string fwDataPath)
    {
        var full = Path.GetFullPath(fwDataPath);
        if (!File.Exists(full))
            throw new FileNotFoundException("Project file not found: '" + full + "'.", full);
        return new ProjectLocator(full, Path.GetFileNameWithoutExtension(full));
    }
}
