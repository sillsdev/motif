using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Store;

public sealed class ReportRepositoryLatestTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-report-latest-" + Guid.NewGuid().ToString("N"));

    public ReportRepositoryLatestTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void NoStoredReportOfTheKindReadsAsNull()
    {
        using var database = OpenDatabase();

        Assert.Null(new ReportRepository(database).GetLatest("parsimony"));
    }

    [Fact]
    public void OneStoredReportIsTheLatest()
    {
        using var database = OpenDatabase();
        var repository = new ReportRepository(database);
        repository.Save(Report("report/only", "parsimony", "2026-10-01T10:00:00.0000000+00:00"));

        Assert.Equal("report/only", repository.GetLatest("parsimony")!.ReportId);
    }

    [Fact]
    public void TheNewestCreatedUtcWinsWhateverTheInsertionOrder()
    {
        using var database = OpenDatabase();
        var repository = new ReportRepository(database);
        repository.Save(Report("report/newest", "parsimony", "2026-10-03T09:00:00.0000000+00:00"));
        repository.Save(Report("report/oldest", "parsimony", "2026-10-01T09:00:00.0000000+00:00"));
        repository.Save(Report("report/middle", "parsimony", "2026-10-02T09:00:00.0000000+00:00"));

        Assert.Equal("report/newest", repository.GetLatest("parsimony")!.ReportId);
    }

    [Fact]
    public void ACreatedUtcTieIsBrokenByTheGreaterReportId()
    {
        using var database = OpenDatabase();
        var repository = new ReportRepository(database);
        const string created = "2026-10-02T09:00:00.0000000+00:00";
        repository.Save(Report("report/a", "parsimony", created));
        repository.Save(Report("report/c", "parsimony", created));
        repository.Save(Report("report/b", "parsimony", created));

        Assert.Equal("report/c", repository.GetLatest("parsimony")!.ReportId);
    }

    [Fact]
    public void ReportsOfOtherKindsAreIgnoredEvenWhenNewer()
    {
        using var database = OpenDatabase();
        var repository = new ReportRepository(database);
        repository.Save(Report("report/parsimony", "parsimony", "2026-10-01T09:00:00.0000000+00:00"));
        repository.Save(Report("report/correctness", "correctness", "2026-10-09T09:00:00.0000000+00:00"));
        repository.Save(Report("report/unkinded", null, "2026-10-09T10:00:00.0000000+00:00"));

        Assert.Equal("report/parsimony", repository.GetLatest("parsimony")!.ReportId);
        Assert.Null(repository.GetLatest("difference"));
    }

    private MotifDatabase OpenDatabase()
    {
        var project = new ProjectLocator(Path.Combine(_root, "latest.fwdata"), "latest");
        return MotifDatabase.OpenOwned(Path.Combine(_root, "latest.motif.db"), project,
            MotifSchema.CurrentSchema, new Version(1, 0));
    }

    private static ReportRecord Report(string reportId, string? kind, string createdUtc) =>
        new(reportId, null, null, "{}", "{}", kind, "text", createdUtc);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
