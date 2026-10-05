using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands.Store;

public sealed class ParserRefusalRepositoryTests
{
    [Fact]
    public void ReopenedStoreKeepsTheMessageFactsAndIssuesOnlyForTheSameBaseline()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-parser-refusal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "project.motif.db");
        var project = new ProjectLocator(Path.Combine(root, "project.fwdata"), "project");
        var token = new BaselineToken("project", "sha256:" + new string('a', 64), "1", "2026-10-03T00:00:00Z", "sha256:" + new string('b', 64));
        var refusal = new Refusal("assess.parser-unavailable", FailureReason.Refused, "The grammar cannot compile.",
            new Dictionary<string, string> { ["exitCode"] = "1", ["projectPath"] = project.FullFwDataPath },
            new ParserCompileDiagnostic("PanGloss can't use this grammar.",
                [new("EnvironmentInvalid", "PhEnvironment", "item", "StringRepresentation", "/", "Fix in FieldWorks.")],
                "original parser output"));
        try
        {
            using (var database = MotifDatabase.OpenOwned(path, project, MotifSchema.CurrentSchema, new Version(1, 0)))
                new ParserRefusalRepository(database).Save(token, refusal);
            using var reopened = MotifDatabase.OpenOwned(path, project, MotifSchema.CurrentSchema, new Version(1, 0));
            var repository = new ParserRefusalRepository(reopened);
            var stored = repository.Get(token)!;
            Assert.Equal(refusal.Message, stored.Message);
            Assert.Equal("1", stored.Facts["exitCode"]);
            Assert.Equal("original parser output", stored.ParserDiagnostic!.RawText);
            Assert.Equal("/", Assert.Single(stored.ParserDiagnostic.Issues).Text);
            var changed = new BaselineToken("project", "sha256:" + new string('c', 64), "1", "2026-10-03T01:00:00Z", "sha256:" + new string('d', 64));
            Assert.Null(repository.Get(changed));
            repository.Clear(changed);
            Assert.NotNull(repository.Get(token));
            repository.Clear(token);
            Assert.Null(repository.Get(token));
        }
        finally { Directory.Delete(root, true); }
    }
}
