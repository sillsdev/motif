using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Host.Store;

/// <summary>Admits one parse per paired project store without holding a SQLite transaction during parsing.</summary>
/// <remarks>Process identity prevents reusing a PID from reviving a lease left by a crashed caller.</remarks>
public sealed class ProjectParseLease : IDisposable
{
    private readonly MotifDatabase _database;
    private readonly string _token = Guid.NewGuid().ToString("N");
    private bool _disposed;

    private ProjectParseLease(MotifDatabase database) => _database = database;

    public static ProjectParseLease? TryAcquire(MotifDatabase database)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT ProcessId, ProcessStartIdentity FROM ActiveParse WHERE Id = 1;";
            using var row = read.ExecuteReader();
            if (row.Read() && OwnerExists(row.GetInt32(0), row.GetString(1))) return null;
        }
        var lease = new ProjectParseLease(database);
        using var process = Process.GetCurrentProcess();
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT OR REPLACE INTO ActiveParse (Id, Token, ProcessId, ProcessStartIdentity)
            VALUES (1, $token, $pid, $start);
            """;
        insert.Parameters.AddWithValue("$token", lease._token);
        insert.Parameters.AddWithValue("$pid", process.Id);
        insert.Parameters.AddWithValue("$start", StartIdentity(process));
        insert.ExecuteNonQuery();
        transaction.Commit();
        return lease;
    }

    public static Refusal BusyRefusal() => new(RefusalCodes.ParseAlreadyRunning, FailureReason.Busy,
        "A parse is already running for this project in another Motif window or process. " +
        "Use that window to see its progress or cancel it.");

    /// <summary>Checks that this lease still admits writes to the transaction's project store.</summary>
    public void EnsureHeld(SqliteConnection connection, SqliteTransaction transaction)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.GetFullPath(connection.DataSource), _database.FullPath, comparison))
            throw new InvalidOperationException("The parse lease belongs to another project store.");
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Token FROM ActiveParse WHERE Id = 1;";
        if (!string.Equals(command.ExecuteScalar() as string, _token, StringComparison.Ordinal))
            throw new InvalidOperationException("The project parse lease is no longer held.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        using var connection = _database.OpenConnection();
        using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM ActiveParse WHERE Id = 1 AND Token = $token;";
        delete.Parameters.AddWithValue("$token", _token);
        delete.ExecuteNonQuery();
    }

    private static bool OwnerExists(int pid, string started)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited && StartIdentity(process) == started;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        // If the OS denies inspection, keep the live owner's admission rather than admit a competing parse.
        catch (Win32Exception) { return true; }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    private static string StartIdentity(Process process)
    {
        if (!OperatingSystem.IsLinux())
            return process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);
        // Boot-relative ticks avoid turning a wall-clock estimate into a process identity.
        var stat = File.ReadAllText($"/proc/{process.Id}/stat");
        var fields = stat[(stat.LastIndexOf(')') + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return File.ReadAllText("/proc/sys/kernel/random/boot_id").Trim() + ":" + fields[19];
    }
}
