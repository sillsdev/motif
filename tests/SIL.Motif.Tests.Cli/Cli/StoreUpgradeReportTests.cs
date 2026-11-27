using System.Text.Json.Nodes;
using SIL.Motif.Cli.Rendering;
using SIL.Motif.Host.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>A verb that upgraded a store on the way says so on standard error and, under --json, in its result.</summary>
public sealed class StoreUpgradeReportTests
{
    private static readonly StoreUpgradeNotice Notice = new("/p/a.motif.db", 62, 79, "/p/a.motif-backups/pre-upgrade-schema62-x",
        "/p/a.motif-backups/pre-upgrade-schema62-x/motif-backup.json",
        new Dictionary<string, long> { ["Baselines"] = 3, ["Jobs"] = 0, ["Assessments"] = 12 });

    [Fact]
    public void AnUpgradeAddsOneStandardErrorLineWithTheBackupAndTheNonEmptyDroppedKinds()
    {
        var error = new StringWriter();

        var result = StoreUpgradeReport.Apply([Notice], new CommandResult(0, "text\n"), asJson: false, error);

        Assert.Equal("text\n", result.Output);
        Assert.Equal("Upgraded store schema 62 -> 79; backup: /p/a.motif-backups/pre-upgrade-schema62-x; " +
            "dropped: Assessments (12), Baselines (3)" + Environment.NewLine, error.ToString());
    }

    [Fact]
    public void UnderJsonTheResultCarriesAnUpgradeObjectAndStdoutStaysOneDocument()
    {
        var result = StoreUpgradeReport.Apply([Notice], new CommandResult(0, "{\"words\":[]}"), asJson: true, new StringWriter());

        var document = JsonNode.Parse(result.Output)!.AsObject();
        Assert.NotNull(document["words"]);
        var upgrade = document["upgrade"]!.AsObject();
        Assert.Equal(62, (int)upgrade["fromSchema"]!);
        Assert.Equal("/p/a.motif-backups/pre-upgrade-schema62-x", (string)upgrade["backupPath"]!);
        Assert.Equal(12, (long)upgrade["dropped"]!["Assessments"]!);
        Assert.Contains("a backup is at", (string)upgrade["message"]!);
    }

    [Fact]
    public void NoUpgradeChangesNothing()
    {
        var error = new StringWriter();
        var original = new CommandResult(0, "{\"a\":1}");

        Assert.Same(original, StoreUpgradeReport.Apply([], original, asJson: true, error));
        Assert.Empty(error.ToString());
    }
}
