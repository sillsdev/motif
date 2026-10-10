using System;
using System.IO;
using System.Text;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Parsing;
using Xunit;

namespace SIL.Motif.Tests.Contract;

/// <summary>
/// Loads the frozen, language-agnostic conformance vectors under <c>tests/conformance/proposal-digest</c>
/// (input Proposal JSON → expected RFC 8785 canonical bytes → expected intent digest) and checks
/// this C# implementation reproduces them exactly. A Python or Rust runner is expected to load the
/// same <c>input.json</c> files and reproduce the same <c>canonical.json</c> bytes and
/// <c>digest.txt</c> value, per ADR 0007.
/// </summary>
public class ConformanceVectorTests
{
    static ConformanceVectorTests()
    {
        // Contract vectors test hashing without referencing Runner, so register this slice's kinds here.
        OperationKindRegistry.Register("grammar/phPhonData/createPhonRules");
        OperationKindRegistry.Register("grammar/phPhonData/movePhonRules");
        OperationKindRegistry.Register("grammar/phSegmentRule/setName");
        OperationKindRegistry.Register("grammar/phSegmentRule/setDirection");
        OperationKindRegistry.Register("grammar/phSegmentRule/setDisabled");
        OperationKindRegistry.Register("grammar/phRegularRule/createRightHandSides");
        OperationKindRegistry.Register("grammar/phPhonData/createContexts");
        OperationKindRegistry.Register("grammar/phSegmentRule/createStrucDesc");
        OperationKindRegistry.Register("grammar/phSegRuleRHS/createStrucChange");
        OperationKindRegistry.Register("grammar/phSegRuleRHS/createLeftContext");
        OperationKindRegistry.Register("grammar/phSegRuleRHS/createRightContext");
        OperationKindRegistry.Register("grammar/phSequenceContext/addRefMembers");
        OperationKindRegistry.Register("grammar/phSequenceContext/moveMembers");
        OperationKindRegistry.Register("grammar/phSimpleContextSeg/setFeatureStructure");
        OperationKindRegistry.Register("grammar/phSimpleContextNC/setFeatureStructure");
        OperationKindRegistry.Register("grammar/phSimpleContextBdry/setFeatureStructure");
    }

    public static TheoryData<string> VectorDirectories()
    {
        var data = new TheoryData<string>();
        foreach (var dir in Directory.GetDirectories(FindConformanceRoot()))
            data.Add(dir);
        return data;
    }

    [Theory]
    [MemberData(nameof(VectorDirectories))]
    public void Vector_CanonicalBytesAndDigest_MatchFrozenExpectation(string vectorDir)
    {
        var inputJson = File.ReadAllText(Path.Combine(vectorDir, "input.json"));
        var expectedCanonical = File.ReadAllText(Path.Combine(vectorDir, "canonical.json"));
        var expectedDigest = File.ReadAllText(Path.Combine(vectorDir, "digest.txt"));

        var proposal = ProposalJsonParser.Parse(inputJson);
        var canonicalBytes = IntentDigest.CanonicalBytes(proposal);
        var actualCanonical = Encoding.UTF8.GetString(canonicalBytes);
        var actualDigest = IntentDigest.Sha256Of(canonicalBytes);

        Assert.Equal(expectedCanonical, actualCanonical);
        Assert.Equal(expectedDigest, actualDigest);
    }

    [Fact]
    public void AtLeastTwoVectorsExist()
    {
        var count = Directory.GetDirectories(FindConformanceRoot()).Length;
        Assert.True(count >= 2, $"Expected at least two frozen conformance vectors, found {count}.");
    }

    /// <summary>Walks up from the test assembly to the repo root to find the conformance vectors.</summary>
    private static string FindConformanceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Motif.sln")))
            dir = dir.Parent;

        if (dir is null)
        {
            throw new InvalidOperationException(
                "Could not locate the Motif repo root from the test assembly location.");
        }

        var conformanceRoot = Path.Combine(dir.FullName, "tests", "conformance", "proposal-digest");
        if (!Directory.Exists(conformanceRoot))
            throw new InvalidOperationException($"Expected conformance vectors at '{conformanceRoot}'.");

        return conformanceRoot;
    }
}
