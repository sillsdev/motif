namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Replies recorded from a real <c>pangloss serve</c> <c>match</c> request, byte for byte, taken from sillsdev/PanGloss
/// main at 8be69a8bf (release v0.28.1 plus #436); the environment, bundle, alpha and inventory replies and <see cref="Describe"/> are taken from release v0.30.0 (e898e5ea5) on PanGloss's own public synthetic grammar
/// <c>rust/crates/pg-cli/tests/data/match-fixture.xml</c>: stops, vowels, a <c>voice</c> feature, the natural classes
/// <c>V</c>, <c>Stop</c> and <c>Voiced</c>, and one boundary marker. The conformance fixture has no natural classes, so
/// this grammar stands in; it holds no owner data. Each constant names the pattern and words that produced it.
/// Only the <c>word</c> field of each result word is blanked, because the grammar-data gate (ADR 0002) cannot tell a
/// test string from a lexicon record; <see cref="Fill"/> puts the requested words back, in order, before a reply is read.
/// </summary>
internal static class RecordedMatch
{
    /// <summary>Writes the words that were sent into the blanked <c>word</c> fields of a recorded reply, in order.</summary>
    internal static string Fill(string reply, params string[] words)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(reply)!;
        var array = node["words"]?.AsArray();
        if (array is null) return reply;
        for (var i = 0; i < array.Count; i++) array[i]!["word"] = words[i];
        return node.ToJsonString(new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    /// <summary>The <c>interfaces.serveOps.match</c> object of that older build's <c>--describe</c>: environments, bundles and alpha variables are gated.</summary>
    internal const string DescribeBefore030 = """
        {"constructs":{"gated":["featureBundle","alphaVariable","environment"],"supported":["naturalClass","literal","optional","repeat","wordEdge","morphemeBoundary"]},"errors":["bad_request","pattern_parse","unknown_natural_class","unknown_feature","unsupported_construct","boundary_needs_marked_input","unknown_boundary_marker","too_many_words","words_file_unreadable","pattern_compile_failed","internal"],"limits":{"defaultMaxMatchesPerWord":1000,"defaultMaxWords":100000,"defaultMaxWork":50000000,"defaultWordTimeoutMs":10000,"maxMatchesPerWord":100000,"maxMatchesTotal":1000000,"maxPatternBytes":4096,"maxWordTimeoutMs":600000,"maxWords":1000000},"offsets":"utf8ByteOffsetsInNfdText","request":["pattern","words","wordsFile","boundaryMarker","maxWords","maxMatchesPerWord","maxWork","wordTimeoutMs"],"statuses":["ok","unsegmentable","incomplete"],"stopReasons":["match_cap","total_match_cap","work_limit","deadline"],"version":1,"wordReasons":["no_character_definition","boundary_in_unmarked_word","unexpected_boundary"],"zeroWidth":"reported: a pattern that can match the empty string reports an empty match at every position its anchors allow"}
        """;

    /// <summary>Stop V Stop V over pata, kaki, pat, atan: two matches and two misses.</summary>
    internal const string CvcvClasses = """
        {"complete":true,"id":"cvcv2","op":"match","stopReason":null,"type":"result","words":[{"matches":[{"left":[0,0],"offsets":[0,4],"right":[4,4],"segments":[0,4]}],"status":"ok","word":""},{"matches":[{"left":[0,0],"offsets":[0,4],"right":[4,4],"segments":[0,4]}],"status":"ok","word":""},{"matches":[],"status":"ok","word":""},{"matches":[],"status":"ok","word":""}]}
        """;

    /// <summary>a vowel over pata and xyz: two spans, and xyz unsegmentable (no_character_definition).</summary>
    internal const string VowelWithUnsegmentable = """
        {"complete":true,"id":"unseg","op":"match","stopReason":null,"type":"result","words":[{"matches":[{"left":[0,1],"offsets":[1,2],"right":[2,4],"segments":[1,2]},{"left":[0,3],"offsets":[3,4],"right":[4,4],"segments":[3,4]}],"status":"ok","word":""},{"at":{"charIndex":0,"offset":0},"matches":[],"reason":"no_character_definition","status":"unsegmentable","word":""}]}
        """;

    /// <summary># V over apa and pa: a match only at the start.</summary>
    internal const string WordEdge = """
        {"complete":true,"id":"edge","op":"match","stopReason":null,"type":"result","words":[{"matches":[{"left":[0,0],"offsets":[0,1],"right":[1,3],"segments":[0,1]}],"status":"ok","word":""},{"matches":[],"status":"ok","word":""}]}
        """;

    /// <summary>/p/ + V without boundaryMarker: boundary_needs_marked_input.</summary>
    internal const string PlusNeedsMarker = """
        {"code":"boundary_needs_marked_input","id":"plus","message":"a pattern with '+' needs words with marked boundaries; send \"boundaryMarker\"","offset":4,"type":"error"}
        """;

    /// <summary>Voiced V over ba and pa.</summary>
    internal const string NaturalClass = """
        {"complete":true,"id":"voiced","op":"match","stopReason":null,"type":"result","words":[{"matches":[{"left":[0,0],"offsets":[0,2],"right":[2,2],"segments":[0,2]}],"status":"ok","word":""},{"matches":[],"status":"ok","word":""}]}
        """;

    /// <summary>an unknown natural class: unknown_natural_class.</summary>
    internal const string UnknownClass = """
        {"code":"unknown_natural_class","id":"badclass","message":"no natural class named \"Nope\"","name":"Nope","near":[],"offset":1,"type":"error"}
        """;

    /// <summary>/pa/ (V)* over paaa: three matches.</summary>
    internal const string Literals = """
        {"complete":true,"id":"lit","op":"match","stopReason":null,"type":"result","words":[{"matches":[{"left":[0,0],"offsets":[0,2],"right":[2,4],"segments":[0,2]},{"left":[0,0],"offsets":[0,3],"right":[3,4],"segments":[0,3]},{"left":[0,0],"offsets":[0,4],"right":[4,4],"segments":[0,4]}],"status":"ok","word":""}]}
        """;

    /// <summary>an unclosed bundle: pattern_parse.</summary>
    internal const string PatternParse = """
        {"code":"pattern_parse","expected":["']'"],"id":"parse","message":"unexpected \"end of pattern\" at offset 2; expected ']'","offset":2,"token":"end of pattern","type":"error"}
        """;

    /// <summary>/p/ + V with boundaryMarker + over p+a and pa.</summary>
    internal const string MarkedBoundary = """
        {"boundaryMarker":"+","complete":true,"id":"marked","op":"match","stopReason":null,"type":"result","words":[{"matches":[{"left":[0,0],"offsets":[0,3],"right":[2,2],"segments":[0,2]}],"status":"ok","word":""},{"matches":[],"status":"ok","word":""}]}
        """;

    /// <summary># Stop V over pa and apa.</summary>
    internal const string EdgeLeft = """
        {"complete":true,"id":"env-left","op":"match","stopReason":null,"type":"result","words":[{"matches":[{"left":[0,0],"offsets":[0,2],"right":[2,2],"segments":[0,2]}],"status":"ok","word":""},{"matches":[],"status":"ok","word":""}]}
        """;

    /// <summary>The <c>interfaces.serveOps.match</c> object of PanGloss v0.30.0's <c>--describe</c>: environments, bundles and alpha variables are supported.</summary>
    internal const string Describe = """
        {"constructs":{"gated":["featureValue","alphaAcrossFeatures"],"supported":["naturalClass","literal","optional","repeat","wordEdge","morphemeBoundary","environment","featureBundle","alphaVariable"]},"errors":["bad_request","pattern_parse","unknown_natural_class","unknown_feature","unsupported_construct","boundary_needs_marked_input","unknown_boundary_marker","too_many_words","words_file_unreadable","pattern_compile_failed","internal"],"limits":{"defaultMaxMatchesPerWord":1000,"defaultMaxWords":100000,"defaultMaxWork":50000000,"defaultWordTimeoutMs":10000,"maxMatchesPerWord":100000,"maxMatchesTotal":1000000,"maxPatternBytes":4096,"maxWordTimeoutMs":600000,"maxWords":1000000},"offsets":"utf8ByteOffsetsInNfdText","request":["pattern","words","wordsFile","boundaryMarker","maxWords","maxMatchesPerWord","maxWork","wordTimeoutMs"],"statuses":["ok","unsegmentable","incomplete"],"stopReasons":["match_cap","total_match_cap","work_limit","deadline"],"version":1,"wordReasons":["no_character_definition","boundary_in_unmarked_word","unexpected_boundary"],"zeroWidth":"reported: a pattern that can match the empty string reports an empty match at every position its anchors allow"}
        """;

    /// <summary>The <c>interfaces.serveOps.inventory</c> object of PanGloss v0.30.0's <c>--describe</c>.</summary>
    internal const string DescribeInventory = """
        {"classFields":["token","name","id","kind"],"classKinds":["segment","feature"],"classToken":"the text between the brackets of [token] in a match pattern: the class name, else its XML id when the name is empty, repeated by an earlier class, or not writable in brackets; null when neither spelling reaches the class","errors":["bad_request","internal"],"featureFields":["name","id","values"],"readOnly":true,"request":["maxWork","wordTimeoutMs"],"stopReasons":["work_limit","deadline"],"version":1,"work":"one unit per class and per feature value listed"}
        """;

    /// <summary>The <c>inventory</c> reply for the match fixture: segment and feature classes, a class whose token is its id, and one with no token.</summary>
    internal const string Inventory = """
        {"classes":[{"id":"ncV","kind":"segment","name":"V","token":"V"},{"id":"ncStop","kind":"segment","name":"Stop","token":"Stop"},{"id":"ncVoiced","kind":"feature","name":"Voiced","token":"Voiced"},{"id":"ncV2","kind":"feature","name":"V","token":"ncV2"},{"id":"ncW","kind":"feature","name":"W","token":"W"},{"id":"W","kind":"feature","name":"Other","token":"Other"},{"id":"ncAnon","kind":"feature","name":"","token":"ncAnon"},{"id":"ncPlus","kind":"feature","name":"+hi","token":"ncPlus"},{"id":"ncNas","kind":"segment","name":"Nasal Stop","token":"Nasal Stop"},{"id":"ncEng","kind":"segment","name":"Ŋ-class","token":"Ŋ-class"},{"id":"ncBr","kind":"segment","name":"A]B","token":"ncBr"},{"id":"ncOpen","kind":"segment","name":"[Open","token":"[Open"},{"id":"no]way","kind":"segment","name":"","token":null}],"complete":true,"features":[{"id":"featVoice","name":"voice","values":["+","-"]},{"id":"featSon","name":"son","values":["+","-"]},{"id":"featTone","name":"tone","values":["hi","lo"]}],"id":"inv","op":"inventory","stopReason":null,"type":"result"}
        """;

    /// <summary>[Stop] / [V] _ [V] over pata, kaki, pat, apa: the span is the target, the stop between vowels.</summary>
    internal const string EnvNative = """
        {"complete":true,"id":"env-native","op":"match","stopReason":null,"type":"result","words":[{"matches":[{"left":[0,2],"offsets":[2,3],"right":[3,4],"segments":[2,3]}],"status":"ok","word":""},{"matches":[{"left":[0,2],"offsets":[2,3],"right":[3,4],"segments":[2,3]}],"status":"ok","word":""},{"matches":[],"status":"ok","word":""},{"matches":[{"left":[0,1],"offsets":[1,2],"right":[2,3],"segments":[1,2]}],"status":"ok","word":""}]}
        """;

    /// <summary>[V] / # _ [Stop] over apa and pa.</summary>
    internal const string EnvEdge = """
        {"complete":true,"id":"env-edge","op":"match","stopReason":null,"type":"result","words":[{"matches":[{"left":[0,0],"offsets":[0,1],"right":[1,3],"segments":[0,1]}],"status":"ok","word":""},{"matches":[],"status":"ok","word":""}]}
        """;

    /// <summary>[V] / _ [Stop] (an empty left side) over apa and pa.</summary>
    internal const string EnvEmptyLeft = """
        {"complete":true,"id":"env-empty-left","op":"match","stopReason":null,"type":"result","words":[{"matches":[{"left":[0,0],"offsets":[0,1],"right":[1,3],"segments":[0,1]}],"status":"ok","word":""},{"matches":[],"status":"ok","word":""}]}
        """;

    /// <summary>[+voice] [V] over ba, pa and aba.</summary>
    internal const string FeatureBundle = """
        {"complete":true,"id":"bundle","op":"match","stopReason":null,"type":"result","words":[{"matches":[{"left":[0,0],"offsets":[0,2],"right":[2,2],"segments":[0,2]}],"status":"ok","word":""},{"matches":[],"status":"ok","word":""},{"matches":[{"left":[0,1],"offsets":[1,3],"right":[3,3],"segments":[1,3]}],"status":"ok","word":""}]}
        """;

    /// <summary>[αvoice] [αvoice] over bada, pata and bata.</summary>
    internal const string Alpha = """
        {"complete":true,"id":"alpha","op":"match","stopReason":null,"type":"result","words":[{"matches":[{"left":[0,0],"offsets":[0,2],"right":[2,4],"segments":[0,2]},{"left":[0,1],"offsets":[1,3],"right":[3,4],"segments":[1,3]},{"left":[0,2],"offsets":[2,4],"right":[4,4],"segments":[2,4]}],"status":"ok","word":""},{"matches":[],"status":"ok","word":""},{"matches":[{"left":[0,0],"offsets":[0,2],"right":[2,4],"segments":[0,2]}],"status":"ok","word":""}]}
        """;

    /// <summary>[αvoice] [αson]: unsupported_construct, alphaAcrossFeatures.</summary>
    internal const string AlphaAcrossFeatures = """
        {"code":"unsupported_construct","construct":"alphaAcrossFeatures","id":"alpha-across","message":"alphaAcrossFeatures is not served yet","offset":11,"type":"error"}
        """;

    /// <summary>[+nope] [V]: unknown_feature.</summary>
    internal const string UnknownFeature = """
        {"code":"unknown_feature","id":"unknown-feature","message":"no phonological feature named \"nope\"","name":"nope","near":[],"offset":2,"type":"error"}
        """;
}
