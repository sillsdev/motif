using System.Text.RegularExpressions;
using SIL.Motif.App.Services;
using SIL.Motif.Contract.Requests;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins App service-interface policy and keeps clipboard and storage APIs inside their desktop adapters.
/// </summary>
public sealed class DesktopServiceBoundaryTests
{
    [Theory]
    [InlineData(typeof(ICommandClient))]
    [InlineData(typeof(IProjectPicker))]
    [InlineData(typeof(IHandoffFolderPicker))]
    [InlineData(typeof(IClipboard))]
    [InlineData(typeof(IDiagnosticFilePicker))]
    [InlineData(typeof(IReportFilePicker))]
    [InlineData(typeof(IUriLauncher))]
    public void ViewModelFacingServiceInterfaceNamesNoAvaloniaType(Type serviceInterface)
    {
        foreach (var method in serviceInterface.GetMethods())
        {
            AssertNoAvaloniaType(method.ReturnType, serviceInterface, method.Name);
            foreach (var parameter in method.GetParameters())
                AssertNoAvaloniaType(parameter.ParameterType, serviceInterface, method.Name);
        }
    }

    private static void AssertNoAvaloniaType(Type type, Type owner, string memberName)
    {
        Assert.False(
            type.Namespace?.StartsWith("Avalonia", StringComparison.Ordinal) == true,
            $"{owner.Name}.{memberName} names Avalonia type {type}.");
        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
                AssertNoAvaloniaType(argument, owner, memberName);
        }
    }

    [Fact]
    public void NoViewCodeBehindReachesTheClipboardOrTheStorageProvider()
    {
        var offenders = AppSources("Views")
            .Where(file => file.EndsWith(".axaml.cs", StringComparison.Ordinal))
            .Where(file => Regex.IsMatch(File.ReadAllText(file), @"\b(StorageProvider|Clipboard)\b"))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void OnlyTheAvaloniaAdaptersNameAvaloniasClipboardAndStorageNamespaces()
    {
        var offenders = AppSources()
            .Where(file => !Path.GetFileName(file).StartsWith("Avalonia", StringComparison.Ordinal))
            .Where(file => Regex.IsMatch(File.ReadAllText(file), @"\bAvalonia\.(Platform\.Storage|Input\.Platform)\b"))
            .ToList();

        Assert.Empty(offenders);
    }

    private static IEnumerable<string> AppSources(string? under = null)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Motif.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var app = Path.Combine(root.FullName, "src", "SIL.Motif.App");
        var directory = under is null ? app : Path.Combine(app, under);
        var obj = Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(obj, StringComparison.Ordinal));
    }

    [Fact]
    public void TheFolderPickerNamesTheAiHandoff() =>
        Assert.Contains("AI Handoff", AvaloniaStoragePickers.FolderPickerTitle, StringComparison.Ordinal);

    [Fact]
    public async Task UnconfiguredCommandThrowsRatherThanReturningANullBehavior()
    {
        var fake = new FakeCommandClient();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fake.StatsAsync(new StatsRequest(@"C:\p.fwdata", null, StatsOutputKind.Text, []), CancellationToken.None));
    }
}
