using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Query.Phon;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// A grammar that answers from replies recorded off the real PanGloss: effects, generate and match each by a lookup the test
/// writes, and every request it was asked kept, so a test sees what Motif sent.
/// </summary>
internal sealed class ReplayGrammar : IRealizesGrammar
{
    public Func<ServeEffectsRequest, string>? OnEffects { get; init; }

    public Func<ServeGenerateRequest, string?>? OnGenerate { get; init; }

    public Func<string, IReadOnlyList<string>, string>? OnMatch { get; init; }

    public List<ServeEffectsRequest> Effected { get; } = [];

    public List<ServeGenerateRequest> Generated { get; } = [];

    public List<(string Pattern, IReadOnlyList<string> Words)> Matched { get; } = [];

    public EffectsResult Effects(ServeEffectsRequest request, CancellationToken cancellationToken)
    {
        Effected.Add(request);
        return EffectsResult.Read(OnEffects!(request));
    }

    public string? Generate(ServeGenerateRequest request, CancellationToken cancellationToken)
    {
        Generated.Add(request);
        return OnGenerate!(request);
    }

    public PhonBatchResult Match(string pattern, IReadOnlyList<string> words, CancellationToken cancellationToken)
    {
        Matched.Add((pattern, words));
        return ResidentPhonMatcher.Read(OnMatch!(pattern, words));
    }
}

/// <summary>The identities of the morphemes in the recorded fixtures.</summary>
internal static class RecordedMorphemes
{
    internal const string KatEntry = "00000000-0000-0000-0000-000000000030";
    internal const string KatMsa = "00000000-0000-0000-0000-000000000032";
    internal const string TaEntry = "00000000-0000-0000-0000-000000000200";
    internal const string TaMsa = "00000000-0000-0000-0000-000000000202";
    internal const string DaEntry = "00000000-0000-0000-0000-000000000100";
    internal const string DaMsa = "00000000-0000-0000-0000-000000000102";
}
