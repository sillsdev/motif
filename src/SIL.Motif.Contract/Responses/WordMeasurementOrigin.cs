namespace SIL.Motif.Contract.Responses;

/// <summary>The exact run that produced one word's answer and when that measurement was recorded.</summary>
public sealed record WordMeasurementOrigin(string AssessmentId, string? InvocationId, DateTimeOffset MeasuredUtc);
