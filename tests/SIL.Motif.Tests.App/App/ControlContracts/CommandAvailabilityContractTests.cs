using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
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
                var parameter = contract.ParameterFactory?.Invoke(workspace) ?? contract.Parameter;
                Assert.True(parameter is null || contract.ParameterType?.IsInstanceOfType(parameter) == true,
                    $"{command.Key} has an untyped {parameter?.GetType().Name ?? "null"} parameter.");
                var canExecute = command.Command.CanExecute(parameter);
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

    [Theory]
    [InlineData("completed")]
    [InlineData("refused")]
    [InlineData("cancelled")]
    public async Task ACompletedRefusedOrCancelledActionReleasesItsAvailability(string outcomeKind)
    {
        var (fake, workspace) = CommandContractCases.CreateWorkspace();
        await using (workspace)
        {
            var assess = workspace.Context.Assess;
            workspace.Context.Selection.AllWordforms = true;
            assess.ProjectPath = ProjectPath;
            var completion = new TaskCompletionSource<CommandOutcome<AssessCommandResponse>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            if (outcomeKind == "cancelled")
            {
                fake.AssessBlocksUntilCancelled(
                    new Refusal("assessment.cancelled", FailureReason.Cancelled, "Cancelled."));
            }
            else
            {
                fake.OnAssess((_, _, _) => completion.Task);
            }

            var running = assess.RunCommand.ExecuteAsync(null);

            Assert.Equal(RunState.Running, assess.State);
            Assert.False(assess.RunCommand.CanExecute(null));
            if (outcomeKind == "cancelled")
                assess.CancelCommand.Execute(null);
            else if (outcomeKind == "completed")
                completion.SetResult(CommandOutcome<AssessCommandResponse>.Success(NewAssessmentResponse()));
            else
                completion.SetResult(CommandOutcome<AssessCommandResponse>.Refused(
                    new Refusal("assess.parser-unavailable", FailureReason.Refused, "Parser unavailable.")));

            await running;

            Assert.True(assess.RunCommand.CanExecute(null));
        }
    }

    private const string ProjectPath = @"C:\projects\one.fwdata";

    private static AssessCommandResponse NewAssessmentResponse() => new(
        new BaselineCaptureResponse(
            new BaselineToken("project-1", "sha256:" + new string('a', 64), "1",
                "2026-09-05T00:00:00Z", "sha256:" + new string('b', 64)),
            ProjectPath, new DateTimeOffset(2026, 9, 5, 10, 58, 0, TimeSpan.Zero),
            FieldWorksHeldProject: false, ReusedExistingBytes: true),
        new SelectionProjection([], []), [], "(summary)");
}
