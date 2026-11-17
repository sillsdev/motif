using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SettingsGroup = SIL.Motif.App.Services.SettingsGroup;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>The window has no Claude: the retired key is deleted once, the old settings group loads as Display, and unrecorded Proposal lines stay hidden.</summary>
public sealed class NoClaudeInTheWindowTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("motif-no-claude-").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    private sealed class FakeLegacyCredentials : ILegacyCredentialStore
    {
        public int Deletes { get; private set; }
        public bool Fail { get; set; }

        public void Delete()
        {
            if (Fail) throw new IOException("The credential store is locked.");
            Deletes++;
        }
    }

    [Fact]
    public void TheFirstStartDeletesTheStoredKeyOnceAndLeavesAMarker()
    {
        var marker = Path.Combine(_folder, "Motif", "assistant-key-removed");
        var credentials = new FakeLegacyCredentials();

        new LegacyAssistantCleanup(credentials, marker).RunOnce();
        Assert.Equal(1, credentials.Deletes);
        Assert.True(File.Exists(marker));

        new LegacyAssistantCleanup(credentials, marker).RunOnce();
        Assert.Equal(1, credentials.Deletes);
    }

    [Fact]
    public void AFailedDeleteLeavesNoMarkerSoTheNextStartTriesAgain()
    {
        var marker = Path.Combine(_folder, "assistant-key-removed");
        var credentials = new FakeLegacyCredentials { Fail = true };

        new LegacyAssistantCleanup(credentials, marker).RunOnce();
        Assert.False(File.Exists(marker));

        credentials.Fail = false;
        new LegacyAssistantCleanup(credentials, marker).RunOnce();
        Assert.Equal(1, credentials.Deletes);
        Assert.True(File.Exists(marker));
    }

    [Fact]
    public void ThePreferencesOfAnOlderVersionThatEndedOnAssistantConnectionsOpenOnDisplayAndAreRewritten()
    {
        var path = Path.Combine(_folder, "user-preferences.json");
        var current = new FileUserPreferencesStore(path);
        current.Update(preferences => preferences with { LastSettingsGroup = SettingsGroup.Texts });
        current.Dispose();
        var text = File.ReadAllText(path).Replace("\"Texts\"", "\"AssistantConnections\"", StringComparison.Ordinal);
        Assert.Contains("AssistantConnections", text, StringComparison.Ordinal);
        File.WriteAllText(path, text);

        using var reopened = new FileUserPreferencesStore(path);

        Assert.Equal(SettingsGroup.Display, reopened.Current.LastSettingsGroup);
        Assert.DoesNotContain("AssistantConnections", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, null, null, false, false, false)]
    [InlineData("claude-code", "model-1", "Why?", true, true, true)]
    public void BuiltByModelAndQuestionShowOnlyWhenAnMcpSessionRecordedThem(
        string? builder, string? model, string? question, bool hasBuilder, bool hasModel, bool hasQuestion)
    {
        var card = new ProposalCard("proposal/1", "First", "proposed", ProposalGroup.WaitingForYou, false, [],
            TrialStanding.Current, null, [])
        { Builder = builder, Model = model, Question = question };

        var row = new ProposalCardViewModel(card);

        Assert.Equal(hasBuilder, row.HasBuilder);
        Assert.Equal(hasModel, row.HasModel);
        Assert.Equal(hasQuestion, row.HasQuestion);
    }
}
