using SIL.Motif.Contract.Parsimony;
using Xunit;

namespace SIL.Motif.Tests.Contract.Parsimony;

public sealed class ParsimonyViewCatalogTests
{
    [Theory]
    [InlineData("environment-excess")]
    [InlineData("natural-class-excess")]
    public void ExcessViewsDeclareEveryRequiredCapability(string viewCode)
    {
        var view = Assert.IsType<ParsimonyViewDefinition>(ParsimonyViewCatalog.Find(viewCode));

        Assert.Equal(new[]
        {
            "allomorphs", "compiled_mappings", "entries", "environments", "evidence", "features",
            "msas", "patterns", "phonology",
        }, view.RequiredCapabilities.Order(StringComparer.Ordinal));
    }
}
