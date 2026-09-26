namespace SIL.Motif.App.ViewModels;

internal enum ProjectOpenStage
{
    Context,
    Baseline,
    Independent,
    Setup,
}

internal interface IProjectStateParticipant
{
    ProjectOpenStage OpenStage { get; }

    void ClearProject();

    Task OpenProjectAsync(string projectPath, CancellationToken cancellationToken);
}
