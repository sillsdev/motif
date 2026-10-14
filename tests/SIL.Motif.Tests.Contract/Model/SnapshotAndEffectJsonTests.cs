using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using Xunit;

namespace SIL.Motif.Tests.Model;

/// <summary>
/// LibLCM-free unit tests for snapshot/effect JSON rendering and digest: fast checks
/// that complement <see cref="SIL.Motif.Tests.Runner.ProposalDryRunnerTests"/>'s real-project
/// proof. Per the Change Set contract, the snapshot set is keyed by canonical id, never a storage
/// GUID, and a field with no populated alternative is omitted rather than written as an empty
/// value; the effect digest hashes the full set of <c>(canonicalId, field, before, after)</c>
/// deltas, so a changed <c>before</c> moves the digest even when <c>after</c> is unchanged, and the
/// digest is stable under reordering because it is a hash of a set, not a sequence.
/// </summary>
public class SnapshotAndEffectJsonTests
{
    private static readonly CanonicalId SenseId = CanonicalId.FromGuid(
        Guid.ParseExact("00010203-0405-0607-0809-0a0b0c0d0e0f", "D"));

    [Fact]
    public void ObjectSnapshotJsonWriter_KeysTheSnapshotSetByCanonicalId()
    {
        var snapshot = new ObjectSnapshot(
            SenseId,
            new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                [SnapshotFields.LexSenseGloss] = new Dictionary<string, string> { ["en"] = "run quickly on foot" },
            });

        var json = ObjectSnapshotJsonWriter.WriteJson(new[] { snapshot });
        using var document = JsonDocument.Parse(json);

        // Multi-snapshot: outer key IS the canonical id, so no redundant nested "fields" wrapper.
        Assert.True(document.RootElement.TryGetProperty(SenseId.Value, out var bySenseId));
        Assert.Equal(
            "run quickly on foot",
            bySenseId.GetProperty(SnapshotFields.LexSenseGloss).GetProperty("en").GetString());

        // Single-snapshot: no outer key to carry identity, so "canonicalId" and "fields" are explicit.
        var singleJson = ObjectSnapshotJsonWriter.WriteJson(snapshot);
        using var singleDocument = JsonDocument.Parse(singleJson);
        Assert.Equal(SenseId.Value, singleDocument.RootElement.GetProperty("canonicalId").GetString());
        Assert.Equal(
            "run quickly on foot",
            singleDocument.RootElement.GetProperty("fields").GetProperty(SnapshotFields.LexSenseGloss)
                .GetProperty("en").GetString());
    }

    [Fact]
    public void ObjectSnapshotJsonWriter_OmitsFieldsWithNoPopulatedAlternatives()
    {
        // "No populated alternative" means the field is absent entirely, never an empty-string alternative.
        var emptySnapshot = ObjectSnapshot.Empty(SenseId);

        var json = ObjectSnapshotJsonWriter.WriteJson(emptySnapshot);
        using var document = JsonDocument.Parse(json);

        Assert.Empty(document.RootElement.GetProperty("fields").EnumerateObject());
    }

    [Fact]
    public void ExpectedEffectSetDigest_IsStableAcrossRepeatedComputation()
    {
        var effects = new[]
        {
            new ExpectedEffect(
                SenseId,
                SnapshotFields.LexSenseGloss,
                new Dictionary<string, string> { ["en"] = "run quickly on foot" },
                new Dictionary<string, string> { ["en"] = "move quickly on foot" }),
        };

        var digest1 = ExpectedEffectSetDigest.Compute(effects);
        var digest2 = ExpectedEffectSetDigest.Compute(effects);

        Assert.StartsWith("sha256:", digest1);
        Assert.Equal(digest1, digest2);
    }

    [Fact]
    public void ExpectedEffectSetDigest_ChangesWhenBeforeDiffersEvenIfAfterIsTheSame()
    {
        // Hash the transition, not the destination: a changed `before` must move the digest too.
        var after = new Dictionary<string, string> { ["en"] = "move quickly on foot" };

        var digestA = ExpectedEffectSetDigest.Compute(new[]
        {
            new ExpectedEffect(SenseId, SnapshotFields.LexSenseGloss,
                new Dictionary<string, string> { ["en"] = "run quickly on foot" }, after),
        });

        var digestB = ExpectedEffectSetDigest.Compute(new[]
        {
            new ExpectedEffect(SenseId, SnapshotFields.LexSenseGloss,
                new Dictionary<string, string> { ["en"] = "sprint on foot" }, after),
        });

        Assert.NotEqual(digestA, digestB);
    }

    [Fact]
    public void PreviewEvidenceDoesNotChangeSemanticEffectDigestButDoesChangeTheFootprint()
    {
        using var firstPreview = JsonDocument.Parse("{\"textUses\":[{\"index\":1}]}");
        using var changedPreview = JsonDocument.Parse("{\"textUses\":[{\"index\":2}]}");
        var before = new Dictionary<string, string> { [SenseId.Value] = "snapshot row" };
        var after = new Dictionary<string, string>();
        var withoutPreview = new ExpectedEffect(SenseId, SnapshotFields.WfiWordformAnalyses, before, after);
        var first = withoutPreview with { Preview = firstPreview.RootElement.Clone() };
        var changed = withoutPreview with { Preview = changedPreview.RootElement.Clone() };

        Assert.Equal(ExpectedEffectSetDigest.Compute([withoutPreview]), ExpectedEffectSetDigest.Compute([first]));
        Assert.NotEqual(FootprintDigest.Compute([first]), FootprintDigest.Compute([changed]));
        using var semantic = JsonDocument.Parse(ExpectedEffectSetJsonWriter.WriteJson([first]));
        using var published = JsonDocument.Parse(ExpectedEffectSetJsonWriter.WritePublishedJson([first]));
        Assert.False(semantic.RootElement[0].TryGetProperty("preview", out _));
        Assert.Equal(1, published.RootElement[0].GetProperty("preview").GetProperty("textUses")[0]
            .GetProperty("index").GetInt32());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentCollectionMemberEffectsIgnoreInputOrderWhenTheirIdentityAndFieldTie(bool removal)
    {
        var firstMember = CanonicalId.FromGuid(Guid.ParseExact("10111213-1415-1617-1819-1a1b1c1d1e1f", "D"));
        var secondMember = CanonicalId.FromGuid(Guid.ParseExact("20212223-2425-2627-2829-2a2b2c2d2e2f", "D"));
        var empty = new Dictionary<string, string>();
        var firstReference = new Dictionary<string, string> { ["ref"] = firstMember.Value };
        var secondReference = new Dictionary<string, string> { ["ref"] = secondMember.Value };
        var first = new ExpectedEffect(SenseId, "grammar/fsClosedFeature/values",
            removal ? firstReference : empty, removal ? empty : firstReference);
        var second = new ExpectedEffect(SenseId, "grammar/fsClosedFeature/values",
            removal ? secondReference : empty, removal ? empty : secondReference);
        ExpectedEffect[] forward = [first, second];
        ExpectedEffect[] reversed = [second, first];

        Assert.Equal(ExpectedEffectSetJsonWriter.WriteJson(forward), ExpectedEffectSetJsonWriter.WriteJson(reversed));
        Assert.Equal(ExpectedEffectSetDigest.Compute(forward), ExpectedEffectSetDigest.Compute(reversed));
        using var rendered = JsonDocument.Parse(ExpectedEffectSetJsonWriter.WriteJson(forward));
        Assert.Equal(2, rendered.RootElement.GetArrayLength());
    }

    [Fact]
    public void TiedEffectMapsUseCanonicalMemberOrderAndUtf8ValueOrder()
    {
        var bmp = "\uE000";
        var supplementary = "\U00010000";
        var bmpFirst = new ExpectedEffect(SenseId, SnapshotFields.LexSenseGloss,
            new Dictionary<string, string>(),
            new Dictionary<string, string> { [bmp] = "shared", [supplementary] = bmp });
        var supplementaryFirst = bmpFirst with
        {
            After = new Dictionary<string, string> { [supplementary] = supplementary, [bmp] = "shared" },
        };
        var bmpReordered = bmpFirst with
        {
            After = new Dictionary<string, string> { [supplementary] = bmp, [bmp] = "shared" },
        };
        var forward = ExpectedEffectSetJsonWriter.WriteJson([bmpFirst, supplementaryFirst]);
        var reversed = ExpectedEffectSetJsonWriter.WriteJson([supplementaryFirst, bmpReordered]);

        Assert.Equal(forward, reversed);
        Assert.Equal(ExpectedEffectSetDigest.Compute([bmpFirst, supplementaryFirst]),
            ExpectedEffectSetDigest.Compute([supplementaryFirst, bmpReordered]));
        using var rendered = JsonDocument.Parse(forward);
        var firstAfter = rendered.RootElement[0].GetProperty("after");
        Assert.Equal(bmp, firstAfter.GetProperty(supplementary).GetString());
        Assert.Equal(new[] { supplementary, bmp }, firstAfter.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public void TiedPreviewEvidenceKeepsDestinationOutOfFootprintOrdering()
    {
        using var firstPreview = JsonDocument.Parse("{\"witness\":\"first\"}");
        using var secondPreview = JsonDocument.Parse("{\"witness\":\"second\"}");
        using var changedPreview = JsonDocument.Parse("{\"witness\":\"changed\"}");
        var before = new Dictionary<string, string> { ["en"] = "same baseline" };
        var first = new ExpectedEffect(SenseId, SnapshotFields.LexSenseGloss, before,
            new Dictionary<string, string> { ["en"] = "z" }, firstPreview.RootElement.Clone());
        var second = first with
        {
            After = new Dictionary<string, string> { ["en"] = "a" },
            Preview = secondPreview.RootElement.Clone(),
        };
        var destinationChanged = first with { After = new Dictionary<string, string> { ["en"] = "0" } };
        var previewChanged = first with { Preview = changedPreview.RootElement.Clone() };

        Assert.Equal(ExpectedEffectSetJsonWriter.WritePublishedJson([first, second]),
            ExpectedEffectSetJsonWriter.WritePublishedJson([second, first]));
        Assert.Equal(FootprintDigest.Compute([first, second]), FootprintDigest.Compute([second, first]));
        Assert.Equal(FootprintDigest.Compute([first, second]), FootprintDigest.Compute([destinationChanged, second]));
        Assert.Equal(ExpectedEffectSetDigest.Compute([first, second]),
            ExpectedEffectSetDigest.Compute([previewChanged, second]));
        Assert.NotEqual(FootprintDigest.Compute([first, second]), FootprintDigest.Compute([previewChanged, second]));
        var sameDestination = second with { After = first.After };
        Assert.Equal(ExpectedEffectSetJsonWriter.WritePublishedJson([first, sameDestination]),
            ExpectedEffectSetJsonWriter.WritePublishedJson([sameDestination, first]));
    }

    [Fact]
    public void ExpectedEffectSetDigest_IsOrderIndependentOverTheEffectSet()
    {
        // The effect set is semantically a set: authored order must not affect the digest.
        var otherId = CanonicalId.FromGuid(Guid.ParseExact("10111213-1415-1617-1819-1a1b1c1d1e1f", "D"));

        var effectOne = new ExpectedEffect(
            SenseId, SnapshotFields.LexSenseGloss,
            new Dictionary<string, string> { ["en"] = "run quickly on foot" },
            new Dictionary<string, string> { ["en"] = "move quickly on foot" });
        var effectTwo = new ExpectedEffect(
            otherId, SnapshotFields.LexSenseGloss,
            new Dictionary<string, string> { ["en"] = "a container" },
            new Dictionary<string, string> { ["en"] = "a vessel" });

        var digestForward = ExpectedEffectSetDigest.Compute(new[] { effectOne, effectTwo });
        var digestReversed = ExpectedEffectSetDigest.Compute(new[] { effectTwo, effectOne });

        Assert.Equal(digestForward, digestReversed);
    }
}
