using Xunit;
using SIL.Motif.Tests.App.Walkthrough;

namespace SIL.Motif.Tests.App;

public sealed class InteractiveMarkupContractTests
{
    [Fact]
    public void DiscoveryResolvesTypeAliasesAndIgnoresDecorativeContainers()
    {
        const string markup = """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:views="clr-namespace:SIL.Motif.App.Views"
                    xmlns:panels="using:SIL.Motif.App.Views">
              <StackPanel>
                <Border />
                <Button Command="{Binding RunCommand}" AutomationProperties.Name="Run" />
                <views:FilterChip Command="{Binding FirstFilterCommand}" />
                <panels:FilterChip Command="{Binding SecondFilterCommand}" />
                <Border Focusable="True" AutomationProperties.Name="Details" />
                <TextBlock Text="Read only" />
              </StackPanel>
            </Window>
            """;

        var controls = InteractiveMarkupContracts.DiscoverMarkup("sample.axaml", markup);

        Assert.Equal(4, controls.Count);
        Assert.Contains(controls, control => control.TypeName == "Button" &&
            control.Family == InteractiveControlFamily.Action && control.Triggers.Contains("Command"));
        var filters = controls.Where(control => control.TypeName == "FilterChip").ToArray();
        Assert.Equal(2, filters.Length);
        Assert.All(filters, control =>
        {
            Assert.Equal("clr-namespace:SIL.Motif.App.Views", control.XamlNamespace);
            Assert.Equal(InteractiveControlFamily.Filter, control.Family);
        });
        Assert.Contains(controls, control => control.TypeName == "Border" &&
            control.Family == InteractiveControlFamily.FocusableSurface && control.Triggers.Contains("Focusable"));
    }

    [Fact]
    public void EveryActionableViewDeclarationHasAResolvedFamilyAndBehaviorCase()
    {
        var repository = RepositoryRoot();
        var viewRoot = Path.Combine(repository, "src", "SIL.Motif.App", "Views");
        var controls = Directory.EnumerateFiles(viewRoot, "*.axaml", SearchOption.AllDirectories)
            .SelectMany(path => InteractiveMarkupContracts.DiscoverFile(path,
                Path.GetRelativePath(repository, path).Replace('\\', '/')))
            .ToArray();

        Assert.NotEmpty(controls);
        var unmapped = controls.Where(control => control.Family is null).ToArray();
        Assert.True(unmapped.Length == 0,
            "Actionable AXAML types need an authored family: " + string.Join(", ",
                unmapped.Select(control => $"{control.Source}:{control.Line} {control.XamlNamespace}:{control.TypeName}")));

        foreach (var family in controls.Select(control => control.Family!.Value).Distinct())
        {
            Assert.True(InteractiveMarkupContracts.BehaviorCases.TryGetValue(family, out var behavior),
                $"Actionable family {family} has no behavior case.");
            var method = behavior.TestClass.GetMethod(behavior.MethodName);
            Assert.NotNull(method);
            Assert.Contains(method!.GetCustomAttributes(inherit: true),
                attribute => attribute is FactAttribute or TheoryAttribute);
        }
    }

    [Fact]
    public void SceneContractsRequireMinimumFamiliesAndCriticalActionIds()
    {
        Assert.Empty(InteractiveControlSweep.MissingRequiredFamilies(
            [InteractiveControlFamily.Action],
            [InteractiveControlFamily.Action, InteractiveControlFamily.Summary]));
        Assert.Equal([InteractiveControlFamily.Link], InteractiveControlSweep.MissingRequiredFamilies(
            [InteractiveControlFamily.Action, InteractiveControlFamily.Link],
            [InteractiveControlFamily.Action]));

        Assert.Empty(InteractiveControlSweep.MissingCriticalAutomationIds(
            ["motif-apply-changes"], ["motif-apply-changes", "motif-measure-changes"]));
        Assert.Equal(["motif-apply-changes"], InteractiveControlSweep.MissingCriticalAutomationIds(
            ["motif-apply-changes"], ["motif-measure-changes"]));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Motif.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("The Motif repository root was not found.");
    }
}
