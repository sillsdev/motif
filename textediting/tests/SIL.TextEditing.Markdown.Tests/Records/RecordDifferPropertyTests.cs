using System;
using System.Collections.Generic;
using System.Linq;
using SIL.TextEditing.Markdown.Records;
using Xunit;
using static SIL.TextEditing.Markdown.Tests.Records.RecordDiffSupport;

namespace SIL.TextEditing.Markdown.Tests.Records
{
    /// <summary>Properties of the record diff: applying it to the base gives the target, it is slot-unique and deterministic, and a document differs from
    /// itself by nothing.</summary>
    public class RecordDifferPropertyTests
    {
        static readonly string[] Words = { "alpha", "beta", "gamma", "delta", "kappa", "omega" };
        static readonly string[] Wss = { "en", "fr", "de", "es" };

        /// <summary>A random entry. Every GUID is owned by one place whatever the draw (senses draw from one pool; subsenses and examples from a pool per sense), so
        /// two draws never move an object between owners.</summary>
        static RecordObject Random(Random r, bool unguided = false)
        {
            string Word() => Words[r.Next(Words.Length)];
            string Phrase() => string.Join(" ", Enumerable.Range(0, r.Next(1, 6)).Select(_ => Word()));
            bool Coin(int outOf = 2) => r.Next(outOf) == 0;
            RecordAlternatives Alt(bool rich)
            {
                var a = new RecordAlternatives();
                foreach (string ws in Wss.Where(_ => Coin())) a.Add(ws, S(rich ? Phrase() : Word()));
                return a;
            }
            List<T> Some<T>(IEnumerable<T> pool) => pool.Where(_ => Coin()).OrderBy(_ => r.Next()).ToList();

            var e = Entry();
            if (Coin()) e.Set("lexeme", S(Word()));
            if (Coin()) e.Set("citation", Alt(false));
            if (Coin()) e.Set("note", Alt(true));
            if (Coin()) e.Set("comment", S(Phrase()));
            if (Coin()) e.Set("homograph", RecordScalar.Of(r.Next(1, 4)));
            if (Coin()) e.Set("flag", RecordScalar.Of(Coin()));
            if (Coin()) e.Set("category", Ref(G(500 + r.Next(3))));
            e.Set("seeAlso", List(Some(Enumerable.Range(0, 5)).Select(n => (RecordValue)Ref(G(600 + n))).ToArray()));
            e.Set("tags", List(Some(Enumerable.Range(0, 5)).Select(n => (RecordValue)Ref(G(700 + n))).ToArray()));
            e.Set("variants", List(Some(Words).Select(w => (RecordValue)S(w)).ToArray()));
            e.Set("labels", List(Some(Words).Select(w => (RecordValue)S(w)).ToArray()));
            e.Set("pronunciations", List(Some(Enumerable.Range(0, 5)).Select(n => (RecordValue)new RecordObject { Guid = G(800 + n) }.Set("form", S(Word()))).ToArray()));
            if (Coin()) e.Set("etymology", new RecordObject { Guid = G(900), Class = "etym" }.Set("source", S(Word())));
            e.Set("glosses", List(Enumerable.Range(0, r.Next(0, 4)).Select(_ => (RecordValue)new RecordObject().Set("text", S(Word()))).ToArray()));
            var senses = new List<RecordValue>();
            foreach (int n in Some(Enumerable.Range(1, 6)))
            {
                var s = Sense(n);
                if (Coin()) s.Set("gloss", Alt(false));
                if (Coin()) s.Set("definition", Alt(true));
                s.Set("domains", List(Some(Enumerable.Range(0, 4)).Select(d => (RecordValue)Ref(G(1000 + d))).ToArray()));
                s.Set("examples", List(Some(Enumerable.Range(0, 3)).Select(x => (RecordValue)new RecordObject { Guid = G(2000 + n * 10 + x) }.Set("text", S(Phrase()))).ToArray()));
                s.Set("subsenses", List(Some(Enumerable.Range(0, 3)).Select(x => (RecordValue)Sense(3000 + n * 10 + x, Word())).ToArray()));
                senses.Add(s);
            }
            if (unguided)
            {
                foreach (int _ in Enumerable.Range(0, r.Next(0, 3))) senses.Insert(r.Next(senses.Count + 1), new RecordObject().Set("gloss", Alts(("en", "new " + Word()))));
                var prons = ((RecordList)e["pronunciations"]!).Items;
                if (Coin()) prons.Insert(r.Next(prons.Count + 1), new RecordObject().Set("form", S("fresh " + Word())));
            }
            e.Set("senses", List(senses.ToArray()));
            return e;
        }

        static void CheckSlotsAreUnique(RecordChangeSet set)
        {
            var dup = set.Changes.GroupBy(c => c.Slot).FirstOrDefault(g => g.Count() > 1);
            Assert.True(dup == null, "Two changes write the slot " + dup?.Key);
        }

        public static IEnumerable<object[]> Batches()
        {
            int count = int.TryParse(Environment.GetEnvironmentVariable("TEXTEDITING_RECORD_DIFF_FUZZ"), out int n) ? n : 2000;
            for (int batch = 0; batch < count / 100; batch++) yield return new object[] { batch };
        }

        [Theory]
        [MemberData(nameof(Batches))]
        public void AppliedToTheBaseTheDiffGivesTheTargetAndIsUniqueAndDeterministic(int batch)
        {
            var schema = Schema();
            for (int k = 0; k < 100; k++)
            {
                int seed = batch * 100 + k;
                var r = new Random(seed);
                var b = Random(r);
                var t = Random(r, unguided: true);
                var set = RecordDiffer.Diff(Doc(b), Doc(t), schema);
                string context = "seed " + seed + "\n" + Describe(set);
                Assert.True(set.Refusals.Count == 0, context);
                CheckSlotsAreUnique(set);
                Assert.Equal(Describe(set), Describe(RecordDiffer.Diff(Doc(Clone(b)), Doc(Clone(t)), schema)));
                RecordObject applied;
                try { applied = Apply(b, set, schema); }
                catch (Exception e) { throw new InvalidOperationException(context + "\nbase   " + Dump(b, schema) + "\ntarget " + Dump(t, schema), e); }
                Assert.True(Dump(t, schema) == Dump(applied, schema), context + "\nbase   " + Dump(b, schema) + "\ntarget " + Dump(t, schema) + "\napplied " + Dump(applied, schema));
                Assert.True(RecordDiffer.Diff(Doc(b), Doc(Clone(b)), schema).IsEmpty, "seed " + seed);
                Assert.True(RecordDiffer.Diff(Doc(applied), Doc(t), schema).IsEmpty, "seed " + seed);
            }
        }

        [Fact]
        public void ShufflingTheTargetsFieldsAndAlternativesDoesNotChangeTheDiff()
        {
            var schema = Schema();
            for (int seed = 0; seed < 200; seed++)
            {
                var r = new Random(seed);
                var b = Random(r);
                var t = Random(r);
                string expected = Describe(RecordDiffer.Diff(Doc(b), Doc(t), schema));
                Assert.Equal(expected, Describe(RecordDiffer.Diff(Doc(b), Doc(Shuffle(t, r)), schema)));
            }
        }

        // Reorders fields and alternatives (not list items, whose order means something in a sequence) and the items of collections.
        static RecordObject Shuffle(RecordObject o, Random r)
        {
            var c = new RecordObject { Guid = o.Guid, Class = o.Class };
            foreach (var f in o.Fields.OrderBy(_ => r.Next()))
            {
                RecordValue v = f.Value;
                switch (f.Value)
                {
                    case RecordObject child: v = Shuffle(child, r); break;
                    case RecordAlternatives a:
                    {
                        var x = new RecordAlternatives();
                        foreach (var i in a.Items.OrderBy(_ => r.Next())) x.Add(i.Key, i.Value);
                        v = x;
                        break;
                    }
                    case RecordList l:
                    {
                        var x = new RecordList();
                        var items = l.Items.Select(i => i is RecordObject io ? Shuffle(io, r) : i);
                        if (f.Key == "tags" || f.Key == "labels" || f.Key == "pronunciations" || f.Key == "domains") items = items.OrderBy(_ => r.Next());
                        foreach (var i in items) x.Add(i);
                        v = x;
                        break;
                    }
                }
                c.Fields.Add(new KeyValuePair<string, RecordValue>(f.Key, v));
            }
            return c;
        }

        [Fact]
        public void ARandomPairOfSequenceOrdersIsAppliedWithFewMoves()
        {
            var r = new Random(7);
            for (int round = 0; round < 300; round++)
            {
                int n = r.Next(2, 9);
                var order = Enumerable.Range(1, n).ToArray();
                var shuffled = order.OrderBy(_ => r.Next()).ToArray();
                var set = Diff(Doc(Senses(order)), Doc(Senses(shuffled)));
                Assert.True(set.Changes.Count <= n - 1, "moves " + set.Changes.Count + " of " + n);
                Assert.Equal(Dump(Senses(shuffled), Schema()), Dump(Apply(Senses(order), set, Schema()), Schema()));
            }
        }

        static RecordObject Senses(params int[] ns) => Entry().Set("senses", List(ns.Select(n => (RecordValue)Sense(n)).ToArray()));

        static RecordObject WithoutSense(RecordObject entry, Guid guid)
        {
            var c = Clone(entry);
            var senses = (RecordList)c["senses"]!;
            senses.Items.RemoveAll(i => ((RecordObject)i).Guid == guid);
            return c;
        }

        // A partial file holds a random part of one sense: some of its fields, drawn from another random entry's sense of the same GUID (or a fresh draw).
        [Fact]
        public void APartialSubtreeChangesOnlyItsOwnObjectAndNeverClearsOrRemoves()
        {
            var schema = Schema();
            int checkedCases = 0;
            for (int seed = 0; seed < 600; seed++)
            {
                var r = new Random(seed);
                var b = Random(r);
                var other = Random(r);
                var bSenses = ((RecordList)b["senses"]!).Items.OfType<RecordObject>().ToList();
                var oSenses = ((RecordList)other["senses"]!).Items.OfType<RecordObject>().ToList();
                var pick = bSenses.FirstOrDefault(s => oSenses.Any(o => o.Guid == s.Guid));
                if (pick == null) continue;
                var source = Clone(oSenses.First(o => o.Guid == pick.Guid));
                var part = new RecordObject();
                if (r.Next(2) == 0) part.Guid = pick.Guid;
                foreach (var f in source.Fields.Where(f => !f.Value.IsEmpty && r.Next(2) == 0)) part.Fields.Add(f);
                string root = "/lexicon/foo/senses/" + (bSenses.IndexOf(pick) + 1);
                var set = RecordDiffer.Diff(Doc(b), Doc(part, root), schema);
                string context = "seed " + seed + "\n" + Describe(set);
                Assert.True(set.IsPartial && set.Refusals.Count == 0, context);
                Assert.True(set.Changes.All(c => !c.ByOmission && c.Kind != RecordChangeKind.RemoveObject && c.Kind != RecordChangeKind.Clear && c.Kind != RecordChangeKind.ClearAlternative
                    && c.Kind != RecordChangeKind.ClearRich && c.Kind != RecordChangeKind.RemoveReference && c.Kind != RecordChangeKind.RemoveItem), context);
                CheckSlotsAreUnique(set);
                var applied = Apply(b, set, schema);
                Assert.True(Dump(WithoutSense(b, pick.Guid!.Value), schema) == Dump(WithoutSense(applied, pick.Guid.Value), schema), "outside the object changed: " + context);
                Assert.True(RecordDiffer.Diff(Doc(applied), Doc(part, root), schema).IsEmpty, "not idempotent: " + context);
                checkedCases++;
            }
            Assert.True(checkedCases > 200, "only " + checkedCases + " partial cases ran");
        }

        // Each case edits a copy of the base in one way the differ must refuse. Nothing is changed, the refusal is typed, and applying the set leaves the base as it was.
        [Fact]
        public void EveryKindOfRefusalIsRaisedAndNeverApplied()
        {
            var schema = Schema();
            var seen = new HashSet<RecordRefusalKind>();
            int cases = 0;
            for (int seed = 0; seed < 600; seed++)
            {
                var r = new Random(seed);
                var b = Random(r);
                var t = Clone(b);
                var tSenses = ((RecordList)t["senses"]!).Items.OfType<RecordObject>().ToList();
                RecordRefusalKind expected;
                Guid? slotGuid = null;
                switch (seed % 6)
                {
                    case 0: // an example moves to another sense
                    {
                        var withExamples = tSenses.Where(s => ((RecordList)s["examples"]!).Items.Count > 0).ToList();
                        var targets = tSenses.ToList();
                        if (withExamples.Count == 0 || targets.Count < 2) continue;
                        var from = withExamples[r.Next(withExamples.Count)];
                        var to = targets.First(s => s != from);
                        var moved = (RecordObject)((RecordList)from["examples"]!).Items[0];
                        ((RecordList)from["examples"]!).Items.RemoveAt(0);
                        ((RecordList)to["examples"]!).Items.Add(moved);
                        expected = RecordRefusalKind.MovedToOtherOwner;
                        slotGuid = moved.Guid;
                        break;
                    }
                    case 1: // a class changes
                    {
                        if (!(t["etymology"] is RecordObject e)) continue;
                        e.Class = "etym2";
                        expected = RecordRefusalKind.ClassChanged;
                        break;
                    }
                    case 2: // an owned object's guid changes in place
                    {
                        if (!(t["etymology"] is RecordObject e)) continue;
                        e.Guid = G(950);
                        expected = RecordRefusalKind.IdentityChanged;
                        break;
                    }
                    case 3: // the record's guid changes
                        t.Guid = G(777);
                        expected = RecordRefusalKind.IdentityChanged;
                        break;
                    case 4: // a guid appears twice in the file
                    {
                        if (tSenses.Count == 0) continue;
                        var dup = Clone(tSenses[0]);
                        dup.Set("gloss", Alts(("en", "a copy")));
                        ((RecordList)t["senses"]!).Items.Add(dup);
                        expected = RecordRefusalKind.DuplicateGuid;
                        slotGuid = dup.Guid;
                        break;
                    }
                    default: // a computed field is edited
                        t.Set("headword", S("edited" + seed));
                        expected = RecordRefusalKind.ComputedField;
                        break;
                }
                var set = RecordDiffer.Diff(Doc(b), Doc(t), schema);
                string context = "seed " + seed + " " + expected + "\n" + Describe(set);
                Assert.True(set.Refusals.Any(x => x.Kind == expected), context);
                Assert.True(set.Changes.Count == 0, context);
                if (slotGuid != null) Assert.DoesNotContain(set.Changes, c => c.ItemGuid == slotGuid || c.ObjectGuid == slotGuid);
                Assert.True(Dump(b, schema) == Dump(Apply(b, set, schema), schema), context);
                seen.Add(expected);
                cases++;
            }
            Assert.True(cases > 300, "only " + cases + " refusal cases ran");
            Assert.Equal(new[] { RecordRefusalKind.ComputedField, RecordRefusalKind.IdentityChanged, RecordRefusalKind.ClassChanged, RecordRefusalKind.MovedToOtherOwner, RecordRefusalKind.DuplicateGuid }.OrderBy(k => k), seen.OrderBy(k => k));
        }

        [Fact]
        public void AGuidDuplicatedInTheBaseIsRefusedToo()
        {
            var schema = Schema();
            for (int seed = 0; seed < 100; seed++)
            {
                var r = new Random(seed);
                var t = Random(r);
                var senses = ((RecordList)t["senses"]!).Items;
                if (senses.Count == 0) continue;
                var b = Clone(t);
                var dup = Clone((RecordObject)senses[0]);
                dup.Set("gloss", Alts(("en", "copy")));
                ((RecordList)b["senses"]!).Items.Add(dup);
                var set = RecordDiffer.Diff(Doc(b), Doc(t), schema);
                Assert.Contains(set.Refusals, x => x.Kind == RecordRefusalKind.DuplicateGuid);
                Assert.Empty(set.Changes);
            }
        }

        [Fact]
        public void EveryReadableConformanceRecordDiffersFromItselfByNothing()
        {
            int checkedFiles = 0;
            foreach (var c in RecordFixtures.All().Where(c => c.IsRead))
            {
                var read = RecordFileReader.Read(c.Text!, c.Schema);
                if (read.Document == null) continue;
                var doc = read.Document;
                var copy = RecordFileReader.Read(c.Text!, c.Schema).Document!;
                RecordChangeSet set;
                try { set = RecordDiffer.Diff(doc, copy, c.Schema); }
                catch (ArgumentException) { continue; } // a root the schema cannot walk is a reader error, not a diff input
                Assert.True(set.Changes.Count == 0 && set.Refusals.All(x => x.Kind == RecordRefusalKind.DuplicateGuid), c.Id + "\n" + Describe(set)); // a file that repeats a guid is refused, not compared
                checkedFiles++;
            }
            Assert.True(checkedFiles > 20, "only " + checkedFiles + " fixtures were diffed");
        }
    }
}
