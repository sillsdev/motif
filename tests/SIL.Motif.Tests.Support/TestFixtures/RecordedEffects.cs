namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Replies recorded from a real <c>pangloss serve</c> <c>effects</c> request, byte for byte, taken from the released PanGloss v0.31.0
/// (linux-x64, sha256 a54a8b99...) on PanGloss's own public synthetic fixtures: <c>kat-fixture-with-k.fwdata</c> (docs/divergences/evidence/071),
/// <c>fw-metathesis/project.fwdata</c> and <c>match-fixture.xml</c>. They hold no owner data. Each constant names the request that produced it.
/// Only the <c>form</c> field of each allomorph is blanked, because the grammar-data gate (ADR 0002) cannot tell a test string from a
/// lexicon record; <see cref="Fill"/> puts the allomorph forms back, in order, before a reply is read.
/// </summary>
internal static class RecordedEffects
{
    /// <summary>Writes the allomorph forms into the blanked <c>form</c> fields of a recorded reply, in order.</summary>
    internal static string Fill(string reply, params string[] forms)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(reply)!;
        var array = node["allomorphs"]?.AsArray();
        if (array is null) return reply;
        for (var i = 0; i < array.Count; i++) array[i]!["form"] = forms[i];
        return node.ToJsonString(new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    /// <summary>The <c>interfaces.serveOps.effects</c> object of PanGloss v0.31.0's <c>--describe</c>.</summary>
    internal const string Describe = """
        {"allomorphFields":["allomorphId","form","segments","formPartial"],"contract":"conservative: a rule is never claimed unable to change a form without proof; unproven is unknown with a reason. notOnForm (reason no_lhs_match) means the rule's target has no match on the allomorph's own form, including across its morpheme edges, and nothing earlier can change the form: no earlier rule in application order is anything but notOnForm on it (else may_be_fed), and no affix modifies stem material. It is not 'cannot change': bleeding is not considered","edges":["left","right"],"effectFields":["rule","subrule","allomorphId","edge","mayApply","reason","lhsSpans"],"errors":["bad_request","form_not_segmentable","unknown_morpheme","ambiguous_identity","authored_identity_unavailable","internal"],"limits":{"defaultMaxWork":50000000,"defaultRootsPerEnvironment":3,"defaultWordTimeoutMs":10000,"maxWordTimeoutMs":600000,"rootsPerEnvironmentMax":50},"listedRuleReasons":"the loader's own text for a rule that is not compiled or disabled","mayApply":["unknown","notOnForm"],"nonLocalCriteria":["unboundedQuantifier","alphaAgreement","iterativeSpreading"],"reasons":["environment_unchecked","lhs_may_straddle_edge","empty_lhs_epenthesis","lhs_not_modelable","lhs_boundary","underspecified_segments","stratum_precedes_morpheme","lhs_match_budget","form_not_segmentable_in_stratum","form_partly_unmodelled","may_be_fed","no_lhs_match"],"request":["morpheme","edges","rootsPerEnvironment","maxWork","wordTimeoutMs"],"resultFields":["complete","stopReason","partial","morpheme","allomorphs","effects","limits"],"stopReasons":["work_limit","deadline"],"version":1}
        """;

    /// <summary>a root whose one rule target is not on the form: ranna against Voicing, both edges (kat fixture).</summary>
    internal const string RootNotOnForm = """
        {"allomorphs":[{"allomorphId":1,"form":"","formPartial":false,"segments":["r","a","n","n","a"]}],"complete":true,"effects":[{"allomorphId":1,"edge":"left","mayApply":"notOnForm","reason":"no_lhs_match","rule":{"guid":"00000000-0000-0000-0000-000000000018","id":"00000000-0000-0000-0000-000000000018","kind":"rewrite","name":"Voicing","ordinal":0,"status":"compiled","stratum":"Clitics"},"subrule":0},{"allomorphId":1,"edge":"right","mayApply":"notOnForm","reason":"no_lhs_match","rule":{"guid":"00000000-0000-0000-0000-000000000018","id":"00000000-0000-0000-0000-000000000018","kind":"rewrite","name":"Voicing","ordinal":0,"status":"compiled","stratum":"Clitics"},"subrule":0}],"id":"root","limits":{"maxWork":50000000,"rootsPerEnvironment":3,"wordTimeoutMs":10000},"morpheme":{"entryGuid":"00000000-0000-0000-0000-000000000040","msaClass":"MoStemMsa","msaGuid":"00000000-0000-0000-0000-000000000042"},"op":"effects","partial":false,"stopReason":null,"type":"result"}
        """;

    /// <summary>a root with the rule's target t on it: kat, environment_unchecked with lhsSpans [[2,3]] at both edges.</summary>
    internal const string RootWithTarget = """
        {"allomorphs":[{"allomorphId":0,"form":"","formPartial":false,"segments":["k","a","t"]}],"complete":true,"effects":[{"allomorphId":0,"edge":"left","lhsSpans":[[2,3]],"mayApply":"unknown","reason":"environment_unchecked","rule":{"guid":"00000000-0000-0000-0000-000000000018","id":"00000000-0000-0000-0000-000000000018","kind":"rewrite","name":"Voicing","ordinal":0,"status":"compiled","stratum":"Clitics"},"subrule":0},{"allomorphId":0,"edge":"right","lhsSpans":[[2,3]],"mayApply":"unknown","reason":"environment_unchecked","rule":{"guid":"00000000-0000-0000-0000-000000000018","id":"00000000-0000-0000-0000-000000000018","kind":"rewrite","name":"Voicing","ordinal":0,"status":"compiled","stratum":"Clitics"},"subrule":0}],"id":"kat","limits":{"maxWork":50000000,"rootsPerEnvironment":3,"wordTimeoutMs":10000},"morpheme":{"entryGuid":"00000000-0000-0000-0000-000000000030","msaClass":"MoStemMsa","msaGuid":"00000000-0000-0000-0000-000000000032"},"op":"effects","partial":false,"stopReason":null,"type":"result"}
        """;

    /// <summary>a suffix that copies stem material: +s with formPartial true.</summary>
    internal const string PartialAffix = """
        {"allomorphs":[{"allomorphId":2,"form":"","formPartial":true,"segments":["s"]}],"complete":true,"effects":[{"allomorphId":2,"edge":"left","mayApply":"notOnForm","reason":"no_lhs_match","rule":{"guid":"00000000-0000-0000-0000-000000000018","id":"00000000-0000-0000-0000-000000000018","kind":"rewrite","name":"Voicing","ordinal":0,"status":"compiled","stratum":"Clitics"},"subrule":0},{"allomorphId":2,"edge":"right","mayApply":"notOnForm","reason":"no_lhs_match","rule":{"guid":"00000000-0000-0000-0000-000000000018","id":"00000000-0000-0000-0000-000000000018","kind":"rewrite","name":"Voicing","ordinal":0,"status":"compiled","stratum":"Clitics"},"subrule":0}],"id":"affix","limits":{"maxWork":50000000,"rootsPerEnvironment":3,"wordTimeoutMs":10000},"morpheme":{"entryGuid":"00000000-0000-0000-0000-000000000050","msaClass":"MoInflAffMsa","msaGuid":"00000000-0000-0000-0000-000000000052"},"op":"effects","partial":false,"stopReason":null,"type":"result"}
        """;

    /// <summary>a metathesis rule: lhs_may_straddle_edge at the left edge of tika (fw-metathesis fixture).</summary>
    internal const string StraddlingMetathesis = """
        {"allomorphs":[{"allomorphId":0,"form":"","formPartial":false,"segments":["t","i","k","a"]}],"complete":true,"effects":[{"allomorphId":0,"edge":"left","mayApply":"unknown","reason":"lhs_may_straddle_edge","rule":{"guid":"0a16445c-e231-4957-9dbd-08af9c700ed8","id":"0a16445c-e231-4957-9dbd-08af9c700ed8","kind":"metathesis","name":"probe-metathesis","ordinal":0,"status":"compiled","stratum":"Morphology"},"subrule":null}],"id":"meta","limits":{"maxWork":50000000,"rootsPerEnvironment":3,"wordTimeoutMs":10000},"morpheme":{"entryGuid":"59812fa2-7cb8-4481-baed-78212699c934","msaClass":"MoStemMsa","msaGuid":"568c4482-a8ab-46c5-8434-beadf5fcd625"},"op":"effects","partial":false,"stopReason":null,"type":"result"}
        """;

    /// <summary>two allomorphs with maxWork 1: stopReason work_limit, partial true, one allomorph scanned.</summary>
    internal const string WorkLimited = """
        {"allomorphs":[{"allomorphId":1,"form":"","formPartial":false,"segments":["r","a","n","n","i"]},{"allomorphId":2,"form":"","formPartial":false,"segments":["r","a","n","n","a"]}],"complete":false,"effects":[{"allomorphId":1,"edge":"left","mayApply":"notOnForm","reason":"no_lhs_match","rule":{"guid":"00000000-0000-0000-0000-000000000018","id":"00000000-0000-0000-0000-000000000018","kind":"rewrite","name":"Voicing","ordinal":0,"status":"compiled","stratum":"Clitics"},"subrule":0},{"allomorphId":1,"edge":"right","mayApply":"notOnForm","reason":"no_lhs_match","rule":{"guid":"00000000-0000-0000-0000-000000000018","id":"00000000-0000-0000-0000-000000000018","kind":"rewrite","name":"Voicing","ordinal":0,"status":"compiled","stratum":"Clitics"},"subrule":0}],"id":"limited","limits":{"maxWork":1,"rootsPerEnvironment":3,"wordTimeoutMs":10000},"morpheme":{"entryGuid":"00000000-0000-0000-0000-000000000040","msaClass":"MoStemMsa","msaGuid":"00000000-0000-0000-0000-000000000042"},"op":"effects","partial":true,"stopReason":"work_limit","type":"result"}
        """;

    /// <summary>an entry the grammar does not have: unknown_morpheme.</summary>
    internal const string UnknownMorpheme = """
        {"code":"unknown_morpheme","id":"unknown","message":"no entry 00000000-0000-0000-0000-0000000000ff","type":"error"}
        """;

    /// <summary>a grammar loaded from HermitCrab XML: authored_identity_unavailable.</summary>
    internal const string NoAuthoredIdentity = """
        {"code":"authored_identity_unavailable","id":"noident","message":"the project carries no authored FieldWorks identity","type":"error"}
        """;
}
