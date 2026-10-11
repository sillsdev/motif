namespace SIL.Motif.Contract.Jobs;

/// <summary>The stage a long-running job is in, published to the job row so a waiting caller can show it.</summary>
/// <param name="Stage">A stable stage code such as <c>copy</c>, <c>import</c>, <c>facts</c> or <c>measure</c>.</param>
/// <param name="Step">The one-based position of this stage among the job's steps.</param>
/// <param name="Steps">How many steps the job takes in all, which grows with the measures it runs.</param>
/// <param name="Detail">What the stage is working on, such as the measure id, or null when it names nothing.</param>
public sealed record JobStageProgress(string Stage, int Step, int Steps, string? Detail = null);
