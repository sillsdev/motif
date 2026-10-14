namespace SIL.Motif.App.ViewModels;

public sealed partial class WorkspaceShellViewModel
{
    private readonly List<Task> _projectIdentityReads = [];
    private ProjectIdentityRead? _projectIdentityRead;
    private bool _isDisposed;

    private sealed class ProjectIdentityRead(int generation, string path)
    {
        public int Generation { get; } = generation;
        public string Path { get; } = path;
        public string? Identity { get; set; }
        public Task Completion { get; set; } = Task.CompletedTask;
    }

    private bool IsCurrentProjectIdentityRead(ProjectIdentityRead read) =>
        !_isDisposed && ReferenceEquals(read, _projectIdentityRead) &&
        read.Generation == Volatile.Read(ref _projectOpenGeneration) &&
        string.Equals(read.Path, Context.ProjectPath,
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    private Task ResolveOpenProjectIdentityAsync()
    {
        if (_isDisposed || Context.ProjectPath is not { } path) return Task.CompletedTask;
        if (_projectIdentityRead is { } previous && IsCurrentProjectIdentityRead(previous) &&
            (!previous.Completion.IsCompleted || previous.Identity is not null)) return previous.Completion;

        var read = new ProjectIdentityRead(Volatile.Read(ref _projectOpenGeneration), path);
        _projectIdentityRead = read;
        read.Completion = ReadAndPublishProjectIdentityAsync(read);
        _projectIdentityReads.RemoveAll(task => task.IsCompleted);
        _projectIdentityReads.Add(read.Completion);
        return read.Completion;
    }

    private async Task ReadAndPublishProjectIdentityAsync(ProjectIdentityRead read)
    {
        var identity = await Task.Run(() => ReadSavedProjectIdentity(read.Path)).ConfigureAwait(true);
        if (!IsCurrentProjectIdentityRead(read)) return;
        read.Identity = identity;
        RefreshRecentProjects(resolveIdentity: false);
    }
}
