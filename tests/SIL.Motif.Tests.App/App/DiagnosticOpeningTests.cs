using System.Text.Json;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class DiagnosticOpeningTests
{
    private const string ValidDiagnosticJson = """
        {"schemaVersion":"pangloss.trace-details.v3","word":"word",
         "search":{"completed":false,"capped":false,"timedOut":false,"invalidShape":false,"steps":1,"elapsedNs":2},
         "result":{"signature":"-","guessed":false,"analyses":[]},"categories":{},"trace":null}
        """;

    [Fact]
    public async Task InvalidDiagnosticReportsTheParserErrorWithoutShowingADiagnostic()
    {
        const string invalidJson = "{";
        Assert.Throws<JsonException>(() => TraceWordViewModel.FromDiagnosticJson(invalidJson));
        var showDiagnosticCalls = 0;
        WindowRefusal? shownError = null;

        await SavedDiagnosticOpener.OpenAsync(
            () => Task.FromResult<string?>(invalidJson),
            _ => showDiagnosticCalls++,
            message => shownError = message);

        var expected = WindowRefusal.From(TraceWordViewModel.LoadDiagnostic(invalidJson).Refusal!);
        Assert.Equal(expected.Sentence, shownError?.Sentence);
        Assert.Equal(expected.Details, shownError?.Details);
        Assert.Equal(0, showDiagnosticCalls);
    }

    [Fact]
    public async Task CancelledDiagnosticPickDoesNothing()
    {
        var showDiagnosticCalls = 0;
        var showErrorCalls = 0;

        await SavedDiagnosticOpener.OpenAsync(
            () => Task.FromResult<string?>(null),
            _ => showDiagnosticCalls++,
            _ => showErrorCalls++);

        Assert.Equal(0, showDiagnosticCalls);
        Assert.Equal(0, showErrorCalls);
    }

    [Fact]
    public async Task ValidDiagnosticIsShownOnce()
    {
        TraceWordViewModel? shownDiagnostic = null;
        var showDiagnosticCalls = 0;
        var showErrorCalls = 0;

        await SavedDiagnosticOpener.OpenAsync(
            () => Task.FromResult<string?>(ValidDiagnosticJson),
            diagnostic =>
            {
                showDiagnosticCalls++;
                shownDiagnostic = diagnostic;
            },
            _ => showErrorCalls++);

        Assert.Equal(1, showDiagnosticCalls);
        Assert.Equal(0, showErrorCalls);
        Assert.Equal(ValidDiagnosticJson, Assert.IsType<TraceWordViewModel>(shownDiagnostic).DiagnosticJson);
    }
}
