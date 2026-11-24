using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Query.Corpus;
using SIL.Motif.Contract.Query.Phon;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// <c>phon</c> and <c>env</c> over a corpus, driven by <c>match</c> replies recorded from a real PanGloss
/// (<see cref="RecordedMatch"/>). A replay matcher stands in for the resident serve: it answers each word from the
/// recording and checks the pattern text Motif sent.
/// </summary>
public sealed class PhonQueryTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("phon-query").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    private sealed class Replay(string recorded, string expectedPattern, params string[] sentWords) : IPhonMatcher
    {
        private readonly PhonBatchResult _recorded = ResidentPhonMatcher.Read(RecordedMatch.Fill(recorded, sentWords));
        private Dictionary<string, PhonWordResult> _words => _recorded.Words.ToDictionary(word => word.Word);

        public string? ParserVersion => "0.31.0";

        public List<string> SegmentTests { get; } = [];

        public PhonInventory? Inventory(CancellationToken cancellationToken) => ResidentPhonMatcher.ReadInventory(RecordedMatch.Inventory);

        public List<IReadOnlyList<string>> Batches { get; } = [];

        public PhonBatchResult Match(string patternText, IReadOnlyList<string> words, string segmentTest, CancellationToken cancellationToken)
        {
            Assert.Equal(expectedPattern, patternText);
            SegmentTests.Add(segmentTest);
            Batches.Add(words);
            return new PhonBatchResult([.. words.Select(word => _words[word])], _recorded.Complete, _recorded.StopReason);
        }
    }

    private CorpusWordList Corpus(params string[] words)
    {
        var path = Path.Combine(_folder, "words.tsv");
        File.WriteAllText(path, string.Concat(words.Select((word, index) => word + "\t" + (10 - index) + "\n")));
        return CorpusWordList.Load(path);
    }

    private static QueryExpr Where(string text)
    {
        Assert.True(QueryExprText.TryParse(text, out var expression, out var refusal), refusal?.Message);
        return expression!;
    }

    [Fact]
    public void ClassesAreSentInBracketsAndEachWordGetsItsSpan()
    {
        var matcher = new Replay(RecordedMatch.CvcvClasses, "[Stop] [V] [Stop] [V]", "pata", "kaki", "pat", "atan");
        var session = new PhonSession(matcher);
        var rows = CorpusFormsQuery.Run(Corpus("pata", "kaki", "pat", "atan"), Where("phon(Stop V Stop V)"), session);
        Assert.Equal(["pata", "kaki"], rows.Select(row => row.Form));
        var span = Assert.Single(Assert.Single(rows[0].Matches!).Spans);
        Assert.Equal((0, 4, "pata"), (span.Start, span.End, span.Text));
        Assert.Single(matcher.Batches);
        Assert.Equal(4, matcher.Batches[0].Count);
        Assert.Equal(1, session.Report.Requests);
        Assert.Equal(0, session.Report.Unsegmentable);
    }

    [Fact]
    public void UnsegmentableWordsAreCountedAndSampledAndNeverMatchedEvenByNot()
    {
        var corpus = Corpus("pata", "xyz");
        var session = new PhonSession(new Replay(RecordedMatch.VowelWithUnsegmentable, "[V]", "pata", "xyz"));
        Assert.Equal(["pata"], CorpusFormsQuery.Run(corpus, Where("phon(V)"), session).Select(row => row.Form));
        Assert.Equal(2, session.Report.Candidates);
        Assert.Equal(1, session.Report.Unsegmentable);
        var sample = Assert.Single(session.Report.UnsegmentableSample);
        Assert.Equal(("xyz", "no_character_definition", 0), (sample.Word, sample.Reason, sample.CharIndex));

        var negated = new PhonSession(new Replay(RecordedMatch.VowelWithUnsegmentable, "[V]", "pata", "xyz"));
        Assert.Empty(CorpusFormsQuery.Run(corpus, Where("not phon(V)"), negated));
        Assert.Equal(1, negated.Report.Unsegmentable);
    }

    [Fact]
    public void AnEnvSendsLeftTargetRightAndReportsTheTargetSpan()
    {
        var session = new PhonSession(new Replay(RecordedMatch.EnvEdge, "[V] / # _ [Stop]", "apa", "pa"));
        var rows = CorpusFormsQuery.Run(Corpus("apa", "pa"), Where("env(V / # _ Stop)"), session);
        Assert.Equal("apa", Assert.Single(rows).Form);
        var span = Assert.Single(Assert.Single(rows[0].Matches!).Spans);
        Assert.Equal((0, 1, "a"), (span.TargetStart, span.TargetEnd, span.Text));
        Assert.Equal("env", rows[0].Matches![0].Kind);
    }

    [Fact]
    public void AnEnvWhoseTargetIsBetweenVowelsReportsTheTargetOnly()
    {
        var session = new PhonSession(new Replay(RecordedMatch.EnvNative, "[Stop] / [V] _ [V]", "pata", "kaki", "pat", "apa"));
        var rows = CorpusFormsQuery.Run(Corpus("pata", "kaki", "pat", "apa"), Where("env(Stop / V _ V)"), session);
        Assert.Equal(["apa", "kaki", "pata"], rows.Select(row => row.Form).Order());
        var span = Assert.Single(rows.Single(row => row.Form == "kaki").Matches![0].Spans);
        Assert.Equal((2, 3, "k"), (span.TargetStart, span.TargetEnd, span.Text));
    }

    [Fact]
    public void AnEmptySideOfAnEnvIsLeftOutOfTheText()
    {
        var session = new PhonSession(new Replay(RecordedMatch.EnvEmptyLeft, "[V] / _ [Stop]", "apa", "pa"));
        Assert.Equal(["apa"], CorpusFormsQuery.Run(Corpus("apa", "pa"), Where("env(V / _ Stop)"), session).Select(row => row.Form));
    }

    [Fact]
    public void AFeatureBundleAndAnAlphaVariableAreSentAsWritten()
    {
        var bundle = new PhonSession(new Replay(RecordedMatch.FeatureBundle, "[+voice] [V]", "ba", "pa", "aba"));
        Assert.Equal(["aba", "ba"], CorpusFormsQuery.Run(Corpus("ba", "pa", "aba"), Where("phon([+voice] V)"), bundle).Select(row => row.Form).Order());
        var alpha = new PhonSession(new Replay(RecordedMatch.Alpha, "[αvoice] [αvoice]", "bada", "pata", "bata"));
        Assert.Equal(["bada", "bata"], CorpusFormsQuery.Run(Corpus("bada", "pata", "bata"), Where("phon([αvoice] [αvoice])"), alpha).Select(row => row.Form).Order());
        Assert.False(alpha.Report.MayUnderReport);
    }

    private static PhonSession Identity(IPhonMatcher matcher) => new(matcher, segmentTest: PhonSegmentTest.Identity);

    [Fact]
    public void UnderIdentityAClassNotListedAsAFeatureClassOrALiteralMakesTheReportSayIdentity()
    {
        var literal = Identity(new Replay(RecordedMatch.Literals, "/pa/ ( [V])*", "paaa"));
        CorpusFormsQuery.Run(Corpus("paaa"), Where("phon(/pa/ (V)*)"), literal);
        Assert.Equal(("identity", true, "identity"), (literal.Report.MatchMode, literal.Report.MayUnderReport, literal.Report.SegmentTest));
        Assert.Null(literal.Report.Warnings);
        var segmentClass = Identity(new Replay(RecordedMatch.CvcvClasses, "[Stop] [V] [Stop] [V]", "pata", "kaki", "pat", "atan"));
        CorpusFormsQuery.Run(Corpus("pata", "kaki", "pat", "atan"), Where("phon(Stop V Stop V)"), segmentClass);
        Assert.True(segmentClass.Report.MayUnderReport);
        var featureClass = Identity(new Replay(RecordedMatch.NaturalClass, "[Voiced] [V]", "ba", "pa"));
        CorpusFormsQuery.Run(Corpus("ba", "pa"), Where("phon(Voiced V)"), featureClass);
        Assert.True(featureClass.Report.MayUnderReport, "V is a segment list, so the pattern is identity-tested");
        var onlyFeature = Identity(new Replay(RecordedMatch.FeatureBundle, "[+voice] [Voiced]", "ba", "pa", "aba"));
        CorpusFormsQuery.Run(Corpus("ba", "pa", "aba"), Where("phon([+voice] Voiced)"), onlyFeature);
        Assert.Null(onlyFeature.Report.MatchMode);
    }

    [Fact]
    public void UnderTheEngineTestNoPatternCarriesTheLabelAndTheReportNamesTheMode()
    {
        var replay = new Replay(RecordedMatch.Literals, "/pa/ ( [V])*", "paaa");
        var literal = new PhonSession(replay);
        CorpusFormsQuery.Run(Corpus("paaa"), Where("phon(/pa/ (V)*)"), literal);
        Assert.Equal(("engine", null, false, null), (literal.Report.SegmentTest, literal.Report.MatchMode, literal.Report.MayUnderReport, literal.Report.Warnings));
        Assert.Equal(["engine"], replay.SegmentTests);
        var segmentClass = new PhonSession(new Replay(RecordedMatch.CvcvClasses, "[Stop] [V] [Stop] [V]", "pata", "kaki", "pat", "atan"));
        CorpusFormsQuery.Run(Corpus("pata", "kaki", "pat", "atan"), Where("phon(Stop V Stop V)"), segmentClass);
        Assert.False(segmentClass.Report.MayUnderReport);
        var envWithLiteralSide = new PhonSession(new Replay(RecordedMatch.EnvSideLiteral, "[Stop] / /i/ _", "kiki", "kuŋ", "kaki"));
        var rows = CorpusFormsQuery.Run(Corpus("kiki", "kuŋ", "kaki"), Where("env(Stop / /i/ _)"), envWithLiteralSide);
        Assert.Equal(["kiki"], rows.Select(row => row.Form));
        Assert.Equal(("engine", null), (envWithLiteralSide.Report.SegmentTest, envWithLiteralSide.Report.MatchMode));
    }

    [Fact]
    public void NotOverAnIdentityTestedPatternWarnsAbsenceIsNotProvenAndUnderTheEngineTestItDoesNot()
    {
        var negated = Identity(new Replay(RecordedMatch.VowelWithUnsegmentable, "[V]", "pata", "xyz"));
        CorpusFormsQuery.Run(Corpus("pata", "xyz"), Where("not phon(V)"), negated);
        var warning = Assert.Single(negated.Report.Warnings!);
        Assert.Contains("absence is not proven", warning);
        var engine = new PhonSession(new Replay(RecordedMatch.VowelWithUnsegmentable, "[V]", "pata", "xyz"));
        CorpusFormsQuery.Run(Corpus("pata", "xyz"), Where("not phon(V)"), engine);
        Assert.Null(engine.Report.Warnings);
        Assert.Null(engine.Report.MatchMode);
    }

    [Fact]
    public void TheEngineAndIdentityRepliesDifferWhereTheRecordedPanGlossSaysTheyDo()
    {
        var engine = ResidentPhonMatcher.Read(RecordedMatch.Fill(RecordedMatch.ClassEngine, "kuŋ", "pata"));
        var identity = ResidentPhonMatcher.Read(RecordedMatch.Fill(RecordedMatch.ClassIdentity, "kuŋ", "pata"));
        Assert.Equal(2, engine.Words[0].Spans.Count);
        Assert.Single(identity.Words[0].Spans);
        Assert.Equal(engine.Words[1].Spans, identity.Words[1].Spans);
        var literalEngine = ResidentPhonMatcher.Read(RecordedMatch.Fill(RecordedMatch.LiteralEngine, "kiki", "kuŋ", "kaki"));
        var literalIdentity = ResidentPhonMatcher.Read(RecordedMatch.Fill(RecordedMatch.LiteralIdentity, "kiki", "kuŋ", "kaki"));
        Assert.Equal([2, 2, 2], literalEngine.Words.Select(word => word.Spans.Count));
        Assert.Equal([2, 0, 1], literalIdentity.Words.Select(word => word.Spans.Count));
    }

    [Fact]
    public void TheRequestCarriesTheSegmentTestAndTheCapabilityMustListIt()
    {
        var line = new ServeMatchRequest("[V]", ["a"]) { SegmentTest = "identity" }.ToLine("1");
        Assert.Contains("\"segmentTest\":\"identity\"", line);
        Assert.DoesNotContain("segmentTest", new ServeMatchRequest("[V]", ["a"]).ToLine("1"));
        var node = JsonNode.Parse(SIL.Motif.Tests.PanGloss.WhyNotRecordedTests.Recorded("describe"))!;
        var interfaces = node["interfaces"] as JsonObject ?? (JsonObject)(node["interfaces"] = new JsonObject());
        var ops = interfaces["serveOps"] as JsonObject ?? (JsonObject)(interfaces["serveOps"] = new JsonObject());
        ops["match"] = JsonNode.Parse(RecordedMatch.DescribeBefore030);
        var older = PanGlossSurface.ReadDescription(node.ToJsonString(), out _)!;
        Assert.Contains("segmentTests", older.ServeMatchRefusal(null, "engine"));
        ops["match"] = JsonNode.Parse(RecordedMatch.Describe);
        var current = PanGlossSurface.ReadDescription(node.ToJsonString(), out _)!;
        Assert.Null(current.ServeMatchRefusal(null, "engine"));
        Assert.Null(current.ServeMatchRefusal(null, "identity"));
        Assert.NotNull(current.ServeMatchRefusal(null, "bogus"));
        Assert.Equal("bad_request", JsonNode.Parse(RecordedMatch.BadSegmentTest)!["code"]!.GetValue<string>());
    }

    [Fact]
    public void TheInventoryReplyIsReadWithItsKindsAndTokens()
    {
        var inventory = ResidentPhonMatcher.ReadInventory(RecordedMatch.Inventory);
        Assert.Equal("segment", inventory.Classes.Single(c => c.Token == "V").Kind);
        Assert.Equal("feature", inventory.Classes.Single(c => c.Token == "Voiced").Kind);
        Assert.Null(inventory.Classes.Single(c => c.Id == "no]way").Token);
        Assert.Equal(["+", "-"], inventory.Features.Single(f => f.Name == "voice").Values);
        Assert.All(inventory.Classes, c => Assert.Null(c.FullName));
        var fieldWorks = ResidentPhonMatcher.ReadInventory(RecordedMatch.InventoryWithFullName);
        Assert.Equal("Vowels", fieldWorks.Classes.Single(c => c.Token == "V").FullName);
        Assert.Null(fieldWorks.Classes.Single(c => c.Token == "Any").FullName);
    }

    [Theory]
    [InlineData("env", "V / # _ Stop", new[] { "environment" })]
    [InlineData("phon", "[+voice] V", new[] { "featureBundle" })]
    [InlineData("phon", "[αvoice] [-αvoice]", new[] { "alphaVariable" })]
    [InlineData("phon", "V", new string[0])]
    public void TheConstructsAPatternNeedsAreNamedForTheCapabilityCheck(string kind, string pattern, string[] needs)
    {
        var plan = kind == "env" ? PhonPattern.CompileEnv(pattern) : PhonPattern.CompilePhon(pattern);
        Assert.Equal(needs, PhonPattern.ConstructsOf(plan.Text));
    }

    [Fact]
    public void CheaperPredicatesRunFirstSoPanGlossSeesOnlyTheSurvivors()
    {
        var matcher = new Replay(RecordedMatch.CvcvClasses, "[Stop] [V] [Stop] [V]", "pata", "kaki", "pat", "atan");
        var rows = CorpusFormsQuery.Run(Corpus("pata", "kaki", "pat", "atan"), Where("tokens > 8 and phon(Stop V Stop V)"), new PhonSession(matcher));
        Assert.Equal(["pata", "kaki"], rows.Select(row => row.Form));
        Assert.Equal(new[] { "kaki", "pata" }, matcher.Batches.Single().Order());
    }

    [Fact]
    public void ABatchLargerThanTheLimitIsSplitNotSentAWordAtATime()
    {
        var matcher = new Replay(RecordedMatch.CvcvClasses, "[Stop] [V] [Stop] [V]", "pata", "kaki", "pat", "atan");
        var session = new PhonSession(matcher, batchSize: 3);
        CorpusFormsQuery.Run(Corpus("pata", "kaki", "pat", "atan"), Where("phon(Stop V Stop V)"), session);
        Assert.Equal(new[] { 3, 1 }, matcher.Batches.Select(batch => batch.Count));
        Assert.Equal(2, session.Report.Requests);
    }

    [Theory]
    [InlineData("phon(V)")]
    [InlineData("env(V / # _)")]
    public void WithoutAGrammarACorpusRefusesAndNamesTheFlag(string where)
    {
        var outcome = CorpusQueryCommand.Forms(new CorpusFormsRequest(Path.Combine(_folder, "any.tsv"), where));
        Assert.False(outcome.Succeeded);
        Assert.Equal(RefusalCodes.QueryPhonNeedsGrammar, outcome.Refusal!.Code);
        Assert.Contains("--grammar", outcome.Refusal.Message);
    }

    [Fact]
    public void ACorpusQueryWithoutPatternsNeedsNoGrammarOrParser()
    {
        var path = Path.Combine(_folder, "plain.tsv");
        File.WriteAllText(path, "kaon\t5\nbaha\t2\n");
        var outcome = CorpusQueryCommand.Forms(new CorpusFormsRequest(path, "tokens > 3"));
        Assert.True(outcome.Succeeded);
        Assert.Equal(["kaon"], outcome.Value!.Rows.Select(row => row.Form));
        Assert.Null(outcome.Value.Phon);
    }

    [Theory]
    [InlineData("PlusNeedsMarker", RecordedMatch.PlusNeedsMarker, "boundary_needs_marked_input")]
    [InlineData("AlphaAcrossFeatures", RecordedMatch.AlphaAcrossFeatures, "unsupported_construct")]
    [InlineData("UnknownFeature", RecordedMatch.UnknownFeature, "unknown_feature")]
    [InlineData("UnknownClass", RecordedMatch.UnknownClass, "unknown_natural_class")]
    [InlineData("PatternParse", RecordedMatch.PatternParse, "pattern_parse")]
    public void PanGlossesTypedRefusalIsKeptWholeAsFacts(string name, string recorded, string code)
    {
        using var document = JsonDocument.Parse(recorded);
        var message = document.RootElement.GetProperty("message").GetString()!;
        var error = ResidentPhonMatcher.Refused(new WarmMatchResult.Rejected(code, message, recorded), "pattern text");
        Assert.Equal(RefusalCodes.QueryPhonPatternRefused, error.Refusal.Code);
        Assert.Equal(code, error.Refusal.Facts!["panglossCode"]);
        Assert.Contains(message, error.Refusal.Message);
        Assert.True(error.Refusal.Facts.ContainsKey("offset"), name);
        if (code == "unsupported_construct") Assert.Contains("does not support", error.Refusal.Message);
    }

    [Fact]
    public void TheMatchCapabilityComesFromTheDescriptionAndItsAbsenceNamesTheRelease()
    {
        var node = JsonNode.Parse(SIL.Motif.Tests.PanGloss.WhyNotRecordedTests.Recorded("describe"))!;
        var interfaces = node["interfaces"] as JsonObject ?? (JsonObject)(node["interfaces"] = new JsonObject());
        var ops = interfaces["serveOps"] as JsonObject ?? (JsonObject)(interfaces["serveOps"] = new JsonObject());
        ops.Remove("match");
        var without = PanGlossSurface.ReadDescription(node.ToJsonString(), out _)!;
        Assert.NotNull(without.ServeMatchRefusal());
        ops["match"] = JsonNode.Parse(RecordedMatch.DescribeBefore030);
        var older = PanGlossSurface.ReadDescription(node.ToJsonString(), out _)!;
        Assert.Null(older.ServeMatchRefusal());
        Assert.Null(older.ServeMatchRefusal(["naturalClass"]));
        Assert.Contains("environment", older.ServeMatchRefusal(["environment"]));
        Assert.Contains("featureBundle", older.ServeMatchRefusal(["featureBundle"]));
        Assert.Contains("alphaVariable", older.ServeMatchRefusal(["alphaVariable"]));
        ops["match"] = JsonNode.Parse(RecordedMatch.Describe);
        ops["inventory"] = JsonNode.Parse(RecordedMatch.DescribeInventory);
        var with = PanGlossSurface.ReadDescription(node.ToJsonString(), out _)!;
        Assert.Null(with.ServeMatchRefusal(["environment", "featureBundle", "alphaVariable"]));
        Assert.Null(with.ServeInventoryRefusal());
        ops.Remove("inventory");
        Assert.NotNull(PanGlossSurface.ReadDescription(node.ToJsonString(), out _)!.ServeInventoryRefusal());
        Assert.Equal(1, with.ServeMatch!.Version);
        var unavailable = QueryRefusalException.PhonUnavailable(PanGlossInterfaceVersions.ServeMatchNeeded, "no match op");
        Assert.Equal(RefusalCodes.QueryPhonUnavailable, unavailable.Refusal.Code);
        Assert.Contains("v0.29.0", unavailable.Refusal.Message);
    }

    [Fact]
    public void AnIncompleteReplyIsCountedNotDropped()
    {
        const string reply = """{"complete":false,"id":"x","op":"match","stopReason":"work_limit","type":"result","words":[{"matches":[],"status":"incomplete","stop":"work_limit","word":""},{"matches":[],"status":"ok","word":""}]}""";
        var session = new PhonSession(new Replay(reply, "[V]", "pata", "kaki"));
        CorpusFormsQuery.Run(Corpus("pata", "kaki"), Where("phon(V)"), session);
        Assert.False(session.Report.Complete);
        Assert.Equal("work_limit", session.Report.StopReason);
        Assert.Equal("pata", Assert.Single(session.Report.IncompleteSample).Word);
    }

    private sealed class Counting : IPhonMatcher
    {
        public string? ParserVersion => null;

        public List<IReadOnlyList<string>> Batches { get; } = [];

        public PhonBatchResult Match(string patternText, IReadOnlyList<string> words, string segmentTest, CancellationToken cancellationToken)
        {
            Batches.Add(words);
            return new PhonBatchResult([.. words.Select(word => new PhonWordResult(word, "ok", [], null, null, null))], true, null);
        }
    }

    [Fact]
    public void AnOversizedBatchIsSplitByEncodedSizeSoEveryRequestLineStaysUnderHalfAMebibyte()
    {
        var words = Enumerable.Range(0, 40000).Select(index => "kaonanaban" + index.ToString("D6") + "\u014b\u0301\u014b").ToArray();
        var path = Path.Combine(_folder, "big.tsv");
        File.WriteAllLines(path, words.Select(word => word + "\t1"));
        var matcher = new Counting();
        var session = new PhonSession(matcher);
        CorpusFormsQuery.Run(CorpusWordList.Load(path), Where("phon(V)"), session);
        Assert.True(matcher.Batches.Count > 1);
        Assert.Equal(words.Length, matcher.Batches.Sum(batch => batch.Count));
        foreach (var batch in matcher.Batches)
        {
            var line = new ServeMatchRequest("[V]", batch, WordTimeoutMs: ResidentPhonMatcher.WordTimeoutMs).ToLine("1");
            Assert.True(System.Text.Encoding.UTF8.GetByteCount(line) <= 512 * 1024, "a request line is over 512 KiB");
        }
        Assert.Equal(matcher.Batches.Count, session.Report.Requests);
    }

    [Fact]
    public void TheRequestAlwaysCarriesAWordDeadlineAndTheSessionLimitScalesWithTheBatch()
    {
        var line = new ServeMatchRequest("[V]", ["a"], WordTimeoutMs: ResidentPhonMatcher.WordTimeoutMs).ToLine("1");
        Assert.Contains("\"wordTimeoutMs\":" + ResidentPhonMatcher.WordTimeoutMs, line);
        var floor = TimeSpan.FromMinutes(5);
        var small = new ServeMatchRequest("[V]", ["a"], WordTimeoutMs: 5000).Timeout(floor);
        var large = new ServeMatchRequest("[V]", [.. Enumerable.Repeat("a", 500)], WordTimeoutMs: 5000).Timeout(floor);
        var huge = new ServeMatchRequest("[V]", [.. Enumerable.Repeat("a", 50000)], WordTimeoutMs: 5000).Timeout(floor);
        Assert.Equal(floor, small);
        Assert.True(large > TimeSpan.FromSeconds(500 * 5), "the limit must outlast every word using its deadline");
        Assert.Equal(TimeSpan.FromHours(1), huge);
    }

    [Fact]
    public void AWordStoppedAtTheMatchCapKeepsItsSpansFlaggedPartialAndIsCounted()
    {
        const string reply = """{"complete":false,"id":"x","op":"match","stopReason":"match_cap","type":"result","words":[{"matches":[{"left":[0,0],"offsets":[0,1],"right":[1,2],"segments":[0,1]}],"status":"incomplete","stop":"match_cap","word":""},{"matches":[],"status":"ok","word":""}]}""";
        var corpus = Corpus("pata", "kaki");
        var positive = new PhonSession(new Replay(reply, "[V]", "pata", "kaki"));
        var rows = CorpusFormsQuery.Run(corpus, Where("phon(V)"), positive);
        var row = Assert.Single(rows);
        Assert.Equal("pata", row.Form);
        Assert.True(Assert.Single(row.Matches!).Partial);
        Assert.Equal(1, positive.Report.Incomplete);
        Assert.Equal("pata", Assert.Single(positive.Report.IncompleteSample).Word);
        var negated = new PhonSession(new Replay(reply, "[V]", "pata", "kaki"));
        Assert.Equal(["kaki"], CorpusFormsQuery.Run(corpus, Where("not phon(V)"), negated).Select(r => r.Form));
        Assert.Equal(1, negated.Report.Incomplete);
    }
}
