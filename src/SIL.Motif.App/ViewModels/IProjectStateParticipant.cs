namespace SIL.Motif.App.ViewModels;

internal enum ProjectOpenStage
{
    Context = 0,
    Baseline = 1,
    Independent = 2,
    Setup = 3,
    Evidence = 4,
}

internal interface IProjectStateParticipant
{
    ProjectOpenStage OpenStage { get; }

    void ClearProject();

    Task OpenProjectAsync(string projectPath, CancellationToken cancellationToken);
}
