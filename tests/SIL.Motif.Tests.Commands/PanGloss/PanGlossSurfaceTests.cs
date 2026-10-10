using SIL.Motif.Host.Parser;
using SurfaceContract = SIL.Motif.Tests.PanGloss.FakePanGlossSurfaceTests;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.PanGloss;

[Trait("MotifTestLevel", "System")]
public sealed class PanGlossSurfaceTests
{
    [RealParserFact]
    public async Task RealDescriptionContainsEveryTypedRequestAndFakeCommand()
    {
        using var realDescription = await SurfaceContract.Describe(PanGlossExecutable.TryLocate()!);
        using var fakeDescription = await SurfaceContract.Describe(FakeParser.ExecutablePath);
        Assert.Equal(1, realDescription.RootElement.GetProperty("schema_version").GetInt32());
        var commands = SurfaceContract.Commands(realDescription);
        SurfaceContract.AssertRequestsMatch(commands);
        SurfaceContract.AssertTraceCommandIsDeclared(commands);
        Assert.DoesNotContain("compare", commands.Keys);
        Assert.DoesNotContain("golden-diff", commands.Keys);
        Assert.DoesNotContain("investigate", commands.Keys);
        var fakeCommands = SurfaceContract.Commands(fakeDescription);
        SurfaceContract.AssertRequestsMatch(fakeCommands);
        SurfaceContract.AssertTraceCommandIsDeclared(fakeCommands);
        foreach (var fake in fakeCommands)
        {
            Assert.True(commands.TryGetValue(fake.Key, out var real), $"FakePanGloss invented '{fake.Key}'.");
            Assert.False(real.GetProperty("hidden").GetBoolean());
            Assert.Equal(real.GetProperty("positionals").EnumerateArray()
                    .Select(positional => positional.GetString()).ToArray(),
                fake.Value.GetProperty("positionals").EnumerateArray()
                    .Select(positional => positional.GetString()).ToArray());
            var realFlags = real.GetProperty("flags").EnumerateArray()
                .ToDictionary(flag => flag.GetProperty("name").GetString()!, flag => flag);
            foreach (var fakeFlag in fake.Value.GetProperty("flags").EnumerateArray())
            {
                var name = fakeFlag.GetProperty("name").GetString()!;
                Assert.True(realFlags.TryGetValue(name, out var realFlag),
                    $"FakePanGloss invented '{fake.Key} {name}'.");
                Assert.Equal(realFlag.GetProperty("takes_value").GetBoolean(),
                    fakeFlag.GetProperty("takes_value").GetBoolean());
            }
        }
        Assert.DoesNotContain("assess", SurfaceContract.Commands(fakeDescription).Keys);
    }

}
