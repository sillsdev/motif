using SIL.Motif.App.ViewModels;
using Xunit;

namespace SIL.Motif.Tests.App;

[Trait("MotifTestLevel", "Unit")]
public sealed class CommandAvailabilityContractTests
{
    [Fact]
    public async Task EveryDiscoveredCommandHasAnAuthoredContract()
    {
        var (_, workspace) = CommandContractCases.CreateWorkspace();
        await using (workspace)
        {
            var discovered = CommandContractCases.Discover(workspace);
            var contracts = CommandContractCases.Authored.ToDictionary(contract => contract.Key);
            var missing = discovered.Select(command => command.Key).Except(contracts.Keys).Order().ToArray();
            var stale = contracts.Keys.Except(discovered.Select(command => command.Key)).Order().ToArray();
            Assert.True(missing.Length == 0 && stale.Length == 0,
                $"Command contracts differ. Missing: [{string.Join(", ", missing)}]. " +
                $"No longer instantiated: [{string.Join(", ", stale)}].");

            Assert.Equal(discovered.Count, discovered.Select(command => command.Key).Distinct().Count());
            foreach (var command in discovered)
            {
                var contract = contracts[command.Key];
                Assert.Equal(contract.ParameterType, command.ParameterType);
                Assert.False(string.IsNullOrWhiteSpace(contract.SideEffectOwner));
                var canExecute = command.Command.CanExecute(contract.Parameter);
                Assert.True(contract.CanExecute == canExecute,
                    $"{command.Key} expected CanExecute={contract.CanExecute}, got {canExecute}.");
                if (contract.AliasOf is { } alias)
                    Assert.Same(discovered.Single(candidate => candidate.Key == alias).Command, command.Command);
            }
        }
    }

    [Fact]
    public async Task ChangingAnAvailabilityInputNotifiesAndRecomputesTheCommand()
    {
        var (_, workspace) = CommandContractCases.CreateWorkspace();
        await using (workspace)
        {
            var setup = Assert.IsType<SetupViewModel>(workspace.Context.Setup);
            var notifications = 0;
            setup.BackCommand.CanExecuteChanged += (_, _) => notifications++;

            Assert.False(setup.BackCommand.CanExecute(null));
            setup.NextCommand.Execute(null);

            Assert.Equal(1, setup.Step);
            Assert.True(setup.BackCommand.CanExecute(null));
            Assert.Equal(1, notifications);
        }
    }
}
