using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>A pending change that is not about a wordform is checked by the check its fit evidence names.</summary>
[Collection(LcmCacheParallelCollections.Group2)]
public sealed class GenericChangeFitTests(PristineProjectFixture pristine)
{
    private const string Domain = "test-generic-domain";
    private static readonly object Once = new();
    private static bool _registered;

    private static void Register()
    {
        lock (Once)
        {
            if (_registered) return;
            GenericChangeFits.Register(Domain, (_, _, _, fit) => fit.Subject == "stale" ? "The subject moved." : null);
            _registered = true;
        }
    }

    private static OperationEnvelope Operation(string domain, string subject, bool withChangeId = true)
    {
        var fit = new { generic = new GenericChangeFit(domain, subject) };
        var extensions = withChangeId
            ? JsonSerializer.SerializeToElement(new { changeId = "change-" + subject, changeFit = fit })
            : JsonSerializer.SerializeToElement(new { changeFit = fit });
        return new OperationEnvelope(CanonicalId.Mint(), "scripture/test/op", target: CanonicalId.Mint(),
            after: JsonSerializer.SerializeToElement(new { }), extensions: extensions);
    }

    [Fact]
    public void AGenericChangeFitsUnlessItsCheckSaysOtherwise()
    {
        Register();
        using var scratch = pristine.NewScratch();
        using var cache = new FwDataProjectLoader().LoadCache(scratch.ProjectId.Path);
        var fits = Operation(Domain, "WEB");
        var stale = Operation(Domain, "stale");
        var unknown = Operation("no-such-domain", "WEB");
        var proposal = new Proposal(new Dictionary<string, string> { ["scripture"] = "1.0" }, CanonicalId.Mint(),
            null, [fits, stale, unknown]);

        var results = ChangeFitPreflight.Check(cache, proposal).ToDictionary(item => item.OperationId);

        Assert.True(results[fits.OperationId.Value].StillFits);
        Assert.Equal("change-WEB", results[fits.OperationId.Value].ChangeId);
        Assert.False(results[stale.OperationId.Value].StillFits);
        Assert.Equal("The subject moved.", results[stale.OperationId.Value].Reason);
        Assert.False(results[unknown.OperationId.Value].StillFits);
        Assert.Equal(ChangeFitReasons.FingerprintMalformed, results[unknown.OperationId.Value].Reason);
    }
}
