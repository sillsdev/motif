using System;
using System.ComponentModel;
using SIL.LCModel.Utils;

namespace SIL.TextEditing.Project.Tests
{
    /// <summary>A progress reporter that observes nothing: project loading needs one, and runs its task on the calling thread.</summary>
    sealed class QuietProgress : IThreadedProgress
    {
#pragma warning disable CS0067
        public event CancelEventHandler? Canceling;
#pragma warning restore CS0067
        public void Step(int amount) { }
        public string? Title { get; set; }
        public string? Message { get; set; }
        public int Position { get; set; }
        public int StepSize { get; set; }
        public int Minimum { get; set; }
        public int Maximum { get; set; }
        public ISynchronizeInvoke SynchronizeInvoke { get; } = new SingleThreadedSynchronizeInvoke();
        public bool IsIndeterminate { get; set; }
        public bool AllowCancel { get; set; }
        public object RunTask(Func<IThreadedProgress, object[], object> backgroundTask, params object[] parameters) => backgroundTask(this, parameters);
        public object RunTask(bool fDisplayUi, Func<IThreadedProgress, object[], object> backgroundTask, params object[] parameters) => backgroundTask(this, parameters);
        public bool Canceled => false;
        public bool IsCanceling => false;
    }
}
