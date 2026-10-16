using SIL.Motif.Contract.Phonology;

namespace SIL.Motif.Host.Assess;

/// <summary>Producer evidence validated against one invocation's frozen inputs and executable bytes.</summary>
public sealed class PhonologyMeasurement
{
    internal PhonologyMeasurement(string grammarSha256, IReadOnlyList<PhonologyGrammarDependency> dependencies,
        string requestFileSha256, string producerRequestSha256, string producerBuild, string executableSha256,
        string outputSha256, int outputBytes, PhonologyLimits limits, ulong measuredEntryCount,
        IReadOnlyList<PhonologyMeasuredRow> rows)
    {
        GrammarSha256 = grammarSha256;
        GrammarDependencies = dependencies;
        RequestFileSha256 = requestFileSha256;
        ProducerRequestSha256 = producerRequestSha256;
        ProducerBuild = producerBuild;
        ExecutableSha256 = executableSha256;
        OutputSha256 = outputSha256;
        OutputBytes = outputBytes;
        Limits = limits;
        MeasuredEntryCount = measuredEntryCount;
        Rows = rows;
    }

    public string GrammarSha256 { get; }
    public IReadOnlyList<PhonologyGrammarDependency> GrammarDependencies { get; }
    public string RequestFileSha256 { get; }
    public string ProducerRequestSha256 { get; }
    public string ProducerBuild { get; }
    /// <summary>The independently hashed selected executable, distinct from producer assertions.</summary>
    public string ExecutableSha256 { get; }
    public string OutputSha256 { get; }
    public int OutputBytes { get; }
    public PhonologyLimits Limits { get; }
    /// <summary>The producer's measured compiled lexical-context count, not a source-entry count.</summary>
    public ulong MeasuredEntryCount { get; }
    public IReadOnlyList<PhonologyMeasuredRow> Rows { get; }
}

/// <summary>One copied grammar dependency identified relative to its grammar directory.</summary>
public sealed record PhonologyGrammarDependency(string Path, string Sha256);

/// <summary>The mechanical producer classification, independent of linguistic judgement.</summary>
public enum PhonologyDisposition { Agree, Disagree, Unavailable }

/// <summary>Whether a row finished its declared fold.</summary>
public enum PhonologyCompletion { Complete, Incomplete, NotStarted }

/// <summary>The closed producer reasons for an unavailable observation.</summary>
public enum PhonologyReason
{
    EmptyLexicon, UnknownEntry, AmbiguousContext, MsaNotOwnedByEntry, InvalidShape, InvalidStratum,
    ResultLimit, WorkCap, StepCap, Timeout
}

/// <summary>One validated row preserving authored strings, producer comparison facts and separate rule traces.</summary>
public sealed record PhonologyMeasuredRow(
    string Id, PhonologyGenerationContext RequestedContext, PhonologyGenerationContext? ResolvedContext,
    string Underlying, IReadOnlyList<string> Expected, PhonologyLimits EffectiveLimits,
    PhonologyDisposition Disposition, PhonologyCompletion Completion, IReadOnlyList<bool> ExpectedObserved,
    string? Observed, PhonologyReason? Reason, ulong Steps, ulong WorkSteps, bool Capped, bool TimedOut,
    IReadOnlyList<string> OrderedRuleIds, IReadOnlyList<string> VisitedRuleIds, IReadOnlyList<string> AppliedRuleIds);
