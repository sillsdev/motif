using SIL.ListCrdt;
using Xunit;

namespace SIL.ListCrdt.Tests;

public class JsonTests
{
    [Fact]
    public void Rfc8785_StringEscapes_MatchSpecificationExample()
    {
        string value = "€$\u000f\nA'B\"\\\\\"/";
        Assert.Equal("\"€$\\u000f\\nA'B\\\"\\\\\\\\\\\"/\"", CanonicalJson.Write(value));
    }

    [Fact]
    public void Rfc8785_LowercaseHexAndNamedControlEscapes()
    {
        Assert.Equal("\"\\b\\t\\n\\f\\r\\u0000\\u001f\\u007f\"".Replace("\\u007f", "\u007f"), CanonicalJson.Write("\b\t\n\f\r\0\u001f\u007f"));
        Assert.Equal("\"<>&+\"", CanonicalJson.Write("<>&+"));
    }

    [Fact]
    public void Rfc8785_ObjectKeysSortByUtf16CodeUnits()
    {
        var map = new Dictionary<string, object?> { ["b"] = 1, ["a"] = null, ["€"] = true, ["😀"] = "x", ["B"] = 2L };
        Assert.Equal("{\"B\":2,\"a\":null,\"b\":1,\"€\":true,\"😀\":\"x\"}", CanonicalJson.Write(map));
    }

    [Fact]
    public void CanonicalJson_RejectsLoneSurrogatesAndUnsafeIntegers()
    {
        Assert.Throws<ArgumentException>(() => CanonicalJson.Write("\ud800"));
        Assert.Throws<ArgumentOutOfRangeException>(() => CanonicalJson.Write(9007199254740992L));
        Assert.Throws<ArgumentException>(() => CanonicalJson.Write(1.5));
    }

    [Fact]
    public void Operations_HaveAFixedGoldenCanonicalForm()
    {
        var op = ListOp.Move(new NodeId("a", 7), 12, "e-1", new NodeId("a", 3), null);
        Assert.Equal("{\"element\":\"e-1\",\"kind\":\"move\",\"lamport\":\"12\",\"left\":[\"a\",\"3\"],\"node\":[\"a\",\"7\"],\"right\":null,\"v\":1}", op.ToJson());
        var del = ListOp.Delete(new NodeId("b", 9007199254740993), 1, "x\"y");
        Assert.Equal("{\"element\":\"x\\\"y\",\"kind\":\"delete\",\"lamport\":\"1\",\"left\":null,\"node\":[\"b\",\"9007199254740993\"],\"right\":null,\"v\":1}", del.ToJson());
    }

    [Fact]
    public void Operations_RoundTripAndStayCanonical()
    {
        var r = new ListReplica("réplica");
        var ops = new List<ListOp> { r.Insert("a", null), r.Insert("b\tq", "a") };
        ops.Add(r.Move("a", "b\tq")!); ops.AddRange(r.MoveBlock(["a", "b\tq"], null)); ops.Add(r.Delete("a"));
        foreach (var op in ops)
        {
            var json = op.ToJson();
            var back = OpJson.FromJson(json);
            Assert.Equal(op, back);
            Assert.Equal(json, back.ToJson());
        }
        var list = OpJson.ToJson(ops);
        Assert.Equal(ops, OpJson.ListFromJson(list));
        Assert.Equal(ops.Count, OpJson.ListFromJson(list).Length);
        var rebuilt = ListState.Fold(OpJson.ListFromJson(list));
        Assert.Equal(r.State.NodeOrder(), rebuilt.NodeOrder());
        Assert.Equal(r.State.Elements(), rebuilt.Elements());
    }

    [Theory]
    [InlineData("{\"element\":\"a\",\"kind\":\"insert\",\"lamport\":\"01\",\"left\":null,\"node\":[\"a\",\"1\"],\"right\":null,\"v\":1}")]
    [InlineData("{\"element\":\"a\",\"kind\":\"insert\",\"lamport\":\"1\",\"left\":null,\"node\":[\"a\",\"1\"],\"right\":null,\"v\":2}")]
    [InlineData("{\"element\":\"a\",\"extra\":1,\"kind\":\"insert\",\"lamport\":\"1\",\"left\":null,\"node\":[\"a\",\"1\"],\"right\":null,\"v\":1}")]
    [InlineData("{\"element\":\"a\",\"kind\":\"nope\",\"lamport\":\"1\",\"left\":null,\"node\":[\"a\",\"1\"],\"right\":null,\"v\":1}")]
    [InlineData("{\"element\":\"a\",\"kind\":\"insert\",\"lamport\":1,\"left\":null,\"node\":[\"a\",\"1\"],\"right\":null,\"v\":1}")]
    public void MalformedOperations_AreRejected(string json) => Assert.Throws<FormatException>(() => OpJson.FromJson(json));
}
