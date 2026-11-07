using SIL.Motif.Host.Config;
using Xunit;

namespace SIL.Motif.Tests.Config;

public sealed class ReviewWordSetConfigurationTests
{
    [Fact]
    public void FiveRolesRoundTripWithoutInventedQueries()
    {
        var declarations = string.Join('\n', new[] { "sampled-tokens", "paradigm-cells", "reference", "regression", "negative" }
            .Select((role, i) => $"[[review-word-set]]\nid = \"set-{i}\"\nname = \"Set {i}\"\nrole = \"{role}\"\nmanifest = \"set-{i}.jsonl\"\n"));
        var original = ProjectConfigurationFile.Parse(declarations, "project.toml");
        var copy = ProjectConfigurationFile.Parse(ProjectConfigurationFile.Render(original), "project.toml");
        Assert.Equal(original.ReviewWordSets, copy.ReviewWordSets);
    }

    [Fact]
    public void DuplicateIdsAndUnknownKeysNameLine()
    {
        const string set = "[[review-word-set]]\nid = \"sample\"\nname = \"Sample\"\nrole = \"reference\"\nselection = \"Words\"\n";
        Assert.Contains("line 6", Assert.Throws<ProjectConfigurationException>(() =>
            ProjectConfigurationFile.Parse(set + set, "project.toml")).Message);
        Assert.Contains("line 6", Assert.Throws<ProjectConfigurationException>(() =>
            ProjectConfigurationFile.Parse(set + "typo = \"x\"\n", "project.toml")).Message);
    }

    [Fact]
    public void SourceKeysAreExclusive()
    {
        Assert.Throws<ProjectConfigurationException>(() => ProjectConfigurationFile.Parse(
            "[[review-word-set]]\nid = \"sample\"\nname = \"Sample\"\nrole = \"reference\"\nselection = \"Words\"\nmanifest = \"words.jsonl\"", "project.toml"));
    }

    [Theory]
    [InlineData("sampled-tokens")]
    [InlineData("paradigm-cells")]
    public void TokenAndCellRolesRequireTheirOwnLedger(string role)
    {
        Assert.Throws<ProjectConfigurationException>(() => ProjectConfigurationFile.Parse(
            $"[[review-word-set]]\nid = \"sample\"\nname = \"Sample\"\nrole = \"{role}\"\nselection = \"Words\"", "project.toml"));
    }

    [Fact]
    public void RelativePathsResolveBesideProject()
    {
        var set = new ReviewWordSetConfiguration("sample", "Sample", "reference", null, "review/words.jsonl");
        var configurationPath = Path.Combine(Path.GetTempPath(), "project", "project.motif.toml");
        Assert.Equal(Path.Combine(Path.GetTempPath(), "project", "review", "words.jsonl"), set.ResolveManifestPath(configurationPath));
    }
    [Fact]
    public void DuplicateCaseIdsAndNonpositiveWeightsAreRejected()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".jsonl");
        const string header = "{\"schema\":\"motif.review-word-set\",\"schemaVersion\":1}\n";
        const string row = "{\"caseId\":\"case\",\"word\":\"abc\",\"writingSystemId\":\"en\",\"source\":{\"citation\":\"source\"},\"expectationRefs\":[],\"occurrenceId\":\"occ\",\"textId\":\"text\",\"position\":0,\"weight\":1}";
        try
        {
            File.WriteAllText(path, header + row + "\n" + row + "\n");
            Assert.Contains("duplicate caseId", Assert.Throws<ProjectConfigurationException>(() => ReviewWordSetManifestReader.Read(path, "sampled-tokens")).Message);
            File.WriteAllText(path, header + row.Replace("\"weight\":1", "\"weight\":0") + "\n");
            Assert.Contains("positive weight", Assert.Throws<ProjectConfigurationException>(() => ReviewWordSetManifestReader.Read(path, "sampled-tokens")).Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ManifestRetainsSourceAndExactCaseIdentity()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".jsonl");
        const string text = "{\"schema\":\"motif.review-word-set\",\"schemaVersion\":1}\n" +
            "{\"caseId\":\"first\",\"word\":\"abc\",\"writingSystemId\":\"en\",\"source\":{\"citation\":\"source\"},\"expectationRefs\":[]}\n";
        try
        {
            File.WriteAllText(path, text);
            var manifest = ReviewWordSetManifestReader.Read(path, "reference");
            var member = Assert.Single(manifest.Members);
            Assert.Equal("first", member.CaseId);
            Assert.Equal("en", member.WritingSystemId);
            Assert.Equal(File.ReadAllBytes(path), manifest.SourceBytes);
            var copy = manifest.SourceBytes;
            copy[0] ^= 1;
            manifest.EnsureSourceUnchanged(path);
            Assert.Equal(64, manifest.SourceDigest.Length);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ChangedBytesDuringCaptureAreRejected()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            File.WriteAllText(path, "{\"schema\":\"motif.review-word-set\",\"schemaVersion\":1}\n");
            var exception = Assert.Throws<ProjectConfigurationException>(() =>
                ReviewWordSetManifestReader.Read(path, "reference", () => File.AppendAllText(path, " ")));
            Assert.Contains("changed during capture", exception.Message);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("sampled-tokens", "\"occurrenceId\":\"occ\",\"textId\":\"text\",\"position\":3,\"weight\":4")]
    [InlineData("paradigm-cells", "\"cellId\":\"cell\",\"lexemeId\":\"lexeme\",\"variantId\":\"variant\",\"features\":[{\"featureId\":\"feature\",\"valueId\":\"value\"}]")]
    public void RoleSpecificLedgersAreTyped(string role, string ledger)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            File.WriteAllText(path, "{\"schema\":\"motif.review-word-set\",\"schemaVersion\":1}\n" +
                "{\"caseId\":\"case\",\"word\":\"abc\",\"writingSystemId\":\"en\",\"source\":{\"citation\":\"source\"},\"expectationRefs\":[]," + ledger + "}\n");
            var member = Assert.Single(ReviewWordSetManifestReader.Read(path, role).Members);
            var inferred = Assert.Single(ReviewWordSetManifestReader.Read(path).Members);
            if (role == "sampled-tokens")
            {
                Assert.Equal(4, Assert.IsType<ReviewTokenMembership>(member.Token).Weight);
                Assert.Equal(4, Assert.IsType<ReviewTokenMembership>(inferred.Token).Weight);
            }
            else
            {
                Assert.Equal("value", Assert.Single(Assert.IsType<ReviewCellMembership>(member.Cell).Features).ValueId);
                Assert.Equal("value", Assert.Single(Assert.IsType<ReviewCellMembership>(inferred.Cell).Features).ValueId);
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExpectationReferencesAreNonblankUniqueIdentifiers()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".jsonl");
        const string prefix = "{\"schema\":\"motif.review-word-set\",\"schemaVersion\":1}\n" +
            "{\"caseId\":\"case\",\"word\":\"abc\",\"writingSystemId\":\"en\",\"source\":{\"citation\":\"source\"},\"expectationRefs\":";
        try
        {
            File.WriteAllText(path, prefix + "[\"ref\",\"ref\"]}\n");
            Assert.Throws<ProjectConfigurationException>(() => ReviewWordSetManifestReader.Read(path));
            File.WriteAllText(path, prefix + "[null]}\n");
            Assert.Throws<ProjectConfigurationException>(() => ReviewWordSetManifestReader.Read(path));
            File.WriteAllText(path, prefix + "[\"ref\"]}\n");
            Assert.Equal("ref", Assert.Single(Assert.Single(ReviewWordSetManifestReader.Read(path).Members).ExpectationRefs));
        }
        finally { File.Delete(path); }
    }

}
