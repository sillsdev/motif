using System.Reflection;
using System.Text.Json;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class DiagnosticOpeningTests
{
    [Fact]
    public async Task InvalidDiagnosticFromTryAWordUsesTheDiagnosticWindowError()
    {
        var expectedError = Assert.Throws<JsonException>(() => TraceWordViewModel.FromDiagnosticJson("{")).Message;
        var opener = typeof(DiagnosticPanel).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .SingleOrDefault(method => method.Name == "OpenSavedDiagnosticAsync" &&
                                       method.GetParameters().Length == 3 &&
                                       method.GetParameters()[0].ParameterType == typeof(Func<Task<string?>>));
        Assert.NotNull(opener);

        var errors = new List<string>();
        await InvokeOpener(opener!, errors.Add);
        await InvokeOpener(opener!, errors.Add);

        Assert.Equal(2, errors.Count);
        Assert.All(errors, error => Assert.Equal(expectedError, error));
    }

    private static async Task InvokeOpener(MethodInfo opener, Action<string> showError)
    {
        var readInvalidJson = new Func<Task<string?>>(() => Task.FromResult<string?>("{"));
        var invocation = opener.Invoke(null,
        [
            readInvalidJson,
            (Action<TraceWordViewModel>)(_ => throw new InvalidOperationException("Invalid diagnostic was opened.")),
            showError,
        ]);
        var operation = Assert.IsAssignableFrom<Task>(invocation);
        await operation;
    }
}
