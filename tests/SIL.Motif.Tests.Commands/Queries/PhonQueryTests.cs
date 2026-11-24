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

        public string? ParserVersion => "0.28.1";

        public List<IReadOnlyList<string>> Batches { get; } = [];

        public PhonBatchResult Match(string patternText, IReadOnlyList<string> words, CancellationToken cancellationToken)
        {
            Assert.Equal(expectedPattern, patternText);
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
        var session = new PhonSession(new Replay(RecordedMatch.WordEdge, "# [V]", "apa", "pa"));
        var rows = CorpusFormsQuery.Run(Corpus("apa", "pa"), Where("env(V / # _)"), session);
        Assert.Equal("apa", Assert.Single(rows).Form);
        var span = Assert.Single(Assert.Single(rows[0].Matches!).Spans);
        Assert.Equal((0, 1), (span.TargetStart, span.TargetEnd));
        Assert.Equal("env", rows[0].Matches![0].Kind);
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
    [InlineData("FeatureBundleGated", RecordedMatch.FeatureBundleGated, "unsupported_construct")]
    [InlineData("AlphaGated", RecordedMatch.AlphaGated, "unsupported_construct")]
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
        ops["match"] = JsonNode.Parse(RecordedMatch.Describe);
        var with = PanGlossSurface.ReadDescription(node.ToJsonString(), out _)!;
        Assert.Null(with.ServeMatchRefusal());
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

        public PhonBatchResult Match(string patternText, IReadOnlyList<string> words, CancellationToken cancellationToken)
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
