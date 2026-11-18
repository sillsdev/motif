using SIL.ListCrdt;
using Xunit;

namespace SIL.ListCrdt.Tests;

/// <summary>
/// Scenarios discussed in the Fugue paper (insertion tree, concurrent siblings, interleaving) and in Kleppmann's
/// "Moving elements in list CRDTs" (concurrent moves, move versus delete). The element names are ours; the scenarios
/// are re-derived from the papers' prose, not copied from their figures or code.
/// </summary>
public class PaperExampleTests
{

    [Fact]
    public void Fugue_SequentialTypingAtEnd_GivesTypedText()
    {
        var r = new ListReplica("a");
        string? prev = null;
        foreach (var e in new[] { "h", "e", "l", "l2", "o" }) { r.Insert(e, prev); prev = e; }
        Assert.Equal(["h", "e", "l", "l2", "o"], r.Live());
    }

    [Fact]
    public void Fugue_InsertBetweenTwoElements_UsesLeftChildOfRightOrigin()
    {
        var r = new ListReplica("a");
        r.Insert("a", null); r.Insert("c", "a");
        var op = r.Insert("b", "a");
        Assert.Equal(["a", "b", "c"], r.Live());
        Assert.NotNull(op.RightOrigin);
    }

    [Fact]
    public void Fugue_ConcurrentInsertsAtSamePlace_OrderedByIdentifierOnEveryReplica()
    {
        var a = new ListReplica("a"); var b = new ListReplica("b");
        var baseOp = a.Insert("p", null); b.Receive(baseOp);
        var x = a.Insert("X", "p"); var y = b.Insert("Y", "p");
        a.Receive(y); b.Receive(x);
        Assert.Equal(["p", "X", "Y"], a.Live());
        Assert.Equal(a.Live(), b.Live());
    }

    [Fact]
    public void Fugue_ForwardTypedRuns_DoNotInterleave()
    {
        var a = new ListReplica("alice"); var b = new ListReplica("bob");
        var baseOps = new List<ListOp>();
        string? prev = null;
        for (int i = 0; i < 6; i++) { baseOps.Add(a.Insert("base" + i, prev)); prev = "base" + i; }
        foreach (var o in baseOps) b.Receive(o);
        var aliceOps = new List<ListOp>(); var bobOps = new List<ListOp>();
        string? p1 = "base5", p2 = "base5";
        foreach (var ch in "ALICE") { aliceOps.Add(a.Insert("a" + ch, p1)); p1 = "a" + ch; }
        int k = 0;
        foreach (var ch in "BOB") { var name = "b" + ch + k++; bobOps.Add(b.Insert(name, p2)); p2 = name; }
        foreach (var o in bobOps) a.Receive(o);
        foreach (var o in aliceOps) b.Receive(o);
        Assert.Equal(a.Live(), b.Live());
        var tail = a.Live().Skip(6).ToArray();
        Assert.Equal(["aA", "aL", "aI", "aC", "aE", "bB0", "bO1", "bB2"], tail);
    }

    [Fact]
    public void Fugue_ForwardRunsInMiddle_DoNotInterleave_WhenLeftOriginHasRightChild()
    {
        var a = new ListReplica("a"); var b = new ListReplica("b");
        var o1 = a.Insert("L", null); var o2 = a.Insert("R", "L"); b.Receive(o1); b.Receive(o2);
        var aOps = new List<ListOp>(); var bOps = new List<ListOp>();
        string? pa = "L", pb = "L";
        for (int i = 0; i < 4; i++) { aOps.Add(a.Insert("a" + i, pa)); pa = "a" + i; bOps.Add(b.Insert("b" + i, pb)); pb = "b" + i; }
        foreach (var o in bOps) a.Receive(o);
        foreach (var o in aOps) b.Receive(o);
        Assert.Equal(a.Live(), b.Live());
        Assert.Equal(["L", "a0", "a1", "a2", "a3", "b0", "b1", "b2", "b3", "R"], a.Live());
    }

    [Fact]
    public void Fugue_BackwardRuns_ConvergeEvenWhenTheyMayInterleave()
    {
        var a = new ListReplica("a"); var b = new ListReplica("b");
        var baseOps = new[] { a.Insert("L", null), a.Insert("R", "L") };
        foreach (var o in baseOps) b.Receive(o);
        var aOps = new List<ListOp>(); var bOps = new List<ListOp>();
        string? nextA = "R", nextB = "R";
        for (int i = 0; i < 4; i++)
        {
            aOps.Add(a.InsertAt(a.Live().ToList().IndexOf(nextA!), "a" + i)); nextA = "a" + i;
            bOps.Add(b.InsertAt(b.Live().ToList().IndexOf(nextB!), "b" + i)); nextB = "b" + i;
        }
        foreach (var o in bOps) a.Receive(o);
        foreach (var o in aOps) b.Receive(o);
        Assert.Equal(a.Live(), b.Live());
        Assert.Equal(10, a.Live().Length);
        Support.AssertConsistent("backward", a.State);
    }

    [Fact]
    public void Fugue_TombstoneKeepsAnchoring_InsertAfterDeletedElementStillLandsThere()
    {
        var a = new ListReplica("a"); var b = new ListReplica("b");
        var ops = new[] { a.Insert("x", null), a.Insert("y", "x"), a.Insert("z", "y") };
        foreach (var o in ops) b.Receive(o);
        var del = a.Delete("y");
        var ins = b.Insert("w", "y");
        a.Receive(ins); b.Receive(del);
        Assert.Equal(["x", "w", "z"], a.Live());
        Assert.Equal(a.Live(), b.Live());
        Assert.True(a.State.IsDeleted("y"));
    }


    [Fact]
    public void Kleppmann_ConcurrentMovesOfSameElement_OneWinner_NoDuplicate()
    {
        var a = new ListReplica("a"); var b = new ListReplica("b");
        string? prev = null; var baseOps = new List<ListOp>();
        foreach (var e in new[] { "A", "B", "C", "D" }) { baseOps.Add(a.Insert(e, prev)); prev = e; }
        foreach (var o in baseOps) b.Receive(o);
        var m1 = a.Move("A", "C")!; var m2 = b.Move("A", "D")!;
        a.Receive(m2); b.Receive(m1);
        Assert.Equal(["B", "C", "D", "A"], a.Live());
        Assert.Equal(a.Live(), b.Live());
        Assert.Equal(1, a.Live().Count(e => e == "A"));
    }

    [Fact]
    public void Kleppmann_ConcurrentMoves_LaterLamportBeatsHigherReplicaId()
    {
        var a = new ListReplica("a"); var b = new ListReplica("b");
        string? prev = null; var baseOps = new List<ListOp>();
        foreach (var e in new[] { "A", "B", "C", "D" }) { baseOps.Add(a.Insert(e, prev)); prev = e; }
        foreach (var o in baseOps) b.Receive(o);
        var mb = b.Move("A", "D")!;
        a.Receive(mb);
        var ma = a.Move("A", "B")!;
        b.Receive(ma);
        Assert.Equal(["B", "A", "C", "D"], a.Live());
        Assert.Equal(a.Live(), b.Live());
    }

    [Fact]
    public void Kleppmann_MovedElementKeepsIdentity_AndOldSlotStaysAsAnchor()
    {
        var r = new ListReplica("a");
        r.Insert("A", null); r.Insert("B", "A"); r.Insert("C", "B");
        var before = r.State.PositionOf("A");
        r.Move("A", "C");
        Assert.Equal(["B", "C", "A"], r.Live());
        Assert.NotEqual(before, r.State.PositionOf("A"));
        Assert.Equal(3, r.State.ElementCount);
        Assert.Equal(4, r.State.NodeCount);
        Assert.Contains("A", r.State.Elements());
    }

    [Fact]
    public void Kleppmann_MoveVersusConcurrentDelete_DeleteWinsAndElementStaysIdentified()
    {
        var a = new ListReplica("a"); var b = new ListReplica("b");
        string? prev = null; var baseOps = new List<ListOp>();
        foreach (var e in new[] { "A", "B", "C" }) { baseOps.Add(a.Insert(e, prev)); prev = e; }
        foreach (var o in baseOps) b.Receive(o);
        var mv = a.Move("A", "C")!; var del = b.Delete("A");
        a.Receive(del); b.Receive(mv);
        Assert.Equal(["B", "C"], a.Live());
        Assert.Equal(a.Live(), b.Live());
        Assert.True(a.State.Contains("A") && a.State.IsDeleted("A"));
        Assert.Equal(["B", "C", "A"], a.State.AllElementsInOrder());
    }

    [Fact]
    public void Kleppmann_DeleteThenInsertWouldDuplicate_ButMoveDoesNot()
    {
        var a = new ListReplica("a"); var b = new ListReplica("b");
        string? prev = null; var baseOps = new List<ListOp>();
        foreach (var e in new[] { "X", "Y", "Z" }) { baseOps.Add(a.Insert(e, prev)); prev = e; }
        foreach (var o in baseOps) b.Receive(o);
        var m1 = a.Move("X", "Z")!; var m2 = b.Move("X", "Y")!;
        a.Receive(m2); b.Receive(m1);
        Assert.Equal(3, a.Live().Length);
        Assert.Equal(a.Live(), b.Live());
    }

    [Fact]
    public void Kleppmann_ConcurrentInsertNextToMovedElement_StaysAtOldSlot()
    {
        var a = new ListReplica("a"); var b = new ListReplica("b");
        string? prev = null; var baseOps = new List<ListOp>();
        foreach (var e in new[] { "A", "B", "C" }) { baseOps.Add(a.Insert(e, prev)); prev = e; }
        foreach (var o in baseOps) b.Receive(o);
        var ins = a.Insert("N", "B"); var mv = b.Move("B", "C")!;
        a.Receive(mv); b.Receive(ins);
        Assert.Equal(["A", "N", "C", "B"], a.Live());
        Assert.Equal(a.Live(), b.Live());
    }

    [Fact]
    public void Move_Block_ConcurrentBlockMoves_StayContiguousAndConsistent()
    {
        var a = new ListReplica("a"); var b = new ListReplica("b");
        string? prev = null; var baseOps = new List<ListOp>();
        foreach (var e in new[] { "1", "2", "3", "4", "5", "6", "7", "8" }) { baseOps.Add(a.Insert(e, prev)); prev = e; }
        foreach (var o in baseOps) b.Receive(o);
        var ma = a.MoveBlock(["2", "3", "4"], "7"); var mb = b.MoveBlock(["2", "3", "4"], "5");
        foreach (var o in mb) a.Receive(o);
        foreach (var o in ma) b.Receive(o);
        Assert.Equal(a.Live(), b.Live());
        Assert.Equal(["1", "5", "2", "3", "4", "6", "7", "8"], a.Live());
    }

    [Fact]
    public void Idempotent_DuplicateDeliveryChangesNothing()
    {
        var a = new ListReplica("a"); var b = new ListReplica("b");
        var ops = new[] { a.Insert("x", null), a.Insert("y", "x"), a.Move("x", "y")!, a.Delete("y") };
        foreach (var o in ops) Assert.Equal(ApplyResult.Applied, b.Receive(o));
        var snapshot = b.Live();
        foreach (var o in ops) Assert.Equal(ApplyResult.Duplicate, b.Receive(o));
        Assert.Equal(snapshot, b.Live());
        Assert.Equal(a.State.NodeOrder(), b.State.NodeOrder());
    }

    [Fact]
    public void Buffering_OperationsBeforeTheirDependenciesWaitAndThenApply()
    {
        var a = new ListReplica("a"); var b = new ListReplica("b");
        var ops = new[] { a.Insert("x", null), a.Insert("y", "x"), a.Move("x", "y")!, a.Delete("y"), a.Insert("z", "x") };
        foreach (var o in ops.Reverse()) b.Receive(o);
        Assert.Equal(0, b.State.PendingCount);
        Assert.Equal(a.Live(), b.Live());
        Assert.Equal(a.State.NodeOrder(), b.State.NodeOrder());
        var c = new ListReplica("c");
        Assert.Equal(ApplyResult.Buffered, c.Receive(ops[1]));
        Assert.Equal(ApplyResult.Duplicate, c.Receive(ops[1]));
        Assert.Equal(1, c.State.PendingCount);
    }

    [Fact]
    public void EveryDeliveryOrderOfAConcurrentScenarioGivesTheSameList()
    {
        var a = new ListReplica("a"); var b = new ListReplica("b"); var c = new ListReplica("c");
        var baseOps = new List<ListOp>(); string? prev = null;
        foreach (var e in new[] { "1", "2", "3", "4" }) { baseOps.Add(a.Insert(e, prev)); prev = e; }
        foreach (var o in baseOps) { b.Receive(o); c.Receive(o); }
        var ops = new List<ListOp> { a.Insert("n1", "2"), a.Move("1", "4")!, b.Insert("n2", "2"), b.Move("1", "3")!, c.Delete("3"), c.Insert("n3", "3") };
        string[]? expected = null;
        int permutations = 0;
        foreach (var perm in Permutations(ops))
        {
            var s = ListState.Fold(baseOps.Concat(perm));
            Assert.Equal(0, s.PendingCount);
            var live = s.Elements().ToArray();
            expected ??= live;
            Assert.Equal(expected, live);
            permutations++;
        }
        Assert.Equal(720, permutations);
    }

    static IEnumerable<List<T>> Permutations<T>(List<T> items)
    {
        if (items.Count <= 1) { yield return [.. items]; yield break; }
        for (int i = 0; i < items.Count; i++)
        {
            var rest = items.Where((_, j) => j != i).ToList();
            foreach (var tail in Permutations(rest)) { tail.Insert(0, items[i]); yield return tail; }
        }
    }
}
