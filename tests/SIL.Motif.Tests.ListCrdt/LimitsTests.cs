using SIL.ListCrdt;
using Xunit;

namespace SIL.ListCrdt.Tests;

public class LimitsTests
{
    static ListOp Pending(int n, string? replica = null) =>
        ListOp.Move(new NodeId(replica ?? "remote", n), n, "absent", null, null);

    [Fact]
    public void PendingBufferIsBoundedAndRefusalLeavesStateUnchanged()
    {
        var state = new ListState(new ListLimits { MaxPendingOperations = 3 });
        for (int i = 1; i <= 3; i++) Assert.Equal(ApplyResult.Buffered, state.Apply(Pending(i)));
        var refused = Assert.Throws<ListLimitException>(() => state.Apply(Pending(4)));
        Assert.Equal(nameof(ListLimits.MaxPendingOperations), refused.LimitName);
        Assert.Contains("pending buffer", refused.Message);
        Assert.Equal(3, state.PendingCount);
        Assert.Equal(3, state.MaximumLamport);
        Assert.Equal(3, state.MaximumCounter("remote"));
        Assert.Equal(3, state.ReceivedOperations.Count());
        // Duplicates, applicable operations and the missing element itself still go through.
        Assert.Equal(ApplyResult.Duplicate, state.Apply(Pending(1)));
        Assert.Equal(ApplyResult.Applied, state.Apply(ListOp.Insert(new NodeId("remote", 10), 1, "absent", null, null)));
        Assert.Equal(0, state.PendingCount);
        Assert.Equal(ApplyResult.Applied, state.Apply(Pending(4)));
    }

    [Fact]
    public void FarFutureClocksAreRefusedSoTheAuthorKeepsWorking()
    {
        var replica = new ListReplica("me");
        var farLamport = ListOp.Insert(new NodeId("evil", 1), long.MaxValue, "x", null, null);
        var farCounter = ListOp.Insert(new NodeId("evil", long.MaxValue), 1, "y", null, null);
        Assert.Equal(nameof(ListLimits.MaxClock), Assert.Throws<ListLimitException>(() => replica.Receive(farLamport)).LimitName);
        Assert.Equal(nameof(ListLimits.MaxClock), Assert.Throws<ListLimitException>(() => replica.Receive(farCounter)).LimitName);
        var edge = ListOp.Insert(new NodeId("ok", ListLimits.Default.MaxClock), ListLimits.Default.MaxClock, "z", null, null);
        Assert.Equal(ApplyResult.Applied, replica.Receive(edge));
        Assert.Equal(0, replica.State.PendingCount);
        Assert.Equal(["z"], replica.State.Elements());
    }

    [Fact]
    public void OversizeIdentifiersAreRefused()
    {
        var state = new ListState(new ListLimits { MaxIdentifierLength = 8 });
        Assert.Equal(ApplyResult.Applied, state.Apply(ListOp.Insert(new NodeId("12345678", 1), 1, "12345678", null, null)));
        Assert.Throws<ListLimitException>(() => state.Apply(ListOp.Insert(new NodeId("r", 2), 2, "123456789", null, null)));
        Assert.Throws<ListLimitException>(() => state.Apply(ListOp.Insert(new NodeId("123456789", 2), 2, "ok", null, null)));
        Assert.Throws<ListLimitException>(() => state.Apply(ListOp.Insert(new NodeId("r", 2), 2, "ok", new NodeId("123456789", 1), null)));
        Assert.Single(state.ReceivedOperations);
    }

    [Fact]
    public void PendingDuplicateItemIdentityIsRefused()
    {
        var state = new ListState();
        var first = ListOp.Insert(new NodeId("a", 2), 2, "item", new NodeId("a", 1), null);
        Assert.Equal(ApplyResult.Buffered, state.Apply(first));
        var second = ListOp.Insert(new NodeId("b", 5), 5, "item", null, null);
        Assert.Throws<ArgumentException>(() => state.Apply(second));
        Assert.Equal(1, state.PendingCount);
        Assert.Equal(ApplyResult.Applied, state.Apply(ListOp.Insert(new NodeId("a", 1), 1, "base", null, null)));
        Assert.Equal(["base", "item"], state.Elements());
        Assert.Throws<ArgumentException>(() => state.Apply(second));
    }

    [Fact]
    public void CyclicOriginsStayBufferedWithoutLoopingOrCorruptingTheList()
    {
        var state = new ListState();
        state.Apply(ListOp.Insert(new NodeId("a", 1), 1, "base", null, null));
        var x = ListOp.Insert(new NodeId("x", 1), 2, "x", new NodeId("y", 1), null);
        var y = ListOp.Insert(new NodeId("y", 1), 2, "y", new NodeId("x", 1), null);
        Assert.Equal(ApplyResult.Buffered, state.Apply(x));
        Assert.Equal(ApplyResult.Buffered, state.Apply(y));
        Assert.Equal(2, state.PendingCount);
        Assert.Equal(["base"], state.Elements());
        Assert.Equal(ApplyResult.Applied, state.Apply(ListOp.Insert(new NodeId("a", 2), 3, "tail", new NodeId("a", 1), null)));
        Assert.Equal(["base", "tail"], state.Elements());
    }

    [Fact]
    public void OversizeJsonIsRefusedBeforeParsingAndBatchesMustBeSplit()
    {
        var limits = new ListLimits { MaxOperationJsonLength = 200, MaxBatchOperations = 2 };
        var small = ListOp.Insert(new NodeId("w", 1), 1, "a", null, null);
        Assert.Equal(small, OpJson.FromJson(small.ToJson(), limits));
        var big = ListOp.Insert(new NodeId("w", 2), 1, new string('x', 300), null, null);
        var ex = Assert.Throws<ListLimitException>(() => OpJson.FromJson(big.ToJson(), limits));
        Assert.Equal(nameof(ListLimits.MaxOperationJsonLength), ex.LimitName);
        Assert.Throws<ListLimitException>(() => NodeRowEncoding.Decode(NodeRowEncoding.Encode(big), limits));
        var three = Enumerable.Range(1, 3).Select(i => ListOp.Insert(new NodeId("w", i), 1, "e" + i, null, null)).ToArray();
        var batch = Assert.Throws<ListLimitException>(() => OpJson.ListFromJson(OpJson.ToJson(three), limits));
        Assert.Equal(nameof(ListLimits.MaxBatchOperations), batch.LimitName);
        Assert.Contains("batches of at most 2", batch.Message);
        Assert.Equal(2, OpJson.ListFromJson(OpJson.ToJson(three.Take(2)), limits).Length);
        var rows = three.Select(NodeRowEncoding.Encode).Append(NodeRowEncoding.Encode(big)).ToArray();
        Assert.Throws<ListLimitException>(() => NodeRowEncoding.Restore(rows, limits));
    }

    [Fact]
    public void RestoreStreamsRowsAndAppliesThePendingLimit()
    {
        var source = ListState.FromInitial(Enumerable.Range(0, 50).Select(i => "p" + i));
        var rows = source.ExportRows().Reverse().ToArray();
        Assert.Throws<ListLimitException>(() => NodeRowEncoding.Restore(rows, new ListLimits { MaxPendingOperations = 10 }));
        var restored = NodeRowEncoding.Restore(rows, new ListLimits { MaxPendingOperations = 60 });
        Assert.Equal(source.Elements(), restored.Elements());
        Assert.Equal(60, restored.Limits.MaxPendingOperations);
        Assert.Equal(60, restored.Clone().Limits.MaxPendingOperations);
    }

    [Fact]
    public void BatchTextIsRefusedAtTheBatchLengthBoundaryBeforeParsing()
    {
        var limits = new ListLimits { MaxBatchJsonLength = 100, MaxBatchOperations = 1_000_000 };
        Assert.True(limits.MaxBatchJsonLength < limits.MaxOperationJsonLength * (long)limits.MaxBatchOperations);
        string atLimit = "[" + new string(' ', 98) + "]";
        Assert.Empty(OpJson.ListFromJson(atLimit, limits));
        var ex = Assert.Throws<ListLimitException>(() => OpJson.ListFromJson(atLimit + " ", limits));
        Assert.Equal(nameof(ListLimits.MaxBatchJsonLength), ex.LimitName);
        Assert.Equal(100, ex.Limit);
        Assert.Equal(16 * 1024 * 1024, ListLimits.Default.MaxBatchJsonLength);
        // Not valid JSON, so only the length pre-check can have produced a limit error.
        Assert.Throws<ListLimitException>(() => OpJson.ListFromJson(new string('x', 101), limits));
    }
}
