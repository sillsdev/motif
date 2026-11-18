using System;
using System.Collections.Generic;
using System.Linq;
using SIL.TextEditing.Markdown.Records;
using Xunit;
using static SIL.TextEditing.Markdown.Tests.Records.RecordDiffSupport;

namespace SIL.TextEditing.Markdown.Tests.Records
{
    public class RecordDifferTests
    {
        static RecordChange One(RecordChangeSet set)
        {
            Assert.Empty(set.Refusals);
            return Assert.Single(set.Changes);
        }

        // ---------------------------------------------------------------- scalars

        [Fact]
        public void IdenticalDocumentsHaveNoChanges()
        {
            var d = Entry().Set("lexeme", S("x")).Set("senses", List(Sense(1, "a"), Sense(2, "b")));
            Assert.True(Diff(Doc(d), Doc(Clone(d))).IsEmpty);
        }

        [Fact]
        public void SetAScalar()
        {
            var c = One(Diff(Doc(Entry()), Doc(Entry().Set("lexeme", S("new")))));
            Assert.Equal(RecordChangeKind.Set, c.Kind);
            Assert.Equal(EntryGuid, c.ObjectGuid);
            Assert.Equal("lexeme", c.Field);
            Assert.Null(c.Old);
            Assert.Equal("new", c.New);
        }

        [Fact]
        public void ChangeAndClearAScalar()
        {
            var b = Entry().Set("lexeme", S("old")).Set("homograph", RecordScalar.Of(1)).Set("flag", RecordScalar.Of(true));
            var set = Diff(Doc(b), Doc(Entry().Set("lexeme", S("new")).Set("homograph", RecordScalar.Of(2))));
            Assert.Equal(new[] { "Set lexeme old new", "Set homograph 1 2", "Clear flag true " }, set.Changes.Select(c => c.Kind + " " + c.Field + " " + c.Old + " " + c.New).ToArray());
        }

        [Fact]
        public void ASingleReferenceCarriesItsTargets()
        {
            var set = Diff(Doc(Entry().Set("category", Ref(G(7)))), Doc(Entry().Set("category", Ref(G(8)))));
            var c = One(set);
            Assert.Equal(RecordChangeKind.Set, c.Kind);
            Assert.Equal(G(7), c.OldTarget);
            Assert.Equal(G(8), c.NewTarget);
            var cleared = One(Diff(Doc(Entry().Set("category", Ref(G(7)))), Doc(Entry())));
            Assert.Equal(RecordChangeKind.Clear, cleared.Kind);
            Assert.Equal(G(7), cleared.OldTarget);
        }

        // ---------------------------------------------------------------- alternatives and rich values

        [Fact]
        public void AlternativesAreSetAndClearedOneWritingSystemAtATime()
        {
            var b = Entry().Set("citation", Alts(("en", "a"), ("fr", "b"), ("de", "c")));
            var t = Entry().Set("citation", Alts(("en", "a"), ("fr", "B"), ("es", "d")));
            var set = Diff(Doc(b), Doc(t));
            Assert.Equal(new[] { "ClearAlternative de", "SetAlternative es", "SetAlternative fr" }, set.Changes.Select(c => c.Kind + " " + c.WritingSystem).ToArray());
            Assert.All(set.Changes, c => Assert.Equal("citation", c.Field));
            Assert.Equal("b", set.Changes.Single(c => c.WritingSystem == "fr").Old);
        }

        [Fact]
        public void ARichValueCarriesTheWholeNewValueTheOldOneAndAWordSummary()
        {
            var set = Diff(Doc(Entry().Set("comment", S("the quick brown fox"))), Doc(Entry().Set("comment", S("the slow brown dog"))));
            var c = One(set);
            Assert.Equal(RecordChangeKind.SetRich, c.Kind);
            Assert.Equal("the quick brown fox", c.Old);
            Assert.Equal("the slow brown dog", c.New);
            Assert.Equal("the quick brown fox", string.Concat(c.Words!.Where(w => w.Kind != RecordWordSpanKind.Added).Select(w => w.Text)));
            Assert.Equal("the slow brown dog", string.Concat(c.Words!.Where(w => w.Kind != RecordWordSpanKind.Removed).Select(w => w.Text)));
            Assert.Contains(c.Words!, w => w.Kind == RecordWordSpanKind.Unchanged && w.Text.Contains("brown"));
            Assert.Contains(c.Words!, w => w.Kind == RecordWordSpanKind.Removed && w.Text == "quick");
            Assert.Contains(c.Words!, w => w.Kind == RecordWordSpanKind.Added && w.Text == "slow");
        }

        [Fact]
        public void AMultiRichAlternativeIsARichChangeWithAWritingSystem()
        {
            var b = Entry().Set("note", Alts(("en", "one two"), ("fr", "x")));
            var t = Entry().Set("note", Alts(("en", "one three")));
            var set = Diff(Doc(b), Doc(t));
            Assert.Equal(new[] { "SetRich en", "ClearRich fr" }, set.Changes.Select(c => c.Kind + " " + c.WritingSystem).ToArray());
            Assert.Null(set.Changes[1].New);
            Assert.Equal("x", set.Changes[1].Old);
        }

        [Fact]
        public void ARichValueWithNoOldValueHasNoWordSummary()
        {
            var c = One(Diff(Doc(Entry()), Doc(Entry().Set("comment", S("hello")))));
            Assert.Null(c.Words);
            Assert.Null(c.Old);
        }

        // ---------------------------------------------------------------- owned objects

        [Fact]
        public void AnAddedObjectCarriesItsWholeSubtreeAndPlace()
        {
            var b = Entry().Set("senses", List(Sense(1, "a")));
            var t = Entry().Set("senses", List(Sense(1, "a"), Sense(2, "b").Set("examples", List(new RecordObject { Guid = G(20) }.Set("text", S("ex"))))));
            var c = One(Diff(Doc(b), Doc(t)));
            Assert.Equal(RecordChangeKind.AddObject, c.Kind);
            Assert.Equal(G(2), c.ItemGuid);
            Assert.Equal(2, c.Index);
            Assert.Equal(G(1).ToString("D"), c.After);
            Assert.Equal("entry", c.ObjectType);
            Assert.Single(((RecordList)c.Subtree!["examples"]!).Items);
        }

        [Fact]
        public void AnObjectMissingFromAWholeTargetIsRemovedByOmission()
        {
            var b = Entry().Set("senses", List(Sense(1, "a"), Sense(2, "b")));
            var t = Entry().Set("senses", List(Sense(2, "b")));
            var set = Diff(Doc(b), Doc(t));
            var c = One(set);
            Assert.Equal(RecordChangeKind.RemoveObject, c.Kind);
            Assert.Equal(G(1), c.ItemGuid);
            Assert.True(c.ByOmission);
            Assert.True(set.HasRemovalsByOmission);
        }

        [Fact]
        public void AnEditInANestedObjectIsAddressedByItsOwnGuid()
        {
            var b = Entry().Set("senses", List(Sense(1, "a").Set("subsenses", List(Sense(3, "deep")))));
            var t = Entry().Set("senses", List(Sense(1, "a").Set("subsenses", List(Sense(3, "deeper")))));
            var c = One(Diff(Doc(b), Doc(t)));
            Assert.Equal(G(3), c.ObjectGuid);
            Assert.Equal("gloss", c.Field);
            Assert.Equal("en", c.WritingSystem);
        }

        [Fact]
        public void ASingleOwnedObjectIsAddedRemovedOrReplaced()
        {
            var etym = new RecordObject { Guid = G(30), Class = "etym" }.Set("source", S("Latin"));
            var added = One(Diff(Doc(Entry()), Doc(Entry().Set("etymology", etym))));
            Assert.Equal(RecordChangeKind.AddObject, added.Kind);
            Assert.Equal("etymology", added.Field);
            var removed = One(Diff(Doc(Entry().Set("etymology", etym)), Doc(Entry())));
            Assert.Equal(RecordChangeKind.RemoveObject, removed.Kind);
            Assert.True(removed.ByOmission);
            var other = new RecordObject { Guid = G(31), Class = "etym" }.Set("source", S("Greek"));
            var replaced = Diff(Doc(Entry().Set("etymology", etym)), Doc(Entry().Set("etymology", other)));
            Assert.Equal(new[] { RecordChangeKind.RemoveObject, RecordChangeKind.AddObject }, replaced.Changes.Select(c => c.Kind).ToArray());
            Assert.False(replaced.Changes[0].ByOmission);
        }

        [Fact]
        public void ObjectsWithNoGuidAreMatchedByPosition()
        {
            RecordObject Gloss(string t) => new RecordObject().Set("text", S(t));
            var b = Entry().Set("glosses", List(Gloss("a"), Gloss("b")));
            var t = Entry().Set("glosses", List(Gloss("a"), Gloss("B"), Gloss("c")));
            var set = Diff(Doc(b), Doc(t));
            Assert.Equal(new[] { "Set glosses/2/text", "AddObject glosses" }, set.Changes.Select(c => c.Kind + " " + c.Field).ToArray());
            Assert.Equal(EntryGuid, set.Changes[0].ObjectGuid);
            var shrunk = Diff(Doc(b), Doc(Entry().Set("glosses", List(Gloss("a")))));
            var removed = One(shrunk);
            Assert.Equal(RecordChangeKind.RemoveObject, removed.Kind);
            Assert.Equal("#2", removed.ItemKey);
        }

        // ---------------------------------------------------------------- order

        static RecordObject Senses(params int[] ns) => Entry().Set("senses", List(ns.Select(n => (RecordValue)Sense(n)).ToArray()));

        [Fact]
        public void MovingOneSequenceItemIsOneMove()
        {
            var set = Diff(Doc(Senses(1, 2, 3, 4)), Doc(Senses(4, 1, 2, 3)));
            var c = One(set);
            Assert.Equal(RecordChangeKind.MoveObject, c.Kind);
            Assert.Equal(G(4), c.ItemGuid);
            Assert.Equal(1, c.Index);
            Assert.Null(c.After);
        }

        [Fact]
        public void ReversingASequenceNeedsAtMostNMinusOneMoves()
        {
            var set = Diff(Doc(Senses(1, 2, 3, 4, 5)), Doc(Senses(5, 4, 3, 2, 1)));
            Assert.Equal(4, set.Changes.Count);
            Assert.Equal(Dump(Apply(Senses(1, 2, 3, 4, 5), set), Schema()), Dump(Senses(5, 4, 3, 2, 1), Schema()));
        }

        [Fact]
        public void ReorderingACollectionIsNoChange()
        {
            RecordObject P(int n) => new RecordObject { Guid = G(n) }.Set("form", S("f" + n));
            var b = Entry().Set("pronunciations", List(P(1), P(2), P(3))).Set("tags", List(Ref(G(7)), Ref(G(8)))).Set("labels", List(S("a"), S("b")));
            var t = Entry().Set("pronunciations", List(P(3), P(1), P(2))).Set("tags", List(Ref(G(8)), Ref(G(7)))).Set("labels", List(S("b"), S("a")));
            Assert.True(Diff(Doc(b), Doc(t)).IsEmpty);
        }

        [Fact]
        public void ACollectionAddsAndRemovesWithNoPlace()
        {
            RecordObject P(int n) => new RecordObject { Guid = G(n) }.Set("form", S("f" + n));
            var b = Entry().Set("pronunciations", List(P(1), P(2))).Set("tags", List(Ref(G(7)))).Set("labels", List(S("a")));
            var t = Entry().Set("pronunciations", List(P(2), P(3))).Set("tags", List(Ref(G(8)))).Set("labels", List(S("b")));
            var set = Diff(Doc(b), Doc(t));
            Assert.Equal(new[] { "pronunciations RemoveObject", "pronunciations AddObject", "tags RemoveReference", "tags AddReference", "labels RemoveItem", "labels AddItem" },
                set.Changes.OrderBy(c => c.Field == "pronunciations" ? 0 : c.Field == "tags" ? 1 : 2).Select(c => c.Field + " " + c.Kind).ToArray());
            Assert.All(set.Changes, c => { Assert.Null(c.Index); Assert.Null(c.After); });
        }

        [Fact]
        public void ReferencesAddRemoveAndMoveInASequence()
        {
            var b = Entry().Set("seeAlso", List(Ref(G(1)), Ref(G(2)), Ref(G(3))));
            var t = Entry().Set("seeAlso", List(Ref(G(3)), Ref(G(1)), Ref(G(9))));
            var set = Diff(Doc(b), Doc(t));
            Assert.Equal(new[] { "RemoveReference " + G(2).ToString("D"), "MoveReference " + G(3).ToString("D"), "AddReference " + G(9).ToString("D") },
                set.Changes.Select(c => c.Kind + " " + c.ItemKey).ToArray());
            Assert.Equal(G(2), set.Changes[0].OldTarget);
            Assert.Equal(G(9), set.Changes[2].NewTarget);
            Assert.Equal(Dump(t, Schema()), Dump(Apply(b, set), Schema()));
        }

        [Fact]
        public void RepeatedTextItemsGetOccurrenceKeys()
        {
            var set = Diff(Doc(Entry().Set("variants", List(S("a"), S("a")))), Doc(Entry().Set("variants", List(S("a"), S("a"), S("a")))));
            var c = One(set);
            Assert.Equal(RecordChangeKind.AddItem, c.Kind);
            Assert.Equal("a#3", c.ItemKey);
            Assert.Equal("a#2", c.After);
        }

        // ---------------------------------------------------------------- refusals

        [Fact]
        public void AnEditToAComputedFieldIsRefusedAndAnUnchangedOneIsNot()
        {
            var set = Diff(Doc(Entry().Set("headword", S("a"))), Doc(Entry().Set("headword", S("b"))));
            Assert.Empty(set.Changes);
            var r = Assert.Single(set.Refusals);
            Assert.Equal(RecordRefusalKind.ComputedField, r.Kind);
            Assert.Equal("headword", r.Field);
            Assert.Contains("computed", r.Reason);
            Assert.True(Diff(Doc(Entry().Set("headword", S("a"))), Doc(Entry().Set("headword", S("a")))).IsEmpty);
        }

        [Fact]
        public void AChangedGuidOfTheRecordIsRefused()
        {
            var set = Diff(Doc(Entry().Set("lexeme", S("a"))), Doc(new RecordObject { Guid = G(99) }.Set("lexeme", S("b"))));
            Assert.Empty(set.Changes);
            Assert.Equal(RecordRefusalKind.IdentityChanged, Assert.Single(set.Refusals).Kind);
        }

        [Fact]
        public void AChangedClassOfAnExistingObjectIsRefused()
        {
            var b = Entry().Set("etymology", new RecordObject { Guid = G(30), Class = "etym" }.Set("source", S("a")));
            var t = Entry().Set("etymology", new RecordObject { Guid = G(30), Class = "etym2" }.Set("language", S("b")));
            var set = Diff(Doc(b), Doc(t));
            Assert.Empty(set.Changes);
            var r = Assert.Single(set.Refusals);
            Assert.Equal(RecordRefusalKind.ClassChanged, r.Kind);
            Assert.Equal("etym", r.Old);
            Assert.Equal("etym2", r.New);
        }

        [Fact]
        public void AnObjectUnderAnotherOwnerIsRefusedOnceAndNeitherAddedNorRemoved()
        {
            var b = Entry().Set("senses", List(Sense(1, "a").Set("subsenses", List(Sense(5, "x"))), Sense(2, "b")));
            var t = Entry().Set("senses", List(Sense(1, "a"), Sense(2, "b").Set("subsenses", List(Sense(5, "x")))));
            var set = Diff(Doc(b), Doc(t));
            Assert.Empty(set.Changes);
            var r = Assert.Single(set.Refusals);
            Assert.Equal(RecordRefusalKind.MovedToOtherOwner, r.Kind);
            Assert.Contains(G(5).ToString("D"), r.Reason);
        }

        [Fact]
        public void AnObjectMovedToAnotherFieldOfItsOwnerIsRefused()
        {
            var o = new RecordObject { Guid = G(40) }.Set("form", S("f"));
            var b = Entry().Set("pronunciations", List(o));
            // the same GUID as a sense in the file, a pronunciation in the export
            var t = Entry().Set("senses", List(new RecordObject { Guid = G(40) }));
            var set = Diff(Doc(b), Doc(t));
            Assert.Empty(set.Changes);
            Assert.Equal(RecordRefusalKind.MovedToOtherOwner, Assert.Single(set.Refusals).Kind);
        }

        // ---------------------------------------------------------------- partial files

        [Fact]
        public void AFieldAbsentFromAPartialFileIsNoChange()
        {
            var b = Entry().Set("lexeme", S("x")).Set("citation", Alts(("en", "a"))).Set("senses", List(Sense(1, "g").Set("definition", Alts(("en", "d"))), Sense(2, "h")));
            var t = new RecordObject { Guid = G(1) }.Set("gloss", Alts(("en", "G")));
            var set = Diff(Doc(b), Doc(t, "/lexicon/foo/senses/1"));
            var c = One(set);
            Assert.True(set.IsPartial);
            Assert.Equal(G(1), c.ObjectGuid);
            Assert.Equal("gloss", c.Field);
            Assert.Equal("G", c.New);
        }

        [Fact]
        public void APartialFileWithNoGuidIsAddressedByTheBasesGuid()
        {
            var b = Entry().Set("senses", List(Sense(1, "g"), Sense(2, "h")));
            var set = Diff(Doc(b), Doc(new RecordObject().Set("gloss", Alts(("en", "H"))), "/lexicon/foo/senses/2"));
            Assert.Equal(G(2), One(set).ObjectGuid);
        }

        [Fact]
        public void APartialFileNeverRemovesOrClearsByOmission()
        {
            var b = Entry().Set("senses", List(Sense(1, "g").Set("examples", List(new RecordObject { Guid = G(20) }.Set("text", S("a")), new RecordObject { Guid = G(21) }.Set("text", S("b"))))));
            var t = new RecordObject { Guid = G(1) }.Set("gloss", Alts(("fr", "x"))).Set("examples", List(new RecordObject { Guid = G(21) }.Set("text", S("b2"))));
            var set = Diff(Doc(b), Doc(t, "/lexicon/foo/senses/1"));
            Assert.Equal(new[] { "SetAlternative gloss fr", "SetRich text " }, set.Changes.Select(c => c.Kind + " " + c.Field + " " + c.WritingSystem).ToArray());
            Assert.DoesNotContain(set.Changes, c => c.Kind == RecordChangeKind.RemoveObject || c.Kind == RecordChangeKind.ClearAlternative);
        }

        [Fact]
        public void APresentButEmptyValueInAPartialFileClears()
        {
            var b = Entry().Set("senses", List(Sense(1, "g").Set("gloss", Alts(("en", "a"), ("fr", "b"))).Set("examples", List(new RecordObject { Guid = G(20) }.Set("translation", S("x"))))));
            var t = new RecordObject().Set("gloss", new RecordAlternatives().Add("en", S("")));
            var set = Diff(Doc(b), Doc(t, "/lexicon/foo/senses/1"));
            var c = One(set);
            Assert.Equal(RecordChangeKind.ClearAlternative, c.Kind);
            Assert.Equal("en", c.WritingSystem);
            var example = Diff(Doc(b), Doc(new RecordObject().Set("translation", S("")), "/lexicon/foo/senses/1/examples/1"));
            Assert.Equal(RecordChangeKind.Clear, One(example).Kind);
            Assert.Equal(G(20), One(example).ObjectGuid);
        }

        [Fact]
        public void ARootNamingTheRecordItselfIsAWholeFile()
        {
            var b = Entry().Set("citation", Alts(("en", "a"), ("fr", "b")));
            var set = Diff(Doc(b), Doc(Entry().Set("citation", Alts(("en", "a"))), "/lexicon/foo"));
            Assert.False(set.IsPartial);
            Assert.Equal(RecordChangeKind.ClearAlternative, One(set).Kind);
        }

        [Fact]
        public void AFragmentPartialDiffsOnlyThatField()
        {
            var b = Entry().Set("senses", List(Sense(1, "g").Set("definition", Alts(("en", "old words"), ("fr", "keep")))));
            var t = new RecordObject().Set("definition", Alts(("en", "new words")));
            var set = Diff(Doc(b), Doc(t, "/lexicon/foo/senses/1#definition.en"));
            var c = One(set);
            Assert.Equal(RecordChangeKind.SetRich, c.Kind);
            Assert.Equal("en", c.WritingSystem);
            Assert.Equal(G(1), c.ObjectGuid);
        }

        [Fact]
        public void APartialBaseWithTheSameRootIsDiffedDirectly()
        {
            var b = new RecordObject { Guid = G(1) }.Set("gloss", Alts(("en", "a")));
            var t = new RecordObject { Guid = G(1) }.Set("gloss", Alts(("en", "b")));
            Assert.Equal("b", One(Diff(Doc(b, "/lexicon/foo/senses/1"), Doc(t, "/lexicon/foo/senses/1"))).New);
        }

        [Fact]
        public void APartialRootTheBaseLacksIsAnError()
        {
            Assert.Throws<ArgumentException>(() => Diff(Doc(Senses(1)), Doc(new RecordObject(), "/lexicon/foo/senses/2")));
            Assert.Throws<ArgumentException>(() => Diff(Doc(Senses(1)), Doc(new RecordObject(), "/lexicon/foo/nope/1")));
        }

        // ---------------------------------------------------------------- determinism and shape

        [Fact]
        public void TheOrderOfFieldsAndAlternativesInTheFileDoesNotChangeTheResult()
        {
            var b = Entry().Set("lexeme", S("a")).Set("citation", Alts(("en", "a"), ("fr", "b")));
            var t1 = Entry().Set("lexeme", S("b")).Set("citation", Alts(("fr", "B"), ("en", "A"))).Set("homograph", RecordScalar.Of(3));
            var t2 = Entry().Set("homograph", RecordScalar.Of(3)).Set("citation", Alts(("en", "A"), ("fr", "B"))).Set("lexeme", S("b"));
            Assert.Equal(Describe(Diff(Doc(b), Doc(t1))), Describe(Diff(Doc(b), Doc(t2))));
        }

        [Fact]
        public void TheDiffDoesNotChangeEitherDocument()
        {
            var b = Doc(Senses(1, 2, 3).Set("lexeme", S("a")));
            var t = Doc(Senses(3, 2, 4).Set("lexeme", S("b")));
            string before = Dump(b.Data, Schema()) + Dump(t.Data, Schema());
            Diff(b, t);
            Assert.Equal(before, Dump(b.Data, Schema()) + Dump(t.Data, Schema()));
        }

        [Fact]
        public void DocumentsOfAnotherKindOrAnUnknownKindAreRefusedWithAnException()
        {
            Assert.Throws<ArgumentException>(() => RecordDiffer.Diff(Doc(), new RecordDocument("nope/kind"), Schema()));
            Assert.Throws<ArgumentNullException>(() => RecordDiffer.Diff(null!, Doc(), Schema()));
        }
    }
}
