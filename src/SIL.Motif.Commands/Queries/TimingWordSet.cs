using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands.Queries;

/// <summary>The project standing used to choose one Compare matrix cell for Timing.</summary>
public enum TimingStanding { Approved, Candidate, Rejected, IncorrectSpelling, NotPresent }

/// <summary>A word selection for the Timing command, decoded from its wire value at the boundary.</summary>
public abstract record TimingWordSet
{
    public sealed record All : TimingWordSet;
    public sealed record StepLimited : TimingWordSet;
    public sealed record Slowest : TimingWordSet;
    public sealed record MatrixCell(TimingStanding Standing, CompareColumnKind Column) : TimingWordSet;
    public sealed record Named(string Name) : TimingWordSet;

    public string ToWireValue() => this switch
    {
        All => "all",
        StepLimited => "step-limit",
        Slowest => "slowest",
        MatrixCell cell => $"cell:{StandingName(cell.Standing)}:{ColumnName(cell.Column)}",
        Named named => named.Name,
        _ => throw new ArgumentOutOfRangeException(nameof(TimingWordSet)),
    };

    public static TimingWordSet? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var name = value.Trim();
        if (name == "all") return new All();
        if (name == "step-limit") return new StepLimited();
        if (name == "slowest") return new Slowest();
        if (!name.StartsWith("cell:", StringComparison.Ordinal)) return new Named(name);
        var parts = name[5..].Split(':');
        if (parts.Length != 2 || !TryStanding(parts[0], out var standing) ||
            !TryColumn(parts[1], out var column)) return null;
        return new MatrixCell(standing, column);
    }

    public static string StandingName(TimingStanding standing) => standing switch
    {
        TimingStanding.Approved => ProjectStanding.Approved,
        TimingStanding.Candidate => ProjectStanding.Candidate,
        TimingStanding.Rejected => ProjectStanding.Rejected,
        TimingStanding.IncorrectSpelling => ProjectStanding.IncorrectSpelling,
        TimingStanding.NotPresent => ProjectStanding.NotPresent,
        _ => throw new ArgumentOutOfRangeException(nameof(standing)),
    };

    private static string ColumnName(CompareColumnKind column) => column switch
    {
        CompareColumnKind.Match => "match",
        CompareColumnKind.NoMatch => "no-match",
        CompareColumnKind.NoParse => "no-parse",
        CompareColumnKind.Timeout => "unknown",
        CompareColumnKind.Skipped => "skipped",
        _ => throw new ArgumentOutOfRangeException(nameof(column)),
    };

    private static bool TryStanding(string name, out TimingStanding standing)
    {
        standing = name switch
        {
            ProjectStanding.Approved => TimingStanding.Approved,
            ProjectStanding.Candidate => TimingStanding.Candidate,
            ProjectStanding.Rejected => TimingStanding.Rejected,
            ProjectStanding.IncorrectSpelling => TimingStanding.IncorrectSpelling,
            ProjectStanding.NotPresent => TimingStanding.NotPresent,
            _ => (TimingStanding)(-1),
        };
        return Enum.IsDefined(standing);
    }

    private static bool TryColumn(string name, out CompareColumnKind column)
    {
        column = name switch
        {
            "match" => CompareColumnKind.Match,
            "no-match" => CompareColumnKind.NoMatch,
            "no-parse" => CompareColumnKind.NoParse,
            "unknown" => CompareColumnKind.Timeout,
            "skipped" => CompareColumnKind.Skipped,
            _ => (CompareColumnKind)(-1),
        };
        return Enum.IsDefined(column);
    }
}
