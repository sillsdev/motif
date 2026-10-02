using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Contract;

public sealed class ObjectIdentityTests
{
    [Fact]
    public void AuthoredKeysCanonicalizeWhileStructuralKeysRemainOpaque()
    {
        const string guid = "aaaaaaaa-0000-0000-0000-000000000001";
        Assert.Equal(ObjectIdentity.Create("form", guid), ObjectIdentity.Create("form", "{" + guid.ToUpperInvariant() + "}"));
        Assert.NotEqual(ObjectIdentity.Create("form", "Alpha"), ObjectIdentity.Create("form", "alpha"));
        Assert.NotEqual(ObjectIdentity.Create("form", guid, "structural"),
            ObjectIdentity.Create("form", "{" + guid + "}", "structural"));
        Assert.NotEqual(ObjectIdentity.Create("form", guid), ObjectIdentity.Create("msa", guid));
    }

    [Fact]
    public void LocalOrdinalsAndEventAddressesRequireTheirSavedScope()
    {
        Assert.Null(ObjectIdentity.Create("template", "0", "grammar-local"));
        Assert.NotEqual(ObjectIdentity.Create("template", "0", "grammar-local", "grammar-a"),
            ObjectIdentity.Create("template", "0", "grammar-local", "grammar-b"));
        Assert.NotEqual(ObjectIdentity.Create("template", "0", "grammar-local", "grammar-a"),
            ObjectIdentity.Create("stratum", "0", "grammar-local", "grammar-a"));
        Assert.NotEqual(ObjectIdentity.Create("event", "0.1", "occurrence", "trace-a"),
            ObjectIdentity.Create("event", "0.1", "occurrence", "trace-b"));
        Assert.NotEqual(ObjectIdentity.Create("event", "0.1", "occurrence", "trace-a"),
            ObjectIdentity.Create("event", "0.2", "occurrence", "trace-a"));
    }
}
